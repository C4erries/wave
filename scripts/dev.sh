#!/bin/bash
set -e
cd "$(dirname "$0")/.."

echo "==> Starting broker + UI (dev mode)..."
docker compose -f deploy/docker-compose.dev.yml up -d

echo ""
echo "Services started:"
echo "  UI:          http://localhost:8080"
echo "  Broker API:  http://localhost:8090"
echo ""
echo "Start Python components manually:"
echo "  source components/integration/.venv/bin/activate"
echo "  wave-gen --waveform=sine --freq=1000"
echo "  wave-fft --input-topic=raw.gen.chA --output-topic=spectrum.gen.chA"
echo ""
echo "Stop: docker compose -f deploy/docker-compose.dev.yml down"
