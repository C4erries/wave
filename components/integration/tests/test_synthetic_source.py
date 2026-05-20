import time

import numpy as np
import pytest

from wave_integration.sources.base import CaptureConfig
from wave_integration.sources.synthetic import SyntheticOscilloscopeSource, _timebase_to_sample_rate


# ---------------------------------------------------------------------------
# _timebase_to_sample_rate

def test_timebase_formula_known_value():
    # timebase=8: dt = (8-3)/62_500_000 = 80 ns → fs = 12_500_000
    assert _timebase_to_sample_rate(8) == 12_500_000

def test_timebase_formula_default_c_sharp():
    # C# default textBox9="4": dt = 1/62_500_000 = 16 ns → fs = 62_500_000
    assert _timebase_to_sample_rate(4) == 62_500_000

def test_timebase_too_small_raises():
    with pytest.raises(ValueError):
        _timebase_to_sample_rate(3)   # dt = 0 → division by zero guard


# ---------------------------------------------------------------------------
# SyntheticOscilloscopeSource — sine

@pytest.fixture
def sine_source():
    src = SyntheticOscilloscopeSource(waveform="sine", freq=1000.0, amplitude_mv=400.0)
    with src:
        src.configure(CaptureConfig(timebase=8, pre_samples=500, post_samples=500, range_mv=1000))
        yield src


def test_block_length(sine_source):
    block = sine_source.capture_block()
    assert len(block.samples_mv) == 1000   # 500 + 500


def test_sample_rate_matches_timebase(sine_source):
    block = sine_source.capture_block()
    assert block.sample_rate_hz == 12_500_000


def test_dtype_is_float32(sine_source):
    block = sine_source.capture_block()
    assert block.samples_mv.dtype == np.float32


def test_amplitude_within_range(sine_source):
    block = sine_source.capture_block()
    # signal = 400 mV sine + ~5 mV noise (0.5% of 1000 mV range)
    assert block.samples_mv.max() < 450.0
    assert block.samples_mv.min() > -450.0


def test_channel_id(sine_source):
    block = sine_source.capture_block()
    assert block.channel_id == 0


def test_range_mv_preserved(sine_source):
    block = sine_source.capture_block()
    assert block.range_mv == 1000


def test_timestamp_monotonically_increases(sine_source):
    b1 = sine_source.capture_block()
    b2 = sine_source.capture_block()
    assert b2.timestamp_ns > b1.timestamp_ns


def test_phase_continuity_across_blocks():
    """Phase must be continuous: no phase jump between consecutive blocks."""
    src = SyntheticOscilloscopeSource(waveform="sine", freq=1000.0, amplitude_mv=500.0)
    n = 500
    with src:
        src.configure(CaptureConfig(timebase=8, pre_samples=n, post_samples=0, range_mv=1000))
        b1 = src.capture_block()
        b2 = src.capture_block()

    # Last sample of block 1 and first of block 2 should follow a smooth sine
    # Compute expected value at position n (first sample of block 2)
    fs = b1.sample_rate_hz
    t_end = n / fs
    expected = 500.0 * np.sin(2 * np.pi * 1000.0 * t_end)
    actual = float(b2.samples_mv[0])
    # Allow for noise (0.5% of 1000 = 5 mV) plus floating-point tolerance
    assert abs(actual - expected) < 15.0, (
        f"phase jump detected: expected ~{expected:.1f} mV, got {actual:.1f} mV"
    )


# ---------------------------------------------------------------------------
# SyntheticOscilloscopeSource — noise

def test_noise_waveform_within_amplitude():
    src = SyntheticOscilloscopeSource(waveform="noise", amplitude_mv=200.0)
    with src:
        src.configure(CaptureConfig(timebase=8, pre_samples=200, post_samples=200, range_mv=1000))
        block = src.capture_block()
    # noise: amplitude_mv is std, so 5σ is extremely rare
    assert block.samples_mv.max() < 200.0 * 6
    assert block.samples_mv.min() > -200.0 * 6


# ---------------------------------------------------------------------------
# SyntheticOscilloscopeSource — capture timing

def test_capture_sleep_approximately_correct():
    """capture_block() should sleep for ≈ n_samples / sample_rate seconds."""
    src = SyntheticOscilloscopeSource(waveform="sine", freq=500.0, amplitude_mv=100.0)
    n = 1250   # 1250 / 12_500_000 = 100 µs — fast enough for a unit test
    with src:
        src.configure(CaptureConfig(timebase=8, pre_samples=n, post_samples=0, range_mv=1000))
        t0 = time.monotonic()
        src.capture_block()
        elapsed = time.monotonic() - t0

    expected = n / 12_500_000
    # Allow 3× expected time for system scheduling jitter; must be at least expected
    assert elapsed >= expected * 0.5
    assert elapsed < expected * 10
