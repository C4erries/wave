from __future__ import annotations

import argparse
import struct
import sys
import time
from typing import IO, Optional

from wave_integration import codec
from wave_integration.sources.base import CaptureBlock, CaptureConfig


# source_id=2 → PicoScope oscilloscope (1 = synthetic)
_SOURCE_ID_REAL = 2
_SOURCE_ID_SYNTH = 1


def _build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="wave-osc — PicoScope 5000a → wave-mq adapter")

    p.add_argument("--source", choices=("real", "synth"), default="real",
                   help="'real' = PicoScope hardware, 'synth' = software imitation")

    # Broker
    p.add_argument("--topic", default="raw.osc.chA")
    p.add_argument("--broker", default="127.0.0.1:7912")
    p.add_argument("--no-broker", action="store_true",
                   help="skip publishing to broker (useful for file-only tests)")

    # Capture params
    p.add_argument("--timebase", type=int, default=8,
                   help="PicoScope timebase (8 → ~12.5 MHz at 16-bit/1ch)")
    p.add_argument("--pre-samples", type=int, default=5000)
    p.add_argument("--post-samples", type=int, default=95000)
    p.add_argument("--range-mv", type=int, default=1000,
                   help="ADC full-scale range in mV (10,20,50,100,200,500,1000,2000,5000,10000,20000)")
    p.add_argument("--coupling", choices=("AC", "DC"), default="AC")
    p.add_argument("--bw-filter", action="store_true", help="enable 20 MHz BW filter")

    # Flow control
    p.add_argument("--rate-limit", type=float, default=5.0,
                   help="max blocks per second, 0 = unlimited")
    p.add_argument("--duration", type=float, default=-1.0,
                   help="run duration in seconds, -1 = infinite")

    # Output file
    p.add_argument("--output-file", default="")
    p.add_argument("--output-mode", choices=("bin", "txt", "none"), default="none",
                   help="'bin': length-prefixed frames; 'txt': tab-separated rows")

    # Synthetic source params
    p.add_argument("--synth-waveform", choices=("sine", "noise", "chirp"), default="sine")
    p.add_argument("--synth-freq", type=float, default=1000.0, help="Hz")
    p.add_argument("--synth-amplitude", type=float, default=500.0, help="mV")
    p.add_argument("--synth-chirp-f1", type=float, default=100.0, help="chirp start Hz")
    p.add_argument("--synth-chirp-f2", type=float, default=5000.0, help="chirp end Hz")

    return p


def _write_bin(f: IO[bytes], frame: bytes) -> None:
    f.write(struct.pack(">I", len(frame)))
    f.write(frame)


def _write_txt(f: IO[str], block: CaptureBlock) -> None:
    values = "\t".join(f"{v:.6f}" for v in block.samples_mv)
    f.write(
        f"{block.timestamp_ns}\t{block.sample_rate_hz}\t"
        f"{len(block.samples_mv)}\t{values}\n"
    )


def main() -> None:
    args = _build_parser().parse_args()

    # Build source
    if args.source == "synth":
        from wave_integration.sources.synthetic import SyntheticOscilloscopeSource
        source = SyntheticOscilloscopeSource(
            waveform=args.synth_waveform,
            freq=args.synth_freq,
            amplitude_mv=args.synth_amplitude,
            chirp_f1=args.synth_chirp_f1,
            chirp_f2=args.synth_chirp_f2,
        )
        source_id = _SOURCE_ID_SYNTH
    else:
        from wave_integration.sources.picoscope import PicoScopeSource
        source = PicoScopeSource()
        source_id = _SOURCE_ID_REAL

    config = CaptureConfig(
        timebase=args.timebase,
        pre_samples=args.pre_samples,
        post_samples=args.post_samples,
        range_mv=args.range_mv,
        coupling=args.coupling,
        bw_filter_20mhz=args.bw_filter,
    )

    # Build broker client
    client = None
    if not args.no_broker:
        from wavemq import WaveMQClient
        client = WaveMQClient(args.broker, transport="tcp")
        client.__enter__()

    # Open output file
    out_file_bin: Optional[IO[bytes]] = None
    out_file_txt: Optional[IO[str]] = None
    if args.output_mode != "none" and args.output_file:
        if args.output_mode == "bin":
            out_file_bin = open(args.output_file, "wb")
        else:
            out_file_txt = open(args.output_file, "w", encoding="utf-8")

    block_count = 0
    start_time = time.monotonic()

    try:
        with source:
            source.configure(config)

            if client is not None:
                client.ensure_topic(args.topic, partitions=1, replication_factor=1)
                print(f"[osc] topic={args.topic} ready")

            while True:
                if args.duration > 0 and time.monotonic() - start_time >= args.duration:
                    break

                t0 = time.monotonic()

                block = source.capture_block()

                frame = codec.encode_block(
                    timestamp_ns=block.timestamp_ns,
                    sample_rate_hz=block.sample_rate_hz,
                    channel_id=block.channel_id,
                    source_id=source_id,
                    samples=block.samples_mv,
                )

                if client is not None:
                    client.produce_one_to_partition(
                        args.topic, 0, frame,
                        content_type="application/octet-stream",
                    )

                if out_file_bin is not None:
                    _write_bin(out_file_bin, frame)
                if out_file_txt is not None:
                    _write_txt(out_file_txt, block)

                block_count += 1
                elapsed = time.monotonic() - t0

                if block_count == 1 or block_count % 100 == 0:
                    total_elapsed = time.monotonic() - start_time
                    print(
                        f"[osc] blocks={block_count}  "
                        f"n_samples={block.sample_rate_hz}Hz×{len(block.samples_mv)}  "
                        f"ts={block.timestamp_ns}  elapsed={total_elapsed:.1f}s"
                    )
                    sys.stdout.flush()

                if args.rate_limit > 0:
                    sleep_s = max(0.0, 1.0 / args.rate_limit - elapsed)
                    if sleep_s > 0:
                        time.sleep(sleep_s)

    except KeyboardInterrupt:
        pass

    finally:
        if client is not None:
            client.__exit__(None, None, None)
        if out_file_bin is not None:
            out_file_bin.close()
        if out_file_txt is not None:
            out_file_txt.close()

    print(f"[osc] stopped after {block_count} blocks")


if __name__ == "__main__":
    main()
