from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass

import numpy as np


@dataclass
class CaptureBlock:
    samples_mv: np.ndarray  # float32, mV
    sample_rate_hz: int
    timestamp_ns: int
    channel_id: int
    range_mv: int


@dataclass
class CaptureConfig:
    timebase: int = 8           # PicoScope timebase; 8 → ~12.5 MHz at 16-bit/1ch
    pre_samples: int = 5000
    post_samples: int = 95000
    range_mv: int = 1000        # ±1 V
    coupling: str = "AC"        # "AC" or "DC"
    channel: str = "A"
    bw_filter_20mhz: bool = False


class CaptureSource(ABC):
    """Contract: open() once → configure() once → capture_block() in loop → close()."""

    @abstractmethod
    def open(self) -> None: ...

    @abstractmethod
    def configure(self, config: CaptureConfig) -> None: ...

    @abstractmethod
    def capture_block(self) -> CaptureBlock: ...

    @abstractmethod
    def close(self) -> None: ...

    def __enter__(self) -> "CaptureSource":
        self.open()
        return self

    def __exit__(self, *exc) -> None:
        self.close()
