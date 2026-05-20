#!/bin/bash
set -e
cd "$(dirname "$0")/.."

echo "==> Starting broker + UI..."
docker compose -f deploy/docker-compose.dev.yml up -d broker ui

echo "==> Waiting for broker to be ready..."
until curl -sf http://localhost:8090/api/broker > /dev/null 2>&1; do
  sleep 1
done
echo "    Broker ready."

echo "==> Activating Python venv..."
source components/integration/.venv/bin/activate

echo "==> Starting wave-gen (sine 1000 Hz)..."
wave-gen --waveform=sine --freq=1000 &
echo $! > /tmp/wave-gen.pid

echo "==> Starting wave-fft for gen pipeline..."
wave-fft --input-topic=raw.gen.chA --output-topic=spectrum.gen.chA \
         --group-id=fft-gen &
echo $! > /tmp/wave-fft-gen.pid

echo "==> Starting wave-fft for osc pipeline (waiting for wave-osc)..."
wave-fft --input-topic=raw.osc.chA --output-topic=spectrum.osc.chA \
         --group-id=fft-osc-chA &
echo $! > /tmp/wave-fft-osc.pid

echo ""
echo "Demo stack ready."
echo "  UI:          http://localhost:8080/lab"
echo "  Broker API:  http://localhost:8090"
echo ""
echo "To connect oscilloscope data:"
echo "  wave-osc --source=synth --synth-freq=1500 --topic=raw.osc.chA"
echo "  # or use: components/osc-adapter-gui/run.sh"
echo ""
echo "Run scripts/stop.sh to stop everything."
