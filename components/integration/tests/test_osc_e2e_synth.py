"""End-to-end test: wave-osc --source=synth → binary file → decode via codec."""
import struct
import subprocess
import sys
import time
from pathlib import Path

import numpy as np
import pytest

from wave_integration.codec import decode_block


def _parse_bin_file(path: Path) -> list[dict]:
    """Parse length-prefixed binary file written by wave-osc --output-mode=bin."""
    records = []
    with open(path, "rb") as f:
        while True:
            hdr = f.read(4)
            if not hdr:
                break
            if len(hdr) < 4:
                raise ValueError(f"Truncated length header at offset {f.tell()}")
            (length,) = struct.unpack(">I", hdr)
            frame = f.read(length)
            if len(frame) != length:
                raise ValueError(f"Truncated frame: expected {length}, got {len(frame)}")
            records.append(decode_block(frame))
    return records


def _run_wave_osc(args: list[str], timeout: float = 30.0) -> subprocess.CompletedProcess:
    cmd = [sys.executable, "-m", "wave_integration.producers.osc"] + args
    return subprocess.run(cmd, capture_output=True, text=True, timeout=timeout,
                          cwd=str(Path(__file__).parent.parent))


# ---------------------------------------------------------------------------

class TestOscSynthBinOutput:
    def test_produces_binary_file(self, tmp_path):
        out = tmp_path / "osc.bin"
        result = _run_wave_osc([
            "--source=synth",
            "--no-broker",
            "--output-mode=bin",
            f"--output-file={out}",
            "--duration=2",
            "--timebase=8",
            "--pre-samples=500",
            "--post-samples=500",
            "--synth-freq=2000",
            "--rate-limit=0",    # no extra rate-limit, rely on capture sleep
        ])
        assert result.returncode == 0, f"wave-osc failed:\n{result.stderr}"
        assert out.exists(), "output file not created"
        assert out.stat().st_size > 0, "output file is empty"

    def test_records_parseable(self, tmp_path):
        out = tmp_path / "osc.bin"
        _run_wave_osc([
            "--source=synth",
            "--no-broker",
            "--output-mode=bin",
            f"--output-file={out}",
            "--duration=2",
            "--timebase=8",
            "--pre-samples=500",
            "--post-samples=500",
            "--synth-freq=2000",
            "--rate-limit=0",
        ])
        records = _parse_bin_file(out)
        assert len(records) >= 1, "expected at least one record"

    def test_record_fields_correct(self, tmp_path):
        out = tmp_path / "osc.bin"
        _run_wave_osc([
            "--source=synth",
            "--no-broker",
            "--output-mode=bin",
            f"--output-file={out}",
            "--duration=2",
            "--timebase=8",
            "--pre-samples=300",
            "--post-samples=700",
            "--synth-freq=2000",
            "--rate-limit=0",
        ])
        records = _parse_bin_file(out)
        r = records[0]

        assert r["n_samples"] == 1000             # 300 + 700
        assert r["sample_rate_hz"] == 12_500_000  # timebase=8 formula
        assert r["channel_id"] == 0
        assert r["source_id"] == 1                # 1 = synthetic
        assert r["timestamp_ns"] > 0

    def test_samples_are_float32(self, tmp_path):
        out = tmp_path / "osc.bin"
        _run_wave_osc([
            "--source=synth",
            "--no-broker",
            "--output-mode=bin",
            f"--output-file={out}",
            "--duration=2",
            "--timebase=8",
            "--pre-samples=500",
            "--post-samples=500",
            "--synth-freq=2000",
            "--rate-limit=0",
        ])
        records = _parse_bin_file(out)
        assert records[0]["samples"].dtype == np.float32

    def test_timestamps_monotone(self, tmp_path):
        out = tmp_path / "osc.bin"
        _run_wave_osc([
            "--source=synth",
            "--no-broker",
            "--output-mode=bin",
            f"--output-file={out}",
            "--duration=2",
            "--timebase=8",
            "--pre-samples=500",
            "--post-samples=500",
            "--rate-limit=0",
        ])
        records = _parse_bin_file(out)
        if len(records) >= 2:
            ts = [r["timestamp_ns"] for r in records]
            assert all(ts[i] < ts[i + 1] for i in range(len(ts) - 1)), \
                "timestamps not monotonically increasing"

    def test_no_broker_flag_does_not_crash(self, tmp_path):
        result = _run_wave_osc([
            "--source=synth",
            "--no-broker",
            "--output-mode=none",
            "--duration=1",
            "--timebase=8",
            "--pre-samples=500",
            "--post-samples=500",
            "--rate-limit=0",
        ])
        assert result.returncode == 0, f"unexpected error:\n{result.stderr}"
        assert "stopped after" in result.stdout
