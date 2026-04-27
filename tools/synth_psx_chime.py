#!/usr/bin/env python3
"""Generate synthesized PSX-BIOS-chime-style instrument samples.

Outputs 16-bit mono PCM WAV at 11025 Hz, ready for the PS1Godot exporter's
ADPCM encoder. Three instruments cover the PSX BIOS MIDI roles:

    bell   — Crystal cascade (GM 98). FM-style bell with inharmonic partials,
             1.5 s exponential decay. Native pitch C5; use Region.RootKey=72.

    sub    — Sub-bass thump (GM 81 SawLead / GM 87 BassLead). Layered low
             sines + harmonics with PSX-bwomm pitch slide on attack and
             ~1 s decay. Native pitch C2; Region.RootKey=36.

    pad    — Sustained NewAgePad (GM 88). Detuned-saw choir, lowpassed,
             loopable steady-state body. Native pitch C5; Region.RootKey=72,
             Region.LoopEnabled=true.

Run:
    python tools/synth_psx_chime.py

Outputs land in:
    godot-ps1/assets/audio/instruments/synth/{bell,sub,pad}_C{5,2,5}.wav
"""

import os
import wave
import numpy as np

SR = 11025  # PS1Godot's instrument convention

OUT_DIR = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "godot-ps1", "assets", "audio", "instruments", "synth")


def to_wav16(path: str, y: np.ndarray) -> None:
    """Write a 1-D float numpy array (range ~[-1,1]) as 16-bit mono PCM WAV."""
    y = np.clip(y, -1.0, 1.0)
    pcm = (y * 32767.0).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"  wrote {path}  ({len(pcm)} samples / {len(pcm)/SR:.2f}s)")


def synth_bell(duration_s: float = 1.5, freq: float = 523.25) -> np.ndarray:
    """FM bell using Fletcher-Strong-ish inharmonic partial ratios.
    Each partial decays at its own rate (faster for higher partials),
    giving the bright strike → metallic shimmer → low residual character.
    Tiny noise burst in the first ~25 ms doubles for the mallet attack."""
    n = int(SR * duration_s)
    t = np.arange(n) / SR
    # (ratio, amplitude, decay-tau-seconds). Ratios derived from
    # ideal-circular-membrane / glockenspiel measurements.
    partials = [
        (1.00,  1.00, 1.5),
        (2.76,  0.55, 0.7),
        (5.40,  0.35, 0.4),
        (8.93,  0.18, 0.25),
        (13.34, 0.10, 0.18),
    ]
    y = np.zeros(n)
    for ratio, amp, tau in partials:
        y += amp * np.sin(2 * np.pi * freq * ratio * t) * np.exp(-t / tau)
    # Strike noise burst (~25 ms). RNG seeded for reproducibility.
    rng = np.random.default_rng(1)
    burst_n = 280
    burst_env = np.linspace(1.0, 0.0, burst_n) ** 2
    y[:burst_n] += rng.uniform(-1, 1, burst_n) * burst_env * 0.08
    # Normalize to ~0.85 peak so the SPU doesn't clip after volume mults.
    peak = max(0.05, np.max(np.abs(y)))
    return y * (0.85 / peak)


def synth_sub(duration_s: float = 2.0, freq: float = 65.41) -> np.ndarray:
    """Deep PSX-style 'BWOOOM' sub. Fundamental + 2 harmonics + a small
    pitch slide on the attack (the iconic ~50 ms drop-into-pitch).
    Long exp tail so a single MIDI note rings naturally."""
    n = int(SR * duration_s)
    t = np.arange(n) / SR
    # Pitch envelope: starts ~5% sharp, settles in 50 ms. Implemented as
    # an instantaneous-frequency multiplier integrated into phase.
    pitch_env = 1.0 + 0.05 * np.exp(-t / 0.05)
    # Phase = 2π ∫ f(t) dt; with our f(t) = freq * pitch_env(t), use cumsum.
    phase = 2 * np.pi * np.cumsum(freq * pitch_env) / SR
    y  = 1.00 * np.sin(phase)
    y += 0.55 * np.sin(2 * phase)
    y += 0.28 * np.sin(3 * phase + 0.3)
    # Amp envelope: tiny attack, long exponential tail.
    attack_n = int(0.005 * SR)
    amp = np.exp(-t / 1.0)
    if attack_n > 0:
        amp[:attack_n] *= np.linspace(0, 1, attack_n)
    y *= amp
    peak = max(0.05, np.max(np.abs(y)))
    return y * (0.9 / peak)


def synth_pad(duration_s: float = 1.5, freq: float = 523.25) -> np.ndarray:
    """Warm detuned-saw choir pad. 7 saw oscillators spread ±10 cents,
    one-pole lowpass at ~2 kHz, short fade-in, loopable steady-state body.

    Loop strategy: trim leading silence, leave the body as one long
    constant-amplitude texture. The fade-in masks any retrigger click;
    the saw layer's natural beating hides the loop boundary."""
    n = int(SR * duration_s)
    t = np.arange(n) / SR
    detunes_cents = [-10, -5, -3, 0, 3, 5, 10]
    y = np.zeros(n)
    for c in detunes_cents:
        f = freq * (2 ** (c / 1200.0))
        # Polyphase saw via modulo, normalised to ±1.
        phase = (f * t) % 1.0
        y += (2 * phase - 1) / len(detunes_cents)
    # One-pole lowpass (cutoff ~2 kHz — alpha tuned by ear).
    alpha = 0.07
    out = np.zeros_like(y)
    out[0] = y[0] * alpha
    # Vectorise the lowpass via lfilter-equivalent loop. n is small.
    for i in range(1, n):
        out[i] = alpha * y[i] + (1 - alpha) * out[i - 1]
    y = out
    # 50 ms fade-in for clean loop retriggering.
    fadein = int(0.05 * SR)
    y[:fadein] *= np.linspace(0, 1, fadein)
    peak = max(0.05, np.max(np.abs(y)))
    return y * (0.7 / peak)


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    print(f"Output dir: {os.path.abspath(OUT_DIR)}")
    to_wav16(os.path.join(OUT_DIR, "bell_C5.wav"), synth_bell())
    to_wav16(os.path.join(OUT_DIR, "sub_C2.wav"),  synth_sub())
    to_wav16(os.path.join(OUT_DIR, "pad_C5.wav"),  synth_pad())


if __name__ == "__main__":
    main()
