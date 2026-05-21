import numpy as np
import pytest

from wave_integration import codec
from wave_integration.operators.stats_op import StatsOperator

SAMPLE_RATE = 100_000
N = 4096


def _make_record(samples: np.ndarray) -> dict:
    return {
        "timestamp_ns": 1_000_000_000,
        "sample_rate_hz": SAMPLE_RATE,
        "channel_id": 0,
        "source_id": 1,
        "n_samples": len(samples),
        "samples": samples,
    }


def test_rms_and_peak_of_sine():
    amplitude = 1.0
    t = np.arange(N) / SAMPLE_RATE
    samples = (amplitude * np.sin(2 * np.pi * 1000 * t)).astype(np.float32)

    op = StatsOperator.__new__(StatsOperator)
    result = codec.decode_block(op.process(_make_record(samples)))

    stats = result["samples"]
    assert result["n_samples"] == 4

    rms = stats[0]
    peak = stats[1]
    expected_rms = amplitude / np.sqrt(2)
    assert abs(rms - expected_rms) < 0.01, f"rms={rms:.4f}, expected≈{expected_rms:.4f}"
    assert abs(peak - amplitude) < 0.01, f"peak={peak:.4f}, expected≈{amplitude:.4f}"


def test_peak_to_peak_of_sine():
    amplitude = 2.0
    t = np.arange(N) / SAMPLE_RATE
    samples = (amplitude * np.sin(2 * np.pi * 500 * t)).astype(np.float32)

    op = StatsOperator.__new__(StatsOperator)
    result = codec.decode_block(op.process(_make_record(samples)))

    peak_to_peak = result["samples"][2]
    # p2p of sine ≈ 2 * amplitude (within a few samples of full cycle)
    assert abs(peak_to_peak - 2 * amplitude) < 0.05


def test_mean_of_dc_signal():
    samples = np.full(N, 0.5, dtype=np.float32)

    op = StatsOperator.__new__(StatsOperator)
    result = codec.decode_block(op.process(_make_record(samples)))

    mean = result["samples"][3]
    assert abs(mean - 0.5) < 1e-5


def test_metadata_preserved():
    samples = np.ones(N, dtype=np.float32)
    record = {
        "timestamp_ns": 9_999_999_999,
        "sample_rate_hz": SAMPLE_RATE,
        "channel_id": 2,
        "source_id": 5,
        "n_samples": N,
        "samples": samples,
    }

    op = StatsOperator.__new__(StatsOperator)
    result = codec.decode_block(op.process(record))

    assert result["timestamp_ns"] == 9_999_999_999
    assert result["channel_id"] == 2
    assert result["source_id"] == 5
