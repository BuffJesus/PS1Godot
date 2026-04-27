# PS1 Asset Metadata panel — per-object PS1 authoring fields. Sits in
# the 3D View N-panel under the "PS1Godot" tab; appears only when
# something is selected.
#
# Per-material fields are surfaced in a sub-panel that pulls the active
# slot's material props. Layout: ID block on top, then the role/phase
# enums grouped in a box, then the alpha/shading row.
#
# UI design follows docs/ps1godot_blender_addon_integration_plan.md § 4.2.

import bpy


class PS1GODOT_PT_object_metadata(bpy.types.Panel):
    bl_label = "PS1 Asset Metadata"
    bl_idname = "PS1GODOT_PT_object_metadata"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "PS1Godot"
    bl_order = 1

    @classmethod
    def poll(cls, context):
        return context.active_object is not None

    def draw(self, context):
        layout = self.layout
        obj = context.active_object
        if obj is None:
            layout.label(text="(no active object)")
            return
        props = obj.ps1godot

        # ── Stable IDs ──────────────────────────────────────────
        col = layout.column(align=True)
        col.label(text="Identity")
        col.prop(props, "asset_id")
        col.prop(props, "mesh_id")

        # ── Streaming + grouping ───────────────────────────────
        col = layout.column(align=True)
        col.label(text="Streaming")
        col.prop(props, "chunk_id")
        col.prop(props, "region_id")
        col.prop(props, "area_archive_id")

        # ── Render policy ──────────────────────────────────────
        box = layout.box()
        box.label(text="Render Policy", icon="MATERIAL")
        col = box.column(align=True)
        col.prop(props, "mesh_role")
        col.prop(props, "export_mode")
        col.prop(props, "draw_phase")
        col.prop(props, "shading_mode")
        col.prop(props, "alpha_mode")

        # ── Author note ────────────────────────────────────────
        layout.prop(props, "note")


class PS1GODOT_PT_material_metadata(bpy.types.Panel):
    bl_label = "PS1 Material"
    bl_idname = "PS1GODOT_PT_material_metadata"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "PS1Godot"
    bl_order = 2

    @classmethod
    def poll(cls, context):
        obj = context.active_object
        if obj is None:
            return False
        # Show when the active object has at least one material slot.
        return any(slot and slot.material for slot in obj.material_slots)

    def draw(self, context):
        layout = self.layout
        obj = context.active_object

        # Operate on the active material slot — same convention as
        # Blender's built-in material panel.
        slot = obj.material_slots[obj.active_material_index] if obj.material_slots else None
        mat = slot.material if slot else None
        if mat is None:
            layout.label(text="(no active material)")
            return

        props = mat.ps1godot

        col = layout.column(align=True)
        col.label(text=f"Material: {mat.name}")
        col.prop(props, "material_id")
        col.prop(props, "texture_page_id")
        col.prop(props, "clut_id")
        col.prop(props, "palette_group")
        col.prop(props, "atlas_group")

        col.separator()
        col.prop(props, "texture_format")
        col.prop(props, "alpha_mode")

        col.separator()
        row = col.row(align=True)
        row.prop(props, "force_no_filter", toggle=True)
        row.prop(props, "approved_16bpp", toggle=True)


_classes = (
    PS1GODOT_PT_object_metadata,
    PS1GODOT_PT_material_metadata,
)


def register():
    for c in _classes:
        bpy.utils.register_class(c)


def unregister():
    for c in reversed(_classes):
        bpy.utils.unregister_class(c)
