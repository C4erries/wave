# Fixes Plan

## Scope

- `wave-mq`
- `wave-python-sdk`
- `examples`

## Findings

### P0

1. Ack происходит до replication/ISR durability.
   - Project: `wave-mq`
   - Files:
     - [broker.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/broker/broker.go:870)
     - [broker.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/broker/broker.go:882)
     - [broker.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/broker/broker.go:887)
     - [broker.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/broker/broker.go:891)
     - [controller.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/controller/controller.go:206)
   - Result:
     - лидер подтверждает запись после локального append и сразу делает её читаемой через local high watermark;
     - replication/ISR progress в path подтверждения записи не участвует.
   - Impact:
     - при падении лидера до catch-up follower возможна потеря уже acked и уже читаемых сообщений.

### P1

2. После failover/restart follower не отрезает дивергентный локальный хвост.
   - Project: `wave-mq`
   - Files:
     - [wal_sink.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/replication/wal_sink.go:27)
     - [wal_sink.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/replication/wal_sink.go:29)
     - [partition_replicator.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/replication/partition_replicator.go:103)
     - [log.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/storage/log.go:585)
   - Result:
     - `leaderHighWatermark` в replication sink игнорируется;
     - при расхождении логов replication path не использует truncate, хотя storage API для этого уже есть.
   - Impact:
     - stale suffix может остаться на диске после смены лидера/restart и позже вернуться в работу.

3. HTTP transport в Python SDK не сохраняет arbitrary bytes как bytes.
   - Projects:
     - `wave-python-sdk`
     - `wave-mq`
   - Files:
     - [client.py](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/src/wavemq/client.py:515)
     - [client.py](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/src/wavemq/client.py:511)
     - [api.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/httpapi/api.go:971)
     - [api.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/httpapi/api.go:330)
     - [api.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/httpapi/api.go:1134)
   - Result:
     - SDK кодирует non-UTF8 payload как строку `base64:...`;
     - HTTP API сохраняет эту строку как literal payload, а не как декодированные байты.
   - Impact:
     - `transport="http"` неэквивалентен `transport="tcp"`;
     - cross-transport roundtrip для binary payload ломается.

4. Replication worker пинит leader endpoint при старте и не пересобирается при смене advertised address.
   - Project: `wave-mq`
   - Files:
     - [manager.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/replication/manager.go:146)
     - [manager.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/replication/manager.go:201)
     - [manager.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/replication/manager.go:211)
   - Result:
     - leader broker snapshot берётся один раз на старте worker;
     - критерий изменения assignment не учитывает host/port и metadata version.
   - Impact:
     - после redeploy/restart с новым endpoint follower может зависнуть на устаревшем адресе.

5. В static/single-controller режиме restart не восстанавливает сохранённую replica layout, а пересчитывает её заново.
   - Project: `wave-mq`
   - Files:
     - [controller.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/controller/controller.go:53)
     - [controller.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/controller/controller.go:59)
     - [controller.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/controller/controller.go:247)
   - Result:
     - recovered topic metadata используется не полностью;
     - replica placement после restart может вычисляться заново.
   - Impact:
     - в static multi-broker сценарии restart может бесшумно перетасовать leaders/replicas.

### P2

6. `auto_route=True` в Python SDK сейчас мёртвый публичный API.
   - Project: `wave-python-sdk`
   - Files:
     - [client.py](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/src/wavemq/client.py:305)
     - [client.py](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/src/wavemq/client.py:316)
     - [client.py](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/src/wavemq/client.py:333)
   - Result:
     - параметр принимается и экспонируется наружу, но request paths его не используют.
   - Impact:
     - API обещает routing behavior, которого фактически нет.

7. HTTP fetch в Python SDK тихо игнорирует `max_bytes`.
   - Project: `wave-python-sdk`
   - Files:
     - [client.py](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/src/wavemq/client.py:187)
     - [client.py](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/src/wavemq/client.py:188)
     - [client.py](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/src/wavemq/client.py:193)
   - Result:
     - HTTP transport не применяет `max_bytes` и формирует окно чтения по `high_watermark - offset + 1`.
   - Impact:
     - поведение SDK по памяти/latency расходится между TCP и HTTP transport.

8. Error taxonomy в HTTP SDK и consumer examples слишком грубая.
   - Projects:
     - `wave-python-sdk`
     - `examples`
   - Files:
     - [errors.py](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/src/wavemq/errors.py:79)
     - [consumer.py](C:/Users/Pavel/GolandProjects/wave/examples/python/sdk/http/consumer.py:61)
     - [consumer.py](C:/Users/Pavel/GolandProjects/wave/examples/python/sdk/http/consumer.py:67)
     - [consumer.py](C:/Users/Pavel/GolandProjects/wave/examples/python/sdk/http/consumer.py:86)
     - [consumer.py](C:/Users/Pavel/GolandProjects/wave/examples/python/sdk/tcp/consumer.py:70)
     - [consumer.py](C:/Users/Pavel/GolandProjects/wave/examples/python/sdk/tcp/consumer.py:76)
     - [consumer.py](C:/Users/Pavel/GolandProjects/wave/examples/python/sdk/tcp/consumer.py:95)
   - Result:
     - HTTP `404` сводится к одному типу ошибки;
     - examples ловят широкий `WaveMQBrokerError` и маскируют permanent failures polling loop’ом.
   - Impact:
     - typo в topic/group/partition может выглядеть как “данных пока нет”.

9. Компакция `offsets.log` не доведена до crash-safe rename.
   - Project: `wave-mq`
   - Files:
     - [offset_store.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/broker/offset_store.go:316)
     - [offset_store.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/broker/offset_store.go:362)
     - [offset_store.go](C:/Users/Pavel/GolandProjects/wave/wave-mq/internal/broker/offset_store.go:384)
   - Result:
     - temp file sync есть;
     - fsync parent directory после `os.Rename(...)` отсутствует.
   - Impact:
     - при crash/power loss вокруг rename можно потерять факт атомарной подмены файла.

10. HTTP transport parity в SDK покрыт слабо.
    - Project: `wave-python-sdk`
    - Files:
      - [test_client.py](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/tests/unit/test_client.py:342)
    - Result:
      - тесты покрывают в основном happy-path со строковым payload;
      - нет покрытия на binary payload, error mapping variants и cross-transport parity.
    - Impact:
      - регрессии в HTTP shim могут проходить незамеченными.

### P3

11. README и package docs уже расходятся с реальным поведением SDK.
    - Project: `wave-python-sdk`
    - Files:
      - [README.md](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/README.md:3)
      - [README.md](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/README.md:10)
      - [README.md](C:/Users/Pavel/GolandProjects/wave/wave-python-sdk/README.md:30)
    - Result:
      - README одновременно заявляет общий API для TCP/HTTP и при этом содержит устаревшее описание возможностей.
    - Impact:
      - documentation drift и support friction после публикации пакета.

## Direction

1. Сначала закрыть data-safety и replication semantics.
2. Затем закрыть restart consistency и divergence handling.
3. После этого определить статус HTTP transport: first-class transport или ограниченный compatibility path.
4. Затем ужесточить error model в SDK/examples и подтянуть tests/docs до реального поведения.
