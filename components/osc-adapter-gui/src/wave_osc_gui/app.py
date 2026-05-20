"""Wave Oscilloscope Adapter — tkinter GUI."""
from __future__ import annotations

import queue
import struct
import threading
import time
import tkinter as tk
from tkinter import filedialog, scrolledtext, ttk
from typing import Optional

from wave_integration import codec
from wave_integration.sources.base import CaptureBlock, CaptureConfig


_RANGE_MV_OPTIONS = [10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000]
_RATE_LIMIT_OPTIONS = [0, 1, 2, 5, 10, 20, 50]
_LOG_MAX_LINES = 200


class CaptureWorker(threading.Thread):
    """Runs in a background thread: open source → capture loop → close."""

    def __init__(
        self,
        source_type: str,
        config: CaptureConfig,
        topic: str,
        broker: str,
        use_broker: bool,
        output_path: str,
        rate_limit: float,
        synth_freq: float,
        log_queue: queue.Queue,
        stats_queue: queue.Queue,
    ) -> None:
        super().__init__(daemon=True)
        self.source_type = source_type
        self.config = config
        self.topic = topic
        self.broker = broker
        self.use_broker = use_broker
        self.output_path = output_path
        self.rate_limit = rate_limit
        self.synth_freq = synth_freq
        self.log_queue = log_queue
        self.stats_queue = stats_queue
        self._stop_flag = threading.Event()

    def stop(self) -> None:
        self._stop_flag.set()

    def _log(self, msg: str) -> None:
        ts = time.strftime('%H:%M:%S')
        self.log_queue.put(f'[{ts}] {msg}')

    def run(self) -> None:
        try:
            self._run()
        except Exception as exc:
            self._log(f'ERROR: {exc}')
        finally:
            self.stats_queue.put(None)  # signal done

    def _run(self) -> None:
        if self.source_type == 'synth':
            from wave_integration.sources.synthetic import SyntheticOscilloscopeSource
            source = SyntheticOscilloscopeSource(freq=self.synth_freq)
            source_id = 1
        else:
            from wave_integration.sources.picoscope import PicoScopeSource
            source = PicoScopeSource()
            source_id = 2

        client = None
        if self.use_broker:
            try:
                from wavemq import WaveMQClient
                client = WaveMQClient(self.broker, transport='tcp')
                client.__enter__()
                client.ensure_topic(self.topic, partitions=1, replication_factor=1)
                self._log(f'Connected to broker {self.broker}, topic={self.topic}')
            except Exception as exc:
                self._log(f'Broker error: {exc}')
                client = None

        out_file = None
        if self.output_path:
            try:
                out_file = open(self.output_path, 'wb')
                self._log(f'Saving to {self.output_path}')
            except OSError as exc:
                self._log(f'Cannot open file: {exc}')

        block_count = 0
        t_window_start = time.monotonic()

        try:
            with source:
                source.configure(self.config)
                self._log(
                    f'Source ready: {self.source_type}, '
                    f'fs={self.config.timebase} timebase, '
                    f'n={self.config.pre_samples + self.config.post_samples} samples'
                )

                while not self._stop_flag.is_set():
                    t0 = time.monotonic()

                    block: CaptureBlock = source.capture_block()

                    frame = codec.encode_block(
                        timestamp_ns=block.timestamp_ns,
                        sample_rate_hz=block.sample_rate_hz,
                        channel_id=block.channel_id,
                        source_id=source_id,
                        samples=block.samples_mv,
                    )

                    if client is not None:
                        try:
                            client.produce_one_to_partition(
                                self.topic, 0, frame,
                                content_type='application/octet-stream',
                            )
                        except Exception as exc:
                            self._log(f'Publish error: {exc}')

                    if out_file is not None:
                        out_file.write(struct.pack('>I', len(frame)))
                        out_file.write(frame)

                    block_count += 1

                    if block_count <= 3 or block_count % 50 == 0:
                        self._log(
                            f'Block #{block_count}  '
                            f'fs={block.sample_rate_hz} Hz  '
                            f'ts={block.timestamp_ns}'
                        )

                    # Stats: blocks/sec over a 1-second window
                    now = time.monotonic()
                    if now - t_window_start >= 1.0:
                        rate = block_count / (now - (t_window_start - (now - t_window_start - 1.0) % 1.0))
                        # Simpler: cumulative / elapsed
                        self.stats_queue.put(block_count)
                        t_window_start = now

                    elapsed = time.monotonic() - t0
                    if self.rate_limit > 0:
                        sleep_s = max(0.0, 1.0 / self.rate_limit - elapsed)
                        if sleep_s > 0:
                            time.sleep(sleep_s)

        finally:
            if client is not None:
                try:
                    client.__exit__(None, None, None)
                except Exception:
                    pass
            if out_file is not None:
                out_file.close()
            self._log(f'Stopped after {block_count} blocks.')


class App(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title('Wave Oscilloscope Adapter')
        self.resizable(True, True)
        self._worker: Optional[CaptureWorker] = None
        self._log_queue: queue.Queue = queue.Queue()
        self._stats_queue: queue.Queue = queue.Queue()
        self._block_count = 0
        self._start_time: Optional[float] = None
        self._build_ui()
        self._poll_queues()

    # ------------------------------------------------------------------
    # UI construction
    # ------------------------------------------------------------------

    def _build_ui(self) -> None:
        pad = {'padx': 8, 'pady': 4}

        # ── Source frame ────────────────────────────────────────────────
        src_frame = ttk.LabelFrame(self, text='Source', padding=6)
        src_frame.pack(fill='x', **pad)

        self._source_var = tk.StringVar(value='synth')
        ttk.Radiobutton(src_frame, text='Synthetic', variable=self._source_var,
                        value='synth', command=self._on_source_change).grid(row=0, column=0, sticky='w')
        ttk.Radiobutton(src_frame, text='Real PicoScope', variable=self._source_var,
                        value='real', command=self._on_source_change).grid(row=0, column=1, sticky='w')

        self._synth_freq_label = ttk.Label(src_frame, text='Synth freq (Hz):')
        self._synth_freq_label.grid(row=1, column=0, sticky='w')
        self._synth_freq_var = tk.StringVar(value='1000')
        self._synth_freq_entry = ttk.Entry(src_frame, textvariable=self._synth_freq_var, width=10)
        self._synth_freq_entry.grid(row=1, column=1, sticky='w')

        # ── Capture params ──────────────────────────────────────────────
        cap_frame = ttk.LabelFrame(self, text='Capture Parameters', padding=6)
        cap_frame.pack(fill='x', **pad)

        fields = [
            ('Topic:', 'raw.osc.chA', '_topic_var'),
            ('Broker:', '127.0.0.1:7912', '_broker_var'),
            ('Timebase:', '8', '_timebase_var'),
            ('Pre samples:', '5000', '_pre_var'),
            ('Post samples:', '95000', '_post_var'),
        ]
        for i, (label, default, attr) in enumerate(fields):
            ttk.Label(cap_frame, text=label).grid(row=i, column=0, sticky='w')
            var = tk.StringVar(value=default)
            setattr(self, attr, var)
            ttk.Entry(cap_frame, textvariable=var, width=18).grid(row=i, column=1, sticky='w')

        # Range dropdown
        ttk.Label(cap_frame, text='Range (mV):').grid(row=len(fields), column=0, sticky='w')
        self._range_var = tk.StringVar(value='1000')
        ttk.Combobox(cap_frame, textvariable=self._range_var,
                     values=[str(v) for v in _RANGE_MV_OPTIONS],
                     width=8, state='readonly').grid(row=len(fields), column=1, sticky='w')

        # Rate limit
        ttk.Label(cap_frame, text='Rate limit (blk/s):').grid(row=len(fields)+1, column=0, sticky='w')
        self._rate_var = tk.StringVar(value='5')
        ttk.Combobox(cap_frame, textvariable=self._rate_var,
                     values=[str(v) for v in _RATE_LIMIT_OPTIONS],
                     width=8).grid(row=len(fields)+1, column=1, sticky='w')

        # ── Output options ───────────────────────────────────────────────
        out_frame = ttk.LabelFrame(self, text='Output', padding=6)
        out_frame.pack(fill='x', **pad)

        self._use_broker_var = tk.BooleanVar(value=True)
        ttk.Checkbutton(out_frame, text='Publish to broker',
                        variable=self._use_broker_var).grid(row=0, column=0, columnspan=3, sticky='w')

        self._save_file_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(out_frame, text='Save to file:',
                        variable=self._save_file_var).grid(row=1, column=0, sticky='w')
        self._file_path_var = tk.StringVar()
        ttk.Entry(out_frame, textvariable=self._file_path_var, width=28).grid(row=1, column=1)
        ttk.Button(out_frame, text='...', width=3,
                   command=self._browse_file).grid(row=1, column=2)

        # ── Log ─────────────────────────────────────────────────────────
        log_frame = ttk.LabelFrame(self, text='Log', padding=4)
        log_frame.pack(fill='both', expand=True, **pad)

        self._log_text = scrolledtext.ScrolledText(
            log_frame, height=12, state='disabled', font=('Courier', 9),
            wrap='word',
        )
        self._log_text.pack(fill='both', expand=True)

        # ── Controls ────────────────────────────────────────────────────
        ctrl_frame = ttk.Frame(self)
        ctrl_frame.pack(fill='x', **pad)

        self._start_btn = ttk.Button(ctrl_frame, text='Start', command=self._on_start)
        self._start_btn.pack(side='left', padx=4)
        self._stop_btn = ttk.Button(ctrl_frame, text='Stop', command=self._on_stop, state='disabled')
        self._stop_btn.pack(side='left', padx=4)

        # ── Status bar ───────────────────────────────────────────────────
        self._status_var = tk.StringVar(value='Ready')
        ttk.Label(self, textvariable=self._status_var, relief='sunken',
                  anchor='w').pack(fill='x', side='bottom')

    # ------------------------------------------------------------------
    # Event handlers
    # ------------------------------------------------------------------

    def _on_source_change(self) -> None:
        is_synth = self._source_var.get() == 'synth'
        state = 'normal' if is_synth else 'disabled'
        self._synth_freq_label.configure(state=state)
        self._synth_freq_entry.configure(state=state)

    def _browse_file(self) -> None:
        path = filedialog.asksaveasfilename(
            defaultextension='.bin',
            filetypes=[('Binary files', '*.bin'), ('All files', '*.*')],
        )
        if path:
            self._file_path_var.set(path)
            self._save_file_var.set(True)

    def _on_start(self) -> None:
        if self._worker and self._worker.is_alive():
            return

        try:
            config = CaptureConfig(
                timebase=int(self._timebase_var.get()),
                pre_samples=int(self._pre_var.get()),
                post_samples=int(self._post_var.get()),
                range_mv=int(self._range_var.get()),
            )
            rate_limit = float(self._rate_var.get())
            synth_freq = float(self._synth_freq_var.get())
        except ValueError as exc:
            self._append_log(f'[ERROR] Invalid parameter: {exc}')
            return

        output_path = self._file_path_var.get() if self._save_file_var.get() else ''

        self._block_count = 0
        self._start_time = time.monotonic()

        self._worker = CaptureWorker(
            source_type=self._source_var.get(),
            config=config,
            topic=self._topic_var.get(),
            broker=self._broker_var.get(),
            use_broker=self._use_broker_var.get(),
            output_path=output_path,
            rate_limit=rate_limit,
            synth_freq=synth_freq,
            log_queue=self._log_queue,
            stats_queue=self._stats_queue,
        )
        self._worker.start()

        self._start_btn.configure(state='disabled')
        self._stop_btn.configure(state='normal')
        self._status_var.set('● Capturing...')

    def _on_stop(self) -> None:
        if self._worker:
            self._worker.stop()
        self._stop_btn.configure(state='disabled')
        self._status_var.set('Stopping...')

    # ------------------------------------------------------------------
    # Queue polling (runs in UI thread via after())
    # ------------------------------------------------------------------

    def _poll_queues(self) -> None:
        # Drain log queue
        try:
            while True:
                msg = self._log_queue.get_nowait()
                self._append_log(msg)
        except queue.Empty:
            pass

        # Drain stats queue
        done = False
        try:
            while True:
                val = self._stats_queue.get_nowait()
                if val is None:
                    done = True
                else:
                    self._block_count = val
        except queue.Empty:
            pass

        # Update status bar
        if self._worker and self._worker.is_alive() and self._start_time:
            elapsed = time.monotonic() - self._start_time
            rate = self._block_count / elapsed if elapsed > 0 else 0.0
            self._status_var.set(
                f'● Capturing... {self._block_count} blocks  ({rate:.1f} blk/s)'
            )
        elif done:
            self._start_btn.configure(state='normal')
            self._stop_btn.configure(state='disabled')
            elapsed = time.monotonic() - self._start_time if self._start_time else 0
            self._status_var.set(
                f'Stopped. {self._block_count} blocks in {elapsed:.1f}s'
            )
            self._worker = None

        self.after(300, self._poll_queues)

    def _append_log(self, msg: str) -> None:
        self._log_text.configure(state='normal')
        self._log_text.insert('end', msg + '\n')
        # Trim to _LOG_MAX_LINES
        lines = int(self._log_text.index('end-1c').split('.')[0])
        if lines > _LOG_MAX_LINES:
            self._log_text.delete('1.0', f'{lines - _LOG_MAX_LINES}.0')
        self._log_text.see('end')
        self._log_text.configure(state='disabled')


def main() -> None:
    app = App()
    app.mainloop()
