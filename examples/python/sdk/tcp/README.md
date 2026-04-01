# Python SDK TCP demos

Install the SDK first:

```powershell
python -m pip install wave-python-sdk
```

Producer:

```powershell
python ./examples/python/sdk/tcp/producer.py --broker 127.0.0.1:7912
```

Consumer:

```powershell
python ./examples/python/sdk/tcp/consumer.py --broker 127.0.0.1:7912 --group demo-group
```

If the group has no committed offset yet, the consumer starts from the beginning.
If the group already exists, it resumes from the last committed offset plus one.
The producer example uses keyed routing through the normal high-level SDK API and prints the chosen partition.

The TCP example flow uses numeric payloads, so it is useful for offset replay and analysis reruns.

Simple in-file variants:

- `examples/python/sdk/tcp/simple/producer.py`
- `examples/python/sdk/tcp/simple/consumer.py`
- `examples/python/sdk/tcp/simple/replay_consumer.py`

These do not use command-line arguments. Change the constants at the top of the file and run them directly.
