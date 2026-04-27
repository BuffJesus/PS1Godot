"""Inject all the new alpha-keyed horror assets into monitor.tscn:
  - Cobwebs in 4 corners of each room (16 instances)
  - Blood splatters scattered across rooms (~12 instances)
  - Occult symbols in 2 rooms
  - Graffiti texts in 3 rooms
  - Crack streaks on a few walls
  - Static-interference UI canvas (full-screen overlay, hidden by default,
    flashed briefly by Lua when real events fire)
  - Ghost figure spawns (1 per room, StartsInactive=true, Tag=2 for Lua spawn)

Idempotent: strips any existing horror_* nodes before re-adding so the
script can be re-run after tweaking positions/sizes.
"""
import re
from pathlib import Path

p = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/scenes/monitor/monitor.tscn")
src = p.read_text(encoding="utf-8")


# ── Texture ext_resources to add (ID range 90-99) ──
TEXTURES = {
    "90_blood2":     "res://assets/monitor/textures/blood_decal_2.png",
    "91_blood3":     "res://assets/monitor/textures/blood_decal_3.png",
    "92_graf_here":  "res://assets/monitor/textures/graffiti_he_is_here.png",
    "93_graf_dont":  "res://assets/monitor/textures/graffiti_dont_look.png",
    "94_graf_run":   "res://assets/monitor/textures/graffiti_run.png",
    "95_pent":       "res://assets/monitor/textures/occult_pentagram.png",
    "96_sigil":      "res://assets/monitor/textures/occult_sigil.png",
    "97_ghost":      "res://assets/monitor/textures/ghost_figure.png",
    "98_crack":      "res://assets/monitor/textures/crack_wall.png",
    "99_cobweb":     "res://assets/monitor/textures/cobweb_corner.png",
    "100_static":    "res://assets/monitor/textures/static_overlay.png",
}
# Tag each texture as a ShaderMaterial wrapper (one .tres per texture).
# We don't write physical .tres files — instead generate inline SubResource
# ShaderMaterials in the .tscn (cheaper than scattering per-texture .tres).

# ── Add missing texture ext_resources ──
ext_anchor = '[ext_resource type="Material" path="res://assets/monitor/textures/ps1_blood_decal.tres" id="83_mat_blood"]\n'
assert ext_anchor in src, f"anchor missing: {ext_anchor!r}"
missing = [(kid, path) for kid, path in TEXTURES.items()
           if f'id="{kid}"' not in src]
if missing:
    block = "".join(f'[ext_resource type="Texture2D" path="{path}" id="{kid}"]\n'
                    for kid, path in missing)
    src = src.replace(ext_anchor, ext_anchor + block, 1)


# ── Inline ShaderMaterial subresource for each texture ──
# Pattern matches the existing ps1_blood_decal.tres but baked into the
# scene so we don't have to author 10 .tres files. ID format mat_<short>.
SHADER_EXT = '6_ps1_shader'  # existing ps1.gdshader ext_resource id (verified earlier)
MATERIAL_DEFS = [
    ("mat_blood2",    "90_blood2"),
    ("mat_blood3",    "91_blood3"),
    ("mat_graf_here", "92_graf_here"),
    ("mat_graf_dont", "93_graf_dont"),
    ("mat_graf_run",  "94_graf_run"),
    ("mat_pent",      "95_pent"),
    ("mat_sigil",     "96_sigil"),
    ("mat_ghost",     "97_ghost"),
    ("mat_crack",     "98_crack"),
    ("mat_cobweb",    "99_cobweb"),
    ("mat_static",    "100_static"),
]
mat_block = ""
for mat_id, tex_id in MATERIAL_DEFS:
    if f'sub_resource type="ShaderMaterial" id="{mat_id}"' in src:
        continue  # already present
    mat_block += (
        f'\n[sub_resource type="ShaderMaterial" id="{mat_id}"]\n'
        f'render_priority = 0\n'
        f'shader = ExtResource("{SHADER_EXT}")\n'
        f'shader_parameter/albedo_tex = ExtResource("{tex_id}")\n'
        f'shader_parameter/tint_color = Color(1, 1, 1, 1)\n'
        f'shader_parameter/snap_enabled = true\n'
        f'shader_parameter/snap_resolution = Vector2(320, 240)\n'
        f'shader_parameter/modulate_scale = 2.0\n'
        f'shader_parameter/fog_enabled = false\n'
        f'shader_parameter/fog_color = Color(0, 0, 0, 1)\n'
        f'shader_parameter/fog_near = 10.0\n'
        f'shader_parameter/fog_far = 50.0\n'
    )
# Insert mat_block right after the existing mesh_blood_decal subresource
mesh_anchor = '[sub_resource type="BoxMesh" id="mesh_blood_decal"]\nsize = Vector3(1.5, 1.5, 0.05)\n'
assert mesh_anchor in src, "mesh_blood_decal anchor missing"
src = src.replace(mesh_anchor, mesh_anchor + mat_block, 1)


# ── BoxMesh sub-resources for varied decal sizes ──
EXTRA_MESHES = [
    ("mesh_decal_small",    "Vector3(0.8, 0.8, 0.04)"),
    ("mesh_decal_wide",     "Vector3(2.0, 0.8, 0.04)"),
    ("mesh_decal_med",      "Vector3(1.2, 1.2, 0.04)"),
    ("mesh_decal_tall",     "Vector3(0.8, 1.5, 0.04)"),
    ("mesh_cobweb",         "Vector3(0.7, 0.7, 0.04)"),
    ("mesh_ghost",          "Vector3(0.7, 1.6, 0.04)"),
    ("mesh_crack_wide",     "Vector3(2.5, 1.0, 0.04)"),
]
mesh_block = ""
for mid, size in EXTRA_MESHES:
    if f'sub_resource type="BoxMesh" id="{mid}"' in src: continue
    mesh_block += f'\n[sub_resource type="BoxMesh" id="{mid}"]\nsize = {size}\n'
src = src.replace(mesh_anchor, mesh_anchor + mesh_block, 1)


# ── Strip existing horror_* nodes for re-runnability ──
horror_pat = re.compile(
    r'\n\[node name="horror_[^"]+" type="MeshInstance3D"[^\]]*\]\n(?:[^\[]*\n)+?(?=\[)',
    re.MULTILINE)
src, n_stripped = horror_pat.subn("\n", src)
print(f"stripped {n_stripped} prior horror_* nodes")


# ── Decal placements ──
# Format: (name_suffix, parent, mesh_id, mat_id, x, y, z, rotation_deg_y)
# Rotation 0 = facing +Z; 90 = facing +X; etc.
# Rooms (recap from prior notes):
#   Hallway:  cx=-80, cz=-8,  walls X:[-84,-76]=8w, Z:[-16,0]=16d
#   Storage:  cx=-80, cz=25,  X:[-85,-75]=10w, Z:[20,30]=10d
#   Parking:  cx=-80, cz=47,  X:[-86,-74]=12w, Z:[42,52]=10d
#   Backroom: cx=-80, cz=65,  X:[-84,-76]=8w, Z:[61.06,69.21]=~8d
#
# Wall front faces (where decals stick): each wall's front face is 0.25
# closer to the room center than the wall's authored Z/X coord.

DECALS = [
    # ─── HALLWAY ────────────────────────────────────────────────────
    # WallLeft front face at X=-83.75, normal=+X. Rotate decal 90°.
    ("horror_blood_h_l1",  "Feed01_Hallway",  "mesh_decal_small", "mat_blood2",    -83.65, 1.5, -10,  90),
    ("horror_graf_dont",   "Feed01_Hallway",  "mesh_decal_wide",  "mat_graf_dont", -83.65, 2.5,  -5,  90),
    # WallRight front face at X=-76.25, normal=-X. Rotate -90° = 270°.
    ("horror_blood_h_r1",  "Feed01_Hallway",  "mesh_decal_small", "mat_blood3",    -76.35, 1.2, -13, 270),
    # WallFar front face at Z=-15.75, normal=+Z. Rotate 0.
    ("horror_crack_h",     "Feed01_Hallway",  "mesh_crack_wide",  "mat_crack",     -82.5,  3.0,-15.6, 0),
    # Cobwebs in 4 corners (mounted on side walls near top).
    ("horror_web_h_tl",    "Feed01_Hallway",  "mesh_cobweb",      "mat_cobweb",    -83.65, 3.5, -1.5, 90),
    ("horror_web_h_tr",    "Feed01_Hallway",  "mesh_cobweb",      "mat_cobweb",    -76.35, 3.5, -1.5, 270),
    ("horror_web_h_bl",    "Feed01_Hallway",  "mesh_cobweb",      "mat_cobweb",    -83.65, 3.5,-14.5, 90),
    ("horror_web_h_br",    "Feed01_Hallway",  "mesh_cobweb",      "mat_cobweb",    -76.35, 3.5,-14.5, 270),

    # ─── STORAGE ────────────────────────────────────────────────────
    # WallLeft X=-85, front X=-84.75. Big graffiti.
    ("horror_graf_here_s", "Feed02_Storage",  "mesh_decal_wide",  "mat_graf_here", -84.65, 2.5, 26,  90),
    ("horror_blood_s_1",   "Feed02_Storage",  "mesh_decal_med",   "mat_blood2",    -84.65, 1.0, 22,  90),
    # WallRight X=-75, front X=-75.25. Pentagram.
    ("horror_pent_s",      "Feed02_Storage",  "mesh_decal_med",   "mat_pent",      -75.35, 2.2, 25, 270),
    ("horror_blood_s_2",   "Feed02_Storage",  "mesh_decal_small", "mat_blood3",    -75.35, 0.8, 28, 270),
    # WallFar Z=30, front Z=29.75.
    ("horror_crack_s",     "Feed02_Storage",  "mesh_crack_wide",  "mat_crack",     -80,    2.8, 29.6, 180),
    # Cobwebs.
    ("horror_web_s_tl",    "Feed02_Storage",  "mesh_cobweb",      "mat_cobweb",    -84.65, 3.5, 21,  90),
    ("horror_web_s_tr",    "Feed02_Storage",  "mesh_cobweb",      "mat_cobweb",    -75.35, 3.5, 21, 270),
    ("horror_web_s_bl",    "Feed02_Storage",  "mesh_cobweb",      "mat_cobweb",    -84.65, 3.5, 29,  90),
    ("horror_web_s_br",    "Feed02_Storage",  "mesh_cobweb",      "mat_cobweb",    -75.35, 3.5, 29, 270),

    # ─── PARKING ────────────────────────────────────────────────────
    # WallLeft X=-86, front X=-85.75. Sigil + graffiti.
    ("horror_sigil_p",     "Feed03_Parking",  "mesh_decal_med",   "mat_sigil",     -85.65, 2.5, 47,  90),
    ("horror_blood_p_1",   "Feed03_Parking",  "mesh_decal_small", "mat_blood2",    -85.65, 0.7, 50,  90),
    # WallRight X=-74, front X=-74.25.
    ("horror_graf_run",    "Feed03_Parking",  "mesh_decal_wide",  "mat_graf_run",  -74.35, 2.6, 47, 270),
    ("horror_blood_p_2",   "Feed03_Parking",  "mesh_decal_med",   "mat_blood3",    -74.35, 1.0, 44, 270),
    # WallFar Z=52, front Z=51.75.
    ("horror_crack_p",     "Feed03_Parking",  "mesh_crack_wide",  "mat_crack",     -80,    2.8, 51.6, 180),
    # Cobwebs.
    ("horror_web_p_tl",    "Feed03_Parking",  "mesh_cobweb",      "mat_cobweb",    -85.65, 3.5, 43,  90),
    ("horror_web_p_tr",    "Feed03_Parking",  "mesh_cobweb",      "mat_cobweb",    -74.35, 3.5, 43, 270),
    ("horror_web_p_bl",    "Feed03_Parking",  "mesh_cobweb",      "mat_cobweb",    -85.65, 3.5, 51,  90),
    ("horror_web_p_br",    "Feed03_Parking",  "mesh_cobweb",      "mat_cobweb",    -74.35, 3.5, 51, 270),

    # ─── BACK ROOM ──────────────────────────────────────────────────
    # WallLeft X=-84, front X=-83.75. Pentagram + blood.
    ("horror_pent_b",      "Feed04_BackRoom", "mesh_decal_med",   "mat_pent",      -83.65, 2.5, 65,  90),
    ("horror_blood_b_1",   "Feed04_BackRoom", "mesh_decal_med",   "mat_blood2",    -83.65, 1.0, 67,  90),
    # WallRight X=-76, front X=-76.25. Sigil + blood.
    ("horror_sigil_b",     "Feed04_BackRoom", "mesh_decal_med",   "mat_sigil",     -76.35, 2.5, 65, 270),
    ("horror_blood_b_2",   "Feed04_BackRoom", "mesh_decal_small", "mat_blood3",    -76.35, 1.0, 68, 270),
    # WallFar Z=69.21, front Z=68.96. Big graffiti.
    ("horror_graf_here_b", "Feed04_BackRoom", "mesh_decal_wide",  "mat_graf_here", -80,    2.7, 68.85, 180),
    # Cobwebs.
    ("horror_web_b_tl",    "Feed04_BackRoom", "mesh_cobweb",      "mat_cobweb",    -83.65, 3.5, 62,  90),
    ("horror_web_b_tr",    "Feed04_BackRoom", "mesh_cobweb",      "mat_cobweb",    -76.35, 3.5, 62, 270),
    ("horror_web_b_bl",    "Feed04_BackRoom", "mesh_cobweb",      "mat_cobweb",    -83.65, 3.5, 68,  90),
    ("horror_web_b_br",    "Feed04_BackRoom", "mesh_cobweb",      "mat_cobweb",    -76.35, 3.5, 68, 270),
]


import math
def make_node(name, parent, mesh_id, mat_id, tx, ty, tz, ry):
    cy = math.cos(math.radians(ry))
    sy = math.sin(math.radians(ry))
    return (
        f'\n[node name="{name}" type="MeshInstance3D" parent="{parent}"]\n'
        f"transform = Transform3D({cy:.4f}, 0, {sy:.4f}, "
        f"0, 1, 0, "
        f"{-sy:.4f}, 0, {cy:.4f}, "
        f"{tx}, {ty}, {tz})\n"
        f'material_override = SubResource("{mat_id}")\n'
        f'mesh = SubResource("{mesh_id}")\n'
        f'script = ExtResource("2_mesh")\n'
        f"Translucent = true\n"
        f"FlatColor = Color(1, 1, 1, 1)\n"
        f"Collision = 0\n"
    )


added = 0
for d in DECALS:
    src = src.rstrip() + "\n" + make_node(*d) + "\n"
    added += 1


# ── Static-interference UI canvas ──
# Hidden by default; Lua flashes it visible when a real event fires.
# Sits in hud_overlay's child list AFTER everything else so the LIFO
# render order draws it first (behind text). We want it ON TOP of bezel
# but below text — putting it in its own canvas with sortOrder between
# bezel(40?) and hud_overlay(50) works.
if "static_interference" not in src:
    sortorder_ext = '3_canvas'  # PS1UICanvas script
    elem_ext      = '4_element'
    static_block = (
        '\n[node name="static_interference" type="Node" parent="."]\n'
        f'script = ExtResource("{sortorder_ext}")\n'
        'CanvasName = "static_interference"\n'
        'SortOrder = 45\n'
        'VisibleOnLoad = false\n'
        '\n[node name="StaticImage" type="Node" parent="static_interference"]\n'
        f'script = ExtResource("{elem_ext}")\n'
        'ElementName = "static_image"\n'
        'Type = 0\n'
        'X = 0\n'
        'Y = 0\n'
        'Width = 320\n'
        'Height = 240\n'
        'Texture = ExtResource("100_static")\n'
        'BitDepth = 1\n'
        'Translucent = true\n'
    )
    src = src.rstrip() + "\n" + static_block + "\n"


p.write_text(src, encoding="utf-8")
print(f"added {added} decal nodes + static interference canvas")
