"""Tests for config parsing and registry command building."""
from __future__ import annotations

import textwrap
from pathlib import Path

import pytest
import yaml

from wave_orchestrator.orchestrator import ConfigError, Orchestrator
from wave_orchestrator.registry import build_command


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _write_yaml(tmp_path: Path, content: str) -> Path:
    p = tmp_path / "cfg.yaml"
    p.write_text(textwrap.dedent(content))
    return p


def _orch(tmp_path: Path) -> Orchestrator:
    return Orchestrator(state_file=tmp_path / "state.json")


# ---------------------------------------------------------------------------
# Valid config
# ---------------------------------------------------------------------------

def test_valid_config_parses(tmp_path):
    cfg_path = _write_yaml(tmp_path, """
        name: test_mode
        broker: "127.0.0.1:7912"
        sources:
          - name: gen_chA
            type: synth
            params:
              waveform: sine
              freq: 1000
              topic: raw.gen.chA
        operators:
          - name: fft_chA
            type: fft
            params:
              input_topic: raw.gen.chA
              output_topic: spectrum.gen.chA
              group_id: fft-gen-chA
    """)
    orch = _orch(tmp_path)
    cfg = orch.load_config(str(cfg_path))

    assert cfg["name"] == "test_mode"
    assert cfg["broker"] == "127.0.0.1:7912"
    assert len(cfg["sources"]) == 1
    assert cfg["sources"][0]["name"] == "gen_chA"
    assert cfg["sources"][0]["type"] == "synth"
    assert cfg["sources"][0]["params"]["freq"] == 1000
    assert len(cfg["operators"]) == 1
    assert cfg["operators"][0]["type"] == "fft"


def test_config_without_operators_is_valid(tmp_path):
    cfg_path = _write_yaml(tmp_path, """
        name: raw_only
        sources:
          - name: gen
            type: synth
            params: {}
    """)
    cfg = _orch(tmp_path).load_config(str(cfg_path))
    assert cfg["name"] == "raw_only"
    assert cfg.get("operators", []) == []


def test_config_without_sources_is_valid(tmp_path):
    cfg_path = _write_yaml(tmp_path, """
        name: ops_only
        operators:
          - name: fft1
            type: fft
            params: {}
    """)
    cfg = _orch(tmp_path).load_config(str(cfg_path))
    assert cfg["name"] == "ops_only"


# ---------------------------------------------------------------------------
# Validation errors
# ---------------------------------------------------------------------------

def test_missing_name_raises(tmp_path):
    cfg_path = _write_yaml(tmp_path, """
        sources: []
        operators: []
    """)
    with pytest.raises(ConfigError, match="name"):
        _orch(tmp_path).load_config(str(cfg_path))


def test_empty_name_raises(tmp_path):
    cfg_path = _write_yaml(tmp_path, """
        name: "   "
        sources: []
    """)
    with pytest.raises(ConfigError, match="name"):
        _orch(tmp_path).load_config(str(cfg_path))


def test_node_missing_type_raises(tmp_path):
    cfg_path = _write_yaml(tmp_path, """
        name: bad
        operators:
          - name: fft1
            params: {}
    """)
    with pytest.raises(ConfigError, match="type"):
        _orch(tmp_path).load_config(str(cfg_path))


def test_node_missing_name_raises(tmp_path):
    cfg_path = _write_yaml(tmp_path, """
        name: bad
        sources:
          - type: synth
            params: {}
    """)
    with pytest.raises(ConfigError, match="name"):
        _orch(tmp_path).load_config(str(cfg_path))


def test_nonexistent_file_raises(tmp_path):
    with pytest.raises(ConfigError, match="not found"):
        _orch(tmp_path).load_config(str(tmp_path / "missing.yaml"))


# ---------------------------------------------------------------------------
# build_command
# ---------------------------------------------------------------------------

def test_build_command_fft():
    cmd = build_command(
        "fft", "operator",
        {"input_topic": "raw.gen.chA", "output_topic": "spectrum.gen.chA", "group_id": "g1"},
        "127.0.0.1:7912",
    )
    assert cmd[0] == "wave-fft"
    assert "--input-topic" in cmd
    assert "raw.gen.chA" in cmd
    assert "--output-topic" in cmd
    assert "spectrum.gen.chA" in cmd
    assert "--group-id" in cmd
    assert "g1" in cmd
    assert "--broker" in cmd
    assert "127.0.0.1:7912" in cmd


def test_build_command_synth():
    cmd = build_command(
        "synth", "source",
        {"waveform": "sine", "freq": 1000, "topic": "raw.gen.chA"},
        "127.0.0.1:7912",
    )
    assert cmd[0] == "wave-gen"
    assert "--waveform" in cmd
    assert "--freq" in cmd
    assert "--broker" in cmd


def test_build_command_unknown_type_raises():
    with pytest.raises(ValueError, match="Unknown source type"):
        build_command("unknown_src", "source", {}, "127.0.0.1:7912")


def test_build_command_unknown_kind_raises():
    with pytest.raises(ValueError, match="Unknown node_kind"):
        build_command("fft", "transformer", {}, "127.0.0.1:7912")


def test_build_command_underscore_to_hyphen():
    cmd = build_command("fft", "operator", {"input_topic": "t1", "output_topic": "t2"}, "b:7912")
    assert "--input-topic" in cmd
    assert "--output-topic" in cmd
    assert "input_topic" not in " ".join(cmd)


def test_build_command_numeric_value_as_string():
    cmd = build_command("synth", "source", {"freq": 1000}, "b:7912")
    idx = cmd.index("--freq")
    assert cmd[idx + 1] == "1000"
