# wave-orchestrator

Declarative YAML orchestrator for the Wave integration pipeline. Manages
`wave-gen`, `wave-osc`, and `wave-fft` processes as a named **mode** defined
in a graph config file.

## Prerequisites

`wave-orchestrator` launches `wave-gen`, `wave-osc`, and `wave-fft` as
subprocesses. Those entry-points must be in `PATH`, meaning the
`wave-integration` venv must be active (or the orchestrator must be installed
into the same venv):

```bash
# Option A: install orchestrator into the integration venv (recommended)
source components/integration/.venv/bin/activate
pip install -e components/orchestrator/

# Option B: activate integration venv, then call via full path
source components/integration/.venv/bin/activate
components/orchestrator/.venv/bin/wave-orchestrator start --config ...
```

## Installation (standalone venv)

```bash
cd components/orchestrator
python3 -m venv .venv
.venv/bin/pip install -e ".[dev]"
```

## CLI

```bash
# Start a mode (processes run in background; orchestrator exits)
wave-orchestrator start --config configs/fft_only.yaml

# Start + keep HTTP API alive (Ctrl-C stops everything)
wave-orchestrator start --config configs/fft_only.yaml --serve-api --api-port 8099

# Check running processes
wave-orchestrator status

# Switch to a different mode (stop current + start new)
wave-orchestrator reload --config configs/multi_signal.yaml

# Stop everything
wave-orchestrator stop
```

## Bundled configs

| File | Description |
|------|-------------|
| `configs/fft_only.yaml` | Sine 1 kHz → raw.gen.chA → FFT → spectrum.gen.chA |
| `configs/multi_signal.yaml` | Sine chA + Chirp chB, FFT for each channel |
| `configs/raw_only.yaml` | Sine only, no FFT (raw stream) |

## HTTP API

Start with `--serve-api` (default port 8099):

```bash
# Current mode and liveness
curl http://localhost:8099/status

# Reload without restarting the server
curl -X POST http://localhost:8099/reload \
  -H "Content-Type: application/json" \
  -d '{"config": "configs/multi_signal.yaml"}'
```

## Runtime state

Process PIDs and config are stored in `.runtime/state.json` (relative to the
package source tree). Delete this file if a crash left stale state.

## Tests

```bash
cd components/orchestrator
.venv/bin/pytest tests/ -v
# No broker required — lifecycle tests use a fake sleep process.
```

## Architecture notes

- Processes are launched via `subprocess.Popen` — one OS process per node.
  Crash isolation: a failing source does not affect operators.
- Broker retains topic data on reload. Operators are stateless consumers that
  track their offset on the broker; stopping and restarting an operator does
  not lose messages.
- Sources are started before operators so that `ensure_topic` has run before
  operators try to subscribe.
- No healthcheck / auto-restart — out of scope for now. Listed as a future direction.
