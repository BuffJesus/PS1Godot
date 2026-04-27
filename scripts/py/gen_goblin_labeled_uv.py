"""Render a UV preview that LABELS each island by which 3D direction
the underlying mesh faces point. Without these labels gpt-image-1
paints features (eyes, etc.) on whichever island it thinks looks
face-shaped, which lands them on the back of the head as often as the
front.

Algorithm:
  1. Load the OBJ mesh + UVs.
  2. For each triangle, classify its normal's dominant axis:
     +Z FRONT, -Z BACK, +X RIGHT, -X LEFT, +Y TOP, -Y BOTTOM.
  3. For each direction, compute the centroid of all UVs whose tris
     belong to that direction.
  4. Draw the direction label at that centroid on top of the UV preview.

Output: tools/uvgami/goblin_uv_labeled.png — the file we'll feed to
gpt-image-1 /edits as the layout reference.
"""
import numpy as np
import trimesh
from PIL import Image, ImageDraw, ImageFont
from pathlib import Path

OBJ = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/models/goblin/GoblinUnwrapped.obj")
OUT = Path(r"D:/Documents/JetBrains/PS1Godot/tools/uvgami/goblin_uv_labeled.png")
SIZE = 1024  # gpt-image-1 likes 1024×1024

m = trimesh.load(OBJ, force="mesh", process=False)
verts3d = m.vertices
uvs = m.visual.uv
faces = m.faces

# Per-triangle normal (cross of two edges, normalized).
def tri_normal(tri):
    p0 = verts3d[tri[0]]
    p1 = verts3d[tri[1]]
    p2 = verts3d[tri[2]]
    n = np.cross(p1 - p0, p2 - p0)
    nl = np.linalg.norm(n)
    return n / nl if nl > 1e-9 else np.array([0, 0, 1.0])

# Classify into 6 buckets by dominant axis sign.
LABELS = ("RIGHT", "LEFT", "TOP", "BOTTOM", "FRONT", "BACK")  # +X,-X,+Y,-Y,+Z,-Z
buckets = {l: [] for l in LABELS}  # collect triangle indices

for t_idx, tri in enumerate(faces):
    n = tri_normal(tri)
    a = np.abs(n)
    axis = int(np.argmax(a))
    sign = n[axis] >= 0
    label = LABELS[axis * 2 + (0 if sign else 1)]
    buckets[label].append(t_idx)

# Subdivide FRONT-facing triangles by Y-height into face features.
# Without this the FRONT centroid is the average across ALL front tris,
# which lands near the chin (lower-poly skull tops, denser jaw mesh) and
# the AI paints features at THAT spot. By splitting FRONT into bands
# we give the AI explicit anchor points for EYES, NOSE, MOUTH, etc.
def tri_centroid_y(t_idx):
    return verts3d[faces[t_idx]][:, 1].mean()

front_tris = buckets["FRONT"]
if front_tris:
    ys = np.array([tri_centroid_y(t) for t in front_tris])
    y_min, y_max = ys.min(), ys.max()
    y_span = y_max - y_min if y_max > y_min else 1.0
    FRONT_BANDS = [
        ("FOREHEAD", 0.80, 1.00),
        ("EYES",     0.55, 0.80),
        ("NOSE",     0.35, 0.55),
        ("MOUTH",    0.18, 0.35),
        ("CHIN",     0.00, 0.18),
    ]
    for band_name, lo, hi in FRONT_BANDS:
        band_tris = []
        for t_idx, y in zip(front_tris, ys):
            t = (y - y_min) / y_span  # normalized 0..1
            if lo <= t < hi or (hi == 1.0 and t == 1.0):
                band_tris.append(t_idx)
        buckets[band_name] = band_tris
    # Drop the catch-all FRONT label so the AI uses the granular ones.
    del buckets["FRONT"]
    LABELS = tuple(b[0] for b in FRONT_BANDS) + ("BACK", "LEFT", "RIGHT", "TOP", "BOTTOM")

# For each bucket, find UV centroid (average over all participating
# vertex UVs across all its triangles).
def uv_centroid(tri_idx_list):
    if not tri_idx_list:
        return None
    pts = []
    for ti in tri_idx_list:
        for vi in faces[ti]:
            pts.append(uvs[vi])
    pts = np.array(pts)
    return pts.mean(axis=0)

# Render: white background, black UV edges, big bold colored labels.
img = Image.new("RGB", (SIZE, SIZE), (255, 255, 255))
draw = ImageDraw.Draw(img)

for tri in faces:
    pts = [(uvs[i][0] * (SIZE - 1), (1 - uvs[i][1]) * (SIZE - 1)) for i in tri]
    draw.line([pts[0], pts[1]], fill=(180, 180, 180), width=2)
    draw.line([pts[1], pts[2]], fill=(180, 180, 180), width=2)
    draw.line([pts[2], pts[0]], fill=(180, 180, 180), width=2)

font = None
for cand in ("C:/Windows/Fonts/arialbd.ttf", "C:/Windows/Fonts/arial.ttf"):
    try:
        font = ImageFont.truetype(cand, 40)  # smaller — more labels now
        break
    except OSError:
        continue
if font is None:
    font = ImageFont.load_default()

LABEL_COLORS = {
    "FOREHEAD": (200, 100, 30),   # orange-red
    "EYES":     (220, 30, 30),    # bright red — most important
    "NOSE":     (200, 30, 100),   # red-pink
    "MOUTH":    (160, 30, 30),    # dark red
    "CHIN":     (120, 30, 30),    # darker red
    "BACK":     (30, 30, 200),    # blue
    "LEFT":     (30, 150, 30),    # green
    "RIGHT":    (150, 30, 150),   # purple
    "TOP":      (200, 120, 30),   # orange
    "BOTTOM":   (60, 60, 60),     # dark gray
}

print("Buckets (tri counts):")
for label in LABELS:
    n = len(buckets[label])
    print(f"  {label:6s}: {n} triangles")
    cen = uv_centroid(buckets[label])
    if cen is None:
        continue
    cx = cen[0] * (SIZE - 1)
    cy = (1 - cen[1]) * (SIZE - 1)
    color = LABEL_COLORS[label]
    # Outlined text for readability against any background.
    text = label
    bbox = draw.textbbox((0, 0), text, font=font)
    tw = bbox[2] - bbox[0]
    th = bbox[3] - bbox[1]
    x = cx - tw / 2
    y = cy - th / 2
    # White outline
    for dx in (-2, 0, 2):
        for dy in (-2, 0, 2):
            draw.text((x + dx, y + dy), text, font=font, fill=(255, 255, 255))
    draw.text((x, y), text, font=font, fill=color)

img.save(OUT)
print(f"wrote {OUT}")
