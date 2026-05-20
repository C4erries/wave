# Wave

Streaming oscilloscope data pipeline: PicoScope 5000a (or synthetic source) →
message broker → FFT operator → web UI with live charts.

## Quick start

```bash
git clone --recurse-submodules <repo-url>
cd wave
scripts/demo.sh
# Open http://localhost:8080/lab
```

## Components

| Path | Description |
|------|-------------|
| [components/integration/](components/integration/) | Python pipeline layer (codec, sources, producers, operators) |
| [components/osc-adapter-gui/](components/osc-adapter-gui/) | tkinter GUI adapter for the oscilloscope |
| [components/orchestrator/](components/orchestrator/) | Planned YAML orchestrator |
| [wave-mq/](wave-mq/) | Message broker (Go, submodule) |
| [wave-ui/](wave-ui/) | React web UI (submodule) |
| [wave-python-sdk/](wave-python-sdk/) | Python client SDK (submodule) |
| [OscilloscopeSupplyFIXed/](OscilloscopeSupplyFIXed/) | C# PicoScope 5000a application |
| [deploy/](deploy/) | Docker Compose files |
| [scripts/](scripts/) | demo.sh, dev.sh, stop.sh |
| [docs/](docs/) | Architecture documentation |

## CLI commands

```bash
# Activate integration venv first:
source components/integration/.venv/bin/activate

wave-gen --waveform=sine --freq=1000            # synthetic generator
wave-osc --source=synth --synth-freq=1500       # oscilloscope adapter
wave-fft --input-topic=raw.gen.chA              # FFT operator
wave-osc-gui                                    # GUI adapter (separate venv)
```

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for full data flow, topic table,
and binary frame format.

## Broker endpoints

| Endpoint | Description |
|----------|-------------|
| `localhost:7912` | Binary protocol (TCP) |
| `localhost:1883` | MQTT |
| `http://localhost:8090` | HTTP admin API |
| `http://localhost:8080` | Web UI |

## Scripts

```bash
scripts/demo.sh    # start full demo stack
scripts/dev.sh     # start broker + UI only
scripts/stop.sh    # stop everything
```

---

> Legacy: `docker-compose.single.yml` / `docker-compose.multi.yml` in root still work
> for running broker+UI without the Python pipeline.
