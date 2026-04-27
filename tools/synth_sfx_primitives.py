#!/usr/bin/env python3
"""Generate the 13 SFX primitives that back the Monitor-scene sound macros.

Output: 11025 Hz mono 16-bit PCM WAV. Each primitive is sized for one
event in a SoundMacro — short, transient, no loops. The runtime layers
+ pitch-shifts these via SoundMacroSequencer events to compose larger
SFX (impacts, UI tones, atmosphere). See docs/sound-macro-plan.md for
which macros use which primitives.

Synthesis is deterministic (seeded RNG) so re-running this script
produces byte-identical output. Tunable knobs per-primitive at the top
of each function — change a number, re-run, re-export.

Run:
    python tools/synth_sfx_primitives.py

Outputs land in:
    godot-ps1/assets/audio/sfx_primitives/*.wav
"""

import os
import wave
import numpy as np

SR = 11025

OUT_DIR = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "godot-ps1", "assets", "audio", "sfx_primitives")


# ─────────────────────────── helpers ───────────────────────────

def to_wav16(path: str, y: np.ndarray) -> None:
    """Write a 1-D float numpy array (range ~[-1,1]) as 16-bit mono PCM WAV."""
    y = np.clip(y, -1.0, 1.0)
    pcm = (y * 32767.0).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"  wrote {os.path.basename(path)}  ({len(pcm)} samples / {len(pcm)/SR:.2f}s)")


def normalize(y: np.ndarray, peak: float = 0.85) -> np.ndarray:
    """Scale so max(abs) hits `peak`. Avoids div-by-zero on silent buffers."""
    p = max(0.001, np.max(np.abs(y)))
    return y * (peak / p)


def lowpass(y: np.ndarray, alpha: float) -> np.ndarray:
    """One-pole IIR lowpass. alpha small = darker; 1.0 = pass-through."""
    out = np.zeros_like(y)
    out[0] = y[0] * alpha
    for i in range(1, len(y)):
        out[i] = alpha * y[i] + (1 - alpha) * out[i - 1]
    return out


def highpass(y: np.ndarray, alpha: float) -> np.ndarray:
    """One-pole HPF. Output = input - LPF(input). alpha tunes corner."""
    return y - lowpass(y, alpha)


def bandpass(y: np.ndarray, lo_alpha: float, hi_alpha: float) -> np.ndarray:
    """Coarse band-pass via cascaded LPF(HPF). For colouring noise; not surgical."""
    return lowpass(highpass(y, hi_alpha), lo_alpha)


def env_exp(n: int, tau_s: float) -> np.ndarray:
    """Exponential decay envelope, time constant in seconds."""
    return np.exp(-(np.arange(n) / SR) / tau_s)


def env_attack_decay(n: int, attack_s: float, tau_s: float) -> np.ndarray:
    """Linear attack into exponential decay. Standard percussive shape."""
    e = env_exp(n, tau_s)
    a = int(attack_s * SR)
    if a > 0:
        e[:a] *= np.linspace(0, 1, a)
    return e


# ─────────────────────────── primitives ───────────────────────────

def synth_wood_thud_short(duration_s: float = 0.45) -> np.ndarray:
    """Deep wood impact. Sine fundamental + 2nd harmonic + noise body,
    fast amp decay. Slight pitch slide on attack adds 'wood' resonance."""
    n = int(SR * duration_s)
    t = np.arange(n) / SR
    f0 = 95.0  # Hz — body resonance
    pitch_env = 1.0 + 0.10 * np.exp(-t / 0.02)  # quick slide-down at attack
    phase = 2 * np.pi * np.cumsum(f0 * pitch_env) / SR
    y  = 1.00 * np.sin(phase)
    y += 0.45 * np.sin(2 * phase + 0.4)
    y += 0.20 * np.sin(3.7 * phase + 1.1)  # inharmonic — slightly woody
    # Attack noise burst (~30 ms) for the strike component.
    rng = np.random.default_rng(11)
    burst_n = int(0.03 * SR)
    burst = rng.uniform(-1, 1, burst_n) * np.linspace(1, 0, burst_n) ** 2
    burst = lowpass(burst, 0.25)
    y[:burst_n] += burst * 0.35
    y *= env_attack_decay(n, 0.003, 0.10)
    return normalize(y, 0.9)


def synth_wood_splinter(duration_s: float = 0.40) -> np.ndarray:
    """High wood crack. Bandpassed noise burst centred ~2.5 kHz, two
    sub-cracks for splinter character. No tonal component."""
    n = int(SR * duration_s)
    rng = np.random.default_rng(13)
    y = rng.uniform(-1, 1, n)
    y = bandpass(y, lo_alpha=0.30, hi_alpha=0.10)
    # Two amp peaks for the "crrrack-tk" feel.
    env = env_exp(n, 0.12)
    secondary_at = int(0.06 * SR)
    secondary = np.zeros(n)
    if secondary_at + 64 < n:
        sec_n = n - secondary_at
        secondary[secondary_at:] = env_exp(sec_n, 0.05) * 0.6
    y *= (env + secondary)
    return normalize(y, 0.85)


def synth_electrical_pop(duration_s: float = 0.30) -> np.ndarray:
    """Sharp electrical pop. Filtered noise + click transient. Used
    everywhere — bulb pop, CRT click, CRT shutdown ignition."""
    n = int(SR * duration_s)
    rng = np.random.default_rng(17)
    y = rng.uniform(-1, 1, n)
    y = bandpass(y, lo_alpha=0.50, hi_alpha=0.20)
    # Sharp click on first 10 ms — square wave half-cycle.
    click_n = int(0.005 * SR)
    if click_n > 0:
        y[:click_n] += np.sign(np.sin(2 * np.pi * 800 * np.arange(click_n) / SR)) * 0.6
    y *= env_exp(n, 0.04)
    return normalize(y, 0.9)


def synth_glass_tinkle(duration_s: float = 0.40) -> np.ndarray:
    """Glass shard tinkle. Three high inharmonic partials (3-7 kHz range)
    with staggered onsets and fast individual decays."""
    n = int(SR * duration_s)
    t = np.arange(n) / SR
    y = np.zeros(n)
    # (freq, amp, attack_offset_s, decay_tau_s)
    shards = [
        (3200.0, 1.0, 0.000, 0.08),
        (4750.0, 0.7, 0.030, 0.06),
        (6400.0, 0.5, 0.070, 0.05),
        (5300.0, 0.6, 0.110, 0.05),
    ]
    for f, a, off, tau in shards:
        start = int(off * SR)
        if start >= n:
            continue
        seg_n = n - start
        seg_t = np.arange(seg_n) / SR
        partial = a * np.sin(2 * np.pi * f * seg_t) * np.exp(-seg_t / tau)
        y[start:] += partial
    # A whisper of broadband noise gives the "shard scatter" texture.
    rng = np.random.default_rng(19)
    noise = rng.uniform(-1, 1, n)
    noise = highpass(noise, 0.05)
    y += noise * env_exp(n, 0.05) * 0.15
    return normalize(y, 0.8)


def synth_metal_strike(duration_s: float = 0.40) -> np.ndarray:
    """Metallic strike transient. Inharmonic partials in 800-3000 Hz with
    sharp attack. Pairs with metal_ring_short for full clang."""
    n = int(SR * duration_s)
    t = np.arange(n) / SR
    y = np.zeros(n)
    # Frequency partials picked off-ratio (NOT integer multiples) — that's
    # what makes metal sound metallic rather than tonal.
    partials = [
        (840.0,  1.00, 0.10),
        (1310.0, 0.70, 0.08),
        (2050.0, 0.50, 0.06),
        (2940.0, 0.35, 0.05),
    ]
    for f, a, tau in partials:
        y += a * np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    # Strike noise burst (~15 ms) — sharper than wood, less mid.
    rng = np.random.default_rng(23)
    burst_n = int(0.015 * SR)
    burst = rng.uniform(-1, 1, burst_n) * np.linspace(1, 0, burst_n) ** 2
    burst = bandpass(burst, lo_alpha=0.55, hi_alpha=0.12)
    y[:burst_n] += burst * 0.5
    y *= env_attack_decay(n, 0.001, 0.18)
    return normalize(y, 0.9)


def synth_metal_ring_short(duration_s: float = 0.55) -> np.ndarray:
    """Sustained metallic ring. Same partial set as metal_strike but
    longer decay and no strike noise — the body of the clang. Macros
    layer this at frame 0 with the strike, then re-trigger at lower
    pitch and volume for echo events."""
    n = int(SR * duration_s)
    t = np.arange(n) / SR
    y = np.zeros(n)
    partials = [
        (840.0,  1.00, 0.45),
        (1310.0, 0.65, 0.30),
        (2050.0, 0.40, 0.20),
        (2940.0, 0.25, 0.14),
        (4100.0, 0.12, 0.08),
    ]
    for f, a, tau in partials:
        y += a * np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    # Soft attack so layering with metal_strike doesn't double-click.
    y *= env_attack_decay(n, 0.010, 0.45)
    return normalize(y, 0.85)


def synth_debris_small(duration_s: float = 0.35) -> np.ndarray:
    """Small chunk bouncing / scattering. 2-3 short noise grains with
    irregular spacing. Mid-band, low overall energy."""
    n = int(SR * duration_s)
    rng = np.random.default_rng(29)
    y = np.zeros(n)
    # Three grains at irregular tick offsets.
    grain_offsets_s = [0.000, 0.080, 0.165]
    grain_amps      = [1.00,  0.65,  0.40]
    for off_s, amp in zip(grain_offsets_s, grain_amps):
        start = int(off_s * SR)
        grain_n = int(0.06 * SR)
        if start + grain_n > n:
            grain_n = n - start
        if grain_n <= 0:
            break
        grain = rng.uniform(-1, 1, grain_n)
        grain = bandpass(grain, lo_alpha=0.40, hi_alpha=0.15)
        grain *= np.exp(-np.arange(grain_n) / SR / 0.025) * amp
        y[start:start + grain_n] += grain
    return normalize(y, 0.75)


def synth_whine_descend(duration_s: float = 0.50) -> np.ndarray:
    """Falling-pitch whine — dying CRT / power-down character. Single
    tone with FM modulation, frequency falls from 1.6 kHz to ~200 Hz."""
    n = int(SR * duration_s)
    t = np.arange(n) / SR
    # Exponential pitch fall: f(t) = f_end + (f_start - f_end) * exp(-t/tau)
    f_start, f_end, tau = 1600.0, 200.0, 0.18
    f_inst = f_end + (f_start - f_end) * np.exp(-t / tau)
    phase = 2 * np.pi * np.cumsum(f_inst) / SR
    # Slight FM via a 4-Hz LFO for instability.
    fm = 0.03 * np.sin(2 * np.pi * 4.0 * t)
    y = np.sin(phase + fm)
    # Add a 2nd harmonic at half amplitude for body.
    y += 0.4 * np.sin(2 * (phase + fm))
    # Slow attack, slow decay — the whine takes a moment to bloom and
    # then fades naturally.
    y *= env_attack_decay(n, 0.020, 0.25)
    return normalize(y, 0.8)


def synth_air_whoosh_short(duration_s: float = 0.40) -> np.ndarray:
    """Air-rush whoosh. Bandpassed noise with a slow attack/release
    envelope and a sweeping band centre — gives directional motion."""
    n = int(SR * duration_s)
    rng = np.random.default_rng(31)
    raw = rng.uniform(-1, 1, n)
    # Two parallel band-passes with different cutoffs, swept by a
    # half-cycle envelope so the band rises and falls.
    sweep = np.sin(np.pi * np.arange(n) / n)  # 0 → 1 → 0
    y = np.zeros(n)
    # Coarse approximation: blend a low-band and high-band noise stream
    # by the sweep amount.
    low_band  = bandpass(raw, lo_alpha=0.25, hi_alpha=0.05)
    high_band = bandpass(raw, lo_alpha=0.45, hi_alpha=0.20)
    y = low_band * (1 - sweep) + high_band * sweep
    # Slow attack/release.
    env = np.sin(np.pi * np.arange(n) / n) ** 1.5
    y *= env
    return normalize(y, 0.7)


def synth_noise_burst_short(duration_s: float = 0.30) -> np.ndarray:
    """Pure-ish broadband noise burst. White noise with a fast attack
    and exponential decay. Used for static / breakup."""
    n = int(SR * duration_s)
    rng = np.random.default_rng(37)
    y = rng.uniform(-1, 1, n)
    # Tilt slightly toward pink (lowpass at high alpha = mostly white).
    y = lowpass(y, 0.65)
    y *= env_attack_decay(n, 0.002, 0.06)
    return normalize(y, 0.85)


def synth_whisper_short(duration_s: float = 0.45) -> np.ndarray:
    """Whispered-voice texture. Bandpassed noise centred ~1.5 kHz with
    formant-like dual peaks (mimics 's', 'sh' sibilants), gentle AM
    modulation gives breath rhythm."""
    n = int(SR * duration_s)
    t = np.arange(n) / SR
    rng = np.random.default_rng(41)
    raw = rng.uniform(-1, 1, n)
    # Two formant peaks via cascaded BPFs at different centres.
    f1 = bandpass(raw, lo_alpha=0.35, hi_alpha=0.18)  # ~1500 Hz
    f2 = bandpass(raw, lo_alpha=0.55, hi_alpha=0.30)  # ~2800 Hz
    y = f1 * 0.7 + f2 * 0.4
    # Breath AM: ~6 Hz LFO with offset so it doesn't mute.
    am = 0.55 + 0.45 * np.sin(2 * np.pi * 6.0 * t)
    y *= am
    # Soft attack/release envelope.
    env = np.sin(np.pi * np.arange(n) / n) ** 1.2
    y *= env
    return normalize(y, 0.65)  # whispers should be subtle


def synth_ui_blip_high(duration_s: float = 0.20) -> np.ndarray:
    """High UI tone. Sine + 2nd harmonic with snappy attack/decay.
    Macros pitch-shift this for variants (cursor move, confirm)."""
    n = int(SR * duration_s)
    t = np.arange(n) / SR
    f = 1320.0  # E6-ish
    y  = 1.00 * np.sin(2 * np.pi * f * t)
    y += 0.30 * np.sin(2 * np.pi * f * 2 * t)
    y *= env_attack_decay(n, 0.002, 0.06)
    return normalize(y, 0.8)


def synth_ui_blip_low(duration_s: float = 0.20) -> np.ndarray:
    """Low UI tone. Same shape as ui_blip_high, lower fundamental.
    Pairs with high blip for two-tone notifications."""
    n = int(SR * duration_s)
    t = np.arange(n) / SR
    f = 660.0  # E5-ish
    y  = 1.00 * np.sin(2 * np.pi * f * t)
    y += 0.35 * np.sin(2 * np.pi * f * 2 * t)
    y += 0.15 * np.sin(2 * np.pi * f * 3 * t)
    y *= env_attack_decay(n, 0.002, 0.07)
    return normalize(y, 0.8)


# ─────────────────────────── driver ───────────────────────────

PRIMITIVES = [
    ("wood_thud_short.wav",  synth_wood_thud_short),
    ("wood_splinter.wav",    synth_wood_splinter),
    ("electrical_pop.wav",   synth_electrical_pop),
    ("glass_tinkle.wav",     synth_glass_tinkle),
    ("metal_strike.wav",     synth_metal_strike),
    ("metal_ring_short.wav", synth_metal_ring_short),
    ("debris_small.wav",     synth_debris_small),
    ("whine_descend.wav",    synth_whine_descend),
    ("air_whoosh_short.wav", synth_air_whoosh_short),
    ("noise_burst_short.wav",synth_noise_burst_short),
    ("whisper_short.wav",    synth_whisper_short),
    ("ui_blip_high.wav",     synth_ui_blip_high),
    ("ui_blip_low.wav",      synth_ui_blip_low),
]


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    print(f"Output dir: {os.path.abspath(OUT_DIR)}")
    for name, fn in PRIMITIVES:
        to_wav16(os.path.join(OUT_DIR, name), fn())


if __name__ == "__main__":
    main()
