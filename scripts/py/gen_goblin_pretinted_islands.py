"""Pretint by UV ISLAND, not by per-triangle bucket. The previous
version bucketed individual triangles by dominant axis, which split
the goblin's eye-area faces across multiple regions (one face on a
side-island, another on the front-island). Eyes painted on the front
appeared while the other "eye" face on the side got plain skin —
result: cyclops with a stray side eye.

This version:
  1. Build a graph of faces connected by shared UV vertices.
  2. Find connected components → UV islands.
  3. For each island, compute its 3D centroid and classify
     anatomically (FOREHEAD / EYES / NOSE / MOUTH / CHIN / BACK /
     LEFT_EAR / RIGHT_EAR / SCALP / etc).
  4. Pretint ENTIRE islands at once with feature colors.

Output: tools/uvgami/goblin_uv_pretinted.png — same target path so the
existing texture-gen script picks it up automatically.
"""
import numpy as np
import trimesh
from PIL import Image, ImageDraw
from pathlib import Path
from collections import defaultdict, deque

OBJ = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/models/goblin/GoblinUnwrapped.obj")
OUT = Path(r"D:/Documents/JetBrains/PS1Godot/tools/uvgami/goblin_uv_pretinted.png")
SIZE = 1024

m = trimesh.load(OBJ, force="mesh", process=False)
verts3d = m.vertices
uvs = m.visual.uv
faces = m.faces
n_faces = len(faces)

# ── 1. UV-island connectivity ──────────────────────────────────────────
# Two faces share a UV island iff they share at least one UV-VERTEX
# index (since OBJ uses per-vertex UVs and the loader splits seam
# vertices, faces in different islands will have *different* indices for
# the same 3D vertex). This means "same UV index" → "same UV island".
vert_to_faces = defaultdict(list)
for fi, tri in enumerate(faces):
    for vi in tri:
        vert_to_faces[vi].append(fi)

face_islands = [-1] * n_faces
island_id = 0
for fi in range(n_faces):
    if face_islands[fi] != -1:
        continue
    # BFS from this face.
    stack = deque([fi])
    while stack:
        cur = stack.popleft()
        if face_islands[cur] != -1:
            continue
        face_islands[cur] = island_id
        for vi in faces[cur]:
            for nb in vert_to_faces[vi]:
                if face_islands[nb] == -1:
                    stack.append(nb)
    island_id += 1
n_islands = island_id

# Group face indices per island.
islands = defaultdict(list)
for fi, iid in enumerate(face_islands):
    islands[iid].append(fi)

print(f"found {n_islands} UV islands")

# ── 2. Per-island anatomical classification ────────────────────────────
# Compute 3D centroid + bounding box of each island, then label by
# position. Goblin model orientation (verified earlier): +Y up, +Z
# forward (face direction). Bounds approx X:[-0.33,+0.33] Y:[0.59,0.98]
# Z:[-0.10,+0.30].

bounds = m.bounds
y_min, y_max = bounds[0][1], bounds[1][1]
y_span = y_max - y_min
x_min, x_max = bounds[0][0], bounds[1][0]
z_min, z_max = bounds[0][2], bounds[1][2]

def island_stats(face_idxs):
    pts = []
    for fi in face_idxs:
        for vi in faces[fi]:
            pts.append(verts3d[vi])
    pts = np.array(pts)
    return pts.mean(axis=0), pts.min(axis=0), pts.max(axis=0)


def classify(face_idxs):
    """Return semantic label for an island based on its 3D position.

    Classification gates use BOTH Y-height AND X-laterality. Earlier
    versions used Y alone, which mis-bucketed cheek faces (high Y, wide
    X) into EYES (high Y, central X) → AI painted yellow eyes onto the
    cheeks. The X gate fixes that: EYES require |cx| < x_eye_max so
    cheeks never qualify."""
    cen, mn, mx = island_stats(face_idxs)
    cx, cy, cz = cen
    ny = (cy - y_min) / y_span if y_span > 1e-6 else 0.5  # 0=bottom 1=top
    abs_cx = abs(cx)
    x_half = max(abs(x_min), abs(x_max))  # furthest from center
    # Detect ear: long thin island far from center on X with extended X span.
    x_span = mx[0] - mn[0]
    is_thin_long = x_span > 0.10 and abs_cx > 0.15
    if is_thin_long:
        return "RIGHT_EAR" if cx > 0 else "LEFT_EAR"

    # Front-facing islands (cz > 0): subdivide by Y AND X.
    # Eyes sit upper-middle of face AND close to centerline (|x|/x_half < 0.55).
    # Cheeks at similar Y but wider X. Nose central X mid Y. Mouth/chin low Y.
    EYE_X_MAX_REL = 0.55  # |cx| < this fraction of half-width = eye-eligible
    if cz > 0.0:
        rel_x = abs_cx / x_half if x_half > 1e-6 else 0.0
        is_central = rel_x < EYE_X_MAX_REL
        if ny > 0.88:
            return "FOREHEAD" if is_central else "TEMPLE"
        # EYES band raised: was 0.55-0.78, now 0.68-0.88 (upper portion)
        if ny > 0.68:
            return "EYES" if is_central else "CHEEK_UPPER"
        if ny > 0.45:
            return "NOSE" if is_central else "CHEEK_LOWER"
        if ny > 0.22:
            return "MOUTH"
        return "CHIN"
    # Back-facing.
    if cz < -0.05:
        return "BACK_TOP" if ny > 0.7 else "BACK_BOTTOM"
    # Sides / top / bottom (mid-Z).
    if ny > 0.85:
        return "TOP"
    if ny < 0.25:
        return "JAW"
    return "RIGHT_CHEEK" if cx > 0 else "LEFT_CHEEK"


island_labels = {iid: classify(face_idxs) for iid, face_idxs in islands.items()}

# Summary by label.
from collections import Counter
label_counts = Counter(island_labels.values())
print("\nlabel distribution:")
for label, n in sorted(label_counts.items(), key=lambda x: -x[1]):
    tri_total = sum(len(islands[i]) for i, lab in island_labels.items() if lab == label)
    print(f"  {label:14s}: {n:3d} islands, {tri_total} tris total")

# ── 3. Pretint per island ──────────────────────────────────────────────
COLORS = {
    "FOREHEAD":    (60, 100, 50),
    "TEMPLE":      (55, 95, 48),
    "EYES":        (80, 130, 60),
    "CHEEK_UPPER": (55, 95, 45),
    "CHEEK_LOWER": (55, 95, 45),
    "NOSE":        (90, 60, 40),
    "MOUTH":       (140, 30, 30),
    "CHIN":        (55, 95, 45),
    "BACK_TOP":    (40, 75, 35),
    "BACK_BOTTOM": (45, 80, 38),
    "LEFT_EAR":    (50, 75, 40),
    "RIGHT_EAR":   (50, 75, 40),
    "TOP":         (45, 80, 40),
    "JAW":         (55, 90, 45),
    "LEFT_CHEEK":  (55, 95, 45),
    "RIGHT_CHEEK": (55, 95, 45),
}

img = Image.new("RGB", (SIZE, SIZE), (0, 0, 0))
draw = ImageDraw.Draw(img)


def to_px(uv):
    return (uv[0] * (SIZE - 1), (1 - uv[1]) * (SIZE - 1))


for iid, face_idxs in islands.items():
    label = island_labels[iid]
    color = COLORS.get(label, (50, 80, 40))
    for fi in face_idxs:
        pts = [to_px(uvs[v]) for v in faces[fi]]
        draw.polygon(pts, fill=color)

# ── 4. Highlight only LARGEST islands per feature ─────────────────────
# 142 fragmented islands means many tiny EYES/EAR splits. Putting a
# yellow dot on every one creates visual chaos and confuses the AI's
# refinement. Instead pick the single largest island per side (left/right
# split by 3D X centroid sign) and only mark THOSE with the dot. The
# small fragments still get the EYES base skin tint above so they blend.

def biggest_islands_per_side(label):
    """Return [largest_left_iid, largest_right_iid] for a label, or
    fewer if one side has no islands."""
    candidates = [(iid, len(islands[iid])) for iid, lab in island_labels.items() if lab == label]
    if not candidates:
        return []
    # Split by X centroid sign.
    left, right = [], []
    for iid, sz in candidates:
        cen, _, _ = island_stats(islands[iid])
        (left if cen[0] < 0 else right).append((iid, sz))
    out = []
    if left:
        out.append(max(left, key=lambda t: t[1])[0])
    if right:
        out.append(max(right, key=lambda t: t[1])[0])
    return out

for iid in biggest_islands_per_side("EYES"):
    eye_uvs = np.array([uvs[v] for fi in islands[iid] for v in faces[fi]])
    cen = eye_uvs.mean(axis=0)
    x, y = to_px(cen)
    r = SIZE // 25
    draw.ellipse([(x - r, y - r), (x + r, y + r)], fill=(255, 220, 30))
    r2 = SIZE // 50
    draw.ellipse([(x - r2, y - r2), (x + r2, y + r2)], fill=(0, 0, 0))
    print(f"  marked EYE on island {iid} at UV ({cen[0]:.2f},{cen[1]:.2f})")

for label in ("LEFT_EAR", "RIGHT_EAR"):
    for iid in biggest_islands_per_side(label):
        ear_uvs = np.array([uvs[v] for fi in islands[iid] for v in faces[fi]])
        cen = ear_uvs.mean(axis=0)
        x, y = to_px(cen)
        r = SIZE // 35
        draw.ellipse([(x - r, y - r), (x + r, y + r)], fill=(180, 90, 100))

img.save(OUT)
print(f"\nwrote {OUT}")
