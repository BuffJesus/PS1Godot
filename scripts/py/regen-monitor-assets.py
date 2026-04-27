#!/usr/bin/env python3
"""
Regenerate procedural placeholder assets for The Monitor jam game.

Source of truth for what each clip/texture is supposed to be:
  docs/the-monitor-asset-prompts.md

Output:
  godot-ps1/assets/monitor/audio/*.wav  (22050 Hz, mono, 16-bit PCM)
  godot-ps1/assets/monitor/textures/*.png

Final shippable assets are CC0-sourced, hand-authored, or AI-generated.
This script keeps a recognizable placeholder under every name the code
references; with --ai openai it instead generates textures via the
OpenAI image API for nicer-looking placeholders during playtesting.

Usage:
  python scripts/regen-monitor-assets.py [--audio | --textures | --all]
  python scripts/regen-monitor-assets.py --textures --ai openai

Requires: ffmpeg in PATH, Python 3.10+, Pillow.
For --ai openai: pip install openai, OPENAI_API_KEY env var.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
AUDIO_OUT = ROOT / "godot-ps1" / "assets" / "monitor" / "audio"
TEX_OUT = ROOT / "godot-ps1" / "assets" / "monitor" / "textures"

SR = 22050  # PSX SPU sample rate target


# ──────────────────────────────────────────────────────────────────────
# Audio: ffmpeg lavfi filter graphs per clip
# ──────────────────────────────────────────────────────────────────────
#
# Each entry: (name, duration_seconds, lavfi filter graph). The graph
# must end at a single mono stream; we pipe it through `aformat=mono` +
# `aresample=22050` and write 16-bit PCM. Placeholders aim for "the
# right SHAPE of the sound" — pitch, transient profile, rhythm — even
# if the timbre is obviously synthetic. The goal is that
# `door_creak.wav` is audibly different from `box_crash.wav` so a
# playtester can identify which event fired by ear.
# ──────────────────────────────────────────────────────────────────────

AUDIO_CLIPS: list[tuple[str, float, str]] = [
    # ── Resident audio ───────────────────────────────────────────────
    # Low industrial drone: 55 Hz fundamental + 110 Hz octave + brown
    # noise rumble, amix'd. Loops cleanly because every component is
    # stationary.
    ("ambient_drone", 8.0,
     "sine=f=55:sample_rate=22050,volume=0.34[a];"
     "sine=f=110:sample_rate=22050,volume=0.10[b];"
     "anoisesrc=color=brown:r=22050,volume=0.18,lowpass=f=180[c];"
     "[a][b][c]amix=inputs=3:normalize=0,volume=0.55"),

    # Mechanical click: 0.12 s 820 Hz sine, sharp 4 ms attack, fast decay.
    ("crt_click", 0.18,
     "sine=f=820:sample_rate=22050,"
     "afade=t=in:st=0:d=0.004,afade=t=out:st=0.04:d=0.14,volume=0.85"),

    # Static burst: white noise + low thump, 0.3 s.
    ("crt_static_short", 0.30,
     "anoisesrc=color=white:r=22050,volume=0.55,"
     "afade=t=in:st=0:d=0.005,afade=t=out:st=0.18:d=0.12"),

    # ── Per-feed room tones (4 s loops) ──────────────────────────────
    ("hum_low", 4.0,
     "sine=f=60:sample_rate=22050,volume=0.45[a];"
     "anoisesrc=color=brown:r=22050,volume=0.10,lowpass=f=140[b];"
     "[a][b]amix=inputs=2:normalize=0"),

    ("hum_fluor", 4.0,
     "sine=f=120:sample_rate=22050,volume=0.30[a];"
     "sine=f=240:sample_rate=22050,volume=0.18[b];"
     "[a][b]amix=inputs=2:normalize=0,"
     "tremolo=f=2:d=0.35,volume=0.55"),

    ("hum_outside", 4.0,
     "anoisesrc=color=brown:r=22050,volume=0.50,lowpass=f=240[r];"
     "sine=f=2200:sample_rate=22050,volume=0.05,bandpass=f=2200:width_type=h:w=400[c];"
     "[r][c]amix=inputs=2:normalize=0,volume=0.7"),

    ("hum_electrical", 4.0,
     "sine=f=1500:sample_rate=22050,volume=0.18[a];"
     "sine=f=3000:sample_rate=22050,volume=0.06[b];"
     "anoisesrc=color=pink:r=22050,volume=0.10,highpass=f=1200[c];"
     "[a][b][c]amix=inputs=3:normalize=0"),

    # ── Event SFX (14) ───────────────────────────────────────────────
    # Distant car alarm — sine gated by a slow tremolo + lowpass to fake
    # "through-walls" muffling. Tremolo at 4 Hz gives 4 yelps/second.
    ("alarm_distant", 3.0,
     "sine=f=900:sample_rate=22050,"
     "tremolo=f=4:d=0.95,lowpass=f=1500,volume=0.45"),

    # Door creak — 300 Hz sine with deep vibrato. Vibrato filter handles
    # the FM sweep so we don't have to fight aevalsrc's expression syntax.
    ("door_creak", 2.0,
     "sine=f=300:sample_rate=22050,"
     "vibrato=f=6:d=0.7,"
     "afade=t=in:st=0:d=0.05,afade=t=out:st=1.7:d=0.3,volume=0.6"),

    # Cardboard box drop on concrete — pink noise impact + thump.
    ("box_crash", 1.0,
     "anoisesrc=color=pink:r=22050,volume=0.85,"
     "afade=t=in:st=0:d=0.003,afade=t=out:st=0.05:d=0.5,"
     "lowpass=f=2200"),

    # Office chair rotating — quiet rolling rumble + brief squeak.
    ("chair_roll", 1.5,
     "anoisesrc=color=brown:r=22050,volume=0.30,lowpass=f=400[r];"
     "sine=f=1800:sample_rate=22050,volume=0.20,"
     "afade=t=in:st=0.6:d=0.04,afade=t=out:st=0.7:d=0.12[s];"
     "[r][s]amix=inputs=2:normalize=0"),

    # 3 slow footsteps — pink noise gated by a frame-evaluated volume
    # expression so the noise hits at 0s, 1s, 2s for ~0.15s each.
    ("footsteps_slow", 3.0,
     "anoisesrc=color=pink:r=22050,lowpass=f=1100,"
     "volume='if(lt(mod(t\\,1)\\,0.15)\\,0.85\\,0)':eval=frame"),

    # Bulb pop — short electrical buzz that cuts to a sharp click.
    ("bulb_pop", 0.5,
     "sine=f=120:sample_rate=22050,volume=0.30[buzz];"
     "anoisesrc=color=white:r=22050,volume=0.7,"
     "afade=t=in:st=0.32:d=0.005,afade=t=out:st=0.36:d=0.08[click];"
     "[buzz]afade=t=out:st=0.30:d=0.02[buzz2];"
     "[buzz2][click]amix=inputs=2:normalize=0"),

    # Wood pallet creak — low sine with vibrato + envelope. Different
    # pitch / vibrato rate than door_creak so they read as distinct.
    ("wood_creak", 1.5,
     "sine=f=140:sample_rate=22050,"
     "vibrato=f=4:d=0.6,lowpass=f=400,"
     "afade=t=in:st=0:d=0.05,afade=t=out:st=1.2:d=0.3,volume=0.55"),

    # CRT TV dies — buzz to high whine to silence.
    ("crt_die", 1.0,
     "sine=f=120:sample_rate=22050,volume=0.30[a];"
     "sine=f=8000:sample_rate=22050,volume=0.15,"
     "afade=t=in:st=0.4:d=0.05,afade=t=out:st=0.85:d=0.15[b];"
     "[a]afade=t=out:st=0.5:d=0.1[a2];"
     "[a2][b]amix=inputs=2:normalize=0"),

    # Whisper — bandpassed noise (vocal range), short.
    ("shadow_whisper", 1.0,
     "anoisesrc=color=white:r=22050,volume=0.35,"
     "bandpass=f=1800:width_type=h:w=900,"
     "afade=t=in:st=0:d=0.1,afade=t=out:st=0.7:d=0.3"),

    # Metal grate falling — sharp transient + ringing high tone.
    ("metal_clang", 1.5,
     "anoisesrc=color=white:r=22050,volume=0.95,"
     "afade=t=in:st=0:d=0.002,afade=t=out:st=0.04:d=0.04,"
     "highpass=f=400[hit];"
     "sine=f=2400:sample_rate=22050,volume=0.45,"
     "afade=t=in:st=0.02:d=0.005,afade=t=out:st=0.4:d=1.0[ring];"
     "[hit][ring]amix=inputs=2:normalize=0"),

    # Lights dimming — slow tremolo on pink noise gives the
    # whoa-everything-just-pulsed feeling without clicks.
    ("light_dim", 2.0,
     "anoisesrc=color=pink:r=22050,lowpass=f=900,"
     "tremolo=f=0.6:d=0.85,volume=0.55"),

    # Box sliding — filtered noise sweep, single shove.
    ("box_slide", 1.0,
     "anoisesrc=color=pink:r=22050,volume=0.55,lowpass=f=1500,"
     "afade=t=in:st=0:d=0.05,afade=t=out:st=0.5:d=0.5"),

    # Heavy breathing — slow tremolo on brown noise (~0.4 Hz =
    # in/out every 1.25 s, roughly the cadence of slow breathing).
    ("breathing_low", 4.0,
     "anoisesrc=color=brown:r=22050,lowpass=f=600,"
     "tremolo=f=0.4:d=0.9,volume=0.7"),

    # Body thud — very low frequency impact.
    ("thud", 0.5,
     "sine=f=70:sample_rate=22050,"
     "afade=t=in:st=0:d=0.005,afade=t=out:st=0.05:d=0.4,volume=0.95"),

    # ── UI SFX ───────────────────────────────────────────────────────
    # Two-tone ascending chirp — old computer "noted" beep.
    ("log_confirm", 0.20,
     "sine=f=600:sample_rate=22050,volume=0.6,"
     "afade=t=in:st=0:d=0.005,afade=t=out:st=0.07:d=0.02[a];"
     "sine=f=800:sample_rate=22050,volume=0.6,"
     "afade=t=in:st=0.10:d=0.005,afade=t=out:st=0.16:d=0.03[b];"
     "[a][b]amix=inputs=2:normalize=0"),

    # Descending stinger — minor-third chord (A4 + C5) detuned via
    # vibrato for slight unease. Linear pitch sweep would need
    # aevalsrc; the ear barely notices the difference at 3 s.
    ("shift_end_stinger", 3.0,
     "sine=f=440:sample_rate=22050,volume=0.40,"
     "vibrato=f=0.3:d=0.5,"
     "afade=t=in:st=0:d=0.1,afade=t=out:st=2.4:d=0.6[a];"
     "sine=f=523:sample_rate=22050,volume=0.30,"
     "vibrato=f=0.3:d=0.5,"
     "afade=t=in:st=0:d=0.1,afade=t=out:st=2.4:d=0.6[b];"
     "[a][b]amix=inputs=2:normalize=0"),
]


# ── ElevenLabs SFX prompts (used by --ai-audio elevenlabs) ──
# Plain-language descriptions of each clip. Names mirror AUDIO_CLIPS so
# `regen_audio` can match by name. Durations come from AUDIO_CLIPS too,
# clamped to ElevenLabs's 0.5–22 s valid range.
AI_AUDIO_PROMPTS = {
    "ambient_drone":     "Continuous low industrial drone, 60Hz electrical hum mixed with HVAC rumble and distant fluorescent buzz. Empty 1990s warehouse at 3 AM. No melody, no rhythm, just dread.",
    "crt_click":         "Single mechanical click of an old TV channel selector knob, dry, no reverb.",
    "crt_static_short":  "Short burst of analog TV static, full bandwidth white noise with a slight low-end thump at the start.",
    "hum_low":           "Low electrical 60Hz hum with a faint footstep echo every couple of seconds. Long empty corridor ambience.",
    "hum_fluor":         "Buzzing fluorescent light tube with a slight ballast flicker, dry, no reverb.",
    "hum_outside":       "Distant outdoor city ambience: faint highway, cricket, occasional very-distant car.",
    "hum_electrical":    "High-pitched CRT monitor whine plus low PC fan, claustrophobic.",
    "alarm_distant":     "Faraway car alarm, muffled by walls and distance.",
    "door_creak":        "Heavy metal fire door slowly opening, single creak rising in pitch then settling, no slam.",
    "box_crash":         "Cardboard box falling from height onto concrete, single impact with a slight rattle of contents.",
    "chair_roll":        "Office chair on wheels rotating, faint plastic-on-vinyl roll, single small squeak.",
    "footsteps_slow":    "Three slow heavy footsteps on hard floor at moderate distance.",
    "bulb_pop":          "Single fluorescent bulb dying: faint electrical buzz cutting out into a dry pop.",
    "wood_creak":        "Wooden pallet shifting under weight, single creak.",
    "crt_die":           "Old CRT television flickering off: short electrical buzz cutting to a high whine then silence.",
    "shadow_whisper":    "Single short breathy whisper, indecipherable, faintly threatening, very quiet.",
    "metal_clang":       "Metal vent grate falling from a wall and clattering on concrete. Sharp transient followed by ringing decay.",
    "light_dim":         "Multiple fluorescent lights dimming and brightening: subtle whirr modulation, no clicks.",
    "box_slide":         "Cardboard box sliding rapidly across a tile floor, single shove.",
    "breathing_low":     "Slow heavy human breathing, very close mic, no words, slightly labored.",
    "thud":              "Single dull body-weight thud on concrete, no echo.",
    "log_confirm":       "Short ascending two-tone synth chirp, friendly but minimal. Like an old computer 'noted' beep.",
    "shift_end_stinger": "Descending synth pad, ominous resolution, dry. Like an arcade game-over but slow and dignified.",
}


def generate_audio_via_elevenlabs(name: str, prompt: str, duration: float) -> bool:
    """Call ElevenLabs sound-generation, transcode MP3 → 22050 Hz mono WAV.
    Returns True on success, False if the API call failed (caller falls
    back to procedural ffmpeg synthesis)."""
    import json
    import tempfile
    import urllib.error
    import urllib.request

    api_key = os.environ.get("ELEVENLABS_API_KEY")
    if not api_key:
        print(f"    SKIP: ELEVENLABS_API_KEY not set. Falling back to procedural "
              f"for {name}.wav.")
        return False

    # ElevenLabs valid range is 0.5 to 22 s; clamp.
    dur = max(0.5, min(22.0, duration))

    body = json.dumps({
        "text": prompt,
        "duration_seconds": dur,
        "prompt_influence": 0.5,
    }).encode("utf-8")

    req = urllib.request.Request(
        "https://api.elevenlabs.io/v1/sound-generation",
        data=body,
        method="POST",
        headers={
            "xi-api-key": api_key,
            "Content-Type": "application/json",
            "Accept": "audio/mpeg",
        },
    )

    print(f"  ElevenLabs: {name}.wav  ({dur:.2f}s)...")

    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            mp3_bytes = resp.read()
    except urllib.error.HTTPError as e:
        body_text = e.read().decode("utf-8", errors="replace")[:200]
        print(f"    FAILED: HTTP {e.code} {e.reason}: {body_text}")
        return False
    except urllib.error.URLError as e:
        print(f"    FAILED: {e}")
        return False

    # MP3 → WAV at our PSX target spec via ffmpeg.
    with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as f:
        f.write(mp3_bytes)
        mp3_path = f.name
    try:
        out_wav = AUDIO_OUT / f"{name}.wav"
        cmd = [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-i", mp3_path,
            "-ar", str(SR), "-ac", "1", "-acodec", "pcm_s16le",
            str(out_wav),
        ]
        subprocess.run(cmd, check=True, capture_output=True, text=True)
    except subprocess.CalledProcessError as e:
        print(f"    FAILED: ffmpeg conversion: "
              f"{e.stderr.strip().splitlines()[-1] if e.stderr else e}")
        return False
    finally:
        try:
            os.unlink(mp3_path)
        except OSError:
            pass

    return True


def regen_audio(ai_provider: str = "") -> None:
    AUDIO_OUT.mkdir(parents=True, exist_ok=True)
    if not shutil.which("ffmpeg"):
        sys.exit("ffmpeg not found in PATH; install it before running --audio.")

    # AI pass first. Track which clips AI successfully wrote so the
    # procedural ffmpeg block below skips them.
    ai_succeeded: set[str] = set()
    if ai_provider == "elevenlabs":
        for name, dur, _graph in AUDIO_CLIPS:
            prompt = AI_AUDIO_PROMPTS.get(name)
            if prompt is None:
                continue
            if generate_audio_via_elevenlabs(name, prompt, dur):
                ai_succeeded.add(name)

    for name, dur, graph in AUDIO_CLIPS:
        if name in ai_succeeded:
            continue
        out = AUDIO_OUT / f"{name}.wav"
        cmd = [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-t", f"{dur}", "-i", graph,
            "-af", f"aresample={SR},aformat=channel_layouts=mono:sample_fmts=s16",
            "-ar", str(SR), "-ac", "1",
            str(out),
        ]
        print(f"  audio: {name}.wav  ({dur:.2f}s)")
        try:
            subprocess.run(cmd, check=True, capture_output=True, text=True)
        except subprocess.CalledProcessError as e:
            print(f"    FAILED: {e.stderr.strip().splitlines()[-1] if e.stderr else e}",
                  file=sys.stderr)
            raise


# ──────────────────────────────────────────────────────────────────────
# Textures: PIL
# ──────────────────────────────────────────────────────────────────────
#
# These are placeholders meant to (a) confirm the import pipeline works
# end-to-end and (b) give the room geometry SOMETHING textured rather
# than flat-color. Final art will replace them. Authoring rules:
#   - PNG, RGBA, no alpha unless intentional (bezel + scanlines need it).
#   - Author at the listed dim; the plugin's importer quantizes to 4/8bpp.
#   - Hard pixel edges; no soft gradients (gets eaten by quantization).

# ── OpenAI image-gen prompts (used by --ai openai) ──
# Tighter than the markdown prompts in `the-monitor-asset-prompts.md` —
# heavier "pixel art / no AA / hard edges" framing because gpt-image-1
# defaults to photographic. Five textures benefit from AI; font_hud and
# scanlines stay procedural (too structured for a generative model to
# get right).
AI_TEXTURE_PROMPTS = {
    "atlas_industrial": (
        "Pixel art texture of a worn grey concrete floor, top-down "
        "orthographic view, seamless tiling pattern. Visible cracks, "
        "scattered dark pebbles, lighter chips, two thin horizontal "
        "expansion joints. Limited palette of greys and beiges. "
        "Hard pixel edges, no anti-aliasing, no blur, no soft gradients. "
        "PSX-era 1995 video game texture, 256x256 final resolution. "
        "Industrial warehouse / mall back-of-house aesthetic. "
        "Low saturation, dirty, lived-in."
    ),
    "atlas_props": (
        "Pixel art texture atlas, 4 by 4 grid of distinct prop face "
        "textures viewed orthographically. Cells show: cardboard box "
        "with packing tape, beige metal filing cabinet with drawer "
        "pulls, beige CRT computer monitor with dark screen, lit "
        "fluorescent ceiling fixture, scuffed wooden door panel, "
        "padlocked locker grid, plain dull red metal panel, plain "
        "beige plaster wall. PSX-era 1995 video game texture, hard "
        "pixel edges, no anti-aliasing, limited muted palette. "
        "IMPORTANT: do not draw any text, letters, words, captions, "
        "labels, watermarks, or signs anywhere in the image."
    ),
    "atlas_figures": (
        "Pixel art texture sheet, fully opaque solid 128 by 128 pixel "
        "image divided into 4 equal 64 by 64 quadrants. Top-left "
        "quadrant: solid navy blue fabric with subtle vertical "
        "pinstripes. Top-right quadrant: solid olive green fabric "
        "with subtle stitching texture. Bottom-left quadrant: pure "
        "solid black, fully filled. Bottom-right quadrant: pale skin "
        "tone showing a simple face with two small black eye dots "
        "and a short dark mouth line. PSX-era 1995 video game "
        "texture, hard pixel edges, no anti-aliasing. IMPORTANT: "
        "the image must be fully opaque with no transparent pixels "
        "and no text or letters anywhere."
    ),
    "bezel_crt": (
        "Pixel art image of a simple thick black picture frame against "
        "a plain white background. The black frame is a single "
        "rectangle, 12 pixels thick on the left and right sides, 16 "
        "pixels thick on the top and bottom edges, surrounding a plain "
        "empty white interior. Single frame only with no inner borders "
        "or decorative trim. A tiny bright red dot in the bottom-right "
        "corner of the black frame. Retro 1995 video game pixel art "
        "style, hard pixel edges, no anti-aliasing, no text or letters."
    ),
    "static_noise": (
        "Pixel art seamless tiling texture of analog TV static noise, "
        "256x256. High contrast black-white-grey salt-and-pepper noise. "
        "No structure, no patterns, just dense pixel noise. Faint "
        "horizontal scanline banding. PSX-era 1995 video game texture."
    ),
}

# Target dimensions per texture (matches the procedural generators).
TEXTURE_TARGETS = {
    "atlas_industrial": (256, 256),
    "atlas_props":      (256, 256),
    "atlas_figures":    (128, 128),
    "bezel_crt":        (320, 240),
    "static_noise":     (256, 256),
}


def generate_texture_via_openai(name: str, prompt: str, target: tuple,
                                quality: str) -> bool:
    """Call OpenAI image-gen API, write `name`.png at `target` size.
    Returns True on success, False if the API call failed (caller falls
    back to procedural)."""
    try:
        from openai import OpenAI
    except ImportError:
        print(f"    SKIP: openai SDK not installed (pip install openai). "
              f"Falling back to procedural for {name}.png.")
        return False

    if not os.environ.get("OPENAI_API_KEY"):
        print(f"    SKIP: OPENAI_API_KEY not set. Falling back to procedural "
              f"for {name}.png.")
        return False

    import base64
    import io
    from PIL import Image

    # Pick the closest API-supported size to our target aspect ratio.
    # gpt-image-1 supports 1024x1024 / 1024x1536 / 1536x1024.
    aspect = target[0] / target[1]
    if aspect > 1.2:
        api_size = "1536x1024"
    elif aspect < 0.83:
        api_size = "1024x1536"
    else:
        api_size = "1024x1024"

    print(f"  OpenAI: {name}.png  ({api_size} -> {target[0]}x{target[1]}, "
          f"quality={quality})...")

    try:
        client = OpenAI()  # reads OPENAI_API_KEY from env
        response = client.images.generate(
            model="gpt-image-1",
            prompt=prompt,
            size=api_size,
            quality=quality,
            n=1,
        )
    except Exception as e:
        print(f"    FAILED: {e}")
        return False

    b64 = response.data[0].b64_json
    if b64 is None:
        print(f"    FAILED: no b64_json in response for {name}")
        return False

    raw = Image.open(io.BytesIO(base64.b64decode(b64)))

    # Center-crop to target aspect ratio so the resize doesn't squish.
    raw_aspect = raw.width / raw.height
    target_aspect = target[0] / target[1]
    if raw_aspect > target_aspect:
        new_w = int(raw.height * target_aspect)
        offset = (raw.width - new_w) // 2
        raw = raw.crop((offset, 0, offset + new_w, raw.height))
    elif raw_aspect < target_aspect:
        new_h = int(raw.width / target_aspect)
        offset = (raw.height - new_h) // 2
        raw = raw.crop((0, offset, raw.width, offset + new_h))

    # Downsample with LANCZOS — preserves detail better than NEAREST when
    # the source is already AA'd. The plugin's import-time quantize will
    # snap to the 4/8bpp palette afterward.
    out = raw.resize(target, Image.LANCZOS).convert("RGBA")
    out.save(TEX_OUT / f"{name}.png")
    return True


def regen_textures(missing_only: bool = False, ai_provider: str = "",
                   ai_quality: str = "medium") -> None:
    from PIL import Image, ImageDraw, ImageFont
    import math
    import random

    TEX_OUT.mkdir(parents=True, exist_ok=True)

    # AI pass first. Track which textures AI successfully wrote so the
    # procedural block below skips them — otherwise --force would
    # immediately overwrite the AI output with the procedural fallback.
    ai_succeeded: set[str] = set()
    if ai_provider == "openai":
        for name, prompt in AI_TEXTURE_PROMPTS.items():
            png = f"{name}.png"
            if missing_only and (TEX_OUT / png).exists():
                continue
            if generate_texture_via_openai(name, prompt,
                                           TEXTURE_TARGETS[name], ai_quality):
                ai_succeeded.add(name)

    def should_write(name: str) -> bool:
        # Strip extension for the AI-handled lookup.
        base = name[:-4] if name.endswith(".png") else name
        if base in ai_succeeded:
            return False
        return (not missing_only) or not (TEX_OUT / name).exists()

    def clamp(v: int) -> int:
        return max(0, min(255, v))

    def add_rgb(c: tuple[int, int, int], delta: int) -> tuple[int, int, int]:
        return (clamp(c[0] + delta), clamp(c[1] + delta), clamp(c[2] + delta))

    # ─── atlas_industrial.png — single coherent 256x256 concrete tile ─
    # NOT a 4x4 grid of solid swatches — auto-UV box faces map the whole
    # atlas to one face, so a swatch grid patchworks badly. A single
    # coherent stippled tile reads cleanly at any UV scale and the per-
    # feed shader tint multiplies through it for color identity.
    if should_write("atlas_industrial.png"):
        random.seed(0xC07C)
        img = Image.new("RGB", (256, 256), (148, 145, 140))
        px = img.load()
        # Base: per-pixel noise + soft top-to-bottom darkening for a worn
        # floor look. Two-octave noise (cell-then-pixel) reads as small-
        # aggregate concrete instead of TV static.
        for y in range(256):
            row_bias = -8 + int(16 * y / 255)
            for x in range(256):
                # Coarse cell tone (8x8) + fine pixel jitter
                cell_n = (((x // 8) * 31 + (y // 8) * 17) % 23) - 11
                fine_n = random.randint(-9, 9)
                v = 148 + row_bias + cell_n + fine_n
                px[x, y] = (clamp(v), clamp(v - 3), clamp(v - 8))
        # Pebbles: ~120 dark 2-3px clusters (the dominant visual feature).
        for _ in range(120):
            cx, cy = random.randint(2, 253), random.randint(2, 253)
            radius = random.choice([1, 1, 1, 2])
            depth = random.randint(40, 70)
            for dy in range(-radius, radius + 1):
                for dx in range(-radius, radius + 1):
                    if dx*dx + dy*dy <= radius*radius and random.random() < 0.75:
                        px[cx + dx, cy + dy] = add_rgb(px[cx + dx, cy + dy], -depth)
        # Highlights: ~40 single-pixel brighter spots (mica / scuffed).
        for _ in range(40):
            cx, cy = random.randint(0, 255), random.randint(0, 255)
            px[cx, cy] = add_rgb(px[cx, cy], 35)
        # Thin diagonal scratches: 12 of them, 20-50px long.
        for _ in range(12):
            x0, y0 = random.randint(0, 255), random.randint(0, 255)
            length = random.randint(20, 50)
            slope = random.choice([0.5, -0.5, 0.7, -0.7, 1.0, -1.0])
            for t in range(length):
                xx = (x0 + t) % 256
                yy = (y0 + int(t * slope)) % 256
                px[xx, yy] = add_rgb(px[xx, yy], -28)
        # Two thin horizontal "construction joint" seams across the tile.
        for joint_y in (random.randint(40, 80), random.randint(160, 220)):
            for x in range(256):
                wobble = (((x * 13) % 7) - 3) // 2
                py = clamp(joint_y + wobble) % 256
                px[x, py] = add_rgb(px[x, py], -50)
        img.save(TEX_OUT / "atlas_industrial.png")
        print("  texture: atlas_industrial.png  (256x256 worn concrete)")

    # ─── atlas_props.png — 4x4 grid of distinct prop-face textures ────
    # Used by per-prop materials in the future (when we author event
    # subjects). Each cell is a 64x64 panel with a recognizable surface
    # design — author can pick the right cell via UV.
    if should_write("atlas_props.png"):
        random.seed(0xC0DE_F00D)
        img = Image.new("RGB", (256, 256), (0, 0, 0))
        px = img.load()

        def fill_cell(idx: int, render):
            """Run `render(local_px, w, h)` against a 64x64 sub-image.
            local_px writes go to the atlas at the cell's offset."""
            cx, cy = (idx % 4) * 64, (idx // 4) * 64
            cell = Image.new("RGB", (64, 64))
            cpx = cell.load()
            render(cpx)
            for y in range(64):
                for x in range(64):
                    px[cx + x, cy + y] = cpx[x, y]

        def cardboard(p):
            base = (172, 138, 92)
            for y in range(64):
                tone = base if (y // 4) % 2 == 0 else add_rgb(base, -14)
                for x in range(64):
                    n = random.randint(-6, 6)
                    p[x, y] = add_rgb(tone, n)
            # Tape rectangle in the middle row.
            for y in range(28, 36):
                for x in range(8, 56):
                    p[x, y] = add_rgb((220, 210, 175), random.randint(-8, 8))

        def filing_cabinet(p):
            base = (110, 110, 116)
            for y in range(64):
                for x in range(64):
                    n = random.randint(-8, 8)
                    p[x, y] = add_rgb(base, n)
            # Four horizontal drawer seams + handle dimples.
            for dy in (15, 31, 47):
                for x in range(2, 62):
                    p[x, dy] = add_rgb(p[x, dy], -45)
                # Handle: thin dark rectangle centered.
                for hy in range(dy - 4, dy - 1):
                    for hx in range(28, 36):
                        p[hx, hy] = (40, 40, 44)

        def crt_monitor(p):
            # Beige casing
            for y in range(64):
                for x in range(64):
                    n = random.randint(-6, 6)
                    p[x, y] = add_rgb((188, 178, 152), n)
            # Inset dark "screen" rectangle
            for y in range(8, 48):
                for x in range(10, 54):
                    edge = (y == 8 or y == 47 or x == 10 or x == 53)
                    p[x, y] = (12, 14, 18) if not edge else (52, 50, 46)
            # Tiny green power LED bottom-right of casing.
            p[50, 54] = (60, 220, 80); p[51, 54] = (60, 220, 80)

        def fluorescent(p):
            # Bright lit rectangle on dark ceiling tile background
            for y in range(64):
                for x in range(64):
                    p[x, y] = add_rgb((90, 92, 96), random.randint(-6, 6))
            for y in range(20, 44):
                for x in range(6, 58):
                    flicker = random.randint(-10, 10)
                    p[x, y] = add_rgb((230, 232, 220), flicker)
            # Dark cap rectangles at left/right ends of the tube.
            for y in range(20, 44):
                for x in range(6, 14):
                    p[x, y] = (42, 42, 46)
                for x in range(50, 58):
                    p[x, y] = (42, 42, 46)

        def wooden_door(p):
            base = (110, 76, 50)
            for y in range(64):
                for x in range(64):
                    grain = (((x * 5 + y) // 4) % 11) - 5
                    n = random.randint(-5, 5)
                    p[x, y] = add_rgb(base, grain + n)
            # Two horizontal panel divisions.
            for dy in (20, 44):
                for x in range(64):
                    p[x, dy] = add_rgb(p[x, dy], -35)
            # Handle (dark dot) right-mid.
            for dy in range(31, 35):
                for dx in range(50, 54):
                    p[dx, dy] = (28, 26, 24)

        def locker_grid(p):
            base = (88, 96, 104)
            for y in range(64):
                for x in range(64):
                    n = random.randint(-8, 8)
                    p[x, y] = add_rgb(base, n)
            # 4 vertical louver slats — 14 px wide with 2 px gaps =
            # 4*14 + 3*2 = 62 px → fits cleanly inside 64.
            for slat in range(4):
                left = 1 + slat * 16
                right = min(left + 13, 63)
                for y in range(8, 56):
                    p[left, y] = add_rgb(p[left, y], -40)
                    p[right, y] = add_rgb(p[right, y], -40)
                # Hinge dot in the center of each slat
                cx_dot = left + 7
                for hy in range(28, 32):
                    for hx in range(max(0, cx_dot - 1), min(64, cx_dot + 2)):
                        p[hx, hy] = (28, 30, 36)

        def exit_sign(p):
            # Red field with white "EXIT" text approximated as 4 white blocks.
            for y in range(64):
                for x in range(64):
                    p[x, y] = add_rgb((180, 30, 30), random.randint(-10, 10))
            # White "X-I-T" placeholder bars
            for letter_x in (10, 22, 34, 46):
                for y in range(24, 40):
                    for x in range(letter_x, letter_x + 8):
                        if 0 < (x - letter_x) < 7 and 0 < (y - 24) < 16:
                            p[x, y] = (240, 240, 230)

        def office_sign(p):
            # Faded label plate
            for y in range(64):
                for x in range(64):
                    p[x, y] = add_rgb((178, 168, 142), random.randint(-8, 8))
            # Border
            for x in range(64):
                p[x, 4] = (78, 70, 56); p[x, 59] = (78, 70, 56)
            for y in range(64):
                p[4, y] = (78, 70, 56); p[59, y] = (78, 70, 56)
            # Approximated "OFFICE" glyph row centered
            for letter_x in (12, 22, 32, 42):
                for y in range(28, 36):
                    for x in range(letter_x, letter_x + 6):
                        if 1 < (x - letter_x) < 5 and 1 < (y - 28) < 7:
                            p[x, y] = (44, 38, 26)

        renders = [
            cardboard, filing_cabinet, crt_monitor, fluorescent,
            wooden_door, locker_grid, exit_sign, office_sign,
        ]
        # Fill 16 cells; second 8 mirrors the first 8 with a slight
        # palette shift so the atlas isn't half blank.
        for i, r in enumerate(renders):
            fill_cell(i, r)
        for i, r in enumerate(renders):
            fill_cell(8 + i, r)  # plain duplicate; final art replaces

        img.save(TEX_OUT / "atlas_props.png")
        print("  texture: atlas_props.png  (256x256, 4x4 prop faces)")

    # ─── atlas_figures.png — 128x128, four 64x64 character panels ────
    if should_write("atlas_figures.png"):
        random.seed(0xCAB1)
        img = Image.new("RGB", (128, 128), (0, 0, 0))
        px = img.load()

        def panel(idx: int, render):
            cx, cy = (idx % 2) * 64, (idx // 2) * 64
            for y in range(64):
                for x in range(64):
                    px[cx + x, cy + y] = render(x, y)

        # Panel 0 (TL): security uniform — navy with vertical pinstripe + brass belt buckle.
        def security(x, y):
            base = (38, 44, 76)
            stripe = -20 if (x % 6) == 0 else random.randint(-5, 5)
            # Brass belt buckle band at y=30..34
            if 30 <= y <= 34:
                buckle = (180, 148, 50)
                return add_rgb(buckle, random.randint(-12, 12))
            return add_rgb(base, stripe)

        # Panel 1 (TR): maintenance jumpsuit — olive drab + chest pocket.
        def maintenance(x, y):
            base = (96, 102, 60)
            n = random.randint(-7, 7)
            # Chest pocket: rectangle at (16..38, 14..28)
            if 16 <= x < 38 and 14 <= y < 28:
                if x in (16, 37) or y in (14, 27):
                    return add_rgb(base, -45)
                return add_rgb(base, -15)
            return add_rgb(base, n)

        # Panel 2 (BL): solid black silhouette (shadow plane).
        def shadow(x, y):
            return (4, 4, 6)

        # Panel 3 (BR): pale face — eyes + mouth dash on skin tone.
        def face(x, y):
            base = (188, 168, 148)
            n = random.randint(-6, 6)
            # Eye at (18-22, 26-30) and (42-46, 26-30)
            if (18 <= x <= 22 and 26 <= y <= 30) or (42 <= x <= 46 and 26 <= y <= 30):
                return (12, 12, 16)
            # Mouth dash y=44, x=24..40
            if 24 <= x <= 40 and 43 <= y <= 45:
                return (60, 32, 28)
            return add_rgb(base, n)

        panel(0, security)
        panel(1, maintenance)
        panel(2, shadow)
        panel(3, face)
        img.save(TEX_OUT / "atlas_figures.png")
        print("  texture: atlas_figures.png  (128x128, 4 character panels)")

    # ─── bezel_crt.png — 320x240 RGBA, beveled frame, transparent center ─
    # NOTE: PS1Godot's UI exporter currently only ships TYPE_RECT / TYPE_TEXT
    # (no image widget). This asset is staged for when a TYPE_IMAGE element
    # type lands; today the in-game crt_bezel canvas is faked with 4 black
    # rect borders. See project_ui_exporter_text_box_only memory.
    if should_write("bezel_crt.png"):
        L, R, T, B = 12, 308, 16, 224  # transparent inner viewport
        img = Image.new("RGBA", (320, 240), (0, 0, 0, 0))
        px = img.load()
        random.seed(0xBE2E1)
        # Outer plastic field — slight per-pixel grain so it doesn't look flat.
        for y in range(240):
            for x in range(320):
                if L <= x < R and T <= y < B:
                    continue  # inner viewport stays transparent
                grain = random.randint(-4, 4)
                px[x, y] = (clamp(14 + grain), clamp(14 + grain), clamp(18 + grain), 255)
        # Inner bezel highlight (1 px lighter, just outside viewport).
        for x in range(L - 1, R + 1):
            if 0 <= T - 1 < 240:
                px[x, T - 1] = (52, 52, 58, 255)
            if 0 <= B < 240:
                px[x, B] = (52, 52, 58, 255)
        for y in range(T - 1, B + 1):
            if 0 <= L - 1 < 320:
                px[L - 1, y] = (52, 52, 58, 255)
            if 0 <= R < 320:
                px[R, y] = (52, 52, 58, 255)
        # Outer shadow stroke (1 px darker around the bezel's outer edge).
        for x in range(320):
            px[x, 0] = (28, 28, 32, 255)
            px[x, 239] = (28, 28, 32, 255)
        for y in range(240):
            px[0, y] = (28, 28, 32, 255)
            px[319, y] = (28, 28, 32, 255)
        # Anti-aliased rounded corners — clip with a 4-px circle at each
        # OUTER corner so the bezel doesn't read as a hard rectangle.
        radius = 5
        for cx, cy in [(0, 0), (319, 0), (0, 239), (319, 239)]:
            ox = 0 if cx == 0 else -1
            oy = 0 if cy == 0 else -1
            for dy in range(radius):
                for dx in range(radius):
                    rx = cx + (dx if cx == 0 else -dx) + ox
                    ry = cy + (dy if cy == 0 else -dy) + oy
                    dist = math.sqrt((dx + 0.5) ** 2 + (dy + 0.5) ** 2)
                    if dist > radius:
                        if 0 <= rx < 320 and 0 <= ry < 240:
                            px[rx, ry] = (0, 0, 0, 0)
        # REC dot bottom-right with a glow halo.
        rec_x, rec_y = 296, 230
        for dy in range(-3, 4):
            for dx in range(-3, 4):
                d = math.sqrt(dx * dx + dy * dy)
                if d > 3.0:
                    continue
                if 0 <= rec_x + dx < 320 and 0 <= rec_y + dy < 240:
                    if d < 1.5:
                        px[rec_x + dx, rec_y + dy] = (240, 60, 50, 255)
                    elif d < 2.5:
                        px[rec_x + dx, rec_y + dy] = (180, 40, 38, 255)
                    else:
                        # Halo over plastic (not viewport)
                        if px[rec_x + dx, rec_y + dy][3] == 255:
                            px[rec_x + dx, rec_y + dy] = (88, 28, 30, 255)
        # Tiny "REC" label text 4 px to the left of the dot, on the bezel.
        try:
            font = ImageFont.truetype("consola.ttf", 9)
        except OSError:
            font = ImageFont.load_default()
        draw = ImageDraw.Draw(img)
        draw.text((272, 228), "REC", font=font, fill=(180, 180, 188, 255))
        img.save(TEX_OUT / "bezel_crt.png")
        print("  texture: bezel_crt.png  (320x240, beveled, REC dot)")

    # ─── static_noise.png — 256x256, high-contrast TV static ─────────
    # Faint horizontal banding (TV scan structure) on top of bimodal
    # salt-and-pepper noise so it reads as "broken signal" not "fuzz".
    if should_write("static_noise.png"):
        random.seed(0xCAFEBEEF)
        img = Image.new("RGBA", (256, 256))
        px = img.load()
        for y in range(256):
            band = 8 if (y % 4 == 0) else (-4 if (y % 4 == 2) else 0)
            for x in range(256):
                r = random.random()
                if r < 0.03:
                    v = 245 + random.randint(-10, 10)
                elif r < 0.06:
                    v = 18 + random.randint(0, 10)
                else:
                    v = random.randint(60, 200) + band
                v = clamp(v)
                px[x, y] = (v, v, v, 255)
        img.save(TEX_OUT / "static_noise.png")
        print("  texture: static_noise.png  (256x256 TV static)")

    # ─── scanlines.png — 320x240, alternating opacity rows ───────────
    # Two-phase pattern (full-opaque, half-opaque, transparent) reads as
    # a real CRT subline structure better than a strict every-other row.
    if should_write("scanlines.png"):
        img = Image.new("RGBA", (320, 240), (0, 0, 0, 0))
        px = img.load()
        # Three-row repeat: dark / dim / clear. ~60% dim per CRT-y look.
        for y in range(240):
            phase = y % 3
            if phase == 0:
                a = 200  # heavy dark line
            elif phase == 1:
                a = 90   # dim follow-up
            else:
                continue  # leave transparent
            for x in range(320):
                px[x, y] = (0, 0, 0, a)
        img.save(TEX_OUT / "scanlines.png")
        print("  texture: scanlines.png  (320x240 CRT scanlines)")

    # ─── font_hud.png — 256x96, monospaced HUD glyph atlas ────────────
    # 16 cols × 6 rows of 16x16 cells, ASCII 32..127. PIL's bundled
    # default font ships an 8-pixel-tall pixel-aligned typeface — exactly
    # what we want for a PSX HUD. Native size, no supersampling, no
    # alpha threshold. Then thicken every glyph by one pixel down-right
    # so the strokes survive PSX 4bpp quantization without breaking up.
    if should_write("font_hud.png"):
        cell = 16
        cols, rows = 16, 6
        atlas_w, atlas_h = cell * cols, cell * rows  # 256x96
        atlas = Image.new("RGBA", (atlas_w, atlas_h), (0, 0, 0, 0))
        bd = ImageDraw.Draw(atlas)
        font = ImageFont.load_default()
        for i in range(96):
            ch = chr(32 + i)
            cx = (i % cols) * cell
            cy = (i // cols) * cell
            try:
                bbox = bd.textbbox((0, 0), ch, font=font)
                gw = bbox[2] - bbox[0]
                gh = bbox[3] - bbox[1]
                ox, oy = bbox[0], bbox[1]
            except AttributeError:
                gw, gh, ox, oy = 8, 8, 0, 0
            tx = cx + (cell - gw) // 2 - ox
            ty = cy + (cell - gh) // 2 - oy
            bd.text((tx, ty), ch, font=font, fill=(255, 255, 255, 255))
        # Pixel-thicken: any opaque pixel turns its (x+1, y) and (x, y+1)
        # neighbors opaque too. Boldens the strokes and helps glyphs
        # survive PSX font's binary-bit-per-pixel sampling.
        thick = Image.new("RGBA", (atlas_w, atlas_h), (0, 0, 0, 0))
        ipx = atlas.load()
        opx = thick.load()
        for y in range(atlas_h):
            for x in range(atlas_w):
                if ipx[x, y][3] > 80:
                    opx[x, y] = (255, 255, 255, 255)
                    if x + 1 < atlas_w:
                        opx[x + 1, y] = (255, 255, 255, 255)
                    if y + 1 < atlas_h:
                        opx[x, y + 1] = (255, 255, 255, 255)
        thick.save(TEX_OUT / "font_hud.png")
        print("  texture: font_hud.png  (256x96, 8-px pixel font, thickened)")


# ──────────────────────────────────────────────────────────────────────
# CLI
# ──────────────────────────────────────────────────────────────────────

def main() -> None:
    parser = argparse.ArgumentParser(description="Regenerate Monitor placeholder assets.")
    parser.add_argument("--audio", action="store_true", help="Regenerate WAVs only.")
    parser.add_argument("--textures", action="store_true",
                        help="Regenerate textures (only fills MISSING ones unless --force).")
    parser.add_argument("--force", action="store_true",
                        help="With --textures, also regenerate textures that already exist.")
    parser.add_argument("--all", action="store_true", help="Both audio and textures.")
    parser.add_argument("--ai-textures", choices=["openai"], default="",
                        dest="ai_textures",
                        help="Use AI image gen for the 5 atlas/bezel/static "
                             "textures (font_hud and scanlines stay procedural). "
                             "Requires `pip install openai` and OPENAI_API_KEY.")
    parser.add_argument("--ai-quality", choices=["low", "medium", "high"],
                        default="medium",
                        help="OpenAI gpt-image-1 quality tier (default medium). "
                             "Per-image cost: low ~$0.01, medium ~$0.04, high ~$0.17.")
    parser.add_argument("--ai-audio", choices=["elevenlabs"], default="",
                        dest="ai_audio",
                        help="Use AI sound-effect gen for all 21 audio clips. "
                             "Requires ELEVENLABS_API_KEY env var. Cost is "
                             "~$0.05–0.15 per clip; full 21-clip set ~$1–3.")
    args = parser.parse_args()

    if not (args.audio or args.textures or args.all):
        args.all = True

    if args.audio or args.all:
        print("Regenerating audio:")
        regen_audio(ai_provider=args.ai_audio)
    if args.textures or args.all:
        print("Regenerating textures:")
        regen_textures(missing_only=not args.force,
                       ai_provider=args.ai_textures,
                       ai_quality=args.ai_quality)

    print("Done.")


if __name__ == "__main__":
    main()
