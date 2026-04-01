# Wave

Wave is a preview-stage message broker stack with a custom binary protocol, MQTT, HTTP admin APIs, consumer groups, and a React UI.

## Current shape

- Single-node broker is the supported preview path.
- Cluster/Raft and follower replication are experimental and should be treated as the next stage.
- Python SDK is released and supports TCP and HTTP transports.
- Examples are split by language and access method.

## Preview quick start

1. Start the single-node stack:

   ```powershell
   docker compose -f .\docker-compose.single.yml up --build
   ```

2. Open the UI:

   - `http://localhost:8080`

3. Use the broker endpoints directly if needed:

   - Binary protocol: `localhost:7912`
   - MQTT: `localhost:1883`
   - HTTP API: `http://localhost:8090`

4. Use the docs and examples that match your access path:

   - Broker: [wave-mq/README.md](wave-mq/README.md)
   - UI: [wave-ui/README.md](wave-ui/README.md)
   - Python SDK: [wave-python-sdk/README.md](wave-python-sdk/README.md)
   - Examples: [examples/README.md](examples/README.md)

## Repository layout

- `wave-mq` - broker implementation
- `wave-ui` - browser UI
- `wave-python-sdk` - Python client SDK
- `examples` - runnable demo scripts and smoke checks

## Notes

- Root [`plan.md`](plan.md) is the only source of truth for remaining work.
- `docker-compose.multi.yml` is the experimental multi-broker preview.
