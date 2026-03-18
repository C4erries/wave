# wave examples for `wave-mq`

These examples include `mbctl` binary-protocol demos and thin HTTP producer/consumer demos under `examples/python`.

Build `mbctl` once:

```powershell
cd C:\Users\Pavel\GolandProjects\wave
powershell -ExecutionPolicy Bypass -File .\examples\build-mbctl.ps1
```

Start a single broker:

```powershell
docker compose -f .\docker-compose.single.yml up --build broker
```

Direct `mbctl` commands:

```powershell
.\examples\bin\mbctl.exe ping
.\examples\bin\mbctl.exe create-topic -topic demo.binary -partitions 1 -replication-factor 1
.\examples\bin\mbctl.exe produce -topic demo.binary -partition 0 -value hello
.\examples\bin\mbctl.exe fetch -topic demo.binary -partition 0 -offset 0
.\examples\bin\mbctl.exe metadata -topic demo.binary -json
.\examples\bin\mbctl.exe list-offsets -topic demo.binary -partition 0 -json
```

Python demo wrapper:

```powershell
python .\examples\python\wavemq_binary_demo.py
```

MATLAB demo wrapper:

Open and run:

```text
examples/matlab/wavemq_binary_demo.m
```

Both wrappers call `examples/bin/mbctl.exe` with `-json` and only orchestrate the demo flow:
- ping broker
- create topic
- inspect metadata
- produce records
- list offsets
- fetch records
- commit offset
- fetch committed offset

HTTP producer/consumer demo:

```text
examples/python/README.md
```

These scripts use only the broker HTTP `addr`, do not use `mbctl`, and show a producer/consumer pair talking through `wave-mq`.
