"""Refine the existing (correctly UV-aligned) goblin_skin.png in PSX
hand-painted style while preserving the spatial layout. The current
texture has eyes/ears/skin placed correctly thanks to a proper UV
unwrap and external generation — we only want to upgrade the look.

Output overwrites assets/monitor/models/goblin/goblin_skin.png after
backing up the current one as goblin_skin.previous.png so you can
revert if a refinement comes out worse.
"""
import requests
import base64
import io
import shutil
from pathlib import Path
from PIL import Image

CURRENT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/models/goblin/goblin_skin.png")
BACKUP = CURRENT.with_name("goblin_skin.previous.png")
KEY_TXT = Path(r"C:/Users/Cornelio/Desktop/key.txt")

PROMPT = (
    "Refine this goblin head UV texture atlas in clean PS1-era hand-painted "
    "pixel art style. CRITICAL CONSTRAINT: do NOT move any feature — every "
    "yellow eye, pink ear, dark skin patch, mouth area must stay at exactly "
    "the same pixel coordinates as the input. Keep all UV island boundaries "
    "in their current positions. Black background stays black.\n"
    "What to improve while keeping layout fixed:\n"
    "- Sharper, brighter glowing yellow eye pupils with clean black sockets.\n"
    "- More definition on inner-ear pink shading.\n"
    "- Cleaner mossy goblin green skin: visible dithering, warts, "
    "  bumps, harsh shading bands (not smooth gradients).\n"
    "- More menacing/sickly atmosphere — slight olive and yellow-green "
    "  variation in the skin.\n"
    "- Subtle teeth/mouth detail in the existing red mouth region.\n"
    "Style: PSX 1990s low-res, hard pixel edges, color banding, no smooth "
    "gradients, no anti-aliasing. Output 256x256 effective resolution."
)


def fetch_openai_key():
    if not KEY_TXT.exists():
        return None
    lines = KEY_TXT.read_text().strip().splitlines()
    return lines[1].strip() if len(lines) >= 2 else None


def gen():
    key = fetch_openai_key()
    if not key:
        print("no OpenAI key in key.txt line 2 — aborting")
        return
    if not CURRENT.exists():
        print(f"{CURRENT} not found — aborting")
        return
    # Back up current.
    shutil.copy2(CURRENT, BACKUP)
    print(f"backed up current -> {BACKUP.name}")

    # gpt-image-1 /edits expects square at standard sizes; upscale for
    # the request, downscale the result back to 256.
    upscaled = CURRENT.with_name("goblin_skin.upscaled.png")
    Image.open(CURRENT).resize((1024, 1024), Image.Resampling.NEAREST).save(upscaled)

    url = "https://api.openai.com/v1/images/edits"
    with open(upscaled, "rb") as f:
        files = {"image": ("goblin_skin.png", f, "image/png")}
        data = {
            "model": "gpt-image-1",
            "prompt": PROMPT,
            "size": "1024x1024",
            "quality": "low",
            "n": "1",
        }
        headers = {"Authorization": f"Bearer {key}"}
        print("calling gpt-image-1 /edits to refine...")
        resp = requests.post(url, headers=headers, files=files, data=data, timeout=180)

    upscaled.unlink()  # cleanup temp

    if resp.status_code != 200:
        print(f"HTTP {resp.status_code}: {resp.text[:500]}")
        return

    payload = resp.json()
    b64 = payload["data"][0]["b64_json"]
    img = Image.open(io.BytesIO(base64.b64decode(b64))).convert("RGB")
    img = img.resize((256, 256), Image.Resampling.NEAREST)
    img.save(CURRENT)
    print(f"wrote refined {CURRENT.name} (256x256). Backup at {BACKUP.name}.")


if __name__ == "__main__":
    gen()
