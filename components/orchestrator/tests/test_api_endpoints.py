"""Tests for new orchestrator methods: apply_graph, list_configs, get_config."""
from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import patch

import pytest
import yaml

from wave_orchestrator.orchestrator import ConfigError, Orchestrator


def _fake_build(node_type, node_kind, params, broker):
    return [sys.executable, "-c", "import time; time.sleep(30)"]


def _make_cfg(name: str = "custom", n_sources: int = 1, n_ops: int = 1) -> dict:
    sources = [
        {"name": f"src_{i}", "type": "synth", "params": {"topic": f"t{i}", "waveform": "sine", "freq": 1000, "rate": 10}}
        for i in range(n_sources)
    ]
    operators = [
        {"name": f"op_{i}", "type": "fft", "params": {"input_topic": f"t{i}", "output_topic": f"out{i}", "group_id": f"g{i}"}}
        for i in range(n_ops)
    ]
    return {"name": name, "broker": "127.0.0.1:7912", "sources": sources, "operators": operators}


@pytest.fixture
def orch(tmp_path):
    return Orchestrator(state_file=tmp_path / "state.json")


# ---------------------------------------------------------------------------
# apply_graph
# ---------------------------------------------------------------------------

def test_apply_graph_valid(orch):
    cfg = _make_cfg("test_apply", n_sources=1, n_ops=1)
    with patch("wave_orchestrator.orchestrator.build_command", side_effect=_fake_build):
        orch.apply_graph(cfg)
        status = orch.status()

    assert status["mode"] == "test_apply"
    assert len(status["processes"]) == 2
    assert all(p["alive"] for p in status["processes"])

    with patch("wave_orchestrator.orchestrator.build_command", side_effect=_fake_build):
        orch.stop()


def test_apply_graph_replaces_running(orch):
    cfg_a = _make_cfg("mode_a", n_sources=1, n_ops=0)
    cfg_b = _make_cfg("mode_b", n_sources=2, n_ops=1)
    with patch("wave_orchestrator.orchestrator.build_command", side_effect=_fake_build):
        orch.apply_graph(cfg_a)
        old_pids = {p["pid"] for p in orch.status()["processes"]}

        orch.apply_graph(cfg_b)
        status = orch.status()

    assert status["mode"] == "mode_b"
    assert len(status["processes"]) == 3
    assert old_pids.isdisjoint({p["pid"] for p in status["processes"]})

    with patch("wave_orchestrator.orchestrator.build_command", side_effect=_fake_build):
        orch.stop()


def test_apply_graph_invalid_missing_name(orch):
    cfg = {"broker": "127.0.0.1:7912", "sources": [], "operators": []}
    with pytest.raises(ConfigError, match="non-empty 'name'"):
        orch.apply_graph(cfg)


def test_apply_graph_invalid_node_missing_type(orch):
    cfg = {
        "name": "bad",
        "sources": [{"name": "src1"}],
        "operators": [],
    }
    with pytest.raises(ConfigError, match="non-empty 'type'"):
        orch.apply_graph(cfg)


def test_apply_graph_sources_only(orch):
    cfg = _make_cfg("src_only", n_sources=1, n_ops=0)
    with patch("wave_orchestrator.orchestrator.build_command", side_effect=_fake_build):
        orch.apply_graph(cfg)
        assert orch.status()["mode"] == "src_only"
        orch.stop()


# ---------------------------------------------------------------------------
# list_configs / get_config
# ---------------------------------------------------------------------------

def test_list_configs_returns_bundled(tmp_path):
    configs_dir = tmp_path / "configs"
    configs_dir.mkdir()
    (configs_dir / "alpha.yaml").write_text("name: alpha\nsources: []\noperators: []\n")
    (configs_dir / "beta.yaml").write_text("name: beta_mode\nsources: []\noperators: []\n")

    orch = Orchestrator(state_file=tmp_path / "state.json", configs_dir=configs_dir)
    result = orch.list_configs()

    names = {r["name"] for r in result}
    filenames = {r["filename"] for r in result}
    assert "alpha" in names
    assert "beta_mode" in names
    assert "alpha.yaml" in filenames
    assert "beta.yaml" in filenames


def test_list_configs_empty_dir(tmp_path):
    configs_dir = tmp_path / "empty_configs"
    configs_dir.mkdir()
    orch = Orchestrator(state_file=tmp_path / "state.json", configs_dir=configs_dir)
    assert orch.list_configs() == []


def test_list_configs_missing_dir(tmp_path):
    orch = Orchestrator(
        state_file=tmp_path / "state.json",
        configs_dir=tmp_path / "nonexistent",
    )
    assert orch.list_configs() == []


def test_get_config_found(tmp_path):
    configs_dir = tmp_path / "configs"
    configs_dir.mkdir()
    cfg_data = {
        "name": "my_mode",
        "broker": "127.0.0.1:7912",
        "sources": [{"name": "s1", "type": "synth", "params": {"topic": "t1", "waveform": "sine", "freq": 1000, "rate": 10}}],
        "operators": [],
    }
    (configs_dir / "my_mode.yaml").write_text(yaml.dump(cfg_data))

    orch = Orchestrator(state_file=tmp_path / "state.json", configs_dir=configs_dir)
    result = orch.get_config("my_mode")
    assert result["name"] == "my_mode"
    assert len(result["sources"]) == 1


def test_get_config_not_found(tmp_path):
    configs_dir = tmp_path / "configs"
    configs_dir.mkdir()
    orch = Orchestrator(state_file=tmp_path / "state.json", configs_dir=configs_dir)
    with pytest.raises(ConfigError, match="not found"):
        orch.get_config("nonexistent")
