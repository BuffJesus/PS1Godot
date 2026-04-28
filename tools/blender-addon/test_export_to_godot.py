# End-to-end test for PS1GODOT_OT_export_to_godot.
#
# Exercises the one-click workflow: Blender registers the addon,
# points at a temp project root, runs export_to_godot against the
# default cube, then confirms BOTH the .glb and the .ps1meshmeta.json
# landed at the expected paths with the right shape.
#
# Run with:
#   blender --background --factory-startup --python tools/blender-addon/test_export_to_godot.py

import os
import sys
import json
import tempfile
import traceback

ADDON_DIR = os.path.dirname(os.path.abspath(__file__))
if ADDON_DIR not in sys.path:
    sys.path.insert(0, ADDON_DIR)

import bpy  # noqa: E402


def _err(msg: str):
    print(f"[export-to-godot TEST FAIL] {msg}", file=sys.stderr)


def _info(msg: str):
    print(f"[export-to-godot TEST] {msg}")


def main() -> int:
    failures = 0

    try:
        import ps1godot_blender
        ps1godot_blender.register()
    except Exception:
        _err("register() raised:")
        traceback.print_exc()
        return 1

    with tempfile.TemporaryDirectory(prefix="ps1godot_e2e_") as tmp:
        scene = bpy.context.scene
        scene.ps1godot.project_root  = tmp
        scene.ps1godot.asset_subdir  = "meshes"
        scene.ps1godot.output_subdir = "metadata"
        scene.ps1godot.default_chunk_id = "test_chunk"

        cube = bpy.data.objects.get("Cube")
        if cube is None:
            bpy.ops.mesh.primitive_cube_add()
            cube = bpy.context.active_object
        # Pre-tag with a known mesh_id so the file path is predictable.
        cube.ps1godot.mesh_id     = "e2e_cube"
        cube.ps1godot.mesh_role   = "DynamicRigid"
        cube.ps1godot.alpha_mode  = "Cutout"

        try:
            result = bpy.ops.ps1godot.export_to_godot()
        except Exception:
            _err("export_to_godot raised:")
            traceback.print_exc()
            ps1godot_blender.unregister()
            return 1

        if "CANCELLED" in result:
            _err(f"export_to_godot cancelled: {result}")
            failures += 1
        else:
            _info(f"export_to_godot returned {result}")

            glb_path     = os.path.join(tmp, "meshes", "e2e_cube.glb")
            sidecar_path = os.path.join(tmp, "metadata", "e2e_cube.ps1meshmeta.json")

            if not os.path.exists(glb_path):
                _err(f"GLB missing at {glb_path}")
                failures += 1
            else:
                size = os.path.getsize(glb_path)
                _info(f"GLB written: {glb_path} ({size} B)")
                if size < 200:
                    _err(f"GLB suspiciously small ({size} B) — exporter probably wrote a stub")
                    failures += 1

            if not os.path.exists(sidecar_path):
                _err(f"sidecar missing at {sidecar_path}")
                failures += 1
            else:
                payload = json.load(open(sidecar_path, encoding="utf-8"))
                checks = (
                    ("mesh_id",    payload.get("mesh_id"),    "e2e_cube"),
                    ("mesh_role",  payload.get("mesh_role"),  "DynamicRigid"),
                    ("alpha_mode", payload.get("alpha_mode"), "Cutout"),
                    ("chunk_id",   payload.get("chunk_id"),   "test_chunk"),
                )
                for k, got, want in checks:
                    if got != want:
                        _err(f"sidecar.{k} = {got!r}, want {want!r}")
                        failures += 1
                    else:
                        _info(f"sidecar.{k} = {got!r}")

    try:
        ps1godot_blender.unregister()
    except Exception:
        pass

    if failures:
        _err(f"{failures} check(s) failed")
        return 1
    _info("ALL CHECKS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
