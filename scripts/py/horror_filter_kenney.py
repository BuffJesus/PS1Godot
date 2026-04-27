"""Apply a horror-aesthetic filter to all Kenney colormap.png atlases.
Each kit's atlas defines the colors every material in that kit samples
from — replacing the atlas in-place propagates a darker/sicklier palette
across every door, chair, barrel, etc. without per-asset edits.

Filter chain (preserves original cell layout so UV samples still hit
the same logical pixels — colors just shift):
  1. Backup the original to colormap.png.original (one-time)
  2. Desaturate to ~50% (less cartoony)
  3. Darken value to ~60% (dimmer)
  4. Tint toward sickly green-yellow (multiply by (0.85, 1.0, 0.75))
  5. Mild grain noise per pixel (-12..+12) for grunge
  6. Slight gamma curve so highlights stay visible

If the result looks too dim, dial back DARKEN.
"""
from PIL import Image
import random
from pathlib import Path

KITS = [
    "kenney_furniture-kit",
    "kenney_retro-urban-kit",
    "kenney_survival-kit",
]
ROOT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/models")

DESAT = 0.55       # 0 = full grayscale, 1 = original color
DARKEN = 0.62      # multiply value by this
TINT = (0.88, 1.0, 0.78)  # sickly green-yellow (less red, more green, less blue)
GRAIN = 12         # +/- per channel
GAMMA = 0.92       # <1 = brighten midtones slightly to preserve readability

random.seed("horror-kenney")

def filter_pixel(r, g, b):
    # 1. Desaturate toward luminance.
    lum = 0.299 * r + 0.587 * g + 0.114 * b
    r = lum + (r - lum) * DESAT
    g = lum + (g - lum) * DESAT
    b = lum + (b - lum) * DESAT
    # 2. Darken.
    r *= DARKEN; g *= DARKEN; b *= DARKEN
    # 3. Tint.
    r *= TINT[0]; g *= TINT[1]; b *= TINT[2]
    # 4. Gamma.
    r = 255 * pow(max(0, r) / 255, GAMMA)
    g = 255 * pow(max(0, g) / 255, GAMMA)
    b = 255 * pow(max(0, b) / 255, GAMMA)
    # 5. Grain.
    r += random.randint(-GRAIN, GRAIN)
    g += random.randint(-GRAIN, GRAIN)
    b += random.randint(-GRAIN, GRAIN)
    return (max(0, min(255, int(r))),
            max(0, min(255, int(g))),
            max(0, min(255, int(b))))


def process(p: Path):
    backup = p.with_suffix(".png.original")
    if not backup.exists():
        # Backup the pristine atlas once so we can re-filter / revert.
        import shutil
        shutil.copy2(p, backup)
    src = Image.open(backup).convert("RGB")
    px = src.load()
    W, H = src.size
    out = Image.new("RGB", (W, H))
    op = out.load()
    for y in range(H):
        for x in range(W):
            op[x, y] = filter_pixel(*px[x, y])
    out.save(p)
    print(f"  filtered {p.relative_to(ROOT)}  ({W}x{H})")


for kit in KITS:
    kit_root = ROOT / kit / "Models"
    if not kit_root.exists():
        print(f"skip {kit}: no Models/ dir"); continue
    for cmap in kit_root.rglob("colormap.png"):
        process(cmap)

print("done — kit colormaps now horror-filtered.")
