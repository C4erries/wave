from __future__ import annotations

import struct

import numpy as np

# Header layout (big-endian): int64 timestamp_ns, int32 sample_rate_hz,
# int32 n_samples, int8 channel_id, int8 source_id, int16 reserved
_HEADER_FMT = ">qiibbh"
_HEADER_SIZE = struct.calcsize(_HEADER_FMT)  # 20 bytes


def encode_block(
    timestamp_ns: int,
    sample_rate_hz: int,
    channel_id: int,
    source_id: int,
    samples: np.ndarray,
) -> bytes:
    if samples.dtype != np.float32:
        raise TypeError(
            f"samples must have dtype float32, got {samples.dtype}"
        )
    header = struct.pack(
        _HEADER_FMT,
        timestamp_ns,
        sample_rate_hz,
        len(samples),
        channel_id,
        source_id,
        0,
    )
    return header + samples.astype(">f4").tobytes()


def decode_block(data: bytes) -> dict:
    if len(data) < _HEADER_SIZE:
        raise ValueError(
            f"data too short: need at least {_HEADER_SIZE} bytes, got {len(data)}"
        )
    timestamp_ns, sample_rate_hz, n_samples, channel_id, source_id, _ = (
        struct.unpack_from(_HEADER_FMT, data, 0)
    )
    expected = _HEADER_SIZE + n_samples * 4
    if len(data) != expected:
        raise ValueError(
            f"data length mismatch: header says {n_samples} samples "
            f"({expected} bytes total), got {len(data)}"
        )
    samples = np.frombuffer(data, dtype=">f4", offset=_HEADER_SIZE).astype("<f4")
    return {
        "timestamp_ns": timestamp_ns,
        "sample_rate_hz": sample_rate_hz,
        "n_samples": n_samples,
        "channel_id": channel_id,
        "source_id": source_id,
        "samples": samples,
    }
