from __future__ import annotations

import struct
import sys

from wavemq import WaveMQBrokerError, WaveMQClient

BROKER = "127.0.0.1:7912"
TOPIC = "wave.sdk.demo"
TOPIC_PARTITIONS = 1
REPLICATION_FACTOR = 1
KEY = "demo-key"
VALUES = [1.4, -3.22, 2.28, 8.8]
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
                    payload,
                    key=KEY,
                    content_type=CONTENT_TYPE,
                )
                print(
                    f"produced partition={result.partition} base_offset={result.base_offset} "
                    f"content_type={CONTENT_TYPE} float64={value} bytes=0x{payload.hex()}"
                )
        return 0
    except (OSError, WaveMQBrokerError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
