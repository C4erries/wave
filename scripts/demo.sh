#!/bin/bash
set -e
cd "$(dirname "$0")/.."

SCENARIO="${1:-full_pipeline}"
CONFIGS="components/orchestrator/configs"
VENV="components/integration/.venv"

if [ ! -f "$CONFIGS/$SCENARIO.yaml" ]; then
  echo "Error: scenario '$SCENARIO' not found in $CONFIGS/"
  echo "Available: $(ls $CONFIGS/*.yaml | xargs -n1 basename | sed 's/\.yaml//' | tr '\n' ' ')"
  exit 1
fi

echo "==> Starting broker + UI..."
docker compose -f deploy/docker-compose.dev.yml up -d broker ui

echo "==> Waiting for broker..."
until curl -sf http://localhost:8090/api/broker > /dev/null 2>&1; do
  sleep 1
done
echo "    Broker ready."

echo "==> Activating venv..."
# shellcheck disable=SC1090
source "$VENV/bin/activate"

# Kill any leftover orchestrator from previous run
if [ -f /tmp/wave-orchestrator.pid ]; then
  kill "$(cat /tmp/wave-orchestrator.pid)" 2>/dev/null || true
  rm -f /tmp/wave-orchestrator.pid
fi
wave-orchestrator stop 2>/dev/null || true

echo "==> Starting orchestrator (scenario: $SCENARIO)..."
wave-orchestrator start --config "$CONFIGS/$SCENARIO.yaml" --serve-api &
echo $! > /tmp/wave-orchestrator.pid
sleep 2

wave-orchestrator status

cat <<EOF

  UI:           http://localhost:8080
  Lab:          http://localhost:8080/lab
  Constructor:  http://localhost:8080/constructor
  Metrics:      http://localhost:8080/metrics
  Topics:       http://localhost:8080/topics
  Orchestrator: http://localhost:8099/status

  Scenario:     $SCENARIO
  Available:    $(ls $CONFIGS/*.yaml | xargs -n1 basename | sed 's/\.yaml//' | tr '\n' ' ')

  Switch live:  wave-orchestrator reload --config $CONFIGS/<name>.yaml
  Stop:         scripts/stop.sh
EOF
