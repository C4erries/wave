# osc-adapter-gui

Минимальный tkinter GUI поверх `wave-integration` для запуска в лаборатории.
Альтернатива командной строке `wave-osc` для неопытных пользователей.

## Установка

```bash
cd components/osc-adapter-gui
python -m venv .venv
source .venv/bin/activate
pip install -e ../integration        # wave-integration (editable)
pip install -e .
```

## Запуск

```bash
wave-osc-gui
# или
python -m wave_osc_gui
```

## Возможности

- Выбор источника: Synthetic / Real PicoScope
- Настройка топика, брокера, timebase, pre/post samples, range, rate limit
- Публикация в брокер и/или сохранение в файл
- Лог-окно с захваченными блоками
- Статус: blocks/sec
