#!/bin/bash
cd "$(dirname "$0")/.."

echo "==> Stopping orchestrator processes..."
# shellcheck disable=SC1091
if source components/integration/.venv/bin/activate 2>/dev/null && command -v wave-orchestrator &>/dev/null; then
  wave-orchestrator stop 2>/dev/null || true
fi

if [ -f /tmp/wave-orchestrator.pid ]; then
  kill "$(cat /tmp/wave-orchestrator.pid)" 2>/dev/null || true
  rm -f /tmp/wave-orchestrator.pid
fi

# Kill legacy PID files (old demo.sh style)
for f in /tmp/wave-gen.pid /tmp/wave-fft-gen.pid /tmp/wave-fft-osc.pid /tmp/wave-osc.pid; do
  [ -f "$f" ] && kill "$(cat "$f")" 2>/dev/null || true; rm -f "$f"
done

# Safety net: kill any orphaned wave-* processes
pkill -f "wave-gen|wave-fft|wave-stats|wave-filter|wave-threshold" 2>/dev/null || true

echo "==> Stopping Docker services..."
docker compose -f deploy/docker-compose.dev.yml down 2>/dev/null || true

echo "Stopped."
