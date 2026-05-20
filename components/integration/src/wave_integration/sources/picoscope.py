from __future__ import annotations

import ctypes
import time
from ctypes import byref, c_float, c_int16, c_int32, c_uint32

import numpy as np

from wave_integration.sources.base import CaptureBlock, CaptureConfig, CaptureSource

# picosdk is an optional dependency; import only in open() so this module
# can be imported on machines without the PicoScope driver installed.
_picosdk_available = False
try:
    import picosdk.ps5000a as _ps_mod
    _picosdk_available = True
except ImportError:
    pass

# ---------------------------------------------------------------------------
# PicoScope 5000a integer constants (from PS5000A Programmer's Guide)
# DeviceResolution enum
_PS5000A_DR_16BIT = 4

# Channel enum
_CHANNEL_A = 0
_CHANNEL_B = 1
_CHANNEL_C = 2
_CHANNEL_D = 3
_CHANNEL_EXTERNAL = 4

# Coupling enum
_COUPLING_AC = 0
_COUPLING_DC = 1

# Range enum  (matches C# Range enum indices)
_RANGE_MV_TO_ENUM: dict[int, int] = {
    10: 0, 20: 1, 50: 2, 100: 3, 200: 4, 500: 5,
    1000: 6, 2000: 7, 5000: 8, 10000: 9, 20000: 10,
}

# ThresholdDirection enum
_DIRECTION_RISING = 2

# RatioMode enum
_RATIO_MODE_NONE = 0

# BandwidthLimiter enum
_BW_20MHZ = 1

# Trigger constants matching C# code exactly
_TRIGGER_ENABLE: int = 1
_TRIGGER_THRESHOLD: int = 20000   # ADC counts (~61% of full scale)
_TRIGGER_DELAY: int = 0
_TRIGGER_AUTO_MS: int = 22222     # auto-trigger after ~22 s

# PICO_OK = 0
_PICO_OK = 0
_PICO_POWER_SUPPLY_NOT_CONNECTED = 0x119
_PICO_USB3_0_DEVICE_NON_USB3_0_PORT = 0x11E


class PicoScopeError(Exception):
    def __init__(self, status: int, fn: str) -> None:
        super().__init__(f"{fn} returned status 0x{status:X}")
        self.status = status
        self.fn = fn


def _check(status: int, fn: str) -> None:
    if status != _PICO_OK:
        raise PicoScopeError(status, fn)


class PicoScopeSource(CaptureSource):
    """Block-capture adapter for PicoScope 5000a.

    Reproduces the exact capture sequence from PS5000ABlockForm.cs:
    - 16-bit resolution, channel A only
    - AC coupling (configurable)
    - External trigger, rising, threshold=20000, auto=22222 ms
    - Polling via ps5000aIsReady (no callback)
    - ADC→mV: value = (bufMax[i] + bufMin[i]) * 0.5 * range_mV / 65536.0
    """

    def __init__(self) -> None:
        self._handle: c_int16 = c_int16(0)
        self._config: CaptureConfig | None = None
        self._sample_rate_hz: int = 0
        self._n_samples: int = 0
        self._buf_max: ctypes.Array | None = None
        self._buf_min: ctypes.Array | None = None
        self._ps = None  # picosdk module reference

    # ------------------------------------------------------------------
    def open(self) -> None:
        if not _picosdk_available:
            raise RuntimeError(
                "PicoScope SDK not available. "
                "Install with: pip install wave-integration[picoscope]"
            )
        import picosdk.ps5000a as ps
        self._ps = ps

        handle = c_int16()
        status = ps.ps5000aOpenUnit(byref(handle), None, _PS5000A_DR_16BIT)

        if status in (_PICO_POWER_SUPPLY_NOT_CONNECTED, _PICO_USB3_0_DEVICE_NON_USB3_0_PORT):
            status = ps.ps5000aChangePowerSource(handle, status)

        _check(status, "ps5000aOpenUnit")
        self._handle = handle
        print(f"[picoscope] opened, handle={handle.value}")

    def configure(self, config: CaptureConfig) -> None:
        ps = self._ps
        h = self._handle

        coupling = _COUPLING_AC if config.coupling.upper() == "AC" else _COUPLING_DC
        range_enum = _RANGE_MV_TO_ENUM.get(config.range_mv)
        if range_enum is None:
            valid = sorted(_RANGE_MV_TO_ENUM.keys())
            raise ValueError(f"range_mv={config.range_mv} not valid; choose from {valid}")

        # Channel A on, all others off
        _check(
            ps.ps5000aSetChannel(h, _CHANNEL_A, 1, coupling, range_enum, 0.0),
            "ps5000aSetChannel(A)",
        )
        for ch in (_CHANNEL_B, _CHANNEL_C, _CHANNEL_D):
            ps.ps5000aSetChannel(h, ch, 0, _COUPLING_DC, 0, 0.0)

        if config.bw_filter_20mhz:
            _check(
                ps.ps5000aSetBandwidthFilter(h, _CHANNEL_A, _BW_20MHZ),
                "ps5000aSetBandwidthFilter",
            )

        # External trigger, rising, auto 22222 ms — identical to C#
        _check(
            ps.ps5000aSetSimpleTrigger(
                h,
                _TRIGGER_ENABLE,
                _CHANNEL_EXTERNAL,
                _TRIGGER_THRESHOLD,
                _DIRECTION_RISING,
                _TRIGGER_DELAY,
                _TRIGGER_AUTO_MS,
            ),
            "ps5000aSetSimpleTrigger",
        )

        total = config.pre_samples + config.post_samples

        # Determine actual sample rate via GetTimebase2
        interval_ns = c_float()
        max_samples = c_int32()
        _check(
            ps.ps5000aGetTimebase2(
                h, config.timebase, total, byref(interval_ns), byref(max_samples), 0
            ),
            "ps5000aGetTimebase2",
        )
        self._sample_rate_hz = int(round(1e9 / interval_ns.value))

        # Pre-allocate ctypes buffers (must stay alive across RunBlock/GetValues)
        self._buf_max = (c_int16 * total)()
        self._buf_min = (c_int16 * total)()
        _check(
            ps.ps5000aSetDataBuffers(
                h, _CHANNEL_A, self._buf_max, self._buf_min, total, 0, _RATIO_MODE_NONE
            ),
            "ps5000aSetDataBuffers",
        )

        self._config = config
        self._n_samples = total
        print(
            f"[picoscope] configured: timebase={config.timebase} "
            f"fs={self._sample_rate_hz} Hz  n={total}  "
            f"range={config.range_mv} mV  coupling={config.coupling}"
        )

    def capture_block(self) -> CaptureBlock:
        assert self._config is not None, "call configure() before capture_block()"
        ps = self._ps
        h = self._handle
        cfg = self._config
        total = self._n_samples

        time_indisposed = c_int32()
        _check(
            ps.ps5000aRunBlock(
                h,
                cfg.pre_samples,
                cfg.post_samples,
                cfg.timebase,
                byref(time_indisposed),
                0,
                None,   # no callback — use polling
                None,
            ),
            "ps5000aRunBlock",
        )

        # Poll until ready
        ready = c_int16(0)
        while True:
            ps.ps5000aIsReady(h, byref(ready))
            if ready.value:
                break
            time.sleep(0.001)

        # Retrieve samples
        n_returned = c_uint32(total)
        overflow = c_int16()
        _check(
            ps.ps5000aGetValues(
                h, 0, byref(n_returned), 1, _RATIO_MODE_NONE, 0, byref(overflow)
            ),
            "ps5000aGetValues",
        )
        ps.ps5000aStop(h)

        # ADC → mV: exact replica of C# continuous-publish formula
        # masA[i] = bufMax[i] + bufMin[i]  (both identical with RatioMode.None)
        # arr_mV[i] = masA[i] * 0.5 * range_mV / 65536.0
        max_arr = np.ctypeslib.as_array(self._buf_max)
        min_arr = np.ctypeslib.as_array(self._buf_min)
        summed = max_arr.astype(np.int64) + min_arr.astype(np.int64)
        mult = 0.5 * cfg.range_mv / 65536.0
        samples_mv = (summed * mult).astype(np.float32)

        ts = time.time_ns()
        return CaptureBlock(
            samples_mv=samples_mv,
            sample_rate_hz=self._sample_rate_hz,
            timestamp_ns=ts,
            channel_id=0,
            range_mv=cfg.range_mv,
        )

    def close(self) -> None:
        if self._ps is not None and self._handle.value != 0:
            self._ps.ps5000aCloseUnit(self._handle)
            self._handle = c_int16(0)
            print("[picoscope] closed")
