#!/usr/bin/env python3
"""HTTP consumer demo for wave-mq.

The consumer polls the topic JSON endpoint, reads committed offsets through the
consumer offset API, and commits progress after each processed message.
"""

from __future__ import annotations

import argparse
import sys

from http_demo_common import (
    DEFAULT_TIMEOUT,
    HttpError,
    TransportError,
    commit_offset,
    fetch_partition_messages,
    get_committed_offset,
    record_text,
    resolve_start_offset,
    sleep_interval,
    sort_records_ascending,
)


DEFAULT_TOPIC = "wave.http.demo"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="wave-mq HTTP consumer demo")
    parser.add_argument("--addr", default="http://127.0.0.1:8090", help="HTTP addr, e.g. http://127.0.0.1:8090")
    parser.add_argument("--topic", default=DEFAULT_TOPIC, help="topic name")
    parser.add_argument("--group", default="demo-group", help="consumer group name")
    parser.add_argument("--partition", type=int, default=0, help="partition id")
    parser.add_argument("--start-from", choices=("latest", "earliest", "offset"), default="earliest", help="where to start when no committed offset exists")
    parser.add_argument("--offset", type=int, default=0, help="starting offset when --start-from=offset")
    parser.add_argument("--limit", type=int, default=50, help="page size")
    parser.add_argument("--max-messages", type=int, default=0, help="stop after this many records; 0 means keep polling")
    parser.add_argument("--poll-interval", type=float, default=1.0, help="poll interval in seconds")
    parser.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT, help="HTTP timeout in seconds")
    return parser


def main() -> int:
    args = build_parser().parse_args()
    committed_offset = get_committed_offset(args.addr, args.group, args.topic, args.partition, timeout=args.timeout)
    if committed_offset is None:
        next_offset = resolve_start_offset(
            args.addr,
            args.topic,
            args.partition,
            args.start_from,
            args.offset,
            timeout=args.timeout,
        )
    else:
        next_offset = committed_offset + 1
    seen = 0

    print(f"consumer addr={args.addr}")
    print(f"consumer topic={args.topic}")
    print(f"consumer group={args.group}")
    print(f"consumer partition={args.partition}")
    if committed_offset is None:
        print(f"consumer committed_offset=unset start_mode={args.start_from} start_offset={next_offset}")
    else:
        print(f"consumer committed_offset={committed_offset} start_offset={next_offset}")

    try:
        while args.max_messages <= 0 or seen < args.max_messages:
            try:
                records = fetch_partition_messages(
                    args.addr,
                    args.topic,
                    args.partition,
                    offset=next_offset,
                    limit=args.limit,
                    timeout=args.timeout,
                )
            except HttpError as exc:
                if exc.status == 404:
                    print("topic not ready, waiting...")
                    sleep_interval(args.poll_interval)
                    continue
                raise
            except TransportError:
                raise

            if not records:
                sleep_interval(args.poll_interval)
                continue

            ordered = sort_records_ascending(records)
            for record in ordered:
                offset = int(record.get("offset", 0))
                if offset < next_offset:
                    continue
                print(record_text(record))
                commit_offset(args.addr, args.group, args.topic, args.partition, offset, timeout=args.timeout)
                next_offset = offset + 1
                seen += 1
                if args.max_messages > 0 and seen >= args.max_messages:
                    break

            if args.max_messages <= 0 or seen < args.max_messages:
                sleep_interval(args.poll_interval)

        return 0
    except Exception as exc:  # noqa: BLE001
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
