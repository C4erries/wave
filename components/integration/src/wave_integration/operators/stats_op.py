from __future__ import annotations

import argparse
import logging
from typing import Optional

import numpy as np

from wave_integration import codec
from wave_integration.operators.base import Operator


class StatsOperator(Operator):
    """Compute per-block aggregate statistics and publish as a 4-sample frame.

    Output sample order: [0]=rms  [1]=peak  [2]=peak_to_peak  [3]=mean
    """

    def process(self, record_in: dict) -> Optional[bytes]:
        x: np.ndarray = record_in["samples"]
        rms = float(np.sqrt(np.mean(x ** 2)))
        peak = float(np.max(np.abs(x)))
        peak_to_peak = float(np.max(x) - np.min(x))
        mean = float(np.mean(x))
        stats = np.array([rms, peak, peak_to_peak, mean], dtype=np.float32)
        return codec.encode_block(
            timestamp_ns=record_in["timestamp_ns"],
            sample_rate_hz=record_in["sample_rate_hz"],
            channel_id=record_in["channel_id"],
            source_id=record_in["source_id"],
            samples=stats,
        )


def main() -> None:
    p = argparse.ArgumentParser(description="wave-integration stats operator")
    p.add_argument("--input-topic", default="raw.gen.chA")
    p.add_argument("--output-topic", default="stats.gen.chA")
    p.add_argument("--group-id", default="stats-gen-chA")
    p.add_argument("--broker", default="127.0.0.1:7912")
    args = p.parse_args()

    logging.basicConfig(level=logging.INFO, format="%(message)s")
    StatsOperator(
        input_topic=args.input_topic,
        output_topic=args.output_topic,
        group_id=args.group_id,
        broker_addr=args.broker,
    ).run()


if __name__ == "__main__":
    main()
