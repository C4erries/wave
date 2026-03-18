#!/usr/bin/env python3
"""Thin demo wrapper around mbctl."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path


class MBctlError(RuntimeError):
    pass


def default_mbctl_path() -> Path:
    repo_root = Path(__file__).resolve().parents[2]
    return repo_root / "examples" / "bin" / "mbctl.exe"


def ensure_binary(path: Path) -> None:
    if path.is_file():
        return
    raise MBctlError(
        f"mbctl not found at {path}. Build it first with "
        r"powershell -ExecutionPolicy Bypass -File .\examples\build-mbctl.ps1"
    )


def run_mbctl(binary: Path, broker: str, args: list[str]) -> dict[str, object]:
    command = [str(binary), *args, "-broker", broker, "-json"]
    completed = subprocess.run(command, capture_output=True, text=True, check=False)

    if completed.returncode != 0:
        message = completed.stderr.strip() or completed.stdout.strip() or f"mbctl exit code {completed.returncode}"
        raise MBctlError(message)

    payload = completed.stdout.strip()
    if not payload:
        raise MBctlError(f"mbctl returned empty stdout for {' '.join(args)}")

    try:
        return json.loads(payload)
    except json.JSONDecodeError as exc:
        raise MBctlError(f"invalid JSON from mbctl for {' '.join(args)}: {exc}") from exc


def print_metadata(result: dict[str, object]) -> None:
    partitions = list(result.get("partitions", []))
    if not partitions:
        print("metadata: no partitions returned")
        return
    for item in partitions:
        print(
            "topic={topic} partition={partition} broker={broker} role={role} leader={leader} "
            "start={start} hwm={hwm} replicas={replicas} isr={isr}".format(
                topic=item["topic"],
                partition=item["partition"],
                broker=item["brokerId"],
                role=item["role"],
                leader=item["leader"],
                start=item["startOffset"],
                hwm=item["highWatermark"],
                replicas=item["replicas"],
                isr=item["isr"],
            )
        )


def print_fetch(result: dict[str, object]) -> None:
    records = list(result.get("records", []))
    print(f"high_watermark={result['highWatermark']} records={len(records)}")
    for record in records:
        print(
            "offset={offset} key={key} value={value}".format(
                offset=record["offset"],
                key=record["key"],
                value=record["value"],
            )
        )


def run_demo(args: argparse.Namespace) -> int:
    mbctl = args.mbctl.resolve()
    ensure_binary(mbctl)

    topic = args.topic or f"demo.binary.{int(time.time())}"
    group = args.group or f"demo-group-{int(time.time())}"

    print(f"broker={args.broker}")
    print(f"mbctl={mbctl}")

    print("1. ping")
    run_mbctl(mbctl, args.broker, ["ping"])
    print("pong")

    print(f"2. create topic {topic}")
    create_result = run_mbctl(
        mbctl,
        args.broker,
        [
            "create-topic",
            "-topic",
            topic,
            "-partitions",
            str(args.partitions),
            "-replication-factor",
            str(args.replication_factor),
        ],
    )
    print(
        "topic created partitions={partitions} rf={rf}".format(
            partitions=create_result["partitions"],
            rf=create_result["replicationFactor"],
        )
    )

    print("3. metadata")
    print_metadata(run_mbctl(mbctl, args.broker, ["metadata", "-topic", topic]))

    print("4. produce")
    base_offset = None
    for idx in range(args.count):
        produce_result = run_mbctl(
            mbctl,
            args.broker,
            [
                "produce",
                "-topic",
                topic,
                "-partition",
                str(args.partition),
                "-key",
                args.key,
                "-value",
                f"demo-message-{idx}",
            ],
        )
        if base_offset is None:
            base_offset = produce_result["baseOffset"]
    print(f"base_offset={base_offset}")

    print("5. list offsets")
    offsets = run_mbctl(
        mbctl,
        args.broker,
        ["list-offsets", "-topic", topic, "-partition", str(args.partition)],
    )
    print(f"earliest={offsets['earliest']} latest={offsets['latest']}")

    print("6. fetch")
    fetch_result = run_mbctl(
        mbctl,
        args.broker,
        [
            "fetch",
            "-topic",
            topic,
            "-partition",
            str(args.partition),
            "-offset",
            str(args.offset),
            "-max-bytes",
            str(args.max_bytes),
        ],
    )
    print_fetch(fetch_result)

    records = list(fetch_result.get("records", []))
    if records:
        committed_offset = int(records[-1]["offset"])
        print(f"7. commit offset group={group} offset={committed_offset}")
        run_mbctl(
            mbctl,
            args.broker,
            [
                "commit-offset",
                "-group",
                group,
                "-topic",
                topic,
                "-partition",
                str(args.partition),
                "-offset",
                str(committed_offset),
            ],
        )
        print("commit ok")

        print("8. fetch committed")
        committed = run_mbctl(
            mbctl,
            args.broker,
            [
                "fetch-committed",
                "-group",
                group,
                "-topic",
                topic,
                "-partition",
                str(args.partition),
            ],
        )
        print(f"committed offset={committed['offset']}")

    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="wave-mq demo wrapper built on mbctl")
    parser.add_argument("command", nargs="?", default="demo", choices=["demo"], help="demo scenario")
    parser.add_argument("--mbctl", type=Path, default=default_mbctl_path(), help="path to mbctl executable")
    parser.add_argument("--broker", default="127.0.0.1:7912", help="broker binary address")
    parser.add_argument("--topic", default="", help="topic name; default is generated")
    parser.add_argument("--group", default="", help="consumer group; default is generated")
    parser.add_argument("--partitions", type=int, default=1, help="topic partition count for demo")
    parser.add_argument("--replication-factor", type=int, default=1, help="topic replication factor for demo")
    parser.add_argument("--partition", type=int, default=0, help="partition used in demo")
    parser.add_argument("--count", type=int, default=3, help="messages to produce in demo")
    parser.add_argument("--offset", type=int, default=0, help="fetch start offset for demo")
    parser.add_argument("--max-bytes", type=int, default=1 << 20, help="fetch byte limit for demo")
    parser.add_argument("--key", default="", help="record key for produced messages")
    return parser


def main() -> int:
    args = build_parser().parse_args()
    try:
        return run_demo(args)
    except (OSError, MBctlError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
