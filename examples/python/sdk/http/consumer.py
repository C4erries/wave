#!/usr/bin/env python3
"""SDK consumer demo over HTTP."""

from __future__ import annotations

import argparse
import sys
import time

try:
    from wavemq import WaveMQBrokerError, WaveMQClient
except ImportError as exc:  # pragma: no cover - import failure path
    raise SystemExit(
        "wave-python-sdk is not installed. Run: "
        "python -m pip install wave-python-sdk"
    ) from exc


DEFAULT_BROKER = "http://127.0.0.1:8090"
DEFAULT_TOPIC = "wave.sdk.demo"
DEFAULT_GROUP = "demo-group"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="wave-mq SDK HTTP consumer demo")
    parser.add_argument("--broker", default=DEFAULT_BROKER, help="broker address")
    parser.add_argument("--topic", default=DEFAULT_TOPIC, help="topic name")
    parser.add_argument("--group", default=DEFAULT_GROUP, help="consumer group")
    parser.add_argument("--partition", type=int, default=0, help="partition id")
    parser.add_argument("--start-from", choices=("latest", "earliest", "offset"), default="earliest", help="where to start when no committed offset exists")
    parser.add_argument("--offset", type=int, default=0, help="starting offset when --start-from=offset")
    parser.add_argument("--poll-interval", type=float, default=1.0, help="poll interval in seconds")
    parser.add_argument("--max-messages", type=int, default=0, help="stop after this many records; 0 means keep polling")
    return parser


def decode_bytes(value: bytes | None) -> str:
    if value is None:
        return ""
    return value.decode("utf-8", errors="replace")


def resolve_start_offset(client: WaveMQClient, topic: str, partition: int, start_from: str, fallback_offset: int) -> int:
    if start_from == "offset":
        return fallback_offset
    offsets = client.list_offsets(topic, partition)
    if start_from == "earliest":
        return offsets.earliest
    latest = offsets.latest
    return 0 if latest < 0 else latest + 1


def main() -> int:
    args = build_parser().parse_args()

    try:
        with WaveMQClient(args.broker, transport="http") as client:
            committed_offset = None
            try:
                committed_offset = client.fetch_committed(args.group, args.topic, args.partition).offset
            except WaveMQBrokerError:
                committed_offset = None

            if committed_offset is None:
                try:
                    next_offset = resolve_start_offset(client, args.topic, args.partition, args.start_from, args.offset)
                except WaveMQBrokerError:
                    next_offset = 0 if args.start_from == "latest" else args.offset
            else:
                next_offset = committed_offset + 1

            print("transport=http")
            print(f"broker={args.broker}")
            print(f"topic={args.topic}")
            print(f"group={args.group}")
            print(f"partition={args.partition}")
            if committed_offset is None:
                print(f"committed_offset=unset start_mode={args.start_from} start_offset={next_offset}")
            else:
                print(f"committed_offset={committed_offset} start_offset={next_offset}")

            seen = 0
            while args.max_messages <= 0 or seen < args.max_messages:
                try:
                    fetched = client.fetch(args.topic, args.partition, next_offset)
                except WaveMQBrokerError:
                    time.sleep(args.poll_interval)
                    continue

                if not fetched.records:
                    time.sleep(args.poll_interval)
                    continue

                for record in fetched.records:
                    if record.offset < next_offset:
                        continue
                    print(
                        "offset={offset} key={key} value={value}".format(
                            offset=record.offset,
                            key=decode_bytes(record.key),
                            value=decode_bytes(record.value),
                        )
                    )
                    client.commit_offset(args.group, args.topic, args.partition, record.offset)
                    next_offset = record.offset + 1
                    seen += 1
                    if args.max_messages > 0 and seen >= args.max_messages:
                        break

                if args.max_messages <= 0 or seen < args.max_messages:
                    time.sleep(args.poll_interval)
        return 0
    except (OSError, WaveMQBrokerError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
