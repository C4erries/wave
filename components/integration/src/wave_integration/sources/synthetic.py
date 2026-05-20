from __future__ import annotations

import math
import time

import numpy as np

from wave_integration.sources.base import CaptureBlock, CaptureConfig, CaptureSource

# timebase → sample interval formula from PicoScope 5000a (16-bit, 1 ch, timebase >= 3)
# dt = (timebase - 3) / 62_500_000  seconds
_CLOCK_HZ = 62_500_000.0
_TIMEBASE_OFFSET = 3


def _timebase_to_sample_rate(timebase: int) -> int:
    if timebase < _TIMEBASE_OFFSET + 1:
        raise ValueError(
            f"timebase must be >= {_TIMEBASE_OFFSET + 1} for 16-bit mode, got {timebase}"
        )
    dt = (timebase - _TIMEBASE_OFFSET) / _CLOCK_HZ
    return int(round(1.0 / dt))


class SyntheticOscilloscopeSource(CaptureSource):
    """Hardware-free imitation of PicoScope 5000a block capture.

    Generates sine / noise / chirp at a realistic sample rate derived from the
    same timebase formula used by the real device.  Sleeps for the actual
    capture window duration so the publish loop runs at realistic pace.
    """

    def __init__(
        self,
        waveform: str = "sine",
        freq: float = 1000.0,
        amplitude_mv: float = 500.0,
        chirp_f1: float = 100.0,
        chirp_f2: float = 5000.0,
    ) -> None:
        self._waveform = waveform
        self._freq = freq
        self._amplitude_mv = amplitude_mv
        self._chirp_f1 = chirp_f1
        self._chirp_f2 = chirp_f2

        self._config: CaptureConfig | None = None
        self._sample_rate_hz: int = 0
        self._n_samples: int = 0
        self._phase_offset: int = 0  # continuous phase across blocks

    # ------------------------------------------------------------------
    def open(self) -> None:
        print("[synthetic] source opened")

    def configure(self, config: CaptureConfig) -> None:
        self._config = config
        self._sample_rate_hz = _timebase_to_sample_rate(config.timebase)
        self._n_samples = config.pre_samples + config.post_samples
        self._phase_offset = 0
        print(
            f"[synthetic] configured: timebase={config.timebase} "
            f"fs={self._sample_rate_hz} Hz  n={self._n_samples}  "
            f"range=±{config.range_mv} mV  waveform={self._waveform}"
        )

    def capture_block(self) -> CaptureBlock:
        assert self._config is not None, "call configure() before capture_block()"

        n = self._n_samples
        fs = self._sample_rate_hz
        t = (self._phase_offset + np.arange(n)) / fs

        if self._waveform == "sine":
            signal = self._amplitude_mv * np.sin(2.0 * math.pi * self._freq * t)
        elif self._waveform == "noise":
            signal = np.random.normal(0.0, self._amplitude_mv, n)
        else:  # chirp
            T = max(n / fs, 1e-6)
            phase = 2.0 * math.pi * (
                self._chirp_f1 * t
                + (self._chirp_f2 - self._chirp_f1) * t**2 / (2.0 * T)
            )
            signal = self._amplitude_mv * np.sin(phase)

        # Realistic noise floor: ~0.5% of full range
        noise_std = self._config.range_mv * 0.005
        signal = signal + np.random.normal(0.0, noise_std, n)
        samples = signal.astype(np.float32)

        # Simulate hardware capture time so the loop runs at device-realistic pace
        capture_seconds = n / fs
        time.sleep(capture_seconds)

        ts = time.time_ns()
        self._phase_offset += n

        return CaptureBlock(
            samples_mv=samples,
            sample_rate_hz=fs,
            timestamp_ns=ts,
            channel_id=0,
            range_mv=self._config.range_mv,
        )

    def close(self) -> None:
        pass
