"""Generate menu screen assets for The Monitor.

Images via PIL (procedural, PSX-friendly palettes — DALL-E outputs photos
that need heavy downsampling to be on-style; PIL gets there in one step).

Sounds via ElevenLabs sound-generation endpoint (key from the user's
desktop key.txt).

Outputs:
  assets/monitor/textures/menu_logo.png      256×80   PSX logo, scanline overlay
  assets/monitor/textures/menu_bg.png        256×256  dark gradient + noise
  assets/monitor/textures/menu_cursor.png    16×16    selection arrow
  assets/monitor/audio/menu_move.wav         cursor blip
  assets/monitor/audio/menu_select.wav       confirm chime
"""
from PIL import Image, ImageDraw, ImageFont
import random
import urllib.request
import urllib.error
import json
import base64
import io
from pathlib import Path

ROOT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1")
TEX_DIR = ROOT / "assets/monitor/textures"
AUDIO_DIR = ROOT / "assets/monitor/audio"
KEY_TXT = Path(r"C:/Users/Cornelio/Desktop/key.txt")

random.seed(2026_04_25)


# ── 1. Logo ─────────────────────────────────────────────────────────────
def make_logo():
    """THE MONITOR title logo via gpt-image-1 — proper PSX horror title
    treatment instead of the procedural PIL version."""
    eleven, openai = fetch_keys()
    if not openai:
        print("  no OpenAI key — skipping logo regen")
        return
    import urllib.request, urllib.error, json
    prompt = (
        "PS1-era horror game title logo for 'THE MONITOR'. Bold sharp "
        "blocky condensed sans-serif lettering, slightly cracked / "
        "weathered, with a faint static-noise glow around the text. "
        "Color palette: dark crimson red, charcoal black, with bright "
        "white highlights on letter edges. Subtle CRT scanline texture "
        "across the lettering. Pixel-art aesthetic, hand-painted, "
        "high contrast. Centered horizontally, transparent background "
        "OUTSIDE the text glow. 1990s VHS / surveillance / horror "
        "vibe. Text must read clearly: 'THE MONITOR' across one line."
    )
    body = json.dumps({
        "model": "gpt-image-1", "prompt": prompt,
        "size": "1024x1024", "quality": "low",
        "background": "transparent", "n": 1,
    }).encode("utf-8")
    req = urllib.request.Request(
        "https://api.openai.com/v1/images/generations", data=body,
        headers={"Authorization": f"Bearer {openai}", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            payload = json.loads(r.read())
    except urllib.error.HTTPError as e:
        print(f"  HTTP {e.code}: {e.read().decode('utf-8','replace')[:300]}")
        return
    img = Image.open(io.BytesIO(base64.b64decode(payload["data"][0]["b64_json"]))).convert("RGBA")
    # gpt-image-1 returns 1024×1024 square. Crop to the actual text
    # bbox (non-transparent pixels) so the output isn't dominated by
    # whitespace, then fit into the menu's 256×80 banner. Earlier
    # fixed 1/3 crop sliced off "THE" top + "MONITOR" descenders when
    # the AI rendered text taller than that band.
    bbox = img.getbbox()  # (left, top, right, bottom) of non-transparent
    if bbox is None:
        # Fallback: full image (shouldn't happen — AI always paints something)
        cropped = img
    else:
        # Add 5% padding so the text doesn't touch the edges.
        sq = img.size[0]
        pad_x = max(8, (bbox[2] - bbox[0]) // 20)
        pad_y = max(8, (bbox[3] - bbox[1]) // 20)
        l = max(0, bbox[0] - pad_x)
        t = max(0, bbox[1] - pad_y)
        r = min(sq, bbox[2] + pad_x)
        b = min(sq, bbox[3] + pad_y)
        cropped = img.crop((l, t, r, b))
    # Fit into 256×80 preserving aspect; pad with transparent.
    target_w, target_h = 256, 80
    src_w, src_h = cropped.size
    scale = min(target_w / src_w, target_h / src_h)
    new_w = max(1, int(src_w * scale))
    new_h = max(1, int(src_h * scale))
    fitted = cropped.resize((new_w, new_h), Image.Resampling.NEAREST)
    out_img = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
    out_img.paste(fitted, ((target_w - new_w) // 2, (target_h - new_h) // 2))
    out = TEX_DIR / "menu_logo.png"
    out_img.save(out)
    print(f"  wrote {out.name} ({target_w}×{target_h}, fit AI bbox {src_w}×{src_h})")


# ── 2. Background ─────────────────────────────────────────────────────────
def make_background():
    """Dark vignette with noise speckle. Tileable along U, fades top→bottom."""
    W, H = 256, 256
    img = Image.new("RGB", (W, H))
    px = img.load()
    for y in range(H):
        # Vertical gradient: deep purple-black top → near-black bottom.
        t = y / (H - 1)
        base_r = int(15 + (1 - t) * 25)
        base_g = int(5  + (1 - t) * 8)
        base_b = int(20 + (1 - t) * 30)
        for x in range(W):
            n = random.randint(-6, 6)  # film-grain noise
            r = max(0, min(255, base_r + n))
            g = max(0, min(255, base_g + n))
            b = max(0, min(255, base_b + n))
            px[x, y] = (r, g, b)
    # Sparse white "stars" / dust pixels.
    for _ in range(60):
        x = random.randint(0, W - 1)
        y = random.randint(0, H - 1)
        v = random.randint(80, 180)
        px[x, y] = (v, v, v)
    out = TEX_DIR / "menu_bg.png"
    img.save(out)
    print(f"  wrote {out.name} ({W}×{H})")


# ── 3. Cursor ────────────────────────────────────────────────────────────
def make_cursor():
    """16×16 right-pointing arrow, white with red shadow."""
    W = H = 16
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    # Triangle: (1,2), (1,13), (13,7) approx — wide-base equilateral.
    draw.polygon([(2, 2), (2, 13), (13, 7)], fill=(120, 0, 0, 255))
    draw.polygon([(1, 1), (1, 12), (12, 6)], fill=(230, 230, 230, 255))
    out = TEX_DIR / "menu_cursor.png"
    img.save(out)
    print(f"  wrote {out.name} ({W}×{H})")


# ── 4. Sounds via ElevenLabs ─────────────────────────────────────────────
def fetch_keys():
    """Returns (elevenlabs, openai). ElevenLabs is line 1, OpenAI line 2."""
    if not KEY_TXT.exists():
        return None, None
    lines = KEY_TXT.read_text().strip().splitlines()
    return (lines[0].strip() if len(lines) >= 1 else None,
            lines[1].strip() if len(lines) >= 2 else None)


def gen_sound(api_key, prompt, out_path, duration=0.5):
    """Call ElevenLabs sound-generation endpoint and write the result
    as 22050Hz mono 16-bit WAV (the PSX exporter rejects non-WAV).
    out_path is the .wav target; we write a temp .mp3 then ffmpeg-convert."""
    import subprocess
    mp3_tmp = out_path.with_suffix(".mp3")
    url = "https://api.elevenlabs.io/v1/sound-generation"
    body = json.dumps({
        "text": prompt,
        "duration_seconds": duration,
        "prompt_influence": 0.7,
    }).encode("utf-8")
    req = urllib.request.Request(
        url, data=body,
        headers={
            "xi-api-key": api_key,
            "Content-Type": "application/json",
            "Accept": "audio/mpeg",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            data = resp.read()
        mp3_tmp.write_bytes(data)
        # Convert to PSX-friendly WAV (mono 16-bit 22050Hz). The PSX
        # exporter rejects MP3 outright with a hard error.
        subprocess.run(
            ["ffmpeg", "-y", "-loglevel", "error", "-i", str(mp3_tmp),
             "-ac", "1", "-ar", "22050", "-sample_fmt", "s16",
             str(out_path)],
            check=True,
        )
        mp3_tmp.unlink()
        print(f"  wrote {out_path.name} ({out_path.stat().st_size} bytes)")
        return True
    except urllib.error.HTTPError as e:
        print(f"  FAILED {out_path.name}: HTTP {e.code} — {e.read().decode('utf-8', 'replace')[:200]}")
        return False
    except Exception as e:
        print(f"  FAILED {out_path.name}: {e}")
        return False


def make_sounds():
    eleven, _ = fetch_keys()
    if not eleven:
        print("  no ElevenLabs key — skipping sound generation")
        return
    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    gen_sound(eleven,
              "short retro PS1 menu cursor blip, soft synthesized tick, no music, "
              "single very short chiptune note, 8-bit",
              AUDIO_DIR / "menu_move.wav", duration=0.5)
    gen_sound(eleven,
              "PS1 horror game menu confirm chime, brief synthesized tone, "
              "slightly ominous, single note, 8-bit retro",
              AUDIO_DIR / "menu_select.wav", duration=0.6)


# ── Main ────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    TEX_DIR.mkdir(parents=True, exist_ok=True)
    print("images:")
    make_logo()
    make_background()
    make_cursor()
    print("sounds:")
    make_sounds()
    print("done.")
