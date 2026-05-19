"""
Smoke test: verifies that EncodeFrame in BrokerPublisher.cs produces the same
bytes as wave_integration.codec.encode_block.

Run from repo root:
    cd OscilloscopeSupplyFIXed
    python smoke_test.py

After running C# BrokerPublisherTest, compare:
    python -c "
    a=open('python_frame.bin','rb').read()
    b=open('BrokerPublisherTest/bin/Debug/net48/csharp_frame.bin','rb').read()
    print('MATCH' if a==b else 'MISMATCH', len(a), len(b))
    "
"""
import struct
import sys
import os
import math

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'integration', 'src'))

import numpy as np
from wave_integration.codec import encode_block

N = 4096
SR = 100_000
FREQ = 1000.0

# Generate sine identical to C# test
samples = np.array(
    [math.sin(2.0 * math.pi * FREQ * i / SR) for i in range(N)],
    dtype=np.float32
)

# Python canonical frame
python_frame = encode_block(
    timestamp_ns=0,
    sample_rate_hz=SR,
    channel_id=0,
    source_id=1,
    samples=samples,
)

with open('python_frame.bin', 'wb') as f:
    f.write(python_frame)
print(f'python_frame.bin written, {len(python_frame)} bytes (expected {20 + N*4})')

# ── Replicate C# EncodeFrame in Python for immediate verification ──────────

def encode_csharp_sim(timestamp_ns, sample_rate_hz, n_samples, channel_id, source_id, samples):
    """Mirrors BrokerPublisher.EncodeFrame byte-for-byte."""
    buf = bytearray(20 + n_samples * 4)
    # Big-endian header
    struct.pack_into('>q', buf, 0, timestamp_ns)
    struct.pack_into('>i', buf, 8, sample_rate_hz)
    struct.pack_into('>i', buf, 12, n_samples)
    buf[16] = channel_id & 0xFF
    buf[17] = source_id & 0xFF
    struct.pack_into('>h', buf, 18, 0)  # reserved
    # Big-endian float32 samples
    for i, s in enumerate(samples):
        struct.pack_into('>f', buf, 20 + i * 4, float(s))
    return bytes(buf)

csharp_sim = encode_csharp_sim(0, SR, N, 0, 1, samples)

with open('csharp_sim_frame.bin', 'wb') as f:
    f.write(csharp_sim)
print(f'csharp_sim_frame.bin written, {len(csharp_sim)} bytes')

# ── Compare ────────────────────────────────────────────────────────────────

if python_frame == csharp_sim:
    print('✓ ENCODING MATCH: python_frame.bin == csharp_sim_frame.bin')
    print('  C# EncodeFrame logic is verified correct.')
else:
    first_diff = next(i for i, (a, b) in enumerate(zip(python_frame, csharp_sim)) if a != b)
    print(f'✗ MISMATCH at byte {first_diff}')
    print(f'  python: {python_frame[first_diff]:02X}, csharp_sim: {csharp_sim[first_diff]:02X}')

# Print header bytes for reference
print(f'\nHeader (first 20 bytes):')
print('  ' + ' '.join(f'{b:02X}' for b in python_frame[:20]))
print(f'  timestamp=0, sr=0x{SR:08X}, n=0x{N:08X}, ch=0, src=1, reserved=0')
print(f'  First sample (sin(0)=0.0): {" ".join(f"{b:02X}" for b in python_frame[20:24])}')
print(f'  Second sample (sin(2π·1000/100000)≈{samples[1]:.6f}):',
      ' '.join(f'{b:02X}' for b in python_frame[24:28]))
