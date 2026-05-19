# wave-integration

Integration layer built on top of [wave-mq](../wave-mq). This package provides
the shared binary codec and signal producers for the streaming data pipeline.

## What's here (E1)

- `src/wave_integration/codec.py` — binary frame encode/decode (shared by producers, operators, and tests)
- `src/wave_integration/producers/synth.py` — synthetic signal generator (`wave-gen` CLI)

## Setup

```bash
cd integration
python -m venv .venv
source .venv/bin/activate   # Windows: .venv\Scripts\activate
pip install -e ".[dev]"
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

## Operators

`wave-fft` subscribes to a raw topic and publishes amplitude spectra:

```bash
wave-fft --input-topic raw.gen.chA --output-topic spectrum.gen.chA
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
