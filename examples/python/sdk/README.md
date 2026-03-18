# Python SDK demos

These demos use the installed `wavemq` Python package.

Install the SDK first:

```powershell
cd C:\Users\Pavel\GolandProjects\wave\wave-python-sdk
python -m pip install -e .
```

Layouts:

- `examples/python/sdk/tcp`
- `examples/python/sdk/http`

Both folders contain:

- `producer.py`
- `consumer.py`

The scripts use the same `WaveMQClient` API and differ only by transport and default broker address.
