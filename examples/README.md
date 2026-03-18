# wave examples for `wave-mq`

Examples are organized first by language, then by integration style:

- `examples/python/mbctl`
- `examples/python/http`
- `examples/matlab/mbctl`
- `examples/matlab/mqtt`

Build `mbctl` once if you want the `mbctl`-based demos:

```powershell
cd C:\Users\Pavel\GolandProjects\wave
powershell -ExecutionPolicy Bypass -File .\examples\build-mbctl.ps1
```

Main entry points:

- Python overview: `examples/python/README.md`
- MATLAB overview: `examples/matlab/README.md`

Typical broker startup:

```powershell
docker compose -f .\docker-compose.single.yml up --build broker
```
