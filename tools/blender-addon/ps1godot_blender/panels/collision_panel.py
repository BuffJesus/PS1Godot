# PS1 Collision Helpers panel — the four spawn buttons + a hint that
# shows what the active selection's collision_layer is (when relevant).
#
# Lives below Asset Metadata + Material panels in the PS1Godot N-panel
# tab so authors find it after they've already tagged their render
# meshes; collision layout is the second pass on a typical scene.

import bpy


class PS1GODOT_PT_collision(bpy.types.Panel):
    bl_label = "PS1 Collision Helpers"
    bl_idname = "PS1GODOT_PT_collision"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "PS1Godot"
    bl_order = 4   # after Project / Metadata / Material / VertexLighting

    def draw(self, context):
        layout = self.layout

        col = layout.column(align=True)
        col.label(text="Spawn at Cursor", icon="OUTLINER_DATA_EMPTY")
        col.operator("ps1godot.add_player_collision",     icon="ARMATURE_DATA")
        col.operator("ps1godot.add_camera_blocker",       icon="CAMERA_DATA")
        col.operator("ps1godot.add_trigger_volume",       icon="MESH_CUBE")
        col.operator("ps1godot.add_interaction_volume",   icon="MESH_UVSPHERE")

        # Show the current selection's collision_layer when applicable
        # — gives authors quick read-back without opening the Asset
        # Metadata panel.
        obj = context.active_object
        if obj is not None and obj.type == "MESH":
            p = obj.ps1godot
            if p.mesh_role == "CollisionOnly":
                layout.separator()
                box = layout.box()
                box.label(text=f"Active: {obj.name}", icon="OBJECT_DATAMODE")
                box.label(text=f"Layer: {p.collision_layer or '(unset)'}")
                box.label(text=f"Role: {p.mesh_role}  ·  Export: {p.export_mode}")


register, unregister = bpy.utils.register_classes_factory((PS1GODOT_PT_collision,))
