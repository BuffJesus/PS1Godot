"""Generate the alpha-keyed assets for the new horror features:
  - cobweb_corner.png (64x64)  : delicate spider web in corner, transparent bg
  - static_overlay.png (256x256): white-noise TV static, transparent bg

Both use OpenAI gpt-image-1 with background='transparent' so the resulting
RGBA images quantize cleanly to PSX 8bpp with CLUT[0]=0x0000 = invisible.
"""
import requests
import base64
import io
from pathlib import Path
from PIL import Image

ROOT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/textures")
KEY_TXT = Path(r"C:/Users/Cornelio/Desktop/key.txt")


def fetch_openai_key():
    if not KEY_TXT.exists():
        return None
    lines = KEY_TXT.read_text().strip().splitlines()
    return lines[1].strip() if len(lines) >= 2 else None


def gen_image(key, prompt, out_path, size_xy):
    url = "https://api.openai.com/v1/images/generations"
    body = {
        "model": "gpt-image-1",
        "prompt": prompt,
        "size": "1024x1024",
        "quality": "low",
        "background": "transparent",
        "n": 1,
    }
    print(f"calling gpt-image-1 for {out_path.name}...")
    resp = requests.post(url, headers={"Authorization": f"Bearer {key}",
                                       "Content-Type": "application/json"},
                         json=body, timeout=180)
    if resp.status_code != 200:
        print(f"  HTTP {resp.status_code}: {resp.text[:300]}")
        return False
    payload = resp.json()
    b64 = payload["data"][0]["b64_json"]
    img = Image.open(io.BytesIO(base64.b64decode(b64))).convert("RGBA")
    img = img.resize(size_xy, Image.Resampling.NEAREST)
    img.save(out_path)
    # Stats so we can verify alpha survived.
    px = img.load()
    a0 = a_mid = a_op = 0
    for y in range(size_xy[1]):
        for x in range(size_xy[0]):
            a = px[x, y][3]
            if a == 0: a0 += 1
            elif a < 128: a_mid += 1
            else: a_op += 1
    total = size_xy[0] * size_xy[1]
    print(f"  wrote {out_path.name} ({size_xy[0]}x{size_xy[1]}) "
          f"alpha=0:{a0} mid:{a_mid} opaque:{a_op} ({100*a0//total}% transparent)")
    return True


COBWEB_PROMPT = (
    "PS1-era retro horror game cobweb corner asset, top-left corner spider "
    "web, light gray-white delicate radial threads emanating from the "
    "top-left corner of the frame, sparse spiral connecting threads. "
    "Transparent background. Hand-painted pixel art, hard thin lines, "
    "low resolution 1990s look, no gradients, slight aged dust haze."
)

STATIC_PROMPT = (
    "PS1-era CRT television static interference, dense fine white noise "
    "with scattered black/dark gray pixel grains, transparent black "
    "background showing through gaps in the noise. Hand-painted pixel "
    "art static texture, 1990s analog TV aesthetic, no gradients, harsh "
    "high-contrast grain. Mostly transparent (about 40-50% of pixels) so "
    "underlying content shows through when overlaid."
)


if __name__ == "__main__":
    key = fetch_openai_key()
    if not key:
        print("no OpenAI key — aborting"); exit()
    gen_image(key, COBWEB_PROMPT,  ROOT / "cobweb_corner.png",  (64, 64))
    gen_image(key, STATIC_PROMPT,  ROOT / "static_overlay.png", (256, 256))
