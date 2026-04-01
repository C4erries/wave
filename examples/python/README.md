# Python examples

Python examples are split by access method:

- `examples/python/mbctl`
- `examples/python/http`
- `examples/python/sdk`

Use `mbctl` if you want a thin wrapper over the native `wave-mq` binary client.
Use `http` if you want plain HTTP producer/consumer scripts that only need broker `addr`.
Use `sdk` if you want producer/consumer examples on top of the `wavemq` Python package, split into `tcp` and `http`.
Inside `examples/python/sdk/tcp` there is also a `simple` folder with in-file producer/consumer/replay scripts for offset replay and analysis reruns.
