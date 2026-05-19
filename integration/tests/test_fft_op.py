import numpy as np

from wave_integration import codec
from wave_integration.operators.fft_op import FFTOperator


def test_fft_of_known_sine_has_peak_at_expected_bin():
    sample_rate = 100_000
    freq = 1000.0
    n = 4096
    t = np.arange(n) / sample_rate
    samples = np.sin(2 * np.pi * freq * t).astype(np.float32)

    record_in = {
        "timestamp_ns": 0,
        "sample_rate_hz": sample_rate,
        "channel_id": 0,
        "source_id": 1,
        "n_samples": n,
        "samples": samples,
    }

    op = FFTOperator.__new__(FFTOperator)
    result_bytes = op.process(record_in)
    decoded = codec.decode_block(result_bytes)
    spectrum = decoded["samples"]

    # freq=1000 Hz, n=4096, sr=100000 → bin = 1000*4096/100000 = 40.96 → 41
    expected_bin = round(freq * n / sample_rate)
    peak_bin = int(np.argmax(spectrum))
    assert abs(peak_bin - expected_bin) <= 1, (
        f"peak at bin {peak_bin}, expected near {expected_bin}"
    )
    # синус амплитуды 1 на 4096 точках → пик ~2048
    assert spectrum[peak_bin] > 100


def test_output_frame_preserves_metadata():
    sample_rate = 100_000
    n = 4096
    samples = np.random.default_rng(0).standard_normal(n).astype(np.float32)
    record_in = {
        "timestamp_ns": 1_700_000_000_000_000_000,
        "sample_rate_hz": sample_rate,
        "channel_id": 3,
        "source_id": 7,
        "n_samples": n,
        "samples": samples,
    }

    op = FFTOperator.__new__(FFTOperator)
    decoded = codec.decode_block(op.process(record_in))

    assert decoded["timestamp_ns"] == 1_700_000_000_000_000_000
    assert decoded["sample_rate_hz"] == sample_rate  # ИСХОДНЫЙ rate
    assert decoded["channel_id"] == 3
    assert decoded["source_id"] == 7
    assert decoded["n_samples"] == n // 2 + 1  # rfft output length
