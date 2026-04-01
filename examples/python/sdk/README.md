# Python SDK demos

These demos use the released `wave-python-sdk` package.

Install the SDK first:

```powershell
python -m pip install wave-python-sdk
```

Layouts:

- `examples/python/sdk/tcp`
- `examples/python/sdk/http`

Both folders contain:

- `producer.py`
- `consumer.py`

The scripts use the same `WaveMQClient` API and differ only by transport and default broker address.

Inside `examples/python/sdk/tcp` there is also a `simple` folder with producer/consumer scripts that do not parse any input arguments. All settings are changed directly in code.
That `simple` folder also includes `replay_consumer.py` for re-reading numeric data from a chosen offset without committing by default.
