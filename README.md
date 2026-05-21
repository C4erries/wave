# Wave

Потоковый пайплайн обработки осциллографических данных.

Данные поступают от синтетического генератора или PicoScope 5000a, проходят через брокер сообщений, обрабатываются FFT/фильтром/статистикой и отображаются в веб-интерфейсе в реальном времени.

## Быстрый старт

```bash
git clone --recurse-submodules <repo-url>
cd wave
./scripts/demo.sh
# Открыть http://localhost:8080/lab
```

По умолчанию запускается сценарий `full_pipeline`. Передать другой сценарий первым аргументом:

```bash
./scripts/demo.sh chirp_sweep
./scripts/demo.sh harmonic_sweep
./scripts/demo.sh noise_band
./scripts/demo.sh multi_signal
```

## Компоненты

| Путь | Описание |
|------|----------|
| [components/integration/](components/integration/) | Python-слой пайплайна: кодек, источники, операторы |
| [components/orchestrator/](components/orchestrator/) | YAML-оркестратор — запускает и управляет процессами |
| [components/osc-adapter-gui/](components/osc-adapter-gui/) | tkinter GUI-адаптер для PicoScope |
| [wave-mq/](wave-mq/) | Брокер сообщений (Go, субмодуль) |
| [wave-ui/](wave-ui/) | Веб-интерфейс (React + TypeScript, субмодуль) |
| [wave-python-sdk/](wave-python-sdk/) | Python-клиент SDK (субмодуль) |
| [OscilloscopeSupplyFIXed/](OscilloscopeSupplyFIXed/) | C# приложение для PicoScope 5000a |
| [deploy/](deploy/) | Docker Compose файлы и Dockerfile |
| [scripts/](scripts/) | demo.sh, dev.sh, stop.sh |
| [docs/](docs/) | Документация, планы, курсовая работа |

## Адреса после запуска

| Страница | URL |
|----------|-----|
| Dashboard | http://localhost:8080 |
| Осциллограф + FFT | http://localhost:8080/lab |
| Конструктор пайплайна | http://localhost:8080/constructor |
| Метрики топиков | http://localhost:8080/metrics |
| Список топиков | http://localhost:8080/topics |
| Статус оркестратора | http://localhost:8099/status |

## CLI команды

```bash
# Активировать виртуальное окружение:
source components/integration/.venv/bin/activate

wave-gen --waveform=sine --freq=1000            # синтетический генератор
wave-fft --input-topic=raw.gen.chA              # FFT оператор
wave-filter --input-topic=raw.gen.chA --low-hz=500 --high-hz=2000  # полосовой фильтр
wave-stats --input-topic=raw.gen.chA            # статистика (RMS, min, max)
wave-threshold --input-topic=raw.gen.chA --threshold-mv=0.8        # пороговые события
wave-orchestrator start --config components/orchestrator/configs/full_pipeline.yaml --serve-api
```

## Переключение сценария без перезапуска

```bash
source components/integration/.venv/bin/activate
wave-orchestrator reload --config components/orchestrator/configs/chirp_sweep.yaml
```

## Сценарии

| Сценарий | Описание |
|----------|----------|
| `full_pipeline` | Синусоида sweep 300–3000 Гц, фильтр 800–2000 Гц |
| `chirp_sweep` | Широкий sweep 100–8000 Гц — самый зрелищный |
| `harmonic_sweep` | Гармонический ряд, гребёнка пиков f0…8f0 |
| `noise_band` | Белый шум → узкополосный фильтр 900–1100 Гц |
| `multi_signal` | Два независимых канала с разными диапазонами |

## Архитектура

Подробная схема топиков, форматов и потоков данных — в [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

Руководство по демонстрации и сценарии для скриншотов — в [docs/DEMO.md](docs/DEMO.md).

## Скрипты

```bash
./scripts/demo.sh [сценарий]  # запустить полный стек
./scripts/dev.sh              # только брокер + UI (без генераторов)
./scripts/stop.sh             # остановить всё
```

## Брокер

| Протокол | Адрес |
|----------|-------|
| TCP (бинарный) | localhost:7912 |
| MQTT | localhost:1883 |
| HTTP API | http://localhost:8090 |
