from __future__ import annotations

import argparse
import math
import random
import time

import numpy as np
from wavemq import WaveMQClient
from wavemq.errors import WaveMQConnectionError

from wave_integration.codec import encode_block

N_SAMPLES = 4096
SAMPLE_RATE_HZ = 100_000


def _build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="wave-integration synthetic signal generator")
    p.add_argument("--waveform", choices=("sine", "noise", "chirp", "sweep"), default="sine")
    p.add_argument("--freq", type=float, default=1000.0, help="sine frequency Hz")
    p.add_argument("--chirp-f1", type=float, default=100.0, help="chirp/sweep start frequency Hz")
    p.add_argument("--chirp-f2", type=float, default=5000.0, help="chirp/sweep end frequency Hz")
    p.add_argument("--sweep-period", type=float, default=8.0, help="sweep cycle duration seconds")
    p.add_argument("--amplitude", type=float, default=1.0)
    p.add_argument("--noise-std", type=float, default=0.3)
    p.add_argument("--topic", default="raw.gen.chA")
    p.add_argument("--rate", type=float, default=10.0, help="blocks per second")
    p.add_argument("--broker", default="127.0.0.1:7912")
    p.add_argument("--duration", type=float, default=-1.0, help="seconds, -1 = infinite")
    p.add_argument("--channel-id", type=int, default=0)
    p.add_argument("--source-id", type=int, default=1)
    return p


def _sweep_phase(t_abs: np.ndarray, f1: float, f2: float, T: float) -> np.ndarray:
    """Continuous phase via integral of triangle-wave frequency — no discontinuities at block boundaries."""
    phase_per_cycle = (f1 + f2) * T / 2  # ∫₀ᵀ f(τ)dτ
    n_cyc = np.floor(t_abs / T)
    t_cyc = t_abs % T
    up = t_cyc <= T / 2
    integral = np.where(
        up,
        f1 * t_cyc + (f2 - f1) * t_cyc ** 2 / T,
        f1 * T / 2 + (f2 - f1) * T / 4 + f2 * (t_cyc - T / 2) - (f2 - f1) * (t_cyc - T / 2) ** 2 / T,
    )
    return 2 * math.pi * (n_cyc * phase_per_cycle + integral)


def _generate(args: argparse.Namespace, phase_offset: int) -> np.ndarray:
    t = (phase_offset + np.arange(N_SAMPLES)) / SAMPLE_RATE_HZ

    if args.waveform == "sine":
        samples = args.amplitude * np.sin(2 * math.pi * args.freq * t)
    elif args.waveform == "noise":
        samples = np.random.normal(0.0, args.noise_std, N_SAMPLES)
    elif args.waveform == "sweep":
        # Continuous-phase FM: frequency triangle-sweeps f1→f2→f1, no jumps between blocks
        phase = _sweep_phase(t, args.chirp_f1, args.chirp_f2, args.sweep_period)
        samples = args.amplitude * np.sin(phase)
    else:  # chirp — single-block instantaneous sweep
        T = 1.0
        phase = 2 * math.pi * (args.chirp_f1 * t + (args.chirp_f2 - args.chirp_f1) * t**2 / (2 * T))
        samples = args.amplitude * np.sin(phase)

    return samples.astype(np.float32)


def main() -> None:
    args = _build_parser().parse_args()

    phase_offset = 0
    block_count = 0
    start_time = time.monotonic()

    while True:
        try:
            with WaveMQClient(args.broker, transport="tcp") as client:
                client.ensure_topic(args.topic, partitions=1, replication_factor=1)
                print(f"[synth] topic={args.topic} ready, starting waveform={args.waveform}")

                while True:
                    if args.duration > 0 and time.monotonic() - start_time >= args.duration:
                        print(f"[synth] stopped after {block_count} blocks")
                        return

                    samples = _generate(args, phase_offset)
                    ts = time.time_ns()
                    data = encode_block(ts, SAMPLE_RATE_HZ, args.channel_id, args.source_id, samples)
                    r = client.produce_one_to_partition(
                        args.topic, 0, data, content_type="application/octet-stream",
                    )
                    phase_offset += N_SAMPLES
                    block_count += 1

                    if block_count == 1 or block_count % 100 == 0:
                        print(
                            f"[synth] topic={args.topic} offset={r.base_offset} "
                            f"waveform={args.waveform} blocks={block_count}"
                        )

                    time.sleep(1.0 / args.rate)

        except KeyboardInterrupt:
            break
        except WaveMQConnectionError:
            print(f"[synth] connection lost, reconnect in 2s...")
            time.sleep(2 + random.uniform(0, 3))

    print(f"[synth] stopped after {block_count} blocks")


if __name__ == "__main__":
    main()
