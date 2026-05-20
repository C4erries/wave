# wave-integration

Integration layer built on top of [wave-mq](../wave-mq). This package provides
the shared binary codec, signal producers, and oscilloscope adapters for the
streaming data pipeline.

## Components

| Module | CLI | Description |
|--------|-----|-------------|
| `codec.py` | — | Binary frame encode/decode (shared) |
| `producers/synth.py` | `wave-gen` | Synthetic signal generator |
| `producers/osc.py` | `wave-osc` | PicoScope 5000a adapter |
| `operators/fft_op.py` | `wave-fft` | FFT operator |

### Sources (`sources/`)

| Class | Description |
|-------|-------------|
| `SyntheticOscilloscopeSource` | Software imitation of PicoScope (no hardware required) |
| `PicoScopeSource` | Real PicoScope 5000a via `picosdk` driver |

## Setup

```bash
cd integration
python -m venv .venv
source .venv/bin/activate   # Windows: .venv\Scripts\activate
pip install -e ".[dev]"
```

**With PicoScope hardware support** (adds `picosdk` dependency):

```bash
pip install -e ".[picoscope,dev]"
```

> **Note:** TCP transport sends raw `bytes` directly — no base64 encoding needed.

## Running

Start the broker first:

```bash
docker compose -f ../docker-compose.single.yml up -d
```

Then run the generator:

```bash
wave-gen --waveform=sine --freq=1000
wave-gen --waveform=noise
wave-gen --waveform=chirp --chirp-f1=100 --chirp-f2=8000
wave-gen --waveform=sine --freq=440 --duration=30 --rate=5
```

Full option list: `wave-gen --help`

## Oscilloscope Adapter (wave-osc)

`wave-osc` captures blocks from a PicoScope 5000a (or its software imitation)
and publishes them to the broker.

### Software imitation (no hardware required)

```bash
# Publish 1500 Hz sine to raw.osc.chA
wave-osc --source=synth --synth-freq=1500 --topic=raw.osc.chA

# Dump to binary file without broker
wave-osc --source=synth --no-broker --output-mode=bin --output-file=/tmp/osc.bin --duration=5

# Test chirp, see what comes out
wave-osc --source=synth --synth-waveform=chirp --synth-freq=500 --no-broker \
         --output-mode=none --duration=3
```

### Real PicoScope 5000a

#### Linux driver installation

```bash
# Add Pico Technology apt repository
sudo bash -c 'wget -O- https://labs.picotech.com/Release.gpg.key \
  | gpg --dearmor > /usr/share/keyrings/picotech-archive-keyring.gpg'
sudo bash -c 'echo "deb [signed-by=/usr/share/keyrings/picotech-archive-keyring.gpg] \
  https://labs.picotech.com/rc/picoscope7/debian/ picoscope main" \
  > /etc/apt/sources.list.d/picoscope7.list'
sudo apt-get update && sudo apt-get install libps5000a
```

USB permissions (add your user to `plugdev`, then reload udev):

```bash
# /etc/udev/rules.d/95-pico.rules
SUBSYSTEM=="usb", ATTR{idVendor}=="0ce9", MODE="0666", GROUP="plugdev"
```

```bash
sudo udevadm control --reload-rules && sudo udevadm trigger
```

#### Capture

```bash
wave-osc --source=real --range-mv=200 --timebase=8 --topic=raw.osc.chA
```

Full option list: `wave-osc --help`

### Key parameters

| Flag | Default | Description |
|------|---------|-------------|
| `--timebase` | 8 | PicoScope timebase (8 → 12.5 MHz at 16-bit/1ch) |
| `--pre-samples` | 5000 | Samples before trigger |
| `--post-samples` | 95000 | Samples after trigger |
| `--range-mv` | 1000 | ADC range ±mV (10…20000) |
| `--coupling` | AC | AC or DC |
| `--rate-limit` | 5 | Max blocks/second (0 = unlimited) |
| `--duration` | -1 | Run seconds (-1 = infinite) |

## Operators

`wave-fft` subscribes to a raw topic and publishes amplitude spectra:

```bash
wave-fft --input-topic raw.gen.chA --output-topic spectrum.gen.chA

# Oscilloscope pipeline
wave-fft --input-topic raw.osc.chA --output-topic spectrum.osc.chA \
         --group-id fft-osc-chA
```

Full option list: `wave-fft --help`

## Tests

```bash
pytest tests/
```

## Binary frame format

All records in `raw.*` and `spectrum.*` topics use the same frame layout (big-endian):

| Offset | Size | Type    | Field          |
|--------|------|---------|----------------|
| 0      | 8    | int64   | timestamp_ns   |
| 8      | 4    | int32   | sample_rate_hz |
| 12     | 4    | int32   | n_samples      |
| 16     | 1    | int8    | channel_id     |
| 17     | 1    | int8    | source_id      |
| 18     | 2    | int16   | reserved (=0)  |
| 20     | 4×N  | float32 | samples        |

For `spectrum.*` topics, `n_samples = N/2 + 1` (rfft output length) and
`sample_rate_hz` carries the **original** sample rate so the UI can compute
`freq[k] = k × sample_rate_hz / (2 × (n_samples − 1))`.
