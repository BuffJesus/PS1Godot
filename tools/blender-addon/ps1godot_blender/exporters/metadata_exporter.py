# PS1Godot metadata exporter — Phase 2.
#
# Walks every tagged mesh object in the scene and writes a per-object
# JSON sidecar (`<mesh_id>.ps1meshmeta.json`) under the project
# output directory. PS1Godot will read these alongside its FBX/GLB
# import to recover the metadata that Blender's geometry-only formats
# strip out.
#
# Round-trip rule (per docs/ps1godot_blender_addon_integration_plan.md
# § 5.3): preserve existing IDs and metadata. asset_id / mesh_id are
# generated when empty and persisted back into the .blend's
# PropertyGroup so the next export reads the same values.
#
# Phase 2 emits per-object sidecars only. Material metadata travels in
# the same JSON (nested under "materials"); separate per-material
# files land in Phase 5 alongside the texture-page workflow.

import os
import bpy

from ..utils.ids import ensure_object_ids
from ..utils.json_io import SCHEMA_VERSION, sidecar_path_for, write_pretty_json


# Roles that don't get a sidecar. EditorOnly + Ignore are the explicit
# "skip me" markers per docs/ps1_asset_pipeline_plan.md § C1.
_SKIP_ROLES = frozenset({"EditorOnly"})
_SKIP_EXPORT_MODES = frozenset({"Ignore"})


def export_scene(context, output_dir: str) -> dict:
    """Walk the scene, write one sidecar per tagged Object.

    Returns a small summary dict the operator surfaces in the info
    area: {"written": int, "skipped": int, "paths": [str, …]}. Mutates
    Object PropertyGroups in-place via ensure_object_ids — those
    writes are persisted when Blender saves the .blend afterwards.
    """
    scene = context.scene
    if not output_dir:
        raise ValueError("output_dir is empty; set Project Root + Output Subdir.")

    os.makedirs(output_dir, exist_ok=True)

    written: list[str] = []
    skipped = 0

    for obj in scene.objects:
        if obj.type != "MESH":
            continue
        if obj.hide_render:
            skipped += 1
            continue
        props = obj.ps1godot
        if props.mesh_role in _SKIP_ROLES or props.export_mode in _SKIP_EXPORT_MODES:
            skipped += 1
            continue

        ensure_object_ids(obj)

        payload = _payload_for_object(scene, obj)
        path = sidecar_path_for(output_dir, props.mesh_id)
        write_pretty_json(path, payload)
        written.append(path)

    return {"written": len(written), "skipped": skipped, "paths": written}


def _payload_for_object(scene, obj) -> dict:
    """Build the JSON-serializable dict for one tagged Object.

    Field order is deliberate: schema version + identity at the top,
    streaming/grouping next, render policy, then materials trailing.
    Empty strings travel as "" (not omitted) so the importer can
    distinguish "explicitly empty" from "absent" without consulting
    the schema version.
    """
    s_props = scene.ps1godot
    o = obj.ps1godot

    materials = [
        _payload_for_material(slot.material)
        for slot in obj.material_slots
        if slot.material is not None
    ]

    # bpy.data.filepath is the path of the currently-open .blend
    # (empty string when running headless on an unsaved file).
    blend_basename = os.path.basename(bpy.data.filepath) if bpy.data.filepath else ""

    return {
        "ps1godot_metadata_version": SCHEMA_VERSION,
        "asset_id":            o.asset_id,
        "mesh_id":             o.mesh_id,
        "source_object_name":  obj.name,
        "blend_file":          blend_basename,
        "chunk_id":            o.chunk_id  or s_props.default_chunk_id,
        "region_id":           o.region_id,
        "disc_id":             s_props.default_disc_id,
        "area_archive_id":     o.area_archive_id,
        "mesh_role":           o.mesh_role,
        "export_mode":         o.export_mode,
        "draw_phase":          o.draw_phase,
        "shading_mode":        o.shading_mode,
        "alpha_mode":          o.alpha_mode,
        "collision_layer":     o.collision_layer,
        "materials":           materials,
    }


def _payload_for_material(mat) -> dict:
    m = mat.ps1godot
    return {
        "blender_name":    mat.name,
        "material_id":     m.material_id or mat.name,
        "texture_page_id": m.texture_page_id,
        "clut_id":         m.clut_id,
        "palette_group":   m.palette_group,
        "atlas_group":     m.atlas_group,
        "texture_format":  m.texture_format,
        "alpha_mode":      m.alpha_mode,
        "force_no_filter": bool(m.force_no_filter),
        "approved_16bpp":  bool(m.approved_16bpp),
    }
