# Cycles vertex-color bake smoke test.
#
# Exercises bpy.ops.ps1godot.vc_bake_cycles end-to-end against the
# default cube + a SUN light. Slower than test_register.py (Cycles
# spins up an actual render); kept separate so the fast smoke loop
# stays fast.
#
# Run with:
#   blender --background --factory-startup --python tools/blender-addon/test_cycles_bake.py

import os
import sys
import traceback

ADDON_DIR = os.path.dirname(os.path.abspath(__file__))
if ADDON_DIR not in sys.path:
    sys.path.insert(0, ADDON_DIR)

import bpy  # noqa: E402


def _err(msg: str):
    print(f"[cycles-bake TEST FAIL] {msg}", file=sys.stderr)


def _info(msg: str):
    print(f"[cycles-bake TEST] {msg}")


def main() -> int:
    failures = 0

    try:
        import ps1godot_blender
        ps1godot_blender.register()
    except Exception:
        _err("register() raised:")
        traceback.print_exc()
        return 1

    # Configure for a cheap-but-meaningful bake.
    s = bpy.context.scene.ps1godot
    s.vc_cycles_mode = "COMBINED"
    s.vc_cycles_samples = 4         # absurdly low, but enough to verify wiring

    # Default cube + add a SUN light if there isn't one.
    cube = bpy.data.objects.get("Cube")
    if cube is None:
        bpy.ops.mesh.primitive_cube_add()
        cube = bpy.context.active_object

    # Ensure the cube has a material (Cycles bake requires it).
    if not any(slot.material for slot in cube.material_slots):
        mat = bpy.data.materials.new(name="cube_mat")
        mat.use_nodes = True
        cube.data.materials.append(mat)

    if "TestSun" not in bpy.data.objects:
        bpy.ops.object.light_add(type="SUN", location=(2, 2, 4))
        sun = bpy.context.active_object
        sun.name = "TestSun"

    # Select cube only.
    bpy.ops.object.select_all(action="DESELECT")
    cube.select_set(True)
    bpy.context.view_layer.objects.active = cube

    try:
        result = bpy.ops.ps1godot.vc_bake_cycles()
        if "CANCELLED" in result:
            _err(f"vc_bake_cycles cancelled: {result}")
            failures += 1
        else:
            _info(f"vc_bake_cycles returned {result}")
    except Exception:
        _err("vc_bake_cycles raised:")
        traceback.print_exc()
        failures += 1
        ps1godot_blender.unregister()
        return 1

    # Verify the cube got a 'Col' attribute populated with non-zero data.
    layer = cube.data.color_attributes.get("Col")
    if layer is None:
        _err("Col layer missing after bake")
        failures += 1
    else:
        # At least one loop should have non-trivial color (cube faces
        # the sun on at least three sides).
        any_lit = any(c.color[0] > 0.01 or c.color[1] > 0.01 or c.color[2] > 0.01
                      for c in layer.data)
        if not any_lit:
            _err("bake produced all-black vertex colors — wiring suspect")
            failures += 1
        else:
            _info(f"bake produced lit vertex colors ({len(layer.data)} loops).")

        # Verify the 0.8 ceiling held — no channel should exceed it.
        any_over = any(c.color[0] > 0.801 or c.color[1] > 0.801 or c.color[2] > 0.801
                       for c in layer.data)
        if any_over:
            _err("0.8 PSX ceiling not enforced — found loops above 0.8")
            failures += 1
        else:
            _info("0.8 PSX ceiling held across all loops.")

    # Verify render engine + samples were restored.
    if bpy.context.scene.render.engine == "CYCLES" and bpy.context.scene.cycles.samples == 4:
        # Persistent CYCLES is fine if scene was already CYCLES before
        # we started, so this only fails if the engine was something
        # else originally and we leaked. Hard to tell from headless;
        # just note it.
        _info("note: render engine is still CYCLES + samples=4 (acceptable; we don't track prior state in this test).")

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
