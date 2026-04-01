#!/usr/bin/env python3
"""Minimal SDK producer demo over TCP with in-file settings only."""

from __future__ import annotations

import struct
import sys

from wavemq import WaveMQBrokerError, WaveMQClient

BROKER = "127.0.0.1:7912"
TOPIC = "wave.sdk.demo"
PARTITION = 0
TOPIC_PARTITIONS = 1
REPLICATION_FACTOR = 1
KEY = "demo-key"
VALUES = [12.5, -3.25, 42.125]
CONTENT_TYPE = "application/x.float64"


def encode_float64(value: float) -> bytes:
    return struct.pack(">d", value)


def main() -> int:
    try:
        with WaveMQClient(BROKER, transport="tcp") as client:
            result = client.ensure_topic(
                TOPIC,
                partitions=TOPIC_PARTITIONS,
                replication_factor=REPLICATION_FACTOR,
            )
            print(f"topic={result.topic} ready partitions={result.partitions}")

            for value in VALUES:
                payload = encode_float64(value)
                result = client.produce_one(
                    TOPIC,
                    PARTITION,
                    payload,
                    key=KEY,
                    content_type=CONTENT_TYPE,
                )
                print(
                    f"produced partition={PARTITION} base_offset={result.base_offset} "
                    f"content_type={CONTENT_TYPE} float64={value} bytes=0x{payload.hex()}"
                )
        return 0
    except (OSError, WaveMQBrokerError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
