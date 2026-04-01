# Python HTTP demos

These scripts use only the broker HTTP `addr` and do not use `mbctl` or the Python SDK.

The producer and consumer both default to the same topic: `wave.http.demo`.
The producer uses the normal keyed `/messages` route, and the consumer reads partition `0` of the default single-partition topic.
The consumer defaults to group `demo-group` and resumes from the last committed offset for that group/partition.
If no committed offset exists yet, it starts from the beginning by default and replays the backlog.

Producer:

```powershell
python .\examples\python\http\http_producer.py --addr http://127.0.0.1:8090 --topic wave.http.demo
```

Consumer:

```powershell
python .\examples\python\http\http_consumer.py --addr http://127.0.0.1:8090 --topic wave.http.demo --group demo-group --partition 0
```

Manual flow:
1. Start the broker and expose its HTTP port.
2. Start the consumer in one terminal with the same topic, group, and partition.
3. Start the producer in another terminal with the same topic and key.
4. The consumer reads the committed offset, fetches from the next offset on that partition, prints each record, and commits the processed offset back to the HTTP API.
5. By default the consumer keeps polling; use `--max-messages N` if you want it to stop after `N` records.

To tail only new messages instead of replaying the backlog:

```powershell
python .\examples\python\http\http_consumer.py --addr http://127.0.0.1:8090 --topic wave.http.demo --group demo-group --partition 0 --start-from latest
```

The `--addr` argument accepts either `http://host:port` or plain `host:port`.
