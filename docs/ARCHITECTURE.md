# Architecture

## Repository structure

```
wave/
├── components/                  Own code
│   ├── integration/             Python pipeline layer (codec, sources, operators, producers)
│   ├── osc-adapter-gui/         tkinter GUI adapter for PicoScope / synthetic source
│   └── orchestrator/            YAML orchestrator — planned, not yet implemented
├── wave-mq/                     Broker (submodule — Go)
├── wave-ui/                     Web UI (submodule — React/TypeScript)
├── wave-python-sdk/             Python client SDK (submodule)
├── OscilloscopeSupplyFIXed/     C# PicoScope 5000a application (legacy)
├── deploy/                      Docker Compose files and Dockerfile.integration
├── scripts/                     demo.sh, dev.sh, stop.sh
└── docs/                        This folder
```

## CLI commands (from components/integration)

| Command | Description |
|---------|-------------|
| `wave-gen` | Publish synthetic signal (sine/noise/chirp) to broker |
| `wave-osc` | Capture from PicoScope 5000a or software imitation → broker |
| `wave-fft` | Subscribe raw.* → compute FFT → publish spectrum.* |
| `wave-osc-gui` | tkinter GUI for wave-osc (from components/osc-adapter-gui) |

## Topics

| Topic | Producer | Consumer |
|-------|----------|----------|
| `raw.gen.chA` | wave-gen | wave-fft, wave-ui |
| `raw.osc.chA` | wave-osc / wave-osc-gui | wave-fft, wave-ui |
| `spectrum.gen.chA` | wave-fft | wave-ui |
| `spectrum.osc.chA` | wave-fft | wave-ui |

## Data flow

```
┌──────────────┐   raw.gen.chA    ┌──────────┐   spectrum.gen.chA   ┌─────────┐
│   wave-gen   │ ───────────────► │ wave-fft │ ──────────────────► │         │
└──────────────┘                  └──────────┘                      │ wave-ui │
                                                                     │  /lab   │
┌──────────────┐   raw.osc.chA    ┌──────────┐   spectrum.osc.chA  │         │
│   wave-osc   │ ───────────────► │ wave-fft │ ──────────────────► │         │
│ wave-osc-gui │                  └──────────┘                      └─────────┘
└──────────────┘
        │
        ▼ (all via wave-mq broker TCP :7912)
```

## Binary frame format (all raw.* and spectrum.* topics)

Big-endian layout:

| Offset | Size | Type    | Field          |
|--------|------|---------|----------------|
| 0      | 8    | int64   | timestamp_ns   |
| 8      | 4    | int32   | sample_rate_hz |
| 12     | 4    | int32   | n_samples      |
| 16     | 1    | int8    | channel_id     |
| 17     | 1    | int8    | source_id (1=synth, 2=PicoScope) |
| 18     | 2    | int16   | reserved (=0)  |
| 20     | 4×N  | float32 | samples (mV)   |

## Quick start

```bash
# 1. Clone with submodules
git clone --recurse-submodules <repo-url>

# 2. Start demo stack
scripts/demo.sh

# 3. Open browser
# http://localhost:8080/lab
```

See also: [scripts/demo.sh](../scripts/demo.sh), [deploy/](../deploy/).
