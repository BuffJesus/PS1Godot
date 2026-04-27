"""AI-regen Kenney textures we actually use, with a horror-aesthetic
prompt. Preserves the original UV layout (uses gpt-image-1 /edits with
the original as input + a "darken/atmospheric" prompt) so each Kenney
material's existing UV coords still sample the same logical region —
the COLORS shift, not the cell layout.

Targets the textures actually referenced by props in monitor.tscn:
  - survival-kit/colormap.png        (barrels)
  - retro-urban-kit/Textures/doors.png      (intruder door)
  - retro-urban-kit/Textures/planks.png     (pallet)
  - retro-urban-kit/Textures/metal.png      (dumpster)
  - retro-urban-kit/Textures/bars.png       (barrier)
  - retro-urban-kit/Textures/wall.png       (light pole etc — generic)
"""
import requests, base64, io, time
from pathlib import Path
from PIL import Image

ROOT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/models")
KEY_TXT = Path(r"C:/Users/Cornelio/Desktop/key.txt")

# Each tuple: (relative path, prompt). Atlas/single texture both work
# the same — gpt-image-1 /edits keeps the spatial layout while shifting
# colors per the prompt.
TARGETS = [
    ("kenney_survival-kit/Models/GLB format/Textures/colormap.png",
     "Recolor this color palette atlas keeping the EXACT cell positions "
     "and grid layout. Shift every cell to a darker, sicklier tone: dim "
     "olive greens, rust browns, deep crimson, charcoal grays. Lower "
     "saturation. Preserve the cell structure pixel-for-pixel — only "
     "the colors change. PSX horror aesthetic."),
    ("kenney_retro-urban-kit/Models/GLB format/Textures/doors.png",
     "Re-paint these doors as weathered, derelict, horror-game doors. "
     "Rust streaks, peeling paint, dark stains. Keep the EXACT panel "
     "layout and pixel positions of the original. Just reskin: dark "
     "browns, rusted reds, dirty grays. PSX low-res aesthetic."),
    ("kenney_retro-urban-kit/Models/GLB format/Textures/planks.png",
     "Re-paint these wood planks as old, rotted, weathered horror "
     "planks. Dark brown with black stains and water damage. Keep the "
     "EXACT plank positions and grain direction. Lower saturation, "
     "darker palette. PSX aesthetic."),
    ("kenney_retro-urban-kit/Models/GLB format/Textures/metal.png",
     "Re-paint this metal panel texture as rusted, dented, weathered "
     "horror-game metal. Deep rust orange, dirty grays, oily black "
     "stains. Keep the EXACT panel layout and rivet positions. "
     "Lower saturation, darker palette. PSX aesthetic."),
]


def fetch_openai_key():
    return KEY_TXT.read_text().strip().splitlines()[1].strip()


def regen(key, rel_path: str, prompt: str):
    full = ROOT / rel_path
    if not full.exists():
        print(f"  SKIP {rel_path}: file not found"); return
    backup = full.with_suffix(full.suffix + ".original")
    if not backup.exists():
        import shutil; shutil.copy2(full, backup)
    # gpt-image-1 /edits requires the input as a multipart upload.
    print(f"  {rel_path} ...", end=" ", flush=True)
    with open(backup, "rb") as f:
        files = {"image": (full.name, f, "image/png")}
        data = {
            "model": "gpt-image-1", "prompt": prompt,
            "size": "1024x1024", "quality": "low",
            "n": "1",
        }
        r = requests.post(
            "https://api.openai.com/v1/images/edits",
            headers={"Authorization": f"Bearer {key}"},
            files=files, data=data, timeout=180,
        )
    if r.status_code != 200:
        print(f"HTTP {r.status_code}: {r.text[:200]}")
        return
    img = Image.open(io.BytesIO(base64.b64decode(
        r.json()["data"][0]["b64_json"]))).convert("RGB")
    # Resize back to original dimensions
    orig = Image.open(backup)
    img = img.resize(orig.size, Image.Resampling.NEAREST)
    img.save(full)
    print(f"ok ({orig.size[0]}x{orig.size[1]})")


if __name__ == "__main__":
    key = fetch_openai_key()
    if not key:
        print("no OpenAI key"); exit()
    print(f"AI-regenerating {len(TARGETS)} Kenney textures...")
    for rel, prompt in TARGETS:
        regen(key, rel, prompt)
        time.sleep(0.5)
    print("done.")
