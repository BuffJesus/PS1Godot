"""AI blood splatter via gpt-image-1 with transparent background.
Replaces the procedural PIL blood with something more painterly. PSX
exporter quantizes alpha pixels to CLUT[0]=0x0000 so they vanish at
runtime when the mesh is Translucent.
"""
import requests
import base64
import io
from pathlib import Path
from PIL import Image

OUT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/textures/blood_decal.png")
KEY_TXT = Path(r"C:/Users/Cornelio/Desktop/key.txt")

PROMPT = (
    "PS1-era horror game blood splatter decal, top-down view, dark crimson "
    "red with deeper maroon shadows and a few brighter scarlet highlights, "
    "irregular organic splatter shape with a central pool, smaller satellite "
    "droplets, 2-3 thin downward drip trails. Transparent background. "
    "Hand-painted pixel art style, hard edges, color banding, no smooth "
    "gradients, no anti-aliasing, retro 1990s low-resolution look. The "
    "splatter occupies maybe 60-70% of the frame; rest is fully transparent."
)


def fetch_openai_key():
    if not KEY_TXT.exists():
        return None
    lines = KEY_TXT.read_text().strip().splitlines()
    return lines[1].strip() if len(lines) >= 2 else None


def gen():
    key = fetch_openai_key()
    if not key:
        print("no OpenAI key on key.txt line 2 — aborting")
        return
    url = "https://api.openai.com/v1/images/generations"
    body = {
        "model": "gpt-image-1",
        "prompt": PROMPT,
        "size": "1024x1024",
        "quality": "low",
        "background": "transparent",  # critical: forces RGBA output
        "n": 1,
    }
    headers = {"Authorization": f"Bearer {key}", "Content-Type": "application/json"}
    print("calling gpt-image-1 with transparent background...")
    resp = requests.post(url, headers=headers, json=body, timeout=180)
    if resp.status_code != 200:
        print(f"HTTP {resp.status_code}: {resp.text[:500]}")
        return
    payload = resp.json()
    b64 = payload["data"][0]["b64_json"]
    img = Image.open(io.BytesIO(base64.b64decode(b64))).convert("RGBA")
    # Downscale to 64x64 PSX-friendly. Nearest preserves the chunky look.
    img = img.resize((64, 64), Image.Resampling.NEAREST)
    img.save(OUT)
    # Print alpha stats so we can verify transparency survived.
    px = img.load()
    counts = {"alpha=0": 0, "alpha<128": 0, "alpha>=128": 0}
    for y in range(64):
        for x in range(64):
            a = px[x, y][3]
            if a == 0: counts["alpha=0"] += 1
            elif a < 128: counts["alpha<128"] += 1
            else: counts["alpha>=128"] += 1
    print(f"wrote {OUT} (64x64 RGBA)")
    print(f"  alpha distribution: {counts}")


if __name__ == "__main__":
    gen()
