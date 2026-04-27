"""Generate a tiny PSX-friendly skin texture for GoblinLowPoly.fbx.

The model has no UVs guaranteed, so we paint a 32x32 mostly-uniform mossy
green with low-frequency noise + darker speckles. Even when the model's
UVs sample randomly across the texture, every sample lands on a "goblin
green" pixel, so the model reads as one cohesive creature.

PSX VRAM-friendly: 32x32 = 1KB at 16bpp, ~512B at 8bpp CLUT. Multiple of
4/8/2 for any bit depth.
"""
from PIL import Image
import random
from pathlib import Path

W = H = 32
random.seed(42)  # deterministic across re-runs

# Mossy goblin palette: dark sickly green base, mid-green highlights,
# very dark olive shadows. Avoids saturated colors so it reads on PSX.
PALETTE = [
    (62, 90, 48),   # base mossy green
    (78, 108, 56),  # lighter highlight
    (52, 76, 40),   # mid-shadow
    (38, 58, 32),   # dark crease
    (88, 112, 60),  # rare brighter spot
]
WEIGHTS = [50, 25, 15, 8, 2]

img = Image.new("RGB", (W, H))
px = img.load()

for y in range(H):
    for x in range(W):
        # Weighted random pick. PSX-style chunky variation.
        c = random.choices(PALETTE, weights=WEIGHTS, k=1)[0]
        # Optional: slight darken near the edges to suggest shading.
        if x < 2 or x >= W - 2 or y < 2 or y >= H - 2:
            c = tuple(max(0, ch - 12) for ch in c)
        px[x, y] = c

# A few darker speckles (warts/bumps).
for _ in range(8):
    x = random.randint(2, W - 3)
    y = random.randint(2, H - 3)
    px[x, y] = (32, 48, 28)

out = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/models/goblin/goblin_skin.png")
out.parent.mkdir(parents=True, exist_ok=True)
img.save(out)
print(f"wrote {out} ({W}x{H})")
