#!/usr/bin/env python3
"""Generate three placeholder instrument samples for the music sequencer
demo: bass (short pluck), pad (slow swell), lead (bright pulse). Each
is mono 16-bit PCM WAV at 22050 Hz — drop into PS1Scene.AudioClips and
let the exporter convert to ADPCM.

Real games would supply actual instrument samples; these only exist so
we can hear the sequencer doing something on PSX without needing a
sample-pack dependency."""
import math, os, struct, sys, wave

SR = 22050  # PSX-friendly sample rate

def write_wav(path, samples):
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b''.join(struct.pack('<h', max(-32767, min(32767, int(s)))) for s in samples))

def env_pluck(n, attack=0.005, decay=0.25):
    a = int(attack * SR)
    d = int(decay * SR)
    out = []
    for i in range(n):
        if i < a:
            out.append(i / a)
        elif i - a < d:
            t = (i - a) / d
            out.append(math.exp(-3.5 * t))
        else:
            out.append(0.0)
    return out

def env_pad(n, attack=0.08, sustain=0.6, release=0.25):
    a = int(attack * SR)
    s = int(sustain * SR)
    r = int(release * SR)
    out = []
    for i in range(n):
        if i < a:
            out.append(i / a)
        elif i - a < s:
            out.append(1.0)
        elif i - a - s < r:
            t = (i - a - s) / r
            out.append(1.0 - t)
        else:
            out.append(0.0)
    return out

def synth(freq, env, kind='sine'):
    n = len(env)
    out = [0.0] * n
    phase = 0.0
    step = 2 * math.pi * freq / SR
    for i in range(n):
        if kind == 'sine':
            v = math.sin(phase)
        elif kind == 'square':
            v = 1.0 if math.sin(phase) >= 0 else -1.0
        elif kind == 'pulse25':
            # 25% pulse for that bright NES-y lead
            v = 1.0 if (phase % (2 * math.pi)) < (math.pi / 2) else -1.0
        elif kind == 'triangle':
            t = (phase % (2 * math.pi)) / (2 * math.pi)
            v = 4 * abs(t - 0.5) - 1
        else:
            v = math.sin(phase)
        out[i] = v * env[i]
        phase += step
    return out

def midi_to_hz(note):
    return 440.0 * (2 ** ((note - 69) / 12))

def mix(a, b, ga=1.0, gb=1.0):
    n = max(len(a), len(b))
    out = [0.0] * n
    for i in range(n):
        out[i] = (a[i] if i < len(a) else 0) * ga + (b[i] if i < len(b) else 0) * gb
    return out

def gain(samples, g):
    return [s * g for s in samples]

def gen_bass(out_path, base_note=45):
    # ~250 ms triangle pluck
    n = int(0.30 * SR)
    env = env_pluck(n, attack=0.003, decay=0.22)
    f = midi_to_hz(base_note)
    s = synth(f, env, 'triangle')
    s = gain(s, 18000)
    write_wav(out_path, s)

def gen_pad(out_path, base_note=60):
    # ~0.5 s clean sine — no harmonic overlay. The chord track is
    # actually monophonic (single notes implying chords through
    # progression), so stacking a fifth in the sample turns every note
    # into a dyad and muddies the mix. Single sine + faster envelope
    # lets each note clear before the next.
    n = int(0.55 * SR)
    env = env_pad(n, attack=0.04, sustain=0.30, release=0.18)
    f = midi_to_hz(base_note)
    out = synth(f, env, 'sine')
    out = gain(out, 14000)
    write_wav(out_path, out)

def gen_lead(out_path, base_note=69):
    # ~350 ms 25% pulse, snappy decay
    n = int(0.40 * SR)
    env = env_pluck(n, attack=0.002, decay=0.32)
    f = midi_to_hz(base_note)
    s = synth(f, env, 'pulse25')
    s = gain(s, 16000)
    write_wav(out_path, s)

if __name__ == '__main__':
    out_dir = sys.argv[1] if len(sys.argv) > 1 else '.'
    os.makedirs(out_dir, exist_ok=True)
    gen_bass(os.path.join(out_dir, 'inst_bass.wav'))
    gen_pad(os.path.join(out_dir, 'inst_pad.wav'))
    gen_lead(os.path.join(out_dir, 'inst_lead.wav'))
    print(f'wrote inst_bass.wav inst_pad.wav inst_lead.wav into {out_dir}')
