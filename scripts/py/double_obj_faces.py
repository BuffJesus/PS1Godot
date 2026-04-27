"""Duplicate every face in an OBJ with reversed winding so the mesh
is visible from both sides. Workaround for PSX hardware backface
culling (renderer.cpp line 1357 — nclip + mac0 <= 0 skip), which
hides single-sided protrusions like the goblin's nose at certain
camera angles.

Usage: python double_obj_faces.py <input.obj> <output.obj>
       (or no args: defaults to in-place on the goblin OBJ)
"""
import sys
from pathlib import Path

DEFAULT_IN = Path(r"D:/Documents/JetBrains/PS1Godot/godot-ps1/assets/monitor/models/goblin/GoblinUnwrapped.obj")
DEFAULT_OUT = DEFAULT_IN  # in-place

src = sys.argv[1] if len(sys.argv) > 1 else str(DEFAULT_IN)
dst = sys.argv[2] if len(sys.argv) > 2 else str(DEFAULT_OUT)
src_p = Path(src)
dst_p = Path(dst)

text = src_p.read_text(encoding="utf-8")
out_lines = []
n_orig = 0
n_dup = 0

for line in text.splitlines():
    stripped = line.strip()
    out_lines.append(line)
    if stripped.startswith("f "):
        # OBJ face: "f v[/vt[/vn]] v[/vt[/vn]] ..." with any vertex count.
        # Reversed winding for any-N polygon = reverse the entire vertex list.
        parts = stripped.split()
        verts = parts[1:]
        if len(verts) >= 3:
            out_lines.append("f " + " ".join(reversed(verts)))
            n_orig += 1
            n_dup += 1

dst_p.write_text("\n".join(out_lines) + "\n", encoding="utf-8")
print(f"wrote {dst_p}")
print(f"  original triangles: {n_orig}")
print(f"  duplicated (reversed winding): {n_dup}")
print(f"  total triangles in output: {n_orig + n_dup}")
