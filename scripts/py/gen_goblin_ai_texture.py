"""Generate a PSX-style goblin skin texture via OpenAI gpt-image-1.

The goblin's UV unwrap (via UVgami) split the mesh into many small
fragmented islands — each face has its own tiny chart. That means a
TILEABLE/UNIFORM skin texture works best: every face samples a similar
region, the result reads as coherent goblin skin even though faces
aren't aligned to specific features.

Output: assets/monitor/models/goblin/goblin_skin.png (256×256, replaces
the procedural PIL one we made earlier).
"""
import urllib.request
import urllib.error
import json
import base64
from pathlib import Path
from PIL import Image
import io

ROOT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1")
OUT = ROOT / "assets/monitor/models/goblin/goblin_skin.png"
KEY_TXT = Path(r"C:/Users/Cornelio/Desktop/key.txt")

PROMPT = (
    "A tileable PS1-era low-poly horror game texture: mossy goblin skin, "
    "sickly dark green with patches of yellow-green and brown, leathery, "
    "wart-like bumps, subtle veins. No specific features (no eyes, no "
    "mouth) — just an even green skin pattern. PSX 32-bit color palette, "
    "256x256, no shading gradients, flat-ish texture suitable for "
    "wrapping around a low-poly head."
)

def fetch_keys():
    if not KEY_TXT.exists():
        return None, None
    lines = KEY_TXT.read_text().strip().splitlines()
    return (lines[0].strip() if len(lines) >= 1 else None,
            lines[1].strip() if len(lines) >= 2 else None)

def gen():
    _, openai_key = fetch_keys()
    if not openai_key:
        print("no OpenAI key in key.txt line 2 — aborting")
        return
    url = "https://api.openai.com/v1/images/generations"
    body = json.dumps({
        "model": "gpt-image-1",
        "prompt": PROMPT,
        "size": "1024x1024",
        "quality": "low",  # low = cheap, fine for downsampled PSX texture
        "n": 1,
    }).encode("utf-8")
    req = urllib.request.Request(
        url, data=body,
        headers={
            "Authorization": f"Bearer {openai_key}",
            "Content-Type": "application/json",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            data = json.loads(resp.read())
    except urllib.error.HTTPError as e:
        print(f"HTTP {e.code}: {e.read().decode('utf-8','replace')[:500]}")
        return

    b64 = data["data"][0]["b64_json"]
    img = Image.open(io.BytesIO(base64.b64decode(b64))).convert("RGB")
    # Downscale to PSX-friendly 256×256 (nearest = chunky pixel feel).
    img = img.resize((256, 256), Image.Resampling.NEAREST)
    img.save(OUT)
    print(f"wrote {OUT} (256×256)")

if __name__ == "__main__":
    gen()
