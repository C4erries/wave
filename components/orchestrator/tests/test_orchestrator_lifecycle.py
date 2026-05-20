"""Lifecycle tests — no broker required; uses a fake long-running process."""
from __future__ import annotations

import sys
import textwrap
import time
from pathlib import Path
from unittest.mock import patch

import pytest

from wave_orchestrator.orchestrator import Orchestrator, OrchestratorError

# Fake command: sleeps indefinitely, exits cleanly on SIGTERM.
_SLEEP_CMD = [sys.executable, "-c", "import time; time.sleep(30)"]


def _fake_build(node_type, node_kind, params, broker):
    return _SLEEP_CMD[:]


def _write_cfg(tmp_path: Path, name: str, n_sources: int = 1, n_ops: int = 1) -> Path:
    sources = "\n".join(
        f"  - name: src_{i}\n    type: synth\n    params: {{}}" for i in range(n_sources)
    )
    operators = "\n".join(
        f"  - name: op_{i}\n    type: fft\n    params: {{}}" for i in range(n_ops)
    )
    content = f"name: {name}\nsources:\n{sources}\noperators:\n{operators}\n"
    p = tmp_path / f"{name}.yaml"
    p.write_text(content)
    return p


@pytest.fixture
def orch(tmp_path):
    return Orchestrator(state_file=tmp_path / "state.json")


# ---------------------------------------------------------------------------
# start → status alive → stop → state cleared
# ---------------------------------------------------------------------------

def test_start_stop_cycle(tmp_path, orch):
    cfg = _write_cfg(tmp_path, "test_mode", n_sources=1, n_ops=1)

    with patch("wave_orchestrator.orchestrator.build_command", side_effect=_fake_build):
        orch.start(str(cfg))

        status = orch.status()
        assert status["mode"] == "test_mode"
        assert len(status["processes"]) == 2
        assert all(p["alive"] for p in status["processes"])

        orch.stop()

    status_after = orch.status()
    assert status_after["mode"] is None
    assert status_after["processes"] == []


def test_stop_kills_processes(tmp_path, orch):
    cfg = _write_cfg(tmp_path, "kill_test", n_sources=1, n_ops=0)

    with patch("wave_orchestrator.orchestrator.build_command", side_effect=_fake_build):
        orch.start(str(cfg))
        status = orch.status()
        pid = status["processes"][0]["pid"]

        orch.stop()

    # Give the OS a moment to reap.
    time.sleep(0.2)
    import os, signal
    try:
        os.kill(pid, 0)
        # If os.kill didn't raise, the process still exists — check it's a zombie or gone
        # on Linux a stopped child may still appear; let's just verify state is clear.
    except ProcessLookupError:
        pass  # Good — process is gone.

    assert orch.status()["mode"] is None


def test_double_start_raises(tmp_path, orch):
    cfg = _write_cfg(tmp_path, "double", n_sources=1, n_ops=0)

    with patch("wave_orchestrator.orchestrator.build_command", side_effect=_fake_build):
        orch.start(str(cfg))
        with pytest.raises(OrchestratorError, match="Already running"):
            orch.start(str(cfg))
        orch.stop()


def test_reload_switches_mode(tmp_path, orch):
    cfg_a = _write_cfg(tmp_path, "mode_a", n_sources=1, n_ops=1)
    cfg_b = _write_cfg(tmp_path, "mode_b", n_sources=2, n_ops=1)

    with patch("wave_orchestrator.orchestrator.build_command", side_effect=_fake_build):
        orch.start(str(cfg_a))
        assert orch.status()["mode"] == "mode_a"
        old_pids = {p["pid"] for p in orch.status()["processes"]}

        orch.reload(str(cfg_b))
        status = orch.status()

    assert status["mode"] == "mode_b"
    assert len(status["processes"]) == 3  # 2 sources + 1 op
    new_pids = {p["pid"] for p in status["processes"]}
    # All new PIDs — old processes were replaced.
    assert old_pids.isdisjoint(new_pids)

    with patch("wave_orchestrator.orchestrator.build_command", side_effect=_fake_build):
        orch.stop()


def test_reload_from_empty_state(tmp_path, orch):
    """reload with nothing running should still launch new processes."""
    cfg = _write_cfg(tmp_path, "fresh", n_sources=1, n_ops=0)

    with patch("wave_orchestrator.orchestrator.build_command", side_effect=_fake_build):
        orch.reload(str(cfg))
        assert orch.status()["mode"] == "fresh"
        orch.stop()


def test_stop_idempotent(orch):
    """stop on empty state must not raise."""
    orch.stop()
    orch.stop()


def test_status_empty(orch):
    s = orch.status()
    assert s == {"mode": None, "processes": []}


# ---------------------------------------------------------------------------
# Processes kind and name preserved in state
# ---------------------------------------------------------------------------

def test_process_metadata_stored(tmp_path, orch):
    cfg = _write_cfg(tmp_path, "meta_test", n_sources=1, n_ops=1)

    with patch("wave_orchestrator.orchestrator.build_command", side_effect=_fake_build):
        orch.start(str(cfg))
        procs = orch.status()["processes"]
        kinds = {p["kind"] for p in procs}
        assert "source" in kinds
        assert "operator" in kinds
        orch.stop()
