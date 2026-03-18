# Python SDK HTTP demos

Install the SDK first:

```powershell
cd C:\Users\Pavel\GolandProjects\wave\wave-python-sdk
python -m pip install -e .
```

Producer:

```powershell
python ./examples/python/sdk/http/producer.py --broker http://127.0.0.1:8090
```

Consumer:

```powershell
python ./examples/python/sdk/http/consumer.py --broker http://127.0.0.1:8090 --group demo-group
```
