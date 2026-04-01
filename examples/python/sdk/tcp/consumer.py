#!/usr/bin/env python3
"""SDK consumer demo over TCP."""

from __future__ import annotations

import argparse
import struct
import sys

try:
    from wavemq import WaveMQBrokerError, WaveMQClient
except ImportError as exc:  # pragma: no cover - import failure path
    raise SystemExit(
        "wave-python-sdk is not installed. Run: "
        "python -m pip install wave-python-sdk"
    ) from exc


DEFAULT_BROKER = "127.0.0.1:7912"
DEFAULT_TOPIC = "wave.sdk.demo"
DEFAULT_GROUP = "demo-group"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="wave-mq SDK TCP consumer demo")
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


def decode_float64(value: bytes | None) -> float:
    if value is None:
        raise ValueError("record value is empty")
    if len(value) != 8:
        raise ValueError(f"expected 8 bytes for float64, got {len(value)}")
    return struct.unpack(">d", value)[0]


def main() -> int:
    args = build_parser().parse_args()

    try:
        with WaveMQClient(args.broker, transport="tcp") as client:
            print("transport=tcp")
            print(f"broker={args.broker}")
            print(f"topic={args.topic}")
            print(f"group={args.group}")
            print(f"partition={args.partition}")
            print(f"start_mode={args.start_from} requested_offset={args.offset}")

            def handle_record(record) -> None:
                float_value = decode_float64(record.value)
                print(
                    "offset={offset} key={key} content_type={content_type} float64={value} bytes=0x{raw}".format(
                        offset=record.offset,
                        key=decode_bytes(record.key),
                        content_type=record.content_type or "-",
                        value=float_value,
                        raw=(record.value or b"").hex(),
                    )
                )

            result = client.consume_poll(
                args.group,
                args.topic,
                args.partition,
                start_from=args.start_from,
                start_offset=args.offset,
                max_messages=args.max_messages,
                poll_interval=args.poll_interval,
                commit=True,
                on_record=handle_record,
            )
            print(
                f"start_offset={result.start_offset} next_offset={result.next_offset} "
                f"high_watermark={result.high_watermark}"
            )
        return 0
    except (OSError, WaveMQBrokerError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
