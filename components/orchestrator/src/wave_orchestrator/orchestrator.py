from __future__ import annotations

import json
import logging
import os
import signal
import subprocess
import time
from pathlib import Path
from typing import Any

import yaml

from wave_orchestrator.registry import build_command

# Default state file lives inside the package source tree so the orchestrator
# can be run from any working directory.
_DEFAULT_RUNTIME_DIR = Path(__file__).resolve().parents[2] / ".runtime"

logger = logging.getLogger(__name__)


class ConfigError(ValueError):
    pass


class OrchestratorError(RuntimeError):
    pass


class Orchestrator:
    def __init__(self, state_file: Path | None = None) -> None:
        sf = state_file or (_DEFAULT_RUNTIME_DIR / "state.json")
        self._state_file = sf
        self._runtime_dir = sf.parent

    # ------------------------------------------------------------------
    # Config parsing
    # ------------------------------------------------------------------

    def load_config(self, path: str) -> dict:
        p = Path(path)
        if not p.exists():
            raise ConfigError(f"Config file not found: {path}")
        with open(p) as f:
            cfg = yaml.safe_load(f)
        if not isinstance(cfg, dict):
            raise ConfigError("Config must be a YAML mapping")
        self._validate(cfg)
        return cfg

    def _validate(self, cfg: dict) -> None:
        if not isinstance(cfg.get("name"), str) or not cfg["name"].strip():
            raise ConfigError("Config must have a non-empty 'name' field")
        for section in ("sources", "operators"):
            nodes = cfg.get(section, [])
            if nodes is None:
                nodes = []
            if not isinstance(nodes, list):
                raise ConfigError(f"'{section}' must be a list, got {type(nodes).__name__}")
            for node in nodes:
                if not isinstance(node, dict):
                    raise ConfigError(f"Each entry in '{section}' must be a mapping, got: {node!r}")
                if not isinstance(node.get("name"), str) or not node["name"].strip():
                    raise ConfigError(
                        f"Each entry in '{section}' must have a non-empty 'name' field. Got: {node!r}"
                    )
                if not isinstance(node.get("type"), str) or not node["type"].strip():
                    raise ConfigError(
                        f"Node '{node.get('name', '?')}' in '{section}' must have a non-empty 'type' field"
                    )
                params = node.get("params", {})
                if params is not None and not isinstance(params, dict):
                    raise ConfigError(
                        f"Node '{node['name']}': 'params' must be a mapping, got {type(params).__name__}"
                    )

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    def start(self, config_path: str) -> None:
        state = self._read_state()
        if state:
            raise OrchestratorError(
                f"Already running mode '{state.get('mode')}'. Use 'reload' or 'stop' first."
            )
        cfg = self.load_config(config_path)
        self._launch(cfg)

    def stop(self) -> None:
        state = self._read_state()
        if not state:
            logger.info("No running processes.")
            return
        for proc in state.get("processes", []):
            self._terminate(proc)
        self._write_state({})
        logger.info("All processes stopped, state cleared.")

    def reload(self, config_path: str) -> None:
        # Broker retains all topic data across reloads: operators are stateless
        # observers that track committed offsets on the broker, so stopping a
        # process does not affect topic contents.  The new operator set will
        # resume from the latest committed offset (or the beginning if the
        # group is new).
        old_state = self._read_state()
        old_mode = old_state.get("mode", "(none)") if old_state else "(none)"
        cfg = self.load_config(config_path)
        new_mode = cfg["name"]
        if old_state:
            for proc in old_state.get("processes", []):
                self._terminate(proc)
            self._write_state({})
        logger.info("Mode transition: %s → %s", old_mode, new_mode)
        self._launch(cfg)

    def status(self) -> dict[str, Any]:
        state = self._read_state()
        if not state:
            return {"mode": None, "processes": []}
        procs = []
        for proc in state.get("processes", []):
            pid = proc["pid"]
            try:
                os.kill(pid, 0)
                alive = True
            except ProcessLookupError:
                alive = False
            except PermissionError:
                alive = True
            procs.append({**proc, "alive": alive})
        return {"mode": state.get("mode"), "processes": procs}

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    def _launch(self, cfg: dict) -> None:
        broker = cfg.get("broker", "127.0.0.1:7912")
        procs = []
        # Sources first: each source calls ensure_topic on startup, which creates the
        # topic on the broker.  Operators must not start before their input topics exist
        # or they will crash with TopicNotFoundError.  The broker buffers all messages
        # so no data is lost if operators start slightly after sources.
        for node in cfg.get("sources") or []:
            cmd = build_command(node["type"], "source", node.get("params") or {}, broker)
            p = subprocess.Popen(cmd)
            logger.info("Started source '%s'  pid=%d  cmd=%s", node["name"], p.pid, cmd)
            procs.append(
                {"name": node["name"], "type": node["type"], "kind": "source",
                 "pid": p.pid, "cmd": cmd}
            )
        if cfg.get("sources") and cfg.get("operators"):
            # Give sources time to call ensure_topic before operators try to subscribe.
            time.sleep(1.5)
        for node in cfg.get("operators") or []:
            cmd = build_command(node["type"], "operator", node.get("params") or {}, broker)
            p = subprocess.Popen(cmd)
            logger.info("Started operator '%s'  pid=%d  cmd=%s", node["name"], p.pid, cmd)
            procs.append(
                {"name": node["name"], "type": node["type"], "kind": "operator",
                 "pid": p.pid, "cmd": cmd}
            )
        self._write_state({"mode": cfg["name"], "processes": procs})

    def _terminate(self, proc: dict) -> None:
        pid = proc["pid"]
        name = proc["name"]
        try:
            os.kill(pid, signal.SIGTERM)
            logger.info("SIGTERM → '%s' (pid=%d)", name, pid)
            deadline = time.monotonic() + 5.0
            while time.monotonic() < deadline:
                try:
                    os.kill(pid, 0)
                    time.sleep(0.1)
                except ProcessLookupError:
                    logger.debug("'%s' (pid=%d) exited cleanly.", name, pid)
                    return
            # Timed out — escalate.
            os.kill(pid, signal.SIGKILL)
            logger.warning("SIGKILL → '%s' (pid=%d) after 5 s timeout", name, pid)
        except ProcessLookupError:
            logger.debug("Process '%s' (pid=%d) was already gone.", name, pid)

    def _read_state(self) -> dict:
        if not self._state_file.exists():
            return {}
        try:
            with open(self._state_file) as f:
                return json.load(f)
        except (json.JSONDecodeError, OSError):
            return {}

    def _write_state(self, state: dict) -> None:
        self._runtime_dir.mkdir(parents=True, exist_ok=True)
        with open(self._state_file, "w") as f:
            json.dump(state, f, indent=2)
