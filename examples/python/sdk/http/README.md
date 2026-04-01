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
