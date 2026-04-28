# PS1GODOT_OT_export_to_godot — the one-click workflow operator.
#
# What "Export to Godot" actually means in practice for a PS1Godot
# author has historically been a five-step dance:
#   1. File → Export → glTF / FBX
#   2. Pick the right path (Godot project root)
#   3. Pick the right name (matches the PS1MeshInstance node name)
#   4. Click the addon's separate "Export Metadata" button
#   5. Switch to Godot, click "Apply Blender Metadata Sidecars"
#
# This operator collapses 1-4 into one button: validate → auto-fill
# IDs → export per-Object .glb to <project_root>/<asset_subdir>/ →
# write the JSON sidecars to <output_subdir>. The author then just
# switches windows and Godot's plugin picks up the new .glb on its
# next reimport scan; "Apply Blender Metadata Sidecars" plugs the
# metadata in.
#
# .glb (not .fbx) because Godot 4's GLB importer is the path the engine
# devs maintain — FBX import goes through a buggy intermediate.
#
# Per-Object export is deliberate: the unit of round-trip is the
# tagged Object, so we want one .glb per node so the Godot side can
# bind by mesh_id without cross-referencing a master scene.

import os
import bpy

from ..exporters import metadata_exporter
from ..utils.ids import ensure_object_ids


# Roles / export modes that don't get a .glb (or a sidecar). Authoring
# helpers, gizmos, collision-only proxies live here.
_SKIP_ROLES = frozenset({"EditorOnly"})
_SKIP_EXPORT_MODES = frozenset({"Ignore"})


class PS1GODOT_OT_export_to_godot(bpy.types.Operator):
    """Validate → export tagged objects as .glb + write metadata sidecars in one shot."""

    bl_idname = "ps1godot.export_to_godot"
    bl_label = "Export to Godot"
    bl_description = (
        "One-click workflow: validate the scene, then export every tagged Object as <mesh_id>.glb "
        "into <project_root>/<asset_subdir>/ alongside its .ps1meshmeta.json sidecar. "
        "The Godot side picks up the .glb on its next import scan."
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

        asset_dir   = os.path.join(project_root, s.asset_subdir or "ps1godot_assets/meshes")
        sidecar_dir = os.path.join(project_root, s.output_subdir or "ps1godot_assets/blender_sources")
        os.makedirs(asset_dir, exist_ok=True)

        # Stash + restore active selection so we don't surprise the
        # author. Per-object .glb export needs the object to be the
        # only selection at export time.
        prev_active   = context.view_layer.objects.active
        prev_selected = [o for o in bpy.data.objects if o.select_get()]

        glbs_written = 0
        glbs_failed  = 0
        glb_paths: list[str] = []

        try:
            for obj in self._exportable_objects(context):
                ensure_object_ids(obj)
                glb_path = os.path.join(asset_dir, f"{obj.ps1godot.mesh_id}.glb")

                # Per-object selection — gltf exporter's `use_selection`
                # mode walks only what's selected at the moment.
                bpy.ops.object.select_all(action="DESELECT")
                obj.select_set(True)
                context.view_layer.objects.active = obj

                try:
                    bpy.ops.export_scene.gltf(
                        filepath=glb_path,
                        export_format="GLB",
                        use_selection=True,
                        export_apply=True,             # bake modifiers
                        export_yup=True,               # Godot is Y-up
                        export_animations=False,       # Phase 7 territory
                        export_extras=False,           # custom props go via JSON
                        export_lights=False,
                        export_cameras=False,
                        # Vertex colours: PS1Godot's whole vertex-lighting
                        # workflow (Cycles bake, scene-light bake, ambient
                        # tint) writes a 'Col' attribute. By default glTF
                        # only exports it when the mesh has a material
                        # whose node tree samples it — which the default
                        # Blender material doesn't, silently dropping the
                        # bake. ACTIVE picks the active colour attribute;
                        # active_vertex_color_when_no_material bypasses
                        # the node-tree gate so the bake always rides
                        # through to Godot's PS1MeshInstance.BakedColors.
                        export_vertex_color="ACTIVE",
                        export_active_vertex_color_when_no_material=True,
                    )
                    glb_paths.append(glb_path)
                    glbs_written += 1
                except Exception as e:
                    print(f"[PS1Godot] GLB export failed for '{obj.name}' → '{glb_path}': {e}")
                    glbs_failed += 1
        finally:
            bpy.ops.object.select_all(action="DESELECT")
            for o in prev_selected:
                if o.name in bpy.data.objects:
                    o.select_set(True)
            if prev_active is not None and prev_active.name in bpy.data.objects:
                context.view_layer.objects.active = prev_active

        # Sidecars come after the GLBs so a partial GLB write doesn't
        # leave a sidecar pointing at nothing.
        try:
            sidecar_summary = metadata_exporter.export_scene(context, sidecar_dir)
        except Exception as e:
            self.report({"ERROR"}, f"PS1Godot: sidecar export failed — {e}")
            return {"CANCELLED"}

        # Headline + per-file rows in the console for click-through.
        head = (f"PS1Godot: exported {glbs_written} .glb + {sidecar_summary['written']} sidecar(s) "
                f"to '{project_root}'.")
        if glbs_failed:
            head += f" {glbs_failed} GLB write failure(s)."
        self.report({"INFO"}, head)
        print(f"[PS1Godot] {head}")
        for p in glb_paths:
            print(f"[PS1Godot]   GLB     {p}")
        for p in sidecar_summary["paths"]:
            print(f"[PS1Godot]   sidecar {p}")
        if sidecar_summary["skipped"]:
            print(f"[PS1Godot]   {sidecar_summary['skipped']} object(s) skipped (EditorOnly / Ignore / hidden).")
        if glbs_failed:
            return {"FINISHED"}  # partial success — don't claim cancel
        return {"FINISHED"}

    @staticmethod
    def _exportable_objects(context):
        """Mesh objects that should ship as .glb. Mirrors the
        metadata_exporter skip rules so the two passes agree."""
        for obj in context.scene.objects:
            if obj.type != "MESH":
                continue
            if obj.hide_render:
                continue
            p = obj.ps1godot
            if p.mesh_role in _SKIP_ROLES or p.export_mode in _SKIP_EXPORT_MODES:
                continue
            yield obj


register, unregister = bpy.utils.register_classes_factory((PS1GODOT_OT_export_to_godot,))
