"""Re-paint the goblin texture using the LABELED UV map as reference.

The previous attempt fed an unlabeled UV layout to gpt-image-1, which
painted eyes wherever an island looked face-shaped — landing them on
the back of the head. This pass uses tools/uvgami/goblin_uv_labeled.png
which has FRONT/BACK/LEFT/RIGHT/TOP/BOTTOM labels overlaid on the right
regions, plus a directive prompt telling the AI where features go.

Output: assets/monitor/models/goblin/goblin_skin.png (256×256).
"""
import requests
import base64
import io
from pathlib import Path
from PIL import Image

UV_REF = Path(r"D:/Documents/JetBrains/PS1Godot/tools/uvgami/goblin_uv_pretinted.png")
OUT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/models/goblin/goblin_skin.png")
KEY_TXT = Path(r"C:/Users/Cornelio/Desktop/key.txt")

PROMPT = (
    "Refine this UV texture atlas in PS1 hand-painted style. The input "
    "image already has the correct spatial layout — your job is to "
    "DETAIL it without moving any colored region. CRITICAL: the spatial "
    "positions of every colored area MUST be preserved. Do not relocate "
    "features.\n"
    "Specifically:\n"
    "- The yellow circles ARE eyes — keep them at exactly those pixel "
    "  locations, just add detail (deep black socket around them, glowing "
    "  yellow iris).\n"
    "- The red areas are the MOUTH — keep position, add teeth/lips.\n"
    "- The brown/red triangle near eyes is the NOSE — keep, add nostril "
    "  shading.\n"
    "- All green areas are SKIN — vary shade per region but keep position; "
    "  add wart/wrinkle texture, mossy variation.\n"
    "- The narrow triangular islands (with brown/green) are EARS — add "
    "  pink inner-ear shading on the visible inner side.\n"
    "- Black areas STAY BLACK (UV gutter — outside any island).\n"
    "Style: PS1 1990s hand-painted pixel art, hard edges, dithering, "
    "sickly green palette, no smooth gradients. Output 256x256 effective."
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
    if not UV_REF.exists():
        print(f"UV reference {UV_REF} not found — run gen_goblin_labeled_uv.py first")
        return

    url = "https://api.openai.com/v1/images/edits"
    with open(UV_REF, "rb") as f:
        files = {"image": ("uv_labeled.png", f, "image/png")}
        data = {
            "model": "gpt-image-1",
            "prompt": PROMPT,
            "size": "1024x1024",
            "quality": "low",
            "n": "1",
        }
        headers = {"Authorization": f"Bearer {key}"}
        print("calling gpt-image-1 /edits with labeled UV reference...")
        resp = requests.post(url, headers=headers, files=files, data=data,
                             timeout=180)

    if resp.status_code != 200:
        print(f"HTTP {resp.status_code}: {resp.text[:500]}")
        return

    payload = resp.json()
    b64 = payload["data"][0]["b64_json"]
    img = Image.open(io.BytesIO(base64.b64decode(b64))).convert("RGB")
    img = img.resize((256, 256), Image.Resampling.NEAREST)
    img.save(OUT)
    print(f"wrote {OUT} (256×256)")


if __name__ == "__main__":
    gen()
