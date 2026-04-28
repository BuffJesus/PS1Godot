# PS1 Vertex Lighting panel — sits in the 3D View N-panel under the
# "PS1Godot" tab. Surfaces the four bake operators + their parameters.
#
# UX intent: a single column of fields the author tweaks (sun direction
# + sun color + ambient color + ambient strength), then four action
# rows that read those fields. Saves the iterate-bake-tweak-bake loop
# instead of a popup-per-bake.

import bpy


class PS1GODOT_PT_vertex_lighting(bpy.types.Panel):
    bl_label = "PS1 Vertex Lighting"
    bl_idname = "PS1GODOT_PT_vertex_lighting"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "PS1Godot"
    bl_order = 3   # after Project / Asset Metadata / Material panels

    def draw(self, context):
        layout = self.layout
        s = context.scene.ps1godot

        # Quick action — get a layer up before any bake can run.
        layout.operator("ps1godot.vc_create_layer", icon="GROUP_VCOL")

        # Bake parameters. FloatVectorProperty(subtype=DIRECTION) renders
        # as an interactive 3-axis gizmo; subtype=COLOR renders as a
        # swatch picker. Cheap UX wins for free.
        layout.separator()
        col = layout.column(align=True)
        col.label(text="Directional Bake", icon="LIGHT_SUN")
        col.prop(s, "vc_sun_dir")
        col.prop(s, "vc_sun_color")
        col.prop(s, "vc_ambient_color")
        col.prop(s, "vc_ambient_strength")

        row = layout.row(align=True)
        row.scale_y = 1.3
        row.operator("ps1godot.vc_bake_directional", icon="LIGHT_SUN")

        # Scene-lights bake — author drops Lights into the scene,
        # clicks once. PSX hardware doesn't do runtime shadows, but
        # the bake CAN cast them (raycast at author time, written as
        # darker bytes in the vertex color — Silent Hill / FFIX did
        # this exact thing).
        layout.separator()
        col = layout.column(align=True)
        col.label(text="Bake from Scene", icon="OUTLINER_OB_LIGHT")
        col.prop(s, "vc_cast_shadows")
        sub = col.column(align=True)
        sub.enabled = s.vc_cast_shadows
        sub.prop(s, "vc_shadow_bias")
        col.prop(s, "vc_use_color_temperature")

        row = layout.row(align=True)
        row.scale_y = 1.3
        row.operator("ps1godot.vc_bake_scene_lights", icon="LIGHT")

        layout.separator()
        col = layout.column(align=True)
        col.label(text="Other", icon="MOD_TINT")
        col.operator("ps1godot.vc_ambient_tint", icon="COLOR")
        col.operator("ps1godot.vc_clear", icon="X")


register, unregister = bpy.utils.register_classes_factory((PS1GODOT_PT_vertex_lighting,))
