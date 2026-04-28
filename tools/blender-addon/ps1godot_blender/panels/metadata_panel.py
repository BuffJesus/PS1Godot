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

        # ── Selection stats — answers "what am I about to ship?" ─
        # Walks selected meshes once per draw (cheap; meshes count
        # is small and getattr is fast). Surfaces tri count + unique
        # material count + selection size so authors see budget
        # consumption inline.
        selected_meshes = [o for o in context.selected_objects if o.type == "MESH"]
        if len(selected_meshes) > 1:
            tri_total = 0
            mat_set: set[str] = set()
            for o in selected_meshes:
                if o.data is not None:
                    tri_total += len(o.data.polygons)
                    for slot in o.material_slots:
                        if slot.material is not None:
                            mat_set.add(slot.material.name)
            box = layout.box()
            row = box.row()
            row.label(text=f"{len(selected_meshes)} meshes  ·  {tri_total} tris  ·  {len(mat_set)} materials",
                      icon="OUTLINER_OB_GROUP_INSTANCE")
            box.operator("ps1godot.bulk_apply", icon="DUPLICATE")

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
        # Show whenever an active mesh object exists. The panel adapts
        # internally: when there's no material slot we surface the
        # Create button as the primary action so authors don't fall off
        # the path. (Old behaviour gated on existing material slots,
        # which silently hid the Create button after authors deleted
        # the default material.)
        obj = context.active_object
        return obj is not None and obj.type == "MESH"

    def draw(self, context):
        layout = self.layout
        obj = context.active_object

        # Operate on the active material slot — same convention as
        # Blender's built-in material panel.
        slot = obj.material_slots[obj.active_material_index] if obj.material_slots else None
        mat = slot.material if slot else None
        if mat is None:
            # No material yet — give the author the one button that
            # gets them unstuck. The Cycles bake operator and the gltf
            # vertex-colour export both need a material on the mesh.
            box = layout.box()
            box.label(text="No material assigned.", icon="ERROR")
            box.label(text="Cycles vertex bakes + glTF colour export both need a material on this mesh.")
            box.operator("ps1godot.create_material", icon="MATERIAL")
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

        # ── Phase 5 actions: create a properly-tagged material in one
        # click + preview the texture as PSX would actually quantize it. ──
        col.separator()
        col.label(text="Actions", icon="TOOL_SETTINGS")
        col.operator("ps1godot.create_material", icon="MATERIAL")
        row = col.row(align=True)
        row.operator("ps1godot.preview_4bpp", icon="IMAGE_DATA")
        row.operator("ps1godot.preview_8bpp", icon="IMAGE_DATA")


register, unregister = bpy.utils.register_classes_factory((
    PS1GODOT_PT_object_metadata,
    PS1GODOT_PT_material_metadata,
))
