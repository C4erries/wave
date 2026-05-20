import numpy as np
import pytest

from wave_integration.codec import decode_block, encode_block


def test_round_trip():
    rng = np.random.default_rng(42)
    samples = rng.standard_normal(128).astype(np.float32)

    data = encode_block(
        timestamp_ns=1_700_000_000_000_000_000,
        sample_rate_hz=100_000,
        channel_id=2,
        source_id=7,
        samples=samples,
    )
    result = decode_block(data)

    assert result["timestamp_ns"] == 1_700_000_000_000_000_000
    assert result["sample_rate_hz"] == 100_000
    assert result["n_samples"] == 128
    assert result["channel_id"] == 2
    assert result["source_id"] == 7
    assert np.array_equal(result["samples"], samples)


def test_decode_rejects_short_data():
    with pytest.raises(ValueError):
        decode_block(b"\x00" * 10)


def test_encode_rejects_wrong_dtype():
    samples = np.array([1.0, 2.0], dtype=np.float64)
    with pytest.raises(TypeError):
        encode_block(0, 100_000, 0, 0, samples)
