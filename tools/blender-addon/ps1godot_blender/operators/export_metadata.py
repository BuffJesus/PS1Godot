# PS1GODOT_OT_export_metadata — Phase 2 operator.
#
# Thin wrapper around exporters.metadata_exporter.export_scene that
# resolves the output directory from scene props (project root +
# subdir), reports written/skipped counts via the operator info area,
# and surfaces filesystem errors as ERROR-class reports rather than
# raised exceptions (Blender hides the latter from the status bar).

import os
import bpy

from ..exporters import metadata_exporter


class PS1GODOT_OT_export_metadata(bpy.types.Operator):
    """Write per-object metadata JSON sidecars alongside the project's exported assets."""

    bl_idname = "ps1godot.export_metadata"
    bl_label = "Export Metadata"
    bl_description = (
        "Write a .ps1meshmeta.json sidecar per tagged mesh object into "
        "<project_root>/<output_subdir>/. "
        "Auto-generates asset_id / mesh_id on first export and persists them back."
    )
    bl_options = {"REGISTER"}

    def execute(self, context):
        scene = context.scene
        s = scene.ps1godot

        if not s.project_root:
            self.report({"ERROR"}, "PS1Godot: set Project Root in the PS1Godot Project panel first.")
            return {"CANCELLED"}

        # bpy.path.abspath resolves "//"-prefixed paths relative to the
        # current .blend file. Plain absolute paths pass through.
        project_root = bpy.path.abspath(s.project_root)
        if not os.path.isdir(project_root):
            self.report({"ERROR"}, f"PS1Godot: project_root '{project_root}' is not a directory.")
            return {"CANCELLED"}

        output_dir = os.path.join(project_root, s.output_subdir or "ps1godot_assets/blender_sources")

        try:
            summary = metadata_exporter.export_scene(context, output_dir)
        except Exception as e:
            self.report({"ERROR"}, f"PS1Godot: export failed — {e}")
            return {"CANCELLED"}

        n = summary["written"]
        k = summary["skipped"]
        self.report(
            {"INFO"},
            f"PS1Godot: wrote {n} sidecar(s) to {output_dir}"
            + (f" ({k} skipped)" if k else ""),
        )
        # Echo to console too — operator info is one-line; the console
        # gets the full path list for hand-inspection.
        print(f"[PS1Godot] export_metadata: {n} written, {k} skipped, output_dir={output_dir}")
        for p in summary["paths"]:
            print(f"[PS1Godot]   {p}")
        return {"FINISHED"}


register, unregister = bpy.utils.register_classes_factory((PS1GODOT_OT_export_metadata,))
