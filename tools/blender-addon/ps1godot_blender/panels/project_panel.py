# PS1Godot Project panel — sits in the 3D View N-panel under the
# "PS1Godot" tab. Exposes scene-wide settings (project root, default
# IDs, metadata version) plus a "Validate Scene" action that calls the
# operator stub. Phase 2+ will add Import / Export / Reports buttons
# alongside.
#
# UI design follows docs/ps1godot_blender_addon_integration_plan.md
# § 4.1 verbatim.

import bpy


class PS1GODOT_PT_project(bpy.types.Panel):
    bl_label = "PS1Godot Project"
    bl_idname = "PS1GODOT_PT_project"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "PS1Godot"
    bl_order = 0  # First panel in the tab.

    def draw(self, context):
        layout = self.layout
        scene = context.scene
        props = scene.ps1godot

        col = layout.column(align=True)
        col.prop(props, "project_root")
        col.prop(props, "output_subdir")

        col.separator()
        col.prop(props, "default_chunk_id")
        col.prop(props, "default_disc_id")

        col.separator()
        # Metadata version is informational; readers should respect it
        # but authors don't typically change it manually.
        col.prop(props, "metadata_version")

        # Action row.
        row = layout.row(align=True)
        row.scale_y = 1.2
        row.operator("ps1godot.validate_scene", icon="CHECKMARK")


_classes = (PS1GODOT_PT_project,)


def register():
    for c in _classes:
        bpy.utils.register_class(c)


def unregister():
    for c in reversed(_classes):
        bpy.utils.unregister_class(c)
