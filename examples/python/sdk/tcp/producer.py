#!/usr/bin/env python3
"""SDK producer demo over TCP."""

from __future__ import annotations

import argparse
import struct
import sys

from wavemq import WaveMQBrokerError, WaveMQClient



DEFAULT_BROKER = "127.0.0.1:7912"
DEFAULT_TOPIC = "wave.sdk.demo"
DEFAULT_KEY = "demo-key"
DEFAULT_VALUES = [12.5, -3.25, 42.125]
DEFAULT_CONTENT_TYPE = "application/x.float64"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="wave-mq SDK TCP producer demo")
    parser.add_argument("--broker", default=DEFAULT_BROKER, help="broker address")
    parser.add_argument("--topic", default=DEFAULT_TOPIC, help="topic name")
    parser.add_argument("--partition", type=int, default=0, help="partition id")
    parser.add_argument("--key", default=DEFAULT_KEY, help="message key")
    parser.add_argument("--value", action="append", type=float, default=[], help="float value; may be repeated")
    parser.add_argument("--partitions", type=int, default=1, help="topic partition count")
    parser.add_argument("--replication-factor", type=int, default=1, help="topic replication factor")
    return parser


def selected_values(values: list[float]) -> list[float]:
    if values:
        return list(values)
    return list(DEFAULT_VALUES)


def encode_float64(value: float) -> bytes:
    return struct.pack(">d", value)


def main() -> int:
    args = build_parser().parse_args()
    values = selected_values(args.value)

    try:
        with WaveMQClient(args.broker, transport="tcp") as client:
            ensured = client.ensure_topic(
                args.topic,
                partitions=args.partitions,
                replication_factor=args.replication_factor,
            )
            if ensured.created:
                print(f"topic={args.topic} created")
            else:
                print(f"topic={args.topic} already exists")

            for value in values:
                payload = encode_float64(value)
                result = client.produce_one(
                    args.topic,
                    args.partition,
                    payload,
                    key=args.key,
                    content_type=DEFAULT_CONTENT_TYPE,
                )
                print(
                    f"produced partition={args.partition} base_offset={result.base_offset} "
                    f"content_type={DEFAULT_CONTENT_TYPE} float64={value} bytes=0x{payload.hex()}"
                )
        return 0
    except (OSError, WaveMQBrokerError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
