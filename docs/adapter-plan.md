Разведка принята. Идём в реализацию.

КОНТЕКСТ И РЕШЕНИЯ.

Создаём в существующей integration/ новый компонент — Python-адаптер 
осциллографа PicoScope 5000a. Это самостоятельный продьюсер, по образцу 
существующего wave-gen, но с принципиальным отличием: он публикует данные 
от реального устройства (или от его test-mode имитации), повторяя 
параметризацию C#-программы PS5000ABlockCapture.

ВАЖНО: НЕ сливать с wave-gen. wave-gen остаётся как простой бесконечный 
генератор сигналов для общей синтетики. wave-osc — это адаптер 
осциллографа со своим API, своими параметрами устройства (range, 
coupling, timebase), своим CLI.

АРХИТЕКТУРА.

Вариант А из отчёта, но в существующей integration/, не отдельным пакетом:

integration/src/wave_integration/
├── codec.py                       # уже есть, без изменений
├── operators/
│   ├── base.py                    # уже есть
│   └── fft_op.py                  # уже есть
├── producers/
│   ├── synth.py                   # уже есть (wave-gen)
│   └── osc.py                     # НОВЫЙ — CLI wave-osc
└── sources/                       # НОВАЯ ПАПКА
    ├── __init__.py
    ├── base.py                    # CaptureSource ABC + CaptureBlock dataclass
    ├── synthetic.py               # SyntheticOscilloscopeSource
    └── picoscope.py               # PicoScopeSource (использует picosdk)

ЗАДАЧА.

1. integration/src/wave_integration/sources/base.py.

   Импорты: abc, dataclasses, numpy.
   
   Dataclass для одного блока захвата:
   
     @dataclass
     class CaptureBlock:
         samples_mv: np.ndarray   # float32, длина n_samples, единицы — милливольты
         sample_rate_hz: int
         timestamp_ns: int        # момент завершения захвата (DateTime.UtcNow эквивалент)
         channel_id: int
         range_mv: int            # фактический диапазон (для документации/логов)
   
   Dataclass для конфигурации захвата (общая для real и synthetic):
   
     @dataclass
     class CaptureConfig:
         timebase: int = 8           # PicoScope timebase, default 8 (~12.5 MHz)
         pre_samples: int = 5000
         post_samples: int = 95000
         range_mv: int = 1000        # 1V default
         coupling: str = "AC"        # "AC" or "DC"
         channel: str = "A"          # "A" (на этом этапе только канал A)
         bw_filter_20mhz: bool = False
   
   ABC:
   
     class CaptureSource(ABC):
         @abstractmethod
         def open(self) -> None: ...
         @abstractmethod
         def configure(self, config: CaptureConfig) -> None: ...
         @abstractmethod
         def capture_block(self) -> CaptureBlock: ...
         @abstractmethod
         def close(self) -> None: ...
         def __enter__(self): self.open(); return self
         def __exit__(self, *exc): self.close()
   
   КЛЮЧЕВОЕ: configure() вызывается ОДИН РАЗ после open(). capture_block() 
   вызывается в цикле и возвращает один блок. Это контракт, на нём держится 
   архитектура.

2. integration/src/wave_integration/sources/synthetic.py.

   class SyntheticOscilloscopeSource(CaptureSource).
   
   Имитирует поведение PicoScope, но без железа. Что генерирует:
   
   - В open(): ничего, можно просто залогировать "Synthetic source opened".
   - В configure(): сохраняет config. Вычисляет sample_rate из timebase 
     по формуле PicoScope для 16-bit (та же что в C#): 
       dt = (timebase - 3.0) / 62_500_000.0  если timebase >= 3
       sample_rate = 1.0 / dt
     Это даёт sample_rate, реалистично соответствующий тому, что выдал бы 
     реальный осцилл при том же timebase. n_samples = pre + post.
   - В capture_block():
     * Генерирует тестовый сигнал. Параметры из ENV-переменных или 
       конструктора (waveform, freq, amplitude — см. ниже про CLI).
     * Базовая логика как в wave-gen: sine с непрерывной фазой между блоками, 
       либо noise (gauss), либо chirp.
     * Амплитуда сигнала в милливольтах в диапазоне примерно [-range_mv/2, 
       +range_mv/2], как в реальном осцилле.
     * Добавляет небольшой шум (стандартное отклонение ~0.5% от range_mv), 
       чтобы выглядело реалистично.
     * timestamp_ns = time.time_ns().
     * Имитирует длительность захвата через time.sleep(actual_capture_time), 
       где actual_capture_time = n_samples / sample_rate. Это критично — без 
       sleep блоки будут литься со скоростью CPU, и UI/брокер захлебнётся.
     * Возвращает CaptureBlock.
   - Параметры тестового сигнала: waveform, freq, amplitude — задаются в 
     __init__ конструктора, не в configure (это не параметры захвата, это 
     параметры имитации источника).
   - close(): ничего.

3. integration/src/wave_integration/sources/picoscope.py.

   class PicoScopeSource(CaptureSource).
   
   ИСПОЛЬЗУЕТ pip-пакет picosdk (надо добавить в pyproject.toml как опциональную 
   зависимость, чтобы пользователи без железа могли ставить integration без 
   тяжёлых драйверов).
   
   В pyproject.toml добавь:
     [project.optional-dependencies]
     picoscope = ["picosdk>=1.1"]
     dev = ["pytest"]
   
   Установка с поддержкой PicoScope: pip install -e ".[picoscope]"
   
   В коде picoscope.py сделай try/except на импорт picosdk на верхнем уровне, 
   и если его нет — внятная ошибка в open(): "PicoScope SDK not available. 
   Install with: pip install wave-integration[picoscope]".
   
   Логика, точно повторяющая C#:
   
   - open(): 
     * import picosdk.ps5000a as ps
     * c_handle = ctypes.c_int16()
     * status = ps.ps5000aOpenUnit(byref(c_handle), None, PS5000A_DR_16BIT)
     * Обработка PICO_POWER_SUPPLY_NOT_CONNECTED через ChangePowerSource 
       (как в C#).
     * Сохранить handle.
   
   - configure(config):
     * ps5000aSetChannel(handle, channel='A', enabled=1, coupling=config.coupling, 
       range=mv_to_range_enum(config.range_mv), analogOffset=0.0)
     * Все остальные каналы (B, C, D) — disabled.
     * Опциональный BW-фильтр через ps5000aSetBandwidthFilter, если 
       config.bw_filter_20mhz.
     * Триггер: ps5000aSetSimpleTrigger с теми же параметрами что в C#:
         enable=1, channel=External, threshold=20000, direction=Rising, 
         delay=0, autoTrigger_ms=22222
     * GetTimebase2 для определения фактического sample rate:
         intervalNs = c_float()
         maxSamp = c_int32()
         ps5000aGetTimebase2(handle, config.timebase, total_samples, 
                             byref(intervalNs), byref(maxSamp), 0)
         self._sample_rate_hz = int(round(1e9 / intervalNs.value))
     * Заранее аллоцировать ctypes-буферы для max и min:
         self._buf_max = (c_int16 * total_samples)()
         self._buf_min = (c_int16 * total_samples)()
         ps5000aSetDataBuffers(handle, 'A', self._buf_max, self._buf_min, 
                                total_samples, 0, RATIO_MODE_NONE)
     * Сохранить config, range_mv, n_samples (pre+post), self._range_mv.
   
   - capture_block():
     * ps5000aRunBlock(handle, pre_samples, post_samples, timebase, 
                      byref(timeIndisposed), 0, None, None)
       (Callback не используем — None.)
     * Polling: while True: ps5000aIsReady(handle, byref(ready)); 
                if ready.value: break; time.sleep(0.001)
     * downSampleRatio=1, RATIO_MODE_NONE:
         n_returned = c_uint32(total_samples)
         overflow = c_int16()
         ps5000aGetValues(handle, 0, byref(n_returned), 1, RATIO_MODE_NONE, 
                           0, byref(overflow))
     * ps5000aStop(handle)
     * Преобразование ADC → мВ, ТОЧНО ПО C#-формуле:
         mult = 1.0 / 2.0 * range_mv / 65536.0
         max_arr = np.ctypeslib.as_array(self._buf_max)
         min_arr = np.ctypeslib.as_array(self._buf_min)
         summed = (max_arr.astype(np.int64) + min_arr.astype(np.int64))
         samples_mv = (summed * mult).astype(np.float32)
       Это даёт сэмплы в милливольтах, БИТ В БИТ как C#-программа.
     * timestamp_ns = time.time_ns()  (момент после capture)
     * Возвращает CaptureBlock(samples_mv, self._sample_rate_hz, 
                                 timestamp_ns, channel_id=0, range_mv=range_mv)
   
   - close(): ps5000aCloseUnit(handle).
   
   КРИТИЧНО: Все вызовы picosdk оборачивай в проверку status. Если != PICO_OK 
   — поднимай PicoScopeError(status, function_name). Сделай простой класс 
   ошибки для этого.

4. integration/src/wave_integration/producers/osc.py — CLI wave-osc.

   argparse, бесконечный цикл публикации.
   
   CLI флаги:
     --source        real|synth        default: real (для удобства; на 
                                       машинах без железа явно --source=synth)
     --topic                            default: raw.osc.chA
     --broker                           default: 127.0.0.1:7912
     --timebase      int                default: 8
     --pre-samples   int                default: 5000  
     --post-samples  int                default: 95000
     --range-mv      int                default: 1000 (=1V)
     --coupling      AC|DC              default: AC
     --bw-filter                        флаг, default: off
     --rate-limit    блоков/сек, 0=без  default: 5 (чтобы не задавить брокер)
     --duration      сек, -1=∞          default: -1
     --output-file   путь               default: пусто (без записи в файл)
     --output-mode   bin|txt|none       default: none
     --no-broker                        флаг, если установлен — НЕ публикует 
                                       в брокер, только в файл (для тестов 
                                       без брокера)
     # параметры для --source=synth:
     --synth-waveform sine|noise|chirp  default: sine
     --synth-freq    Гц                 default: 1000
     --synth-amplitude  мВ              default: 500 (половина range)
   
   Логика main():
   
     - Парс CLI.
     - Создать source по флагу:
         if args.source == 'real':
             src = PicoScopeSource()
         else:
             src = SyntheticOscilloscopeSource(
                 waveform=args.synth_waveform, freq=args.synth_freq, 
                 amplitude_mv=args.synth_amplitude)
     - config = CaptureConfig(timebase=..., pre_samples=..., и т.д.)
     - Создать WaveMQClient(args.broker, transport='tcp') если 
       not args.no_broker.
     - Создать file writer если args.output_mode != 'none' и args.output_file.
     - with src: src.configure(config); ensure_topic if publish.
     - Цикл:
         block = src.capture_block()
         data = codec.encode_block(
             timestamp_ns=block.timestamp_ns,
             sample_rate_hz=block.sample_rate_hz,
             channel_id=block.channel_id,
             source_id=2,                      # 2 = осцилл (1 = синтетика)
             samples=block.samples_mv,
         )
         if not args.no_broker:
             client.produce_one_to_partition(args.topic, 0, data, 
                 content_type='application/octet-stream')
         if args.output_mode == 'bin':
             # дописать data как бинарный фрейм с маркером, например
             # 4 байта длины + сам фрейм. Так файл — последовательность 
             # записей, парсимая обратно.
             out_file.write(struct.pack('>I', len(data)))
             out_file.write(data)
         elif args.output_mode == 'txt':
             # одна строка = timestamp_ns sample_rate n_samples values...
             # либо несколько строк (по одному значению) — выбери что проще, 
             # главное чтобы было читаемо MATLAB-like
             # Я предлагаю: один блок = одна строка, значения через табуляцию
             write_txt_row(out_file, block)
         # rate-limit
         if args.rate_limit > 0:
             time.sleep(max(0, 1.0/args.rate_limit - 
                            (block_duration_seconds)))
         # progress log каждый 100-й блок
   
   Корректный shutdown по Ctrl-C: выйти из цикла, закрыть source через 
   __exit__, закрыть файл, закрыть брокер-клиент.

5. Добавить entry-point в pyproject.toml:
     wave-osc = "wave_integration.producers.osc:main"

6. ТЕСТЫ.

   tests/test_synthetic_source.py:
   - Создать SyntheticOscilloscopeSource, конфигурировать, capture_block 
     несколько раз. Проверить:
     * длина samples_mv == pre+post
     * sample_rate_hz > 0 и совпадает с расчётом из timebase
     * timestamp_ns монотонно возрастает между блоками
     * амплитуда в разумных пределах для заданной waveform
   
   tests/test_osc_e2e_synth.py:
   - Запустить wave-osc --source=synth --no-broker --output-mode=bin 
     --output-file=/tmp/test.bin --duration=2 в subprocess.
   - Дождаться завершения.
   - Открыть /tmp/test.bin, распарсить записи (длина + фрейм + длина + 
     фрейм + ...), декодировать через codec.decode_block, проверить что 
     n_samples и sample_rate соответствуют ожидаемым.

7. Обновить README в integration/.
   - Раздел Sources: что есть, для чего.
   - Раздел Adapter Usage: примеры запуска wave-osc в обоих режимах.
   - Замечание о PicoScope SDK: ссылка на установку libps5000a (см. 
     отчёт разведки, пункт 6.1), команда pip install -e ".[picoscope]".
   - udev rules для Linux: добавить в README блок 
     /etc/udev/rules.d/95-pico.rules:
       SUBSYSTEM=="usb", ATTR{idVendor}=="0ce9", MODE="0666", GROUP="plugdev"
     (idVendor 0ce9 — Pico Technology)

ПОСЛЕ РЕАЛИЗАЦИИ.

1. pytest — оба теста должны проходить на твоей машине БЕЗ железа 
   (PicoScopeSource в тестах не используется).

2. Запусти три проверки и покажи результаты:

   а) wave-osc --source=synth --output-mode=none --no-broker --duration=5
      Должен выдать в stdout прогресс, посчитать ~5 блоков (т.к. без 
      rate-limit-а синтетика будет имитировать таймин через time.sleep).
   
   б) wave-osc --source=synth --no-broker --output-mode=bin 
      --output-file=/tmp/osc_test.bin --duration=3
      После завершения — покажи `ls -l /tmp/osc_test.bin` и парсинг 
      нескольких первых записей.
   
   в) Подними брокер. Запусти:
      wave-osc --source=synth --synth-freq=1500 --duration=10
      Параллельно в другом терминале:
      wave-fft --input-topic raw.osc.chA --output-topic spectrum.osc.chA \
               --group-id fft-osc-chA
      Через mbctl прочитай одну запись из spectrum.osc.chA, декодируй, 
      проверь что argmax спектра соответствует 1500 Гц.

НЕ ДЕЛАЙ:

- Не реализуй каналы B/C/D. Только A.
- Не реализуй усреднение (count_avg). Это отдельный оператор (если 
  понадобится).
- Не реализуй БПФ, фильтрацию, спектрограммы — это на стороне Python-
  операторов в интеграционном слое.
- Не используй callback в RunBlock — только polling через 
  ps5000aIsReady. Это упрощает код и убирает GIL-проблемы.
- Не модифицируй wave-gen и старые компоненты.
- Не пытайся установить libps5000a в этой среде — у нас нет железа, и 
  PicoScopeSource протестируется на твоей машине (или в лаборатории) 
  отдельно. Но в коде он должен быть готов и компилироваться 
  (импорты только в open() через try/except, чтобы файл импортировался 
  даже без picosdk).

ЕСЛИ ЧТО-ТО НЕЯСНО ИЛИ ПОЯВИЛИСЬ ВОПРОСЫ ПРИ ИЗУЧЕНИИ picosdk API — 
СПРАШИВАЙ ДО НАЧАЛА КОДИНГА.