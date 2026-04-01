# Python SDK TCP simple demos

These scripts are the same TCP SDK producer/consumer flow, but without command-line arguments.
The consumer path uses the small helper layer from `wave-python-sdk` so the script stays short.

Change the settings directly in the file:

- `BROKER`
- `TOPIC`
- `GROUP`
- `KEY`
- `VALUES`
- `PARTITION` in the consumer scripts
- poll/start settings in `consumer.py`
- replay settings in `replay_consumer.py`

Run:

```powershell
python ./examples/python/sdk/tcp/simple/consumer.py
python ./examples/python/sdk/tcp/simple/producer.py
python ./examples/python/sdk/tcp/simple/replay_consumer.py
```

`replay_consumer.py` is for re-reading numeric data from a chosen offset, for example to rerun analysis from an earlier point. By default it does not commit offsets.
The producer uses keyed routing and prints the broker-chosen partition for each message.
