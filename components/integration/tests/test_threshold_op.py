import numpy as np

from wave_integration import codec
from wave_integration.operators.threshold_op import ThresholdOperator

SAMPLE_RATE = 100_000
N = 4096


def _make_record(samples: np.ndarray) -> dict:
    return {
        "timestamp_ns": 500,
        "sample_rate_hz": SAMPLE_RATE,
        "channel_id": 0,
        "source_id": 1,
        "n_samples": len(samples),
        "samples": samples,
    }


def _make_op(threshold: float) -> ThresholdOperator:
    op = ThresholdOperator.__new__(ThresholdOperator)
    op.threshold_mv = threshold
    return op


def test_signal_above_threshold_emits_event():
    samples = np.full(N, 1.0, dtype=np.float32)  # peak=1.0, threshold=0.8
    op = _make_op(0.8)
    result_bytes = op.process(_make_record(samples))
    assert result_bytes is not None

    event = codec.decode_block(result_bytes)
    assert event["n_samples"] == 3
    assert abs(event["samples"][0] - 1.0) < 1e-5   # peak_value
    assert abs(event["samples"][1] - 0.8) < 1e-5   # threshold
    assert abs(event["samples"][2] - N) < 1e-5      # block_len


def test_signal_below_threshold_returns_none():
    samples = np.full(N, 0.5, dtype=np.float32)  # peak=0.5, threshold=0.8
    op = _make_op(0.8)
    assert op.process(_make_record(samples)) is None


def test_signal_equal_threshold_returns_none():
    # Use 0.75 (exact float32 = 3/4) so peak == threshold without rounding error
    samples = np.full(N, 0.75, dtype=np.float32)
    op = _make_op(0.75)
    assert op.process(_make_record(samples)) is None


def test_negative_peak_detected():
    samples = np.full(N, -1.5, dtype=np.float32)  # abs peak = 1.5
    op = _make_op(1.0)
    result = codec.decode_block(op.process(_make_record(samples)))
    assert abs(result["samples"][0] - 1.5) < 1e-4


def test_metadata_preserved():
    samples = np.full(N, 2.0, dtype=np.float32)
    record = {
        "timestamp_ns": 99999,
        "sample_rate_hz": SAMPLE_RATE,
        "channel_id": 5,
        "source_id": 2,
        "n_samples": N,
        "samples": samples,
    }
    op = _make_op(1.0)
    result = codec.decode_block(op.process(record))
    assert result["timestamp_ns"] == 99999
    assert result["channel_id"] == 5
    assert result["source_id"] == 2
