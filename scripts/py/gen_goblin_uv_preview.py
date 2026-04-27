"""Render the goblin's UV layout to a PNG so we can SEE the unwrap and
optionally feed it to AI image gen as a layout reference.

Output: tools/uvgami/goblin_uv_preview.png — 512×512 with UV edges drawn
in black on white, useful as a reference image for hand-painting or for
GPT-image-1's edit endpoint."""
import trimesh
from PIL import Image, ImageDraw
from pathlib import Path

SRC = Path(r"D:/Documents/JetBrains/PS1Godot/tools/uvgami/goblin_unwrapped.obj")
OUT = Path(r"D:/Documents/JetBrains/PS1Godot/tools/uvgami/goblin_uv_preview.png")
SIZE = 512

m = trimesh.load(SRC, force="mesh", process=False)
uv = m.visual.uv  # (N, 2) in [0,1]
faces = m.faces   # (T, 3) vert indices

img = Image.new("RGB", (SIZE, SIZE), (255, 255, 255))
draw = ImageDraw.Draw(img)

for tri in faces:
    pts = []
    for vi in tri:
        u, v = uv[vi]
        pts.append((u * (SIZE - 1), (1 - v) * (SIZE - 1)))  # flip V for image coords
    draw.line([pts[0], pts[1]], fill=(0, 0, 0), width=1)
    draw.line([pts[1], pts[2]], fill=(0, 0, 0), width=1)
    draw.line([pts[2], pts[0]], fill=(0, 0, 0), width=1)

img.save(OUT)
print(f"wrote {OUT} ({SIZE}×{SIZE}, {len(faces)} triangles)")
