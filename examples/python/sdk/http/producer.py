#!/usr/bin/env python3
"""SDK producer demo over HTTP."""

from __future__ import annotations

import argparse
import sys

try:
    from wavemq import TopicExistsError, WaveMQBrokerError, WaveMQClient
except ImportError as exc:  # pragma: no cover - import failure path
    raise SystemExit(
        "wave-python-sdk is not installed. Run: "
        "python -m pip install wave-python-sdk"
    ) from exc


DEFAULT_BROKER = "http://127.0.0.1:8090"
DEFAULT_TOPIC = "wave.sdk.demo"
DEFAULT_KEY = "demo-key"
DEFAULT_MESSAGES = ["hello from sdk http producer", "second sdk http message", "third sdk http message"]


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="wave-mq SDK HTTP producer demo")
    parser.add_argument("--broker", default=DEFAULT_BROKER, help="broker address")
    parser.add_argument("--topic", default=DEFAULT_TOPIC, help="topic name")
    parser.add_argument("--partition", type=int, default=0, help="partition id")
    parser.add_argument("--key", default=DEFAULT_KEY, help="message key")
    parser.add_argument("--message", action="append", default=[], help="message value; may be repeated")
    parser.add_argument("--partitions", type=int, default=1, help="topic partition count")
    parser.add_argument("--replication-factor", type=int, default=1, help="topic replication factor")
    return parser


def selected_messages(values: list[str]) -> list[str]:
    if values:
        return list(values)
    return list(DEFAULT_MESSAGES)


def main() -> int:
    args = build_parser().parse_args()
    messages = selected_messages(args.message)

    try:
        with WaveMQClient(args.broker, transport="http") as client:
            try:
                client.create_topic(args.topic, partitions=args.partitions, replication_factor=args.replication_factor)
                print(f"topic={args.topic} created")
            except TopicExistsError:
                print(f"topic={args.topic} already exists")

            for message in messages:
                result = client.produce(args.topic, args.partition, [message], key=args.key)
                print(f"produced partition={args.partition} base_offset={result.base_offset} value={message}")
        return 0
    except (OSError, WaveMQBrokerError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
