#!/usr/bin/env python3
"""Minimal SDK consumer demo over TCP with in-file settings only."""

from __future__ import annotations

import struct
import sys

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
START_FROM = "earliest"
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


def render_record(record) -> None:
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


def main() -> int:
    try:
        with WaveMQClient(BROKER, transport="tcp") as client:
            next_offset = client.resolve_consume_offset(
                GROUP,
                TOPIC,
                PARTITION,
                start_from=START_FROM,
                start_offset=START_OFFSET,
            )

            print("transport=tcp")
            print(f"broker={BROKER}")
            print(f"topic={TOPIC}")
            print(f"group={GROUP}")
            print(f"partition={PARTITION}")
            print(f"start_mode={START_FROM} start_offset={next_offset}")

            client.consume_poll(
                GROUP,
                TOPIC,
                PARTITION,
                next_offset=next_offset,
                max_messages=MAX_MESSAGES,
                poll_interval=POLL_INTERVAL_SECONDS,
                commit=True,
                on_record=render_record,
            )
        return 0
    except (OSError, WaveMQBrokerError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
