#!/bin/bash
cd "$(dirname "$0")/.."

echo "==> Stopping Python processes..."
for pidfile in /tmp/wave-gen.pid /tmp/wave-fft-gen.pid /tmp/wave-fft-osc.pid /tmp/wave-osc.pid; do
  if [ -f "$pidfile" ]; then
    pid=$(cat "$pidfile")
    kill "$pid" 2>/dev/null && echo "    killed pid $pid ($(basename $pidfile .pid))" || true
    rm -f "$pidfile"
  fi
done

echo "==> Stopping Docker services..."
docker compose -f deploy/docker-compose.dev.yml down 2>/dev/null || true

echo "Done."
