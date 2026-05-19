# OscilloscopeSupply

Поддержка дефектоскопии. Управление PicoScope 5000a через WinForms + P/Invoke.

## Что добавлено в E5 — адаптер публикации в wave-mq

Проект `PS5000ABlockCapture` расширен публикацией захваченных блоков в брокер
wave-mq с тем же бинарным форматом фрейма, что и в `integration/`.

### Новые файлы

- `PS5000ABlockCapture/BrokerPublisher.cs` — статический класс: `EncodeFrame` +
  `PublishAsync` (fire-and-forget через `HttpClient`).

### Как создать топик перед первым запуском

```bash
# Создать топик raw.osc.chA (один раз)
mbctl topics create raw.osc.chA --partitions 1 --replication-factor 1
```

### Настройки в UI (вкладка «Измерения»)

1. Открыть осциллограф — кнопка **Open** (вкладка 1, поле timebase заполнено).
2. На вкладке «Измерения» в группе **«Публикация в брокер wave-mq»**:
   - **Адрес брокера** — `http://localhost:8090` (по умолчанию).
   - Нажать **«Проверить»** — статус изменится на «Брокер: ОК».
   - Включить чекбокс **«Включить публикацию»**.
3. Режимы:
   - **Одиночный захват** — кнопка «Запустить захват» (`button2`): после записи
     в файл автоматически публикует фрейм в `raw.osc.chA`.
   - **Непрерывный** — кнопка «Непрерывная публикация»: захватывает и
     публикует каждый блок без усреднения, пока не нажать «Стоп».

### Формат фрейма (big-endian)

```
offset 0   int64  timestamp_ns      — DateTimeOffset.UtcNow в наносекундах
offset 8   int32  sample_rate_hz    — реальное значение из GetTimebase2
offset 12  int32  n_samples         — размер блока (из UI textBox13 + textBox10)
offset 16  int8   channel_id = 0    — канал A
offset 17  int8   source_id  = 2    — осциллограф (1 = синтетика, 2 = осцилл)
offset 18  int16  reserved   = 0
offset 20  float32[n_samples]       — амплитуда в милливольтах, big-endian
```

### source_id по источникам

| source_id | Источник | Единицы |
|-----------|----------|---------|
| 1 | `wave-gen` (синтетика) | безразмерные |
| 2 | PicoScope 5000a | милливольты |

### Известные ограничения

- Только канал A (`channel_id = 0`). Каналы B/C/D — E6.
- Sample rate определяется timebase, установленным в UI перед нажатием Open.
- Реле (Switch.cs) и непрерывное сканирование (`button11`) в этом этапе без
  изменений; публикуют только `button2` и кнопка «Непрерывная публикация».
- Сборка только под Windows (WinForms + ps5000a.dll).

## Smoke-тест сериализации

```bash
# В папке OscilloscopeSupplyFIXed:
python smoke_test.py            # генерирует python_frame.bin, проверяет логику

# В Visual Studio — запустить проект BrokerPublisherTest (Console App .NET 4.8).
# Он генерирует csharp_frame.bin. Затем:
python -c "
a=open('python_frame.bin','rb').read()
b=open('BrokerPublisherTest/bin/Debug/net48/csharp_frame.bin','rb').read()
print('MATCH' if a==b else 'MISMATCH', len(a), 'vs', len(b))
"
```

Ожидаемый вывод: `MATCH 16404 vs 16404`.
