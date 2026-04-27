"""Generate a 64x64 blood-splatter PNG with transparent background.
Test asset for the new PS1MeshInstance.Translucent flag — the runtime
renders with PSX semi-trans + CLUT[0]=0x0000 alpha-keying, so RGBA
pixels with alpha=0 disappear on PSX.
"""
from PIL import Image, ImageDraw
import random
from pathlib import Path

random.seed("blood-2026-04-25")
W = H = 64
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))   # fully transparent
draw = ImageDraw.Draw(img)

# Main pool — irregular blob in center.
cx, cy = W // 2, H // 2
for _ in range(6):
    rx = random.randint(8, 18)
    ry = random.randint(8, 18)
    ox = random.randint(-6, 6)
    oy = random.randint(-6, 6)
    draw.ellipse([(cx - rx + ox, cy - ry + oy),
                  (cx + rx + ox, cy + ry + oy)], fill=(120, 10, 10, 255))
# Slightly brighter highlights (clotting variation).
for _ in range(8):
    rx = random.randint(3, 7)
    ry = random.randint(3, 7)
    ox = random.randint(-12, 12)
    oy = random.randint(-12, 12)
    draw.ellipse([(cx - rx + ox, cy - ry + oy),
                  (cx + rx + ox, cy + ry + oy)], fill=(160, 30, 30, 255))
# Splatter droplets at random offsets.
for _ in range(40):
    px = random.randint(2, W - 3)
    py = random.randint(2, H - 3)
    r = random.choice([1, 1, 2])
    draw.ellipse([(px - r, py - r), (px + r, py + r)], fill=(100, 5, 5, 255))
# A few "drip" lines downward from main pool.
for _ in range(4):
    sx = cx + random.randint(-10, 10)
    sy = cy + random.randint(0, 10)
    length = random.randint(10, 22)
    for i in range(length):
        if random.random() < 0.7:
            draw.point((sx, sy + i), fill=(120, 10, 10, 255))

out = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/textures/blood_decal.png")
img.save(out)
print(f"wrote {out} ({W}x{H} RGBA)")
