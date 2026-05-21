from __future__ import annotations

import argparse
import logging
from typing import Optional

import numpy as np

from wave_integration import codec
from wave_integration.operators.base import Operator

logger = logging.getLogger(__name__)


class ThresholdOperator(Operator):
    """Emit an event frame when signal peak exceeds threshold_mv; else return None.

    Output sample order: [0]=peak_value  [1]=threshold  [2]=block_len
    """

    def __init__(self, *, threshold_mv: float, **kwargs) -> None:
        super().__init__(**kwargs)
        self.threshold_mv = threshold_mv

    def process(self, record_in: dict) -> Optional[bytes]:
        x: np.ndarray = record_in["samples"]
        peak = float(np.max(np.abs(x)))
        if peak <= self.threshold_mv:
            return None

        logger.info(
            "[threshold] EVENT ts=%d peak=%.4f threshold=%.4f",
            record_in["timestamp_ns"],
            peak,
            self.threshold_mv,
        )
        event = np.array([peak, self.threshold_mv, float(len(x))], dtype=np.float32)
        return codec.encode_block(
            timestamp_ns=record_in["timestamp_ns"],
            sample_rate_hz=record_in["sample_rate_hz"],
            channel_id=record_in["channel_id"],
            source_id=record_in["source_id"],
            samples=event,
        )


def main() -> None:
    p = argparse.ArgumentParser(description="wave-integration threshold event detector")
    p.add_argument("--input-topic", default="raw.gen.chA")
    p.add_argument("--output-topic", default="events.gen.chA")
    p.add_argument("--group-id", default="threshold-gen-chA")
    p.add_argument("--threshold-mv", type=float, default=0.8)
    p.add_argument("--broker", default="127.0.0.1:7912")
    args = p.parse_args()

    logging.basicConfig(level=logging.INFO, format="%(message)s")
    ThresholdOperator(
        input_topic=args.input_topic,
        output_topic=args.output_topic,
        group_id=args.group_id,
        broker_addr=args.broker,
        threshold_mv=args.threshold_mv,
    ).run()


if __name__ == "__main__":
    main()
