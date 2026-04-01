#!/usr/bin/env python3
"""HTTP producer demo for wave-mq.

This script only needs an HTTP addr and uses the broker's JSON API directly.
"""

from __future__ import annotations

import argparse
import sys
from typing import Sequence

from http_demo_common import DEFAULT_PARTITIONS, DEFAULT_REPLICATION_FACTOR, DEFAULT_TIMEOUT, ensure_topic, produce_message


DEFAULT_TOPIC = "wave.http.demo"
DEFAULT_KEY = "demo-key"
DEFAULT_MESSAGES = ["hello from producer", "second message", "third message"]


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="wave-mq HTTP producer demo")
    parser.add_argument("--addr", default="http://127.0.0.1:8090", help="HTTP addr, e.g. http://127.0.0.1:8090")
    parser.add_argument("--topic", default=DEFAULT_TOPIC, help="topic name")
    parser.add_argument("--key", default=DEFAULT_KEY, help="message key used for routing")
    parser.add_argument("--message", action="append", default=[], help="message value; may be repeated")
    parser.add_argument("--partitions", type=int, default=DEFAULT_PARTITIONS, help="topic partition count")
    parser.add_argument("--replication-factor", type=int, default=DEFAULT_REPLICATION_FACTOR, help="topic replication factor")
    parser.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT, help="HTTP timeout in seconds")
    return parser


def selected_messages(values: Sequence[str]) -> list[str]:
    if values:
        return list(values)
    return list(DEFAULT_MESSAGES)


def main() -> int:
    args = build_parser().parse_args()
    messages = selected_messages(args.message)

    try:
        topic_result = ensure_topic(
            args.addr,
            args.topic,
            partitions=args.partitions,
            replication_factor=args.replication_factor,
            timeout=args.timeout,
        )
        if isinstance(topic_result, dict) and topic_result.get("error") == "topic_exists":
            print(f"topic={args.topic} already exists")
        else:
            print(f"topic={args.topic} created")

        for message in messages:
            result = produce_message(
                args.addr,
                args.topic,
                message,
                args.key,
                timeout=args.timeout,
            )
            print(f"produced partition={result['partition']} base_offset={result['baseOffset']} value={message}")
        return 0
    except Exception as exc:  # noqa: BLE001
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
