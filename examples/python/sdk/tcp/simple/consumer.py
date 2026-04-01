#!/usr/bin/env python3
"""Minimal SDK consumer demo over TCP with in-file settings only."""

from __future__ import annotations

import struct
import sys
import time

try:
    from wavemq import WaveMQBrokerError, WaveMQClient
except ImportError as exc:  # pragma: no cover - import failure path
    raise SystemExit(
        "wave-python-sdk is not installed. Run: "
        "python -m pip install wave-python-sdk"
    ) from exc


BROKER = "127.0.0.1:7912"
TOPIC = "wave.sdk.demo"
GROUP = "demo-group"
PARTITION = 0
START_FROM = "latest"
START_OFFSET = 0
POLL_INTERVAL_SECONDS = 1.0
MAX_MESSAGES = 0


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


def resolve_start_offset(client: WaveMQClient) -> int:
    if START_FROM == "offset":
        return START_OFFSET
    offsets = client.list_offsets(TOPIC, PARTITION)
    if START_FROM == "earliest":
        return offsets.earliest
    latest = offsets.latest
    return 0 if latest < 0 else latest + 1


def main() -> int:
    try:
        with WaveMQClient(BROKER, transport="tcp") as client:
            committed_offset = None
            try:
                committed_offset = client.fetch_committed(GROUP, TOPIC, PARTITION).offset
            except WaveMQBrokerError:
                committed_offset = None

            if committed_offset is None:
                try:
                    next_offset = resolve_start_offset(client)
                except WaveMQBrokerError:
                    next_offset = 0 if START_FROM == "latest" else START_OFFSET
            else:
                next_offset = committed_offset + 1

            print("transport=tcp")
            print(f"broker={BROKER}")
            print(f"topic={TOPIC}")
            print(f"group={GROUP}")
            print(f"partition={PARTITION}")
            if committed_offset is None:
                print(f"committed_offset=unset start_mode={START_FROM} start_offset={next_offset}")
            else:
                print(f"committed_offset={committed_offset} start_offset={next_offset}")

            seen = 0
            while MAX_MESSAGES <= 0 or seen < MAX_MESSAGES:
                try:
                    fetched = client.fetch(TOPIC, PARTITION, next_offset)
                except WaveMQBrokerError:
                    time.sleep(POLL_INTERVAL_SECONDS)
                    continue

                if not fetched.records:
                    time.sleep(POLL_INTERVAL_SECONDS)
                    continue

                for record in fetched.records:
                    if record.offset < next_offset:
                        continue
                    float_value = decode_float64(record.value)
                    print(
                        "offset={offset} key={key} float64={value} bytes=0x{raw}".format(
                            offset=record.offset,
                            key=decode_bytes(record.key),
                            value=float_value,
                            raw=(record.value or b"").hex(),
                        )
                    )
                    client.commit_offset(GROUP, TOPIC, PARTITION, record.offset)
                    next_offset = record.offset + 1
                    seen += 1
                    if MAX_MESSAGES > 0 and seen >= MAX_MESSAGES:
                        break

                if MAX_MESSAGES <= 0 or seen < MAX_MESSAGES:
                    time.sleep(POLL_INTERVAL_SECONDS)
        return 0
    except (OSError, WaveMQBrokerError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
