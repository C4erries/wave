# Python SDK demos

These demos use the released `wave-python-sdk` package.

Install the SDK first:

```powershell
python -m pip install wave-python-sdk
```

Layouts:

- `examples/python/sdk/tcp`
- `examples/python/sdk/http`

Both folders contain a producer and a consumer script.

The scripts use the same `WaveMQClient` API and differ only by transport and default broker address.

The TCP folder also contains `simple` in-file producer/consumer/replay scripts for quick manual checks and offset reruns.

Use the TCP examples when you want to work with raw binary payloads.
Use the HTTP examples when you want a broker-address-only flow over the HTTP API.
