# wave examples

Examples are organized first by language, then by access method.

## Layout

- `examples/python/mbctl`
- `examples/python/http`
- `examples/python/sdk`
- `examples/matlab/mbctl`
- `examples/matlab/mqtt`

## Recommended preview order

1. Start the single-node preview stack:

   ```powershell
   docker compose -f .\docker-compose.single.yml up --build
   ```

2. Use the Python SDK or HTTP examples for broker-address-only flows.
3. Use `mbctl` if you want a native binary-protocol CLI client.
4. Use MATLAB `mqttclient` if you want the shortest MATLAB-native messaging demo.

## Build `mbctl`

The `mbctl`-based examples expect a local binary in `examples/bin/mbctl.exe`.

Build it once from the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\examples\build-mbctl.ps1
```

## Entry points

- Python examples: [python/README.md](python/README.md)
- MATLAB examples: [matlab/README.md](matlab/README.md)
- Broker blackbox suites: [../wave-mq/examples/README.md](../wave-mq/examples/README.md)

## Preview notes

- The root [README.md](../README.md) is the main preview entrypoint.
- Root [plan.md](../plan.md) lists the remaining work.
