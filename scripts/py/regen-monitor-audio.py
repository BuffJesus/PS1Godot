#!/usr/bin/env python
"""Regenerate The Monitor's 23 audio clips via ElevenLabs Sound Effects API.

Reads the API key from C:\\Users\\Cornelio\\Desktop\\key.txt (NEVER commit
to source). Each clip is sized to its design-doc target duration at the
lowest sample rate that still preserves its character — drones/hums at
11025 Hz, percussive transients at 22050 Hz. Loop clips get a 30 ms
fade-in / fade-out so the loop boundary is silence-to-silence (cleaner
than a hard click).

Re-run any time. Existing WAVs are overwritten in place.
"""
import json
import os
import subprocess
import sys
import urllib.request
import urllib.error
from pathlib import Path

KEY_PATH  = Path(r"C:/Users/Cornelio/Desktop/key.txt")
AUDIO_DIR = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/audio")
TMP_DIR   = AUDIO_DIR / "_tmp"
URL       = "https://api.elevenlabs.io/v1/sound-generation"

# ElevenLabs SFX hard minimum is 0.5 s; clips authored shorter (clicks /
# pops) are bumped to 0.5 s and the model uses the tail however it
# wants. Per the asset-prompts doc, all clips are mono dry, no reverb.
# Format: (name, prompt, duration_s, sample_rate_hz, is_loop)
CLIPS = [
    # ── Resident audio ──────────────────────────────────────────────
    ("ambient_drone",     "Continuous low industrial drone, 60Hz electrical hum mixed with HVAC rumble and distant fluorescent buzz. Mono, no stereo, seamless loop. No melody, no rhythm, just dread. 1990s empty warehouse at 3 AM.", 3.0, 11025, True),
    ("crt_click",         "Single mechanical click of an old TV channel selector knob, dry, no reverb.", 0.5, 22050, False),
    ("crt_static_short",  "Short burst of analog TV static, full bandwidth white noise with a slight low-end thump at the start. Mono dry.", 0.5, 22050, False),

    # ── Per-feed room tones (loops) ────────────────────────────────
    ("hum_low",           "Low electrical 60Hz hum with faint distant rumble. Empty corridor ambience. Mono, seamless loop.", 2.0, 11025, True),
    ("hum_fluor",         "Buzzing fluorescent light tube, slight ballast flicker, mono. Dry, no reverb. Seamless loop.", 2.0, 11025, True),
    ("hum_outside",       "Distant outdoor city ambience: faint highway, occasional very-distant car. Mono, seamless loop.", 2.0, 11025, True),
    ("hum_electrical",    "High-pitched CRT monitor whine plus low PC fan, claustrophobic. Mono, seamless loop.", 2.0, 11025, True),

    # ── UI SFX ─────────────────────────────────────────────────────
    ("log_confirm",       "Short ascending two-tone synth chirp, friendly but minimal. Like an old computer 'noted' beep. Mono.", 0.5, 22050, False),
    ("shift_end_stinger", "3-second descending synth pad, ominous resolution, mono, dry. Like an arcade game-over but slow and dignified.", 3.0, 22050, False),

    # ── Event SFX (14 events) ──────────────────────────────────────
    ("alarm_distant",     "Faraway car alarm, muffled by walls and distance. Mono, dry. Slight reverb to suggest outside ambient.", 3.0, 22050, False),
    ("door_creak",        "Heavy metal fire door slowly opening, single creak rising in pitch then settling, no slam. Mono dry.", 2.0, 11025, False),
    ("box_crash",         "Cardboard box falling from height onto concrete, single impact with a slight rattle of contents. Mono dry.", 1.0, 22050, False),
    ("chair_roll",        "Office chair on wheels rotating, faint plastic-on-vinyl roll, single small squeak. Mono dry, very quiet.", 1.5, 11025, False),
    ("footsteps_slow",    "Three slow heavy footsteps on hard floor at moderate distance. Mono dry. No reverb.", 3.0, 11025, False),
    ("bulb_pop",          "Single fluorescent bulb dying: faint electrical buzz cutting out into a dry pop. Mono.", 0.5, 22050, False),
    ("wood_creak",        "Wooden pallet shifting under weight, single creak. Mono dry.", 1.5, 11025, False),
    ("crt_die",           "Old CRT television flickering off: short electrical buzz cutting to a high whine then silence. Mono.", 1.0, 22050, False),
    ("shadow_whisper",    "Single short breathy whisper, indecipherable, faintly threatening. Mono close-mic, very quiet.", 1.0, 11025, False),
    ("metal_clang",       "Metal vent grate falling from a wall and clattering on concrete. Mono dry. Sharp transient followed by ringing decay.", 1.5, 22050, False),
    ("light_dim",         "Multiple fluorescent lights dimming and brightening: subtle whirr modulation, no clicks. Mono.", 2.0, 11025, False),
    ("box_slide",         "Cardboard box sliding rapidly across a tile floor, single shove. Mono dry.", 1.0, 11025, False),
    ("breathing_low",     "Slow heavy human breathing, very close mic. Mono. No words. Slightly labored. Seamless loop.", 2.0, 11025, True),
    ("thud",              "Single dull body-weight thud on concrete. Mono dry. No echo.", 0.5, 22050, False),
]

FADE_S = 0.03  # loop-boundary fade duration


def call_api(api_key: str, prompt: str, duration: float) -> bytes:
    payload = json.dumps({
        "text": prompt,
        "duration_seconds": float(duration),
        "prompt_influence": 0.5,
    }).encode("utf-8")
    req = urllib.request.Request(URL, data=payload, method="POST")
    req.add_header("xi-api-key", api_key)
    req.add_header("Content-Type", "application/json")
    req.add_header("Accept", "audio/mpeg")
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            return resp.read()
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="replace")[:500]
        raise RuntimeError(f"HTTP {e.code}: {body}") from None


def mp3_to_wav(mp3_bytes: bytes, target: Path, rate: int, duration: float, loop: bool) -> None:
    mp3_path = TMP_DIR / "out.mp3"
    mp3_path.write_bytes(mp3_bytes)

    # For looped clips, fade in 30 ms from silence and fade out 30 ms to
    # silence. The loop boundary is then silence-to-silence — no audible
    # click. Drones and hums tolerate the 60 ms total envelope easily;
    # for a 2 s loop it's 3 % of the duration.
    af_args = []
    if loop:
        fade_out_st = max(0.0, duration - FADE_S)
        af_args = ["-af", f"afade=t=in:st=0:d={FADE_S},afade=t=out:st={fade_out_st}:d={FADE_S}"]

    cmd = [
        "ffmpeg", "-loglevel", "error", "-y",
        "-i", str(mp3_path),
        "-ar", str(rate), "-ac", "1", "-sample_fmt", "s16",
        "-t", str(duration),
        *af_args,
        str(target),
    ]
    subprocess.run(cmd, check=True)


def main() -> int:
    if not KEY_PATH.exists():
        print(f"ERROR: API key not found at {KEY_PATH}", file=sys.stderr)
        return 1
    api_key = KEY_PATH.read_text(encoding="utf-8").strip()
    if not api_key:
        print(f"ERROR: API key file is empty", file=sys.stderr)
        return 1

    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    TMP_DIR.mkdir(parents=True, exist_ok=True)

    failed = []
    for name, prompt, dur, rate, loop in CLIPS:
        target = AUDIO_DIR / f"{name}.wav"
        prev = target.stat().st_size if target.exists() else 0
        loop_tag = " [loop]" if loop else ""
        print(f"  {name:22s}", end=" ", flush=True)
        try:
            mp3 = call_api(api_key, prompt, dur)
            mp3_to_wav(mp3, target, rate, dur, loop)
            now = target.stat().st_size
            print(f"{prev/1024:5.1f} KB -> {now/1024:5.1f} KB  ({rate} Hz, {dur}s{loop_tag})")
        except Exception as e:
            print(f"FAIL: {e}")
            failed.append(name)

    # Cleanup tmp file
    tmp_mp3 = TMP_DIR / "out.mp3"
    if tmp_mp3.exists():
        tmp_mp3.unlink()
    try:
        TMP_DIR.rmdir()
    except OSError:
        pass

    total = sum(p.stat().st_size for p in AUDIO_DIR.glob("*.wav"))
    print()
    print(f"  Total: {total/1024:7.1f} KB PCM (~{total/1024/3.5:.1f} KB ADPCM est)")
    if failed:
        print(f"  FAILED: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
