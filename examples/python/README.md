# wave-mq HTTP demos

These are thin HTTP demos. They only need an `addr` and do not use `mbctl` or any SDK.

The producer and consumer both default to the same topic: `wave.http.demo`.
They also default to the same partition: `0`.
The consumer defaults to group `demo-group` and resumes from the last committed offset for that group/partition.
If no committed offset exists yet, it starts from `latest` by default, so it waits for new producer messages instead of replaying the whole backlog.

Producer:

```powershell
python ./examples/python/http_producer.py --addr http://127.0.0.1:8090 --topic wave.http.demo --partition 0
```

Consumer:

```powershell
python ./examples/python/http_consumer.py --addr http://127.0.0.1:8090 --topic wave.http.demo --group demo-group --partition 0
```

Manual flow:
1. Start the broker and expose its HTTP port.
2. Start the consumer in one terminal with the same topic, group, and partition.
3. Start the producer in another terminal with the same topic and partition.
4. The consumer reads the committed offset, fetches from the next offset on that partition, prints each record, and commits the processed offset back to the HTTP API.
5. By default the consumer keeps polling; use `--max-messages N` if you want it to stop after `N` records.

To read a backlog from the beginning instead of only new messages:

```powershell
python ./examples/python/http_consumer.py --addr http://127.0.0.1:8090 --topic wave.http.demo --group demo-group --partition 0 --start-from earliest
```

The `--addr` argument accepts either `http://host:port` or plain `host:port`.
