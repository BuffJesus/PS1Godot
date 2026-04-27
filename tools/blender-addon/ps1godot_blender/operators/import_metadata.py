# PS1GODOT_OT_import_metadata — Phase 8 operator (Godot → Blender).
#
# Symmetric counterpart to PS1GODOT_OT_export_metadata. Reads JSON
# sidecars from <project_root>/<output_subdir>/ and applies their
# metadata to matching Blender Objects via the PropertyGroup fields.
#
# Match strategy mirrors the Godot reader's three-tier fallback:
#   mesh_id (PS1Godot props) → source_object_name (bpy Object name) →
#   mesh_id-as-name. The first hit wins.
#
# Round-trip rule (integration plan § 5.3): the sidecar wins at
# import. The whole point is that whichever side just wrote the JSON
# is the source of truth. Existing Blender-side values are overwritten;
# fields absent from the sidecar are left untouched (so partial
# sidecars don't wipe state the Blender author set after the last
# round-trip).

import os
import json
import bpy

from ..utils.json_io import SCHEMA_VERSION, SIDECAR_SUFFIX


# Wire identifier sets must mirror properties.py exactly. We validate
# imported enum values against these so unknown wire spellings (e.g. a
# field added on the Godot side that this Blender add-on hasn't caught
# up with) get logged and skipped instead of crashing the import.
_VALID_MESH_ROLE = frozenset({
    "StaticWorld", "DynamicRigid", "Skinned", "Segmented",
    "SpriteBillboard", "CollisionOnly", "EditorOnly",
})
_VALID_EXPORT_MODE = frozenset({
    "MergeStatic", "KeepSeparate", "CollisionOnly", "Ignore",
})
_VALID_DRAW_PHASE = frozenset({
    "OpaqueStatic", "OpaqueDynamic", "Characters",
    "CutoutDecals", "TransparentEffects", "UI",
})
_VALID_SHADING_MODE = frozenset({
    "Unlit", "FlatColor", "VertexColor", "BakedLighting",
})
_VALID_ALPHA_MODE = frozenset({
    "Opaque", "Cutout", "SemiTransparent", "Additive", "UI",
})
_VALID_TEXTURE_FORMAT = frozenset({"Auto", "4bpp", "8bpp", "16bpp"})


class PS1GODOT_OT_import_metadata(bpy.types.Operator):
    """Apply <mesh_id>.ps1meshmeta.json sidecars to matching Blender objects."""

    bl_idname = "ps1godot.import_metadata"
    bl_label = "Import Metadata"
    bl_description = (
        "Read .ps1meshmeta.json sidecars under <project_root>/<output_subdir>/ "
        "and apply their metadata to matching Blender Objects (by mesh_id or "
        "source_object_name). Sidecar values overwrite existing PropertyGroup state."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        scene = context.scene
        s = scene.ps1godot

        if not s.project_root:
            self.report({"ERROR"}, "PS1Godot: set Project Root in the PS1Godot Project panel first.")
            return {"CANCELLED"}

        project_root = bpy.path.abspath(s.project_root)
        if not os.path.isdir(project_root):
            self.report({"ERROR"}, f"PS1Godot: project_root '{project_root}' is not a directory.")
            return {"CANCELLED"}

        sidecar_dir = os.path.join(project_root, s.output_subdir or "ps1godot_assets/blender_sources")
        if not os.path.isdir(sidecar_dir):
            self.report({"WARNING"}, f"PS1Godot: sidecar dir '{sidecar_dir}' does not exist; nothing to import.")
            return {"CANCELLED"}

        # Index Blender objects by Object.name + mesh_id (the latter
        # only when explicitly set; empty mesh_id falls through to
        # name match). One pass over the data, N dict lookups below.
        by_name: dict[str, bpy.types.Object] = {}
        by_mesh_id: dict[str, bpy.types.Object] = {}
        for obj in bpy.data.objects:
            if obj.type != "MESH":
                continue
            by_name[obj.name] = obj
            mid = obj.ps1godot.mesh_id
            if mid:
                by_mesh_id[mid] = obj

        found = 0
        applied = 0
        unmatched = 0
        version_skip = 0
        parse_error = 0
        unmatched_names: list[str] = []

        for fname in os.listdir(sidecar_dir):
            if not fname.endswith(SIDECAR_SUFFIX):
                continue
            path = os.path.join(sidecar_dir, fname)
            found += 1
            try:
                with open(path, encoding="utf-8") as f:
                    payload = json.load(f)
            except Exception as e:
                print(f"[PS1Godot] Failed to read sidecar '{path}': {e}")
                parse_error += 1
                continue

            version = int(payload.get("ps1godot_metadata_version", 1))
            if version > SCHEMA_VERSION:
                self.report({"WARNING"}, f"Sidecar '{fname}' is schema v{version} (max supported {SCHEMA_VERSION}); skipped.")
                version_skip += 1
                continue

            mesh_id = payload.get("mesh_id", "") or ""
            src_name = payload.get("source_object_name", "") or ""
            target = (
                (by_mesh_id.get(mesh_id) if mesh_id else None)
                or (by_name.get(src_name) if src_name else None)
                or (by_name.get(mesh_id) if mesh_id else None)
            )
            if target is None:
                unmatched += 1
                unmatched_names.append(f"{fname} (mesh_id='{mesh_id}', source='{src_name}')")
                continue

            self._apply_payload(target, payload)
            applied += 1

        # Report summary. Use INFO for the headline + WARN per
        # unmatched so authors notice them without scrolling.
        self.report(
            {"INFO"},
            f"PS1Godot: sidecars found={found}, applied={applied}, "
            f"unmatched={unmatched}, version-skipped={version_skip}, parse-error={parse_error}.",
        )
        for name in unmatched_names:
            self.report({"WARNING"}, f"PS1Godot: unmatched sidecar: {name}")
            print(f"[PS1Godot] unmatched sidecar: {name}")
        return {"FINISHED"}

    # ── Apply ───────────────────────────────────────────────────────

    def _apply_payload(self, obj: bpy.types.Object, payload: dict) -> None:
        """Mutate `obj.ps1godot` to mirror the sidecar's metadata.

        Each field is guarded individually so a malformed value in one
        slot doesn't abort the whole apply pass — we'd rather get
        partially-correct state than nothing.
        """
        p = obj.ps1godot

        # Strings — empty string is legal (preserves "explicit empty"
        # semantics from the writer). Skipping a key entirely keeps
        # whatever the Blender author had.
        for key in ("asset_id", "mesh_id", "chunk_id", "region_id", "area_archive_id", "collision_layer"):
            if key in payload:
                val = payload[key] or ""
                if isinstance(val, str):
                    setattr(p, key, val)

        # Enums — validate against the wire-id whitelist. Unknown
        # values warn + skip that one field.
        self._set_enum(p, "mesh_role",    payload.get("mesh_role"),    _VALID_MESH_ROLE,    obj.name)
        self._set_enum(p, "export_mode",  payload.get("export_mode"),  _VALID_EXPORT_MODE,  obj.name)
        self._set_enum(p, "draw_phase",   payload.get("draw_phase"),   _VALID_DRAW_PHASE,   obj.name)
        self._set_enum(p, "shading_mode", payload.get("shading_mode"), _VALID_SHADING_MODE, obj.name)
        self._set_enum(p, "alpha_mode",   payload.get("alpha_mode"),   _VALID_ALPHA_MODE,   obj.name)

        # Material payloads round-trip onto Blender Materials by name.
        # Sidecars produced by the Godot side may not yet have rich
        # per-material PS1 metadata (Phase 5), but they do carry the
        # blender_name/material_id so we can at least keep the slot
        # binding intact. Unknown materials are skipped silently.
        for m_payload in payload.get("materials", []) or []:
            self._apply_material(obj, m_payload)

    def _set_enum(self, prop_group, attr: str, value, valid_set: frozenset, obj_name: str) -> None:
        if value is None:
            return
        if not isinstance(value, str):
            return
        if value not in valid_set:
            print(f"[PS1Godot] Sidecar for '{obj_name}': unknown {attr} value '{value}' — kept default.")
            return
        setattr(prop_group, attr, value)

    def _apply_material(self, obj: bpy.types.Object, m_payload: dict) -> None:
        name = m_payload.get("blender_name") or m_payload.get("material_id")
        if not name:
            return
        # Find a material slot whose material name matches. Sidecars
        # written from Godot use the Material.ResourceName which
        # typically matches Blender's default mat name on import.
        target_mat = None
        for slot in obj.material_slots:
            if slot.material is not None and slot.material.name == name:
                target_mat = slot.material
                break
        if target_mat is None:
            return

        m = target_mat.ps1godot
        for key in ("material_id", "texture_page_id", "clut_id", "palette_group", "atlas_group"):
            if key in m_payload and isinstance(m_payload[key], str):
                setattr(m, key, m_payload[key])
        if "alpha_mode" in m_payload and isinstance(m_payload["alpha_mode"], str) and m_payload["alpha_mode"] in _VALID_ALPHA_MODE:
            m.alpha_mode = m_payload["alpha_mode"]
        if ("texture_format" in m_payload
                and isinstance(m_payload["texture_format"], str)
                and m_payload["texture_format"] in _VALID_TEXTURE_FORMAT):
            m.texture_format = m_payload["texture_format"]
        if "force_no_filter" in m_payload:
            m.force_no_filter = bool(m_payload["force_no_filter"])
        if "approved_16bpp" in m_payload:
            m.approved_16bpp = bool(m_payload["approved_16bpp"])


register, unregister = bpy.utils.register_classes_factory((PS1GODOT_OT_import_metadata,))
