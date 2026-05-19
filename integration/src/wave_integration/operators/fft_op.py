from __future__ import annotations

import argparse
import logging
from typing import Optional

import numpy as np

from wave_integration import codec
from wave_integration.operators.base import Operator


class FFTOperator(Operator):
    def process(self, record_in: dict) -> Optional[bytes]:
        samples = record_in["samples"]  # float32
        spectrum = np.abs(np.fft.rfft(samples)).astype(np.float32)
        # sample_rate_hz остаётся ИСХОДНЫМ; UI вычислит:
        # freq[k] = k * sample_rate_hz / (2 * (n_samples - 1))
        return codec.encode_block(
            timestamp_ns=record_in["timestamp_ns"],
            sample_rate_hz=record_in["sample_rate_hz"],
            channel_id=record_in["channel_id"],
            source_id=record_in["source_id"],
            samples=spectrum,
        )


def main() -> None:
    p = argparse.ArgumentParser(description="wave-integration FFT operator")
    p.add_argument("--input-topic", default="raw.gen.chA")
    p.add_argument("--output-topic", default="spectrum.gen.chA")
    p.add_argument("--group-id", default="fft-gen-chA")
    p.add_argument("--broker", default="127.0.0.1:7912")
    args = p.parse_args()

    logging.basicConfig(level=logging.INFO, format="%(message)s")
    FFTOperator(
        input_topic=args.input_topic,
        output_topic=args.output_topic,
        group_id=args.group_id,
        broker_addr=args.broker,
    ).run()


if __name__ == "__main__":
    main()
