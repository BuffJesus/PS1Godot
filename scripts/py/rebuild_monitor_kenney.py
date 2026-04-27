"""Major monitor.tscn rewrite: swap 6 event-entity placeholder boxes for
Kenney GLB models, add dressing props per feed. Preserves entity names +
StartsInactive + Tag so fireEvent / animations still resolve."""
import re
import math
from pathlib import Path

p = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/scenes/monitor/monitor.tscn")
src = p.read_text(encoding="utf-8")

# ── ext_resource entries for every Kenney GLB used. ID range 60-99 ──
# All retro-urban-kit pieces share one texture atlas — adding more wall
# variants from the same kit reuses the existing VRAM allocation.
KENNEY = {
    "60_door":         "res://assets/monitor/models/kenney_retro-urban-kit/Models/GLB format/door-type-a.glb",
    "61_box_closed":   "res://assets/monitor/models/kenney_furniture-kit/Models/GLTF format/cardboardBoxClosed.glb",
    "62_pallet":       "res://assets/monitor/models/kenney_retro-urban-kit/Models/GLB format/pallet.glb",
    "63_chair_desk":   "res://assets/monitor/models/kenney_furniture-kit/Models/GLTF format/chairDesk.glb",
    "64_tv_vintage":   "res://assets/monitor/models/kenney_furniture-kit/Models/GLTF format/televisionVintage.glb",
    "70_bench":        "res://assets/monitor/models/kenney_furniture-kit/Models/GLTF format/bench.glb",
    "71_trashcan":     "res://assets/monitor/models/kenney_furniture-kit/Models/GLTF format/trashcan.glb",
    "72_lamp_wall":    "res://assets/monitor/models/kenney_furniture-kit/Models/GLTF format/lampWall.glb",
    "73_coat_rack":    "res://assets/monitor/models/kenney_furniture-kit/Models/GLTF format/coatRackStanding.glb",
    "74_bookcase":     "res://assets/monitor/models/kenney_furniture-kit/Models/GLTF format/bookcaseClosed.glb",
    "75_barrel":       "res://assets/monitor/models/kenney_survival-kit/Models/GLB format/barrel.glb",
    "76_dumpster":     "res://assets/monitor/models/kenney_retro-urban-kit/Models/GLB format/detail-dumpster-closed.glb",
    "77_barrier":      "res://assets/monitor/models/kenney_retro-urban-kit/Models/GLB format/detail-barrier-type-a.glb",
    "78_light_single": "res://assets/monitor/models/kenney_retro-urban-kit/Models/GLB format/detail-light-single.glb",
    "79_desk":         "res://assets/monitor/models/kenney_furniture-kit/Models/GLTF format/desk.glb",
    "80_computer":     "res://assets/monitor/models/kenney_furniture-kit/Models/GLTF format/computerScreen.glb",
    "81_potted_plant": "res://assets/monitor/models/kenney_furniture-kit/Models/GLTF format/pottedPlant.glb",
    "82_coffee":       "res://assets/monitor/models/kenney_furniture-kit/Models/GLTF format/kitchenCoffeeMachine.glb",
}
# Add only the ext_resource entries that are missing — re-runs of this
# script may add new KENNEY ids over time and the original "all-or-none"
# guard left newly-introduced ids undeclared, breaking parse on instance
# references.
ext_anchor = '[ext_resource type="Script" path="res://addons/ps1godot/nodes/PS1AnimationKeyframe.cs" id="52_kf"]\n'
assert ext_anchor in src, "anim ext_resource anchor missing"
missing = [(kid, path) for kid, path in KENNEY.items()
           if f'id="{kid}"' not in src]
if missing:
    ext_block = "".join(
        f'[ext_resource type="PackedScene" path="{path}" id="{kid}"]\n'
        for kid, path in missing
    )
    src = src.replace(ext_anchor, ext_anchor + ext_block, 1)


def make_node(name, parent, glb_id, tx, ty, tz, ry=0, scale=1.0,
              inactive=False, tag=0, sx=None, sy=None, sz=None,
              collision=0, flat=(1, 1, 1)):
    """Format a PS1MeshGroup wrapping a GLB instance child.

    `scale` sets uniform scale; pass sx/sy/sz to override individual
    axes (lets walls stretch wide+tall+thin without inflating depth).
    `collision` 0=none, 1=static — set 1 for room shells so the runtime
    keeps player-vs-wall collision after we strip the original primitives.
    `flat` is RGB tuple for FlatColor (per-surface tint multiplies it).
    """
    cy = math.cos(math.radians(ry))
    syaw = math.sin(math.radians(ry))
    sx = scale if sx is None else sx
    sy = scale if sy is None else sy
    sz = scale if sz is None else sz
    # Y-axis rotation matrix scaled per axis.
    xform = (
        f"Transform3D({sx*cy:.4f}, 0, {sx*syaw:.4f}, "
        f"0, {sy:.4f}, 0, "
        f"{-sz*syaw:.4f}, 0, {sz*cy:.4f}, "
        f"{tx}, {ty}, {tz})"
    )
    fr, fg, fb = flat
    out = (
        f'\n[node name="{name}" type="Node3D" parent="{parent}"]\n'
        f"transform = {xform}\n"
        f'script = ExtResource("50_meshgroup")\n'
        f"BitDepth = 1\n"
        f"FlatColor = Color({fr}, {fg}, {fb}, 1)\n"
        f"Collision = {collision}\n"
    )
    if inactive:
        out += f"StartsInactive = true\nTag = {tag}\n"
    out += (
        f'\n[node name="model" parent="{parent}/{name}" '
        f'instance=ExtResource("{glb_id}")]\n'
    )
    return out


# ── Replace 6 event entity nodes with Kenney-backed PS1MeshGroups ──
# Scales: rooms are 7m tall × 12m × 20m. Kenney source meshes are
# typically 0.3-1.0m at scale 1, so we scale up 2-3× to read at room
# size. Tall items (doors, lamps) get more, furniture less.
EVENT_REPLACEMENTS = [
    # name, parent, glb_id, tx, ty, tz, ry, scale
    ("hallway_door",     "Feed01_Hallway",  "60_door",       -76.2, 0,    -10.0, 90, 3.0),
    ("storage_box",      "Feed02_Storage",  "61_box_closed", -78.0, 0.0,   26.0,  0, 2.5),
    ("storage_pallet",   "Feed02_Storage",  "62_pallet",     -82.0, 0.0,   24.0,  0, 2.5),
    ("backroom_chair",   "Feed04_BackRoom", "63_chair_desk", -78.0, 0.0,   67.0,  0, 2.5),
    ("backroom_monitor", "Feed04_BackRoom", "64_tv_vintage", -81.5, 1.2,   66.0,  0, 2.0),
    ("backroom_slide",   "Feed04_BackRoom", "61_box_closed", -78.0, 0.0,   64.0,  0, 2.0),
]
def strip_existing(src, name):
    """Remove a previously-emitted PS1MeshGroup node + its child instance.
    Idempotent: matches both the original MeshInstance3D placeholder AND
    the Node3D form this script writes back. Lets the script re-run after
    its own edits without leaving duplicates."""
    # Matches the parent node block (any type) up to the next [section].
    parent_pat = re.compile(
        r'\n\[node name="' + re.escape(name) + r'" type="(?:MeshInstance3D|Node3D)"[^\]]*\]\n'
        r'(?:[^\[]*\n)+?(?=\[)',
        re.MULTILINE,
    )
    src = parent_pat.sub("\n", src, count=1)
    # Matches the "model" child node we wrote underneath, if present.
    child_pat = re.compile(
        r'\n\[node name="model" parent="[^/]+/' + re.escape(name) + r'"[^\]]*\]\n'
        r'(?:[^\[]*\n)*?(?=\[|\Z)',
        re.MULTILINE,
    )
    src = child_pat.sub("\n", src, count=1)
    return src


replaced = 0
for name, parent, glb_id, tx, ty, tz, ry, scale in EVENT_REPLACEMENTS:
    src = strip_existing(src, name)
    block = make_node(name, parent, glb_id, tx, ty, tz, ry, scale,
                       inactive=True, tag=1)
    # Append before the trailing fixed sections; insert after the parent
    # group's last existing node by pattern-matching the parent header.
    parent_anchor = f'[node name="{parent}" type="Node3D"'
    if parent_anchor in src:
        src = src.rstrip() + "\n" + block + "\n"
        replaced += 1


# ── Add dressing props per feed (always-visible, no Tag/StartsInactive) ──
DRESSING = [
    # HALLWAY
    ("h_bench",        "Feed01_Hallway",  "70_bench",       -83.0, 0,   -10.0, 90, 2.5),
    ("h_trashcan",     "Feed01_Hallway",  "71_trashcan",    -83.0, 0,    -2.0,  0, 2.0),
    ("h_lamp_wall_a",  "Feed01_Hallway",  "72_lamp_wall",   -83.7, 4.0,  -6.0, 90, 2.0),
    ("h_lamp_wall_b",  "Feed01_Hallway",  "72_lamp_wall",   -83.7, 4.0, -12.0, 90, 2.0),
    ("h_coat_rack",    "Feed01_Hallway",  "73_coat_rack",   -77.0, 0,    -3.0,  0, 2.5),
    # STORAGE
    ("s_bookcase_a",   "Feed02_Storage",  "74_bookcase",    -83.5, 0,    22.0, 90, 3.0),
    ("s_bookcase_b",   "Feed02_Storage",  "74_bookcase",    -83.5, 0,    28.0, 90, 3.0),
    ("s_barrel_a",     "Feed02_Storage",  "75_barrel",      -76.5, 0,    22.0,  0, 2.0),
    ("s_barrel_b",     "Feed02_Storage",  "75_barrel",      -76.5, 0,    24.0,  0, 2.0),
    ("s_box_dressing", "Feed02_Storage",  "61_box_closed",  -77.0, 0,    30.0,  0, 2.0),
    # PARKING
    ("p_dumpster",     "Feed03_Parking",  "76_dumpster",    -84.0, 0,    50.0, 90, 2.5),
    ("p_barrier_a",    "Feed03_Parking",  "77_barrier",     -78.0, 0,    51.0,  0, 2.0),
    ("p_barrier_b",    "Feed03_Parking",  "77_barrier",     -82.0, 0,    51.0,  0, 2.0),
    ("p_light_pole_a", "Feed03_Parking",  "78_light_single",-84.0, 0,    44.0,  0, 3.0),
    ("p_light_pole_b", "Feed03_Parking",  "78_light_single",-76.0, 0,    50.0,  0, 3.0),
    # BACK ROOM
    ("b_desk",         "Feed04_BackRoom", "79_desk",        -81.5, 0,    66.0,  0, 2.5),
    ("b_computer",     "Feed04_BackRoom", "80_computer",    -81.5, 1.5,  66.5,180, 1.8),
    ("b_potted_plant", "Feed04_BackRoom", "81_potted_plant",-77.0, 0,    62.0,  0, 2.5),
    ("b_coffee",       "Feed04_BackRoom", "82_coffee",      -83.0, 1.5,  68.0,  0, 1.8),
    ("b_bookcase",     "Feed04_BackRoom", "74_bookcase",    -83.5, 0,    64.0, 90, 3.0),
]
# Strip any previously-added dressing nodes so re-runs don't duplicate.
for name, *_ in DRESSING:
    src = strip_existing(src, name)
dressing = "".join(
    make_node(n, parent, gid, tx, ty, tz, ry, sc)
    for n, parent, gid, tx, ty, tz, ry, sc in DRESSING
)
src = src.rstrip() + "\n" + dressing + "\n"



p.write_text(src, encoding="utf-8")
print(f"replaced {replaced}/{len(EVENT_REPLACEMENTS)} event entities + "
      f"added {len(DRESSING)} dressing props (room shell uses original "
      f"PlaneMesh walls/floors; Kenney shell swap reverted)")
