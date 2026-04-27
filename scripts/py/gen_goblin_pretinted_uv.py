"""Render a UV preview with the FACE FEATURES already pre-painted in
their correct UV positions. The AI then RE-PAINTS in PSX style on top
of this strong spatial guide — much more reliable than text labels alone.

For each face-feature region (computed from 3D Y-band of front-facing
triangles), we fill the UV polygons with a bold color cue:
  EYES → yellow circles on dark green
  NOSE → reddish triangle
  MOUTH → red mouth area
  Skin → mossy green
  Ears → pink interior

When this image goes into gpt-image-1 /edits, the AI keeps the spatial
positions of those colored cues but re-paints them in proper detail.
"""
import numpy as np
import trimesh
from PIL import Image, ImageDraw
from pathlib import Path

OBJ = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/models/goblin/GoblinUnwrapped.obj")
OUT = Path(r"D:/Documents/JetBrains/PS1Godot/tools/uvgami/goblin_uv_pretinted.png")
SIZE = 1024

m = trimesh.load(OBJ, force="mesh", process=False)
verts3d = m.vertices
uvs = m.visual.uv
faces = m.faces

def tri_normal(tri):
    p0, p1, p2 = verts3d[tri[0]], verts3d[tri[1]], verts3d[tri[2]]
    n = np.cross(p1 - p0, p2 - p0)
    nl = np.linalg.norm(n)
    return n / nl if nl > 1e-9 else np.array([0, 0, 1.0])

def tri_centroid_3d(tri):
    return verts3d[tri].mean(axis=0)

# Bucket tris by direction.
buckets = {"FRONT": [], "BACK": [], "LEFT": [], "RIGHT": [], "TOP": [], "BOTTOM": []}
for t_idx, tri in enumerate(faces):
    n = tri_normal(tri)
    a = np.abs(n)
    axis = int(np.argmax(a))
    sign = n[axis] >= 0
    label = ("RIGHT", "LEFT", "TOP", "BOTTOM", "FRONT", "BACK")[axis * 2 + (0 if sign else 1)]
    buckets[label].append(t_idx)

# Subdivide FRONT by 3D-Y into face features.
front = buckets["FRONT"]
ys = np.array([tri_centroid_3d(faces[t])[1] for t in front]) if front else np.array([])
y_min, y_max = (ys.min(), ys.max()) if len(ys) else (0.0, 1.0)
y_span = y_max - y_min if y_max > y_min else 1.0

def in_band(y, lo, hi):
    t = (y - y_min) / y_span
    return lo <= t < hi or (hi == 1.0 and t == 1.0)

feature_buckets = {
    "FOREHEAD": [t for t, y in zip(front, ys) if in_band(y, 0.80, 1.00)],
    "EYES":     [t for t, y in zip(front, ys) if in_band(y, 0.55, 0.80)],
    "NOSE":     [t for t, y in zip(front, ys) if in_band(y, 0.35, 0.55)],
    "MOUTH":    [t for t, y in zip(front, ys) if in_band(y, 0.18, 0.35)],
    "CHIN":     [t for t, y in zip(front, ys) if in_band(y, 0.00, 0.18)],
}

# Colors for each region — picked so the AI can't possibly miss them.
SKIN_COLORS = {
    "FOREHEAD": (60, 100, 50),
    "EYES":     (80, 130, 60),   # base; yellow circles overlaid below
    "NOSE":     (90, 60, 40),
    "MOUTH":    (140, 30, 30),
    "CHIN":     (55, 95, 45),
    "BACK":     (40, 75, 35),
    "LEFT":     (50, 90, 45),
    "RIGHT":    (50, 90, 45),
    "TOP":      (45, 80, 40),
    "BOTTOM":   (50, 85, 40),
}

img = Image.new("RGB", (SIZE, SIZE), (0, 0, 0))  # black background
draw = ImageDraw.Draw(img)

def to_px(uv):
    return (uv[0] * (SIZE - 1), (1 - uv[1]) * (SIZE - 1))

def paint_tris(tri_idxs, color):
    for ti in tri_idxs:
        pts = [to_px(uvs[v]) for v in faces[ti]]
        draw.polygon(pts, fill=color)

# Direction-only fills first (back/sides/etc.) so feature fills overwrite.
for direction in ("BACK", "TOP", "BOTTOM", "LEFT", "RIGHT"):
    paint_tris(buckets[direction], SKIN_COLORS[direction])

# Face features.
for feat in ("FOREHEAD", "CHIN", "NOSE", "MOUTH", "EYES"):
    paint_tris(feature_buckets[feat], SKIN_COLORS[feat])

# Add YELLOW EYE DOTS at the centroid of EYES tris.
if feature_buckets["EYES"]:
    eye_uvs = np.array([uvs[v] for ti in feature_buckets["EYES"] for v in faces[ti]])
    cx_uv, cy_uv = eye_uvs.mean(axis=0)
    # Find left/right by splitting at centroid X.
    left_mask = eye_uvs[:, 0] < cx_uv
    right_mask = ~left_mask
    if left_mask.any() and right_mask.any():
        l_cen = eye_uvs[left_mask].mean(axis=0)
        r_cen = eye_uvs[right_mask].mean(axis=0)
        for cen in (l_cen, r_cen):
            x, y = to_px(cen)
            r = SIZE // 30
            draw.ellipse([(x - r, y - r), (x + r, y + r)], fill=(255, 220, 30))
            r2 = SIZE // 60
            draw.ellipse([(x - r2, y - r2), (x + r2, y + r2)], fill=(0, 0, 0))

img.save(OUT)
print(f"wrote {OUT}")
print("feature triangle counts:")
for k, v in feature_buckets.items():
    print(f"  {k:8s}: {len(v)}")
