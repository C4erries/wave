# Python SDK HTTP demos

Install the SDK first:

```powershell
python -m pip install wave-python-sdk
```

Producer:

```powershell
python ./examples/python/sdk/http/producer.py --broker http://127.0.0.1:8090
```

Consumer:

```powershell
python ./examples/python/sdk/http/consumer.py --broker http://127.0.0.1:8090 --group demo-group
```

If the group has no committed offset yet, the consumer starts from the beginning.
If the group already exists, it resumes from the last committed offset plus one.
The producer example uses keyed routing through the normal high-level SDK API and prints the chosen partition.
