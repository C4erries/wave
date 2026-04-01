# Python SDK TCP simple demos

These scripts are the same TCP SDK producer/consumer flow, but without command-line arguments.

Change the settings directly in the file:

- `BROKER`
- `TOPIC`
- `GROUP`
- `PARTITION`
- `VALUES`
- poll/start settings in `consumer.py`

Run:

```powershell
python ./examples/python/sdk/tcp/simple/consumer.py
python ./examples/python/sdk/tcp/simple/producer.py
```
