"""Procedural batch for the rest of the alpha-asset list.
  - footprints.png  (64x32)  — pair of foot-shaped ovals
  - shadow_blob.png (64x32)  — radial gradient shadow disc
  - fog_overlay.png (256x256)— top-to-bottom gray gradient with noise
  - glitch_bars.png (256x32) — horizontal scan-glitch streaks
  - stain_grime.png (64x64)  — dark irregular wall stain
"""
from PIL import Image, ImageDraw
import random
import math
from pathlib import Path

ROOT = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/textures")

# ── Footprints ────────────────────────────────────────────────────────
random.seed("foot")
W, H = 64, 32
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)
COL = (60, 50, 40, 200)  # muddy brown semi-trans
# Two oval shoeprints — one left foot, one right.
draw.ellipse([(8, 6),  (24, 24)], fill=COL)   # left
draw.ellipse([(40, 8), (56, 26)], fill=COL)   # right
# Toe blobs above each foot
for cx, cy in [(16, 5), (48, 7)]:
    for dx, dy in [(-3, -1), (0, -2), (3, -1)]:
        draw.ellipse([(cx + dx - 1, cy + dy - 1), (cx + dx + 1, cy + dy + 1)],
                     fill=COL)
img.save(ROOT / "footprints.png")
print("wrote footprints.png")

# ── Shadow blob ───────────────────────────────────────────────────────
W, H = 64, 32
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
px = img.load()
cx, cy = W / 2.0, H / 2.0
rx, ry = W / 2.5, H / 2.5
for y in range(H):
    for x in range(W):
        # Distance from center as ellipse
        nx = (x - cx) / rx
        ny = (y - cy) / ry
        d = math.sqrt(nx * nx + ny * ny)
        if d < 1.0:
            # Alpha falls off from 200 at center to 0 at edge
            a = int(180 * (1.0 - d) ** 1.5)
            px[x, y] = (0, 0, 0, max(0, a))
img.save(ROOT / "shadow_blob.png")
print("wrote shadow_blob.png")

# ── Fog overlay (DITHERED density — PSX hw alpha is binary) ───────────
# Insight: PSX semi-trans + alpha-key is BINARY per pixel. A pixel is
# either fully invisible (alpha=0 → CLUT[0]=0x0000 → discarded) or
# fully blended at 50/50 (any other color). Smooth alpha gradients in
# the PNG become hard-edged after PSX export.
#
# So instead of soft alpha, we use DENSITY DITHERING: each pixel either
# carries a fully-opaque fog dot or is fully transparent. The density
# of dots per area encodes "fog thickness". Eye reads dense-dots as
# thick fog, sparse-dots as thin fog, fading naturally.
#
# Density gradient: peak ~50% coverage at the floor band (lower 1/3),
# tapering to 0% at the upper 1/2. U-tileable so runtime can scroll.
random.seed("fog3")
W = H = 256
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
px = img.load()

# Density profile by Y. 0 = no dots, 1 = every pixel.
def density_at(y_norm):
    if y_norm < 0.40: return 0.0
    if y_norm < 0.55:
        # Linear ramp 0% at 0.4 → ~25% at 0.55
        return 0.25 * (y_norm - 0.40) / 0.15
    if y_norm < 0.85:
        # Plateau at 50%
        return 0.50
    # Soft fade out at the very bottom edge so fog doesn't cut at the bottom.
    return 0.50 * max(0.0, (1.0 - y_norm) / 0.15)

# Cool gray-blue dot color. Mid-bright so the 50/50 blend is visible
# but not blinding.
DOT_COLOR = (170, 180, 200, 255)

for y in range(H):
    d = density_at(y / (H - 1))
    if d <= 0: continue
    for x in range(W):
        if random.random() < d:
            px[x, y] = DOT_COLOR
img.save(ROOT / "fog_overlay.png")
print("wrote fog_overlay.png (dithered density — proper PSX fade)")

# ── Glitch bars ──────────────────────────────────────────────────────
random.seed("glitch")
W, H = 256, 32
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)
# 3-4 horizontal streaks at random Y, varying widths + brightness.
for _ in range(4):
    y = random.randint(0, H - 3)
    h = random.randint(1, 3)
    bright = random.randint(140, 255)
    a = random.randint(120, 220)
    # Streak with shifted segments to look like horizontal scan tear.
    seg_x = 0
    while seg_x < W:
        seg_w = random.randint(20, 80)
        if random.random() < 0.7:
            draw.rectangle([(seg_x, y), (seg_x + seg_w, y + h)],
                           fill=(bright, bright, bright, a))
        seg_x += seg_w + random.randint(0, 30)
img.save(ROOT / "glitch_bars.png")
print("wrote glitch_bars.png")

# ── Stain/grime ──────────────────────────────────────────────────────
random.seed("stain")
W = H = 64
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)
COL = (45, 35, 25, 180)
COL_DARK = (25, 18, 12, 220)
# Irregular blob using overlapping circles.
cx, cy = W // 2, H // 2
for _ in range(7):
    rx = random.randint(8, 18)
    ry = random.randint(8, 18)
    ox = random.randint(-12, 12)
    oy = random.randint(-12, 12)
    draw.ellipse([(cx + ox - rx, cy + oy - ry),
                  (cx + ox + rx, cy + oy + ry)], fill=COL)
# Darker core spots
for _ in range(4):
    rx = random.randint(3, 6)
    ox = random.randint(-8, 8)
    oy = random.randint(-8, 8)
    draw.ellipse([(cx + ox - rx, cy + oy - rx),
                  (cx + ox + rx, cy + oy + rx)], fill=COL_DARK)
img.save(ROOT / "stain_grime.png")
print("wrote stain_grime.png")
