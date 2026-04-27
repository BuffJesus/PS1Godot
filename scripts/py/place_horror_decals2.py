"""Wave 2 placements: ghost spawns, footprints, shadow blobs, fog canvas,
stain decals, glitch-bars canvas. Idempotent (strips horror2_* prior runs).
"""
import re
import math
from pathlib import Path

p = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/scenes/monitor/monitor.tscn")
src = p.read_text(encoding="utf-8")

# ── ext_resource additions ──
TEXTURES = {
    "101_foot":   "res://assets/monitor/textures/footprints.png",
    "102_shadow": "res://assets/monitor/textures/shadow_blob.png",
    "103_fog":    "res://assets/monitor/textures/fog_overlay.png",
    "104_glitch": "res://assets/monitor/textures/glitch_bars.png",
    "105_stain":  "res://assets/monitor/textures/stain_grime.png",
}
ext_anchor = '[ext_resource type="Texture2D" path="res://assets/monitor/textures/static_overlay.png" id="100_static"]\n'
assert ext_anchor in src
missing = [(k, v) for k, v in TEXTURES.items() if f'id="{k}"' not in src]
if missing:
    block = "".join(f'[ext_resource type="Texture2D" path="{v}" id="{k}"]\n'
                    for k, v in missing)
    src = src.replace(ext_anchor, ext_anchor + block, 1)


# ── Inline ShaderMaterial subresources ──
SHADER_EXT = '6_ps1_shader'
MAT_DEFS = [
    ("mat_foot",   "101_foot"),
    ("mat_shadow", "102_shadow"),
    ("mat_fog",    "103_fog"),
    ("mat_glitch", "104_glitch"),
    ("mat_stain",  "105_stain"),
]
mat_block = ""
for mid, tex in MAT_DEFS:
    if f'sub_resource type="ShaderMaterial" id="{mid}"' in src: continue
    mat_block += (
        f'\n[sub_resource type="ShaderMaterial" id="{mid}"]\n'
        f'render_priority = 0\n'
        f'shader = ExtResource("{SHADER_EXT}")\n'
        f'shader_parameter/albedo_tex = ExtResource("{tex}")\n'
        f'shader_parameter/tint_color = Color(1, 1, 1, 1)\n'
        f'shader_parameter/snap_enabled = true\n'
        f'shader_parameter/snap_resolution = Vector2(320, 240)\n'
        f'shader_parameter/modulate_scale = 2.0\n'
        f'shader_parameter/fog_enabled = false\n'
        f'shader_parameter/fog_color = Color(0, 0, 0, 1)\n'
        f'shader_parameter/fog_near = 10.0\n'
        f'shader_parameter/fog_far = 50.0\n'
    )
mat_anchor = '[sub_resource type="BoxMesh" id="mesh_blood_decal"]\nsize = Vector3(1.5, 1.5, 0.05)\n'
src = src.replace(mat_anchor, mat_anchor + mat_block, 1)


# ── BoxMesh sub-resources ──
EXTRA_MESHES = [
    ("mesh_foot",       "Vector3(0.6, 0.04, 0.3)"),  # flat horizontal
    ("mesh_shadow_sm",  "Vector3(0.7, 0.04, 0.4)"),
    ("mesh_shadow_md",  "Vector3(1.2, 0.04, 0.7)"),
    ("mesh_stain_sm",   "Vector3(0.6, 0.04, 0.6)"),  # floor stain
]
mesh_block = ""
for mid, sz in EXTRA_MESHES:
    if f'sub_resource type="BoxMesh" id="{mid}"' in src: continue
    mesh_block += f'\n[sub_resource type="BoxMesh" id="{mid}"]\nsize = {sz}\n'
src = src.replace(mat_anchor, mat_anchor + mesh_block, 1)


# ── Strip prior horror2_* nodes ──
horror2_pat = re.compile(
    r'\n\[node name="horror2_[^"]+" type="MeshInstance3D"[^\]]*\]\n(?:[^\[]*\n)+?(?=\[)',
    re.MULTILINE)
src, n_stripped = horror2_pat.subn("\n", src)
print(f"stripped {n_stripped} prior horror2_ nodes")


def make_node(name, parent, mesh_id, mat_id, tx, ty, tz, ry,
              translucent=True):
    cy = math.cos(math.radians(ry))
    sy = math.sin(math.radians(ry))
    out = (
        f'\n[node name="{name}" type="MeshInstance3D" parent="{parent}"]\n'
        f"transform = Transform3D({cy:.4f}, 0, {sy:.4f}, "
        f"0, 1, 0, {-sy:.4f}, 0, {cy:.4f}, "
        f"{tx}, {ty}, {tz})\n"
        f'material_override = SubResource("{mat_id}")\n'
        f'mesh = SubResource("{mesh_id}")\n'
        f'script = ExtResource("2_mesh")\n'
    )
    if translucent: out += "Translucent = true\n"
    out += "FlatColor = Color(1, 1, 1, 1)\nCollision = 0\n"
    return out


# ── Decal placements ──
DECALS = [
    # ─── Ghost figures: 1 per room, StartsInactive=true, Tag=2 ──
    # (Lua activates one per shift via Persist seed.)
    # Floor at y=0 + ghost height 1.6 → centered at y=0.8 to stand on floor.
    # We use mesh_ghost (0.7×1.6 vertical card).
    # ─── Footprints trail in hallway floor ──
    ("horror2_foot_h1", "Feed01_Hallway", "mesh_foot", "mat_foot", -80,   -0.20, -3,    0),
    ("horror2_foot_h2", "Feed01_Hallway", "mesh_foot", "mat_foot", -79.5, -0.20, -5,   30),
    ("horror2_foot_h3", "Feed01_Hallway", "mesh_foot", "mat_foot", -80.2, -0.20, -7,  -10),
    ("horror2_foot_h4", "Feed01_Hallway", "mesh_foot", "mat_foot", -79.8, -0.20, -9,   15),
    ("horror2_foot_h5", "Feed01_Hallway", "mesh_foot", "mat_foot", -80.5, -0.20,-11,    0),
    # ─── Shadow blobs under cube humanoid figures ──
    # hallway_figure at (-80, 1, -12) → shadow at floor (-80, -0.20, -12)
    ("horror2_shadow_hf",  "Feed01_Hallway", "mesh_shadow_sm", "mat_shadow", -80,   -0.20, -12, 0),
    # hallway_still at (-83, 1, -6) → shadow under it
    ("horror2_shadow_hs",  "Feed01_Hallway", "mesh_shadow_sm", "mat_shadow", -83,   -0.20, -6,  0),
    # parking_figure at (-78, 1, 49) → shadow
    ("horror2_shadow_pf",  "Feed03_Parking", "mesh_shadow_sm", "mat_shadow", -78,   -0.20, 49,  0),
    # ─── Stain/grime decals on floors ──
    ("horror2_stain_h1",   "Feed01_Hallway", "mesh_stain_sm", "mat_stain", -78,   -0.21, -7,   0),
    ("horror2_stain_h2",   "Feed01_Hallway", "mesh_stain_sm", "mat_stain", -82,   -0.21, -14,  20),
    ("horror2_stain_s1",   "Feed02_Storage", "mesh_stain_sm", "mat_stain", -78.5, -0.21, 27,  -15),
    ("horror2_stain_p1",   "Feed03_Parking", "mesh_stain_sm", "mat_stain", -82,   -0.21, 48,   0),
    ("horror2_stain_b1",   "Feed04_BackRoom","mesh_stain_sm", "mat_stain", -79,   -0.21, 67,  10),
]

added = 0
for d in DECALS:
    src = src.rstrip() + "\n" + make_node(*d) + "\n"
    added += 1


# ── Fog overlay UI canvas (parking-only, Lua manages visibility) ──
if "fog_overlay_canvas" not in src:
    fog_block = (
        '\n[node name="fog_overlay_canvas" type="Node" parent="."]\n'
        f'script = ExtResource("3_canvas")\n'
        'CanvasName = "fog_overlay"\n'
        'SortOrder = 47\n'
        'VisibleOnLoad = false\n'
        '\n[node name="FogImage" type="Node" parent="fog_overlay_canvas"]\n'
        f'script = ExtResource("4_element")\n'
        'ElementName = "fog_image"\n'
        'Type = 0\n'
        'X = 0\n'
        'Y = 0\n'
        'Width = 320\n'
        'Height = 240\n'
        'Texture = ExtResource("103_fog")\n'
        'BitDepth = 1\n'
        'Translucent = true\n'
    )
    src = src.rstrip() + "\n" + fog_block + "\n"


# ── Glitch bars UI canvas (decoy event flash) ──
if "glitch_canvas" not in src:
    glitch_block = (
        '\n[node name="glitch_canvas" type="Node" parent="."]\n'
        f'script = ExtResource("3_canvas")\n'
        'CanvasName = "glitch"\n'
        'SortOrder = 46\n'
        'VisibleOnLoad = false\n'
        '\n[node name="GlitchImage" type="Node" parent="glitch_canvas"]\n'
        f'script = ExtResource("4_element")\n'
        'ElementName = "glitch_image"\n'
        'Type = 0\n'
        'X = 0\n'
        'Y = 100\n'
        'Width = 320\n'
        'Height = 40\n'
        'Texture = ExtResource("104_glitch")\n'
        'BitDepth = 1\n'
        'Translucent = true\n'
    )
    src = src.rstrip() + "\n" + glitch_block + "\n"


# ── Ghost spawn entities (one per room, Tag=2, StartsInactive=true) ──
# Lua picks one per shift via Persist seed. Activates briefly.
# Ghost mesh is 0.7 wide × 1.6 tall vertical card; place facing +Z front.
ghost_pat = re.compile(
    r'\n\[node name="ghost_spawn_\d+" type="MeshInstance3D"[^\]]*\]\n(?:[^\[]*\n)+?(?=\[)',
    re.MULTILINE)
src = ghost_pat.sub("\n", src)

GHOSTS = [
    # name, parent, position, rotation
    ("ghost_spawn_1", "Feed01_Hallway",  -82.5, 0.8, -8,  90),  # left wall
    ("ghost_spawn_2", "Feed02_Storage",  -83,   0.8, 25,  90),
    ("ghost_spawn_3", "Feed03_Parking",  -84,   0.8, 47,  90),
    ("ghost_spawn_4", "Feed04_BackRoom", -78,   0.8, 65, 270),
]

ghost_added = 0
for name, parent, tx, ty, tz, ry in GHOSTS:
    cy = math.cos(math.radians(ry))
    sy = math.sin(math.radians(ry))
    block = (
        f'\n[node name="{name}" type="MeshInstance3D" parent="{parent}"]\n'
        f"transform = Transform3D({cy:.4f}, 0, {sy:.4f}, "
        f"0, 1, 0, {-sy:.4f}, 0, {cy:.4f}, {tx}, {ty}, {tz})\n"
        f'material_override = SubResource("mat_ghost")\n'
        f'mesh = SubResource("mesh_ghost")\n'
        f'script = ExtResource("2_mesh")\n'
        f"Translucent = true\n"
        f"StartsInactive = true\n"
        f"Tag = 2\n"
        f"FlatColor = Color(1, 1, 1, 1)\n"
        f"Collision = 0\n"
    )
    src = src.rstrip() + "\n" + block + "\n"
    ghost_added += 1


p.write_text(src, encoding="utf-8")
print(f"added {added} decals + {ghost_added} ghost spawns + fog/glitch canvases")
