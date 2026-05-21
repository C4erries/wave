from __future__ import annotations

import argparse
import logging
from typing import Optional

import numpy as np

from wave_integration import codec
from wave_integration.operators.base import Operator


class FilterOperator(Operator):
    """Bandpass filter via numpy FFT (no scipy required).

    Zeroes all frequency bins outside [low_hz, high_hz] in the rfft domain,
    then irfft back to the time domain. Output has the same n_samples as input.
    """

    def __init__(self, *, low_hz: float, high_hz: float, **kwargs) -> None:
        super().__init__(**kwargs)
        self.low_hz = low_hz
        self.high_hz = high_hz

    def process(self, record_in: dict) -> Optional[bytes]:
        x: np.ndarray = record_in["samples"]
        n = len(x)
        if n == 0:
            return None

        sr = record_in["sample_rate_hz"]
        spectrum = np.fft.rfft(x)

        # bin k → frequency k * sr / n
        freqs = np.fft.rfftfreq(n, d=1.0 / sr)
        mask = (freqs >= self.low_hz) & (freqs <= self.high_hz)
        spectrum[~mask] = 0.0

        filtered = np.fft.irfft(spectrum, n=n).astype(np.float32)
        return codec.encode_block(
            timestamp_ns=record_in["timestamp_ns"],
            sample_rate_hz=sr,
            channel_id=record_in["channel_id"],
            source_id=record_in["source_id"],
            samples=filtered,
        )


def main() -> None:
    p = argparse.ArgumentParser(description="wave-integration bandpass filter operator")
    p.add_argument("--input-topic", default="raw.gen.chA")
    p.add_argument("--output-topic", default="filtered.gen.chA")
    p.add_argument("--group-id", default="filter-gen-chA")
    p.add_argument("--low-hz", type=float, default=500.0)
    p.add_argument("--high-hz", type=float, default=1500.0)
    p.add_argument("--broker", default="127.0.0.1:7912")
    args = p.parse_args()

    logging.basicConfig(level=logging.INFO, format="%(message)s")
    FilterOperator(
        input_topic=args.input_topic,
        output_topic=args.output_topic,
        group_id=args.group_id,
        broker_addr=args.broker,
        low_hz=args.low_hz,
        high_hz=args.high_hz,
    ).run()


if __name__ == "__main__":
    main()
