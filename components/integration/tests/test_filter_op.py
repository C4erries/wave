import numpy as np

from wave_integration import codec
from wave_integration.operators.filter_op import FilterOperator

SAMPLE_RATE = 100_000
N = 4096


def _make_record(samples: np.ndarray) -> dict:
    return {
        "timestamp_ns": 0,
        "sample_rate_hz": SAMPLE_RATE,
        "channel_id": 0,
        "source_id": 1,
        "n_samples": len(samples),
        "samples": samples,
    }


def _make_filter(low: float, high: float) -> FilterOperator:
    op = FilterOperator.__new__(FilterOperator)
    op.low_hz = low
    op.high_hz = high
    return op


def test_inband_component_passes_outband_suppressed():
    t = np.arange(N) / SAMPLE_RATE
    inband = np.sin(2 * np.pi * 800 * t)   # 800 Hz — inside 500-1500
    outband = np.sin(2 * np.pi * 5000 * t)  # 5000 Hz — outside
    samples = (inband + outband).astype(np.float32)

    op = _make_filter(500.0, 1500.0)
    result = codec.decode_block(op.process(_make_record(samples)))
    filtered = result["samples"].astype(np.float64)

    spectrum = np.abs(np.fft.rfft(filtered))
    bin_800 = round(800 * N / SAMPLE_RATE)
    bin_5000 = round(5000 * N / SAMPLE_RATE)

    assert spectrum[bin_5000] < spectrum[bin_800] * 0.01, (
        f"5kHz component not suppressed: amp_5k={spectrum[bin_5000]:.1f}, "
        f"amp_800={spectrum[bin_800]:.1f}"
    )


def test_output_length_matches_input():
    samples = np.random.default_rng(42).standard_normal(N).astype(np.float32)
    op = _make_filter(100.0, 2000.0)
    result = codec.decode_block(op.process(_make_record(samples)))
    assert result["n_samples"] == N


def test_non_standard_length_does_not_crash():
    # operator must survive n_samples != 4096
    for n in (256, 512, 1023, 8192):
        t = np.arange(n) / SAMPLE_RATE
        samples = np.sin(2 * np.pi * 1000 * t).astype(np.float32)
        op = _make_filter(500.0, 1500.0)
        result = codec.decode_block(op.process(_make_record(samples)))
        assert result["n_samples"] == n


def test_metadata_preserved():
    samples = np.ones(N, dtype=np.float32)
    record = {
        "timestamp_ns": 12345,
        "sample_rate_hz": SAMPLE_RATE,
        "channel_id": 3,
        "source_id": 7,
        "n_samples": N,
        "samples": samples,
    }
    op = _make_filter(0.0, 5000.0)
    result = codec.decode_block(op.process(record))
    assert result["timestamp_ns"] == 12345
    assert result["channel_id"] == 3
    assert result["source_id"] == 7
