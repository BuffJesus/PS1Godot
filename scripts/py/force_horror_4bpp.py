"""Force all horror_*/horror2_*/ghost_spawn_*/hallway_blood_decal nodes
to BitDepth=0 (4bpp / 16-color CLUT). 8bpp was busting VRAM with too
many 256-entry CLUTs. 4bpp halves CLUT space and the decals are
mostly 2-3 dominant colors anyway (red/green/black) — quantize fine.
"""
import re
from pathlib import Path

p = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/scenes/monitor/monitor.tscn")
src = p.read_text(encoding="utf-8")

# For each MeshInstance3D node whose name starts with horror_/horror2_/
# ghost_spawn_/hallway_blood_decal, ensure BitDepth = 0 line is present
# (insert if missing, replace if 1).
PREFIXES = ("horror_", "horror2_", "ghost_spawn_", "hallway_blood_decal")
node_pat = re.compile(
    r'(\[node name="([^"]+)" type="MeshInstance3D" parent="[^"]+"[^\]]*\]\n'
    r'(?:[^\[]+\n)+?)(?=\[)',
    re.MULTILINE,
)

def fix(m):
    head, name = m.group(1), m.group(2)
    if not name.startswith(PREFIXES):
        return m.group(0)
    # Drop existing BitDepth = N line if any.
    head = re.sub(r'^BitDepth = \d+\n', '', head, flags=re.MULTILINE)
    # Insert BitDepth = 0 right before the trailing newline.
    head = head.rstrip("\n") + "\nBitDepth = 0\n\n"
    return head

new_src, n = node_pat.subn(fix, src)
print(f"updated nodes (potentially): {n}")
p.write_text(new_src, encoding="utf-8")
