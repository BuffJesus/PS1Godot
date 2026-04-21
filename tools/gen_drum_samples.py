#!/usr/bin/env python3
"""Generate placeholder kick / snare / hi-hat samples (mono 16-bit PCM
WAV at 22050 Hz). Same idea as gen_instrument_samples.py — synthetic
fillers so the sequencer has something to play on track 1 of the
RetroAdventureSong demo. Author can swap in real drum hits later."""
import math, os, random, struct, sys, wave

SR = 22050

def write_wav(path, samples):
    with wave.open(path, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(b''.join(struct.pack('<h', max(-32767, min(32767, int(s)))) for s in samples))

def env(n, attack, decay):
    a = int(attack * SR); d = int(decay * SR)
    out = []
    for i in range(n):
        if i < a:
            out.append(i / max(1, a))
        elif i - a < d:
            t = (i - a) / max(1, d)
            out.append(math.exp(-4.0 * t))
        else:
            out.append(0.0)
    return out

def gen_kick(path):
    # ~150 ms — 100Hz sine that drops to 40Hz with a fast amplitude decay.
    n = int(0.18 * SR)
    e = env(n, attack=0.001, decay=0.16)
    out = []
    phase = 0.0
    for i in range(n):
        # frequency sweep: 100 → 40 Hz over the first 60 ms
        t = i / SR
        f = 100.0 - (60.0 * min(t / 0.06, 1.0))
        phase += 2 * math.pi * f / SR
        s = math.sin(phase) * e[i]
        out.append(s * 26000)
    write_wav(path, out)

def gen_snare(path):
    # ~130 ms — band-limited noise + a 200Hz body tone, sharp decay.
    n = int(0.16 * SR)
    e = env(n, attack=0.001, decay=0.14)
    rng = random.Random(0xBEEF)
    # crude band-pass via a 1-pole hi + 1-pole lo
    out = []
    phase = 0.0
    hi_prev = 0.0; lo_prev = 0.0
    for i in range(n):
        noise = rng.uniform(-1, 1)
        hi = noise - hi_prev * 0.85
        hi_prev = noise
        lo = lo_prev * 0.5 + hi * 0.5
        lo_prev = lo
        body = math.sin(phase); phase += 2 * math.pi * 200.0 / SR
        s = (lo * 0.85 + body * 0.25) * e[i]
        out.append(s * 22000)
    write_wav(path, out)

def gen_hat(path):
    # ~50 ms — high-passed white noise, very fast decay.
    n = int(0.06 * SR)
    e = env(n, attack=0.0005, decay=0.05)
    rng = random.Random(0xC0DE)
    out = []
    prev = 0.0
    for i in range(n):
        noise = rng.uniform(-1, 1)
        # crude high-pass: differentiator
        hp = noise - prev
        prev = noise
        out.append(hp * e[i] * 14000)
    write_wav(path, out)

if __name__ == '__main__':
    out_dir = sys.argv[1] if len(sys.argv) > 1 else '.'
    os.makedirs(out_dir, exist_ok=True)
    gen_kick(os.path.join(out_dir, 'inst_kick.wav'))
    gen_snare(os.path.join(out_dir, 'inst_snare.wav'))
    gen_hat(os.path.join(out_dir, 'inst_hat.wav'))
    print(f'wrote inst_kick.wav inst_snare.wav inst_hat.wav into {out_dir}')
