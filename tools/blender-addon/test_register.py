# Smoke-test for the PS1Godot Blender add-on.
#
# Runs inside Blender's Python (use `blender --background --python <this>`
# from the repo root). Loads the ps1godot_blender package directly (no
# user-scripts install required), registers it, sanity-checks that the
# expected classes / properties / operator landed, exercises the
# validator on the default startup scene, then reports PASS / FAIL.
#
# This is intentionally NOT a unit-test framework — Blender's bpy is
# stateful and registration is global, so we just want a single
# end-to-end "does it survive a real Blender" check.

import sys
import os
import traceback

ADDON_DIR = os.path.dirname(os.path.abspath(__file__))
if ADDON_DIR not in sys.path:
    sys.path.insert(0, ADDON_DIR)

import bpy  # noqa: E402  (must come after sys.path edit if running standalone)


def _err(msg: str):
    print(f"[ps1godot_blender TEST FAIL] {msg}", file=sys.stderr)


def _info(msg: str):
    print(f"[ps1godot_blender TEST] {msg}")


def main() -> int:
    failures = 0

    _info(f"Blender {bpy.app.version_string}")

    # ── Register addon ──────────────────────────────────────────
    try:
        import ps1godot_blender
        ps1godot_blender.register()
    except Exception:
        _err("register() raised:")
        traceback.print_exc()
        return 1
    _info("register() OK")

    # ── PropertyGroups + PointerProperty installs ───────────────
    expected_pointer_props = (
        ("Scene",    "ps1godot"),
        ("Object",   "ps1godot"),
        ("Material", "ps1godot"),
    )
    for host_name, attr in expected_pointer_props:
        host = getattr(bpy.types, host_name)
        if not hasattr(host, attr):
            _err(f"bpy.types.{host_name}.{attr} not installed")
            failures += 1
        else:
            _info(f"bpy.types.{host_name}.{attr} present")

    # ── Operator + Panels registered with the right bl_idnames ──
    expected_classes = (
        ("PS1GODOT_OT_validate_scene", "Operator"),
        ("PS1GODOT_PT_project",        "Panel"),
        ("PS1GODOT_PT_object_metadata", "Panel"),
        ("PS1GODOT_PT_material_metadata", "Panel"),
    )
    for cls_name, kind in expected_classes:
        if not hasattr(bpy.types, cls_name):
            _err(f"bpy.types.{cls_name} ({kind}) not registered")
            failures += 1
        else:
            _info(f"bpy.types.{cls_name} ({kind}) registered")

    # ── Read a scene-level property (round-trips PropertyGroup) ─
    try:
        scene = bpy.context.scene
        scene.ps1godot.default_chunk_id = "smoketest_chunk"
        if scene.ps1godot.default_chunk_id != "smoketest_chunk":
            _err("scene.ps1godot.default_chunk_id round-trip failed")
            failures += 1
        else:
            _info("scene.ps1godot read/write OK")
    except Exception:
        _err("PropertyGroup read/write raised:")
        traceback.print_exc()
        failures += 1

    # ── Read an object-level property on the default cube ───────
    try:
        cube = bpy.data.objects.get("Cube")
        if cube is None:
            # Headless startup loads the default cube; if it's gone,
            # spawn one rather than failing.
            bpy.ops.mesh.primitive_cube_add()
            cube = bpy.context.active_object
        cube.ps1godot.mesh_id = "smoketest_cube"
        cube.ps1godot.mesh_role = "StaticWorld"
        if cube.ps1godot.mesh_role != "StaticWorld":
            _err("object.ps1godot enum round-trip failed")
            failures += 1
        else:
            _info(f"object.ps1godot on '{cube.name}' read/write OK")
    except Exception:
        _err("Object PropertyGroup read/write raised:")
        traceback.print_exc()
        failures += 1

    # ── Run the validate operator end-to-end ────────────────────
    try:
        result = bpy.ops.ps1godot.validate_scene()
        # bpy returns a set like {'FINISHED'}; any non-cancelled
        # result is good for our smoke test.
        if "CANCELLED" in result:
            _err(f"validate_scene cancelled: {result}")
            failures += 1
        else:
            _info(f"validate_scene returned {result}")
    except Exception:
        _err("validate_scene raised:")
        traceback.print_exc()
        failures += 1

    # ── Exercise the export operator end-to-end ─────────────────
    # Point project_root at a tempdir, run the operator, and verify
    # the expected sidecar file appeared with the right shape.
    import tempfile
    import json
    with tempfile.TemporaryDirectory(prefix="ps1godot_smoketest_") as tmp:
        scene = bpy.context.scene
        scene.ps1godot.project_root = tmp
        scene.ps1godot.output_subdir = "out"

        try:
            result = bpy.ops.ps1godot.export_metadata()
            if "CANCELLED" in result:
                _err(f"export_metadata cancelled: {result}")
                failures += 1
            else:
                _info(f"export_metadata returned {result}")

                out_dir = os.path.join(tmp, "out")
                # The default cube was tagged earlier with mesh_id
                # 'smoketest_cube' so we know the sidecar filename.
                expected = os.path.join(out_dir, "smoketest_cube.ps1meshmeta.json")
                if not os.path.exists(expected):
                    _err(f"expected sidecar not found at {expected}")
                    # List what DID land for diagnosis.
                    for f in (os.listdir(out_dir) if os.path.isdir(out_dir) else []):
                        _info(f"  found: {f}")
                    failures += 1
                else:
                    payload = json.loads(open(expected, encoding="utf-8").read())
                    expected_keys = (
                        "ps1godot_metadata_version", "asset_id", "mesh_id",
                        "source_object_name", "mesh_role", "shading_mode",
                        "alpha_mode", "materials",
                    )
                    missing = [k for k in expected_keys if k not in payload]
                    if missing:
                        _err(f"sidecar missing keys: {missing}")
                        failures += 1
                    elif payload["mesh_role"] != "StaticWorld":
                        _err(f"mesh_role round-trip wrong: {payload['mesh_role']}")
                        failures += 1
                    elif not payload["asset_id"]:
                        _err("asset_id was not auto-generated")
                        failures += 1
                    else:
                        _info(f"sidecar at {expected} has expected shape "
                              f"(asset_id={payload['asset_id'][:8]}…, "
                              f"materials={len(payload['materials'])})")
                        # Dump the wire format so Godot-side reader
                        # tweaks can spot-check against a known sample.
                        _info("--- sidecar wire format ---")
                        for line in json.dumps(payload, indent=2).splitlines():
                            _info(line)
                        _info("--- end sidecar ---")
        except Exception:
            _err("export_metadata raised:")
            traceback.print_exc()
            failures += 1

    # ── Phase 8: import direction (Godot → Blender) ─────────────
    # Drop a hand-crafted sidecar that mirrors what the Godot writer
    # emits, run the import operator, and verify the values landed
    # on the Blender object's PropertyGroup. This proves the wire
    # contract works in both directions without needing Godot installed.
    with tempfile.TemporaryDirectory(prefix="ps1godot_import_smoketest_") as tmp:
        sidecar_dir = os.path.join(tmp, "out")
        os.makedirs(sidecar_dir, exist_ok=True)
        sidecar_path = os.path.join(sidecar_dir, "smoketest_cube.ps1meshmeta.json")
        # Use values that DIFFER from the previous export-pass state
        # so we can tell the import overwrote them.
        synthetic = {
            "ps1godot_metadata_version": 1,
            "asset_id": "deadbeefcafef00daaaaaaaaaaaaaaaa",
            "mesh_id": "smoketest_cube",
            "source_object_name": "Cube",
            "blend_file": "",
            "chunk_id": "imported_chunk",
            "region_id": "imported_region",
            "disc_id": 1,
            "area_archive_id": "AREA_TEST",
            "mesh_role": "DynamicRigid",        # was StaticWorld
            "export_mode": "KeepSeparate",      # was MergeStatic
            "draw_phase": "OpaqueDynamic",      # was OpaqueStatic
            "shading_mode": "VertexColor",      # was FlatColor
            "alpha_mode": "Cutout",             # was Opaque
            "collision_layer": "world_static",
            "materials": [
                {
                    "blender_name":    "Material",
                    "material_id":     "town_wood_dark",
                    "texture_page_id": "tpage_town_world_01",
                    "clut_id":         "town_day",
                    "palette_group":   "town_day",
                    "atlas_group":     "World",
                    "texture_format":  "8bpp",
                    "alpha_mode":      "Cutout",
                    "force_no_filter": True,
                    "approved_16bpp":  False,
                },
            ],
        }
        with open(sidecar_path, "w", encoding="utf-8") as f:
            json.dump(synthetic, f, indent=2)
            f.write("\n")

        scene = bpy.context.scene
        scene.ps1godot.project_root = tmp
        scene.ps1godot.output_subdir = "out"

        try:
            result = bpy.ops.ps1godot.import_metadata()
            if "CANCELLED" in result:
                _err(f"import_metadata cancelled: {result}")
                failures += 1
            else:
                _info(f"import_metadata returned {result}")

                # Verify the cube's PropertyGroup now matches the sidecar.
                cube = bpy.data.objects.get("Cube")
                if cube is None:
                    _err("Cube vanished before import verify")
                    failures += 1
                else:
                    p = cube.ps1godot
                    checks = (
                        ("asset_id",        p.asset_id,        synthetic["asset_id"]),
                        ("mesh_id",         p.mesh_id,         synthetic["mesh_id"]),
                        ("chunk_id",        p.chunk_id,        synthetic["chunk_id"]),
                        ("region_id",       p.region_id,       synthetic["region_id"]),
                        ("area_archive_id", p.area_archive_id, synthetic["area_archive_id"]),
                        ("mesh_role",       p.mesh_role,       synthetic["mesh_role"]),
                        ("export_mode",     p.export_mode,     synthetic["export_mode"]),
                        ("draw_phase",      p.draw_phase,      synthetic["draw_phase"]),
                        ("shading_mode",    p.shading_mode,    synthetic["shading_mode"]),
                        ("alpha_mode",      p.alpha_mode,      synthetic["alpha_mode"]),
                        ("collision_layer", p.collision_layer, synthetic["collision_layer"]),
                    )
                    for field, got, want in checks:
                        if got != want:
                            _err(f"import: {field} = {got!r}, expected {want!r}")
                            failures += 1
                        else:
                            _info(f"import: {field} = {got!r}")

                    # Per-material round-trip — Phase 5.
                    mat = bpy.data.materials.get("Material")
                    if mat is None:
                        _err("Material 'Material' missing for per-material check")
                        failures += 1
                    else:
                        mp = mat.ps1godot
                        m_checks = (
                            ("material_id",     mp.material_id,     "town_wood_dark"),
                            ("texture_page_id", mp.texture_page_id, "tpage_town_world_01"),
                            ("clut_id",         mp.clut_id,         "town_day"),
                            ("palette_group",   mp.palette_group,   "town_day"),
                            ("atlas_group",     mp.atlas_group,     "World"),
                            ("texture_format",  mp.texture_format,  "8bpp"),
                            ("alpha_mode",      mp.alpha_mode,      "Cutout"),
                            ("force_no_filter", mp.force_no_filter, True),
                            ("approved_16bpp",  mp.approved_16bpp,  False),
                        )
                        for field, got, want in m_checks:
                            if got != want:
                                _err(f"mat import: {field} = {got!r}, expected {want!r}")
                                failures += 1
                            else:
                                _info(f"mat import: {field} = {got!r}")
        except Exception:
            _err("import_metadata raised:")
            traceback.print_exc()
            failures += 1

    # ── Phase 4: vertex-lighting bake ───────────────────────────
    # Quick exercise of the bake operators against the cube. We don't
    # validate exact byte values (the bake formula is its own concern);
    # we just check the layer gets created and the operators run
    # without raising.
    try:
        bpy.ops.object.select_all(action="DESELECT")
        cube = bpy.data.objects.get("Cube")
        if cube is not None:
            cube.select_set(True)
            bpy.context.view_layer.objects.active = cube

            r1 = bpy.ops.ps1godot.vc_create_layer()
            r2 = bpy.ops.ps1godot.vc_bake_directional()
            r3 = bpy.ops.ps1godot.vc_ambient_tint()
            r4 = bpy.ops.ps1godot.vc_clear()
            for name, r in (("create_layer", r1), ("bake_directional", r2),
                            ("ambient_tint", r3), ("clear", r4)):
                if "CANCELLED" in r:
                    _err(f"vc.{name} cancelled: {r}")
                    failures += 1
                else:
                    _info(f"vc.{name} returned {r}")

            mesh = cube.data
            layer = mesh.color_attributes.get("Col")
            if layer is None:
                _err("vc 'Col' attribute missing after create_layer + clear")
                failures += 1
            else:
                # After clear, every loop should be (1, 1, 1, 1).
                bad = next((c for c in layer.data
                            if c.color[0] != 1.0 or c.color[1] != 1.0 or c.color[2] != 1.0), None)
                if bad is not None:
                    _err(f"vc clear left a non-white loop: {tuple(bad.color)}")
                    failures += 1
                else:
                    _info(f"vc 'Col' attribute exists and clear left it white ({len(layer.data)} loops).")
    except Exception:
        _err("vertex-lighting smoke raised:")
        traceback.print_exc()
        failures += 1

    # ── Unregister cleanly (catches detach-order bugs) ──────────
    try:
        ps1godot_blender.unregister()
        _info("unregister() OK")
    except Exception:
        _err("unregister() raised:")
        traceback.print_exc()
        failures += 1

    # Verify pointer detach worked.
    for host_name, attr in expected_pointer_props:
        host = getattr(bpy.types, host_name)
        if hasattr(host, attr):
            _err(f"bpy.types.{host_name}.{attr} still present after unregister")
            failures += 1

    if failures:
        _err(f"{failures} check(s) failed")
        return 1
    _info("ALL CHECKS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
