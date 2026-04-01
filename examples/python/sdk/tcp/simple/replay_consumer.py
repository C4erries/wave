#!/usr/bin/env python3
"""Replay SDK consumer over TCP with in-file settings only."""

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
GROUP = "analysis-group"
PARTITION = 0
REPLAY_FROM_OFFSET = 0
MAX_MESSAGES = 0
POLL_INTERVAL_SECONDS = 1.0
STOP_WHEN_CAUGHT_UP = True
COMMIT_AFTER_READ = False


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
    next_offset = REPLAY_FROM_OFFSET
    seen = 0

    try:
        with WaveMQClient(BROKER, transport="tcp") as client:
            print("transport=tcp")
            print(f"broker={BROKER}")
            print(f"topic={TOPIC}")
            print(f"group={GROUP}")
            print(f"partition={PARTITION}")
            print(f"replay_from_offset={REPLAY_FROM_OFFSET}")
            print(f"commit_after_read={COMMIT_AFTER_READ}")

            while MAX_MESSAGES <= 0 or seen < MAX_MESSAGES:
                try:
                    fetched = client.fetch(TOPIC, PARTITION, next_offset)
                except WaveMQBrokerError:
                    time.sleep(POLL_INTERVAL_SECONDS)
                    continue

                if not fetched.records:
                    if STOP_WHEN_CAUGHT_UP and fetched.high_watermark >= 0 and next_offset > fetched.high_watermark:
                        print("replay complete: reached current high watermark")
                        break

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

                    if COMMIT_AFTER_READ:
                        client.commit_offset(GROUP, TOPIC, PARTITION, record.offset)

                    next_offset = record.offset + 1
                    seen += 1
                    if MAX_MESSAGES > 0 and seen >= MAX_MESSAGES:
                        break

                if MAX_MESSAGES <= 0 or seen < MAX_MESSAGES:
                    if STOP_WHEN_CAUGHT_UP and fetched.high_watermark >= 0 and next_offset > fetched.high_watermark:
                        print("replay complete: reached current high watermark")
                        break
                    time.sleep(POLL_INTERVAL_SECONDS)

        return 0
    except (OSError, WaveMQBrokerError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
