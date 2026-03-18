# Python SDK TCP demos

Install the SDK first:

```powershell
cd C:/Users/Pavel/GolandProjects/wave/wave-python-sdk
python -m pip install -e .
```

Producer:

```powershell
python ./examples/python/sdk/tcp/producer.py --broker 127.0.0.1:7912
```

Consumer:

```powershell
python ./examples/python/sdk/tcp/consumer.py --broker 127.0.0.1:7912 --group demo-group
```
