"""Procedural replacements for the AI-bad static overlay (0% transparent)
and AI-faint cobweb (97% transparent). Geometric patterns where exact
control over transparency level matters more than artistic flair."""
from PIL import Image, ImageDraw
import random
import math
from pathlib import Path

ROOT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/textures")

# ── 1. Static overlay (256x256, ~50% transparent noise) ────────────────
random.seed("static")
W = H = 256
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
px = img.load()
# Sprinkle bright dots — 40% coverage. Each dot is white-ish.
for y in range(H):
    for x in range(W):
        r = random.random()
        if r < 0.20:
            v = random.randint(180, 255)
            px[x, y] = (v, v, v, 255)
        elif r < 0.30:
            v = random.randint(40, 100)
            px[x, y] = (v, v, v, 255)
        # else stays transparent
img.save(ROOT / "static_overlay.png")
print(f"wrote static_overlay.png (256x256, ~30% noise pixels)")

# ── 2. Cobweb corner (64x64, radial threads from top-left) ─────────────
W = H = 64
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)
COL = (200, 200, 210, 220)  # light gray with slight transparency for ghostly look
# Radial threads emanating from (0, 0) — corner of the texture.
# 6 main spokes spread across the quadrant.
for i in range(6):
    angle = (i / 5.0) * (math.pi / 2)  # 0 to 90 degrees
    x_end = int(W * math.cos(angle) * 0.95)
    y_end = int(H * math.sin(angle) * 0.95)
    draw.line([(0, 0), (x_end, y_end)], fill=COL, width=1)
# Spiral connecting threads — concentric arcs at increasing radii.
for r_idx in range(1, 5):
    r = r_idx * 14
    # Sample points along the arc
    pts = []
    for i in range(20):
        a = (i / 19.0) * (math.pi / 2)
        x = int(r * math.cos(a))
        y = int(r * math.sin(a))
        if 0 <= x < W and 0 <= y < H:
            pts.append((x, y))
    if len(pts) >= 2:
        draw.line(pts, fill=COL, width=1)
img.save(ROOT / "cobweb_corner.png")
print(f"wrote cobweb_corner.png (64x64 — 6 spokes + 4 spiral arcs)")
