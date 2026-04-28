# Bulk metadata apply — copy the active object's PS1Godot tags to
# every other selected mesh in one click.
#
# The pain point this solves: a typical PS1 scene has 30+ horror
# decals, 20+ wall sections, etc., all needing the same MeshRole /
# DrawPhase / AlphaMode / texture_page_id setup. Without bulk apply,
# authors click each one + set 5 fields. With it: tag one, select
# all, click. The 30-second per-object loop becomes 30 seconds total.
#
# "Smart copy" rule: only fields that DIFFER from their construction
# default get propagated. This means an author who tagged 30 walls
# with `mesh_role=StaticWorld + texture_page_id=tpage_walls` doesn't
# accidentally clear per-mesh `note` strings or `chunk_id` overrides
# when applying. The intent is "apply what I customized," not "make
# everything identical."

import bpy

from ..properties import PS1GodotObjectProps


# Fields we copy. Stable IDs (asset_id, mesh_id) are explicitly
# excluded — they're auto-generated per-mesh on export and copying
# them would create duplicate-ID validator failures.
_COPY_FIELDS = (
    "chunk_id",
    "region_id",
    "area_archive_id",
    "mesh_role",
    "export_mode",
    "draw_phase",
    "shading_mode",
    "alpha_mode",
    "collision_layer",
    "note",
)


def _construction_defaults() -> dict[str, object]:
    """Read the PS1GodotObjectProps construction defaults from the
    PropertyGroup itself. Avoids hard-coding "StaticWorld" etc. here
    so the rule "don't propagate defaults" stays accurate even when
    the EnumProperty defaults change."""
    bl_rna = PS1GodotObjectProps.bl_rna
    defaults: dict[str, object] = {}
    for f in _COPY_FIELDS:
        prop = bl_rna.properties.get(f)
        if prop is None:
            continue
        if hasattr(prop, "default"):
            defaults[f] = prop.default
        else:
            defaults[f] = ""
    return defaults


class PS1GODOT_OT_bulk_apply(bpy.types.Operator):
    """Copy the active object's customized PS1 tags to every other selected mesh."""

    bl_idname = "ps1godot.bulk_apply"
    bl_label = "Apply to Selected"
    bl_description = (
        "Stamp the active object's PS1Godot tags onto every other "
        "selected mesh. Only fields that differ from their construction "
        "default propagate — per-mesh overrides on the targets survive. "
        "asset_id and mesh_id are skipped (auto-generated per-mesh)."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        active = context.active_object
        if active is None or active.type != "MESH":
            self.report({"ERROR"}, "PS1Godot: active object must be a mesh.")
            return {"CANCELLED"}

        targets = [
            o for o in context.selected_objects
            if o.type == "MESH" and o.name != active.name
        ]
        if not targets:
            self.report({"WARNING"},
                        "PS1Godot: no other meshes selected — nothing to apply.")
            return {"CANCELLED"}

        defaults = _construction_defaults()
        src = active.ps1godot

        # Build the field set we'll actually copy: those whose value
        # on the active differs from the construction default.
        to_copy = []
        for f in _COPY_FIELDS:
            try:
                val = getattr(src, f)
            except AttributeError:
                continue
            if defaults.get(f) != val:
                to_copy.append((f, val))

        if not to_copy:
            self.report({"INFO"},
                        f"PS1Godot: '{active.name}' has no customizations to apply (every "
                        f"copyable field matches its default).")
            return {"FINISHED"}

        for tgt in targets:
            for f, val in to_copy:
                setattr(tgt.ps1godot, f, val)

        field_list = ", ".join(f for f, _ in to_copy)
        self.report({"INFO"},
                    f"PS1Godot: applied {len(to_copy)} field(s) from '{active.name}' to "
                    f"{len(targets)} target(s). Fields: {field_list}.")
        return {"FINISHED"}


register, unregister = bpy.utils.register_classes_factory((PS1GODOT_OT_bulk_apply,))
