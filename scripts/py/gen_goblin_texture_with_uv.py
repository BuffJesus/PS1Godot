"""Generate the goblin face texture using OpenAI gpt-image-1 /edits,
feeding the UV layout PNG as a reference image so painted features
(eyes, nose, ears) actually land on the right mesh polygons.

Inputs:
  C:/Users/Cornelio/Pictures/goblin/UV.png  — Blender Smart UV Project layout
Output:
  godot-ps1/assets/monitor/models/goblin/goblin_skin.png  (256×256 PSX-friendly)

The /edits endpoint accepts multipart-form with an image + prompt; gpt-image-1
returns a generated image that respects the input's spatial layout (within
limits — it's not a strict pixel-accurate UV painter, but it does honour
roughly where things should go).
"""
import requests
import base64
from pathlib import Path
from PIL import Image
import io

UV_REF = Path(r"C:/Users/Cornelio/Pictures/goblin/UV.png")
OUT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/models/goblin/goblin_skin.png")
KEY_TXT = Path(r"C:/Users/Cornelio/Desktop/key.txt")

# User's exact ChatGPT-given prompt.
PROMPT = (
    "PS1 style low resolution texture for a goblin head, UV texture atlas "
    "layout, 128x128 resolution, hand-painted pixel art, dark green skin "
    "with harsh lighting, strong baked shadows, angular nose highlight, "
    "deep black eye sockets with glowing yellow eyes, large pointed ears "
    "with pink inner ear shading, no smooth gradients, hard edges only, "
    "visible dithering, color banding, slight pixel noise, retro 1990s "
    "video game aesthetic, texture map on black background, UV islands "
    "clearly visible. Paint each region of the input UV layout: the large "
    "head island gets the face (eyes, nose, mouth, skin); the small ear "
    "islands get pink inner-ear shading."
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
    if not UV_REF.exists():
        print(f"UV reference {UV_REF} not found — aborting")
        return

    url = "https://api.openai.com/v1/images/edits"
    with open(UV_REF, "rb") as f:
        files = {
            "image": ("uv_layout.png", f, "image/png"),
        }
        data = {
            "model": "gpt-image-1",
            "prompt": PROMPT,
            "size": "1024x1024",
            "quality": "low",
            "n": "1",
        }
        headers = {"Authorization": f"Bearer {key}"}
        print("calling gpt-image-1 /edits with UV layout reference...")
        resp = requests.post(url, headers=headers, files=files, data=data,
                             timeout=180)

    if resp.status_code != 200:
        print(f"HTTP {resp.status_code}: {resp.text[:500]}")
        return

    payload = resp.json()
    b64 = payload["data"][0]["b64_json"]
    img = Image.open(io.BytesIO(base64.b64decode(b64))).convert("RGB")
    # Downscale to 256×256 with nearest for chunky PSX feel.
    img = img.resize((256, 256), Image.Resampling.NEAREST)
    img.save(OUT)
    print(f"wrote {OUT} (256×256)")


if __name__ == "__main__":
    gen()
