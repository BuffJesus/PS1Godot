# Phase 6 — collision-helper authoring operators.
#
# PSX games use simple primitive volumes (boxes + spheres) for almost
# all collision: player block, camera block, trigger zone, interaction
# zone. The integration plan (§ 4.6) calls for one-click operators
# that spawn a properly-tagged helper instead of making authors:
#   - add a cube
#   - rename it
#   - tick "hide_render"
#   - set display to wire
#   - set ps1godot.mesh_role = CollisionOnly
#   - set ps1godot.export_mode = CollisionOnly
#   - set ps1godot.collision_layer = "Player" / "Camera" / etc.
#
# This module ships four operators covering the 80% case. RPG-specific
# helpers (NavRegion / TransitionPoint / SavePoint / EncounterVolume)
# are listed in the integration plan but deferred — they need
# Godot-side wire format + runtime hooks that don't exist yet.
#
# Helpers spawn at the 3D cursor for predictable placement. Authors
# move them after spawn; the operator's UNDO option lets them try
# different sizes without piling junk in the outliner.
#
# Wire / mesh choice: helpers are mesh-type (not empties) so they
# travel through the existing JSON sidecar pipeline naturally —
# the Godot side gets the metadata + bounds geometry without any
# changes to the export skip rules. The wireframe display + hide_render
# keep them visually distinct from render geometry.

import bpy
from mathutils import Vector


# Default sizes — chosen to match PSX-era proportions. Authors scale
# after spawn for room-specific volumes.
_PLAYER_DEFAULT_SIZE = (0.6, 0.6, 1.8)   # column (X, Y, Z) in meters
_CAMERA_DEFAULT_SIZE = (1.0, 1.0, 1.0)
_TRIGGER_DEFAULT_SIZE = (2.0, 2.0, 2.0)
_INTERACTION_DEFAULT_RADIUS = 0.75


def _next_name(prefix: str) -> str:
    """Find the next unused name for a helper of this kind. We don't
    rely on Blender's auto-suffix because it inserts a `.001` token
    that's noisy in mesh_id slugs; we want PlayerCollision_01, _02, …"""
    n = 1
    while f"{prefix}_{n:02d}" in bpy.data.objects:
        n += 1
    return f"{prefix}_{n:02d}"


def _configure_helper(obj: bpy.types.Object, collision_layer: str):
    """Apply the standard helper presentation + metadata tags.

    Caller has already created the mesh and added it to the scene; we
    just stamp the wireframe display, hide-from-render flag, and the
    PS1Godot PropertyGroup fields that turn the mesh into a recognised
    helper at export.
    """
    # Wireframe in viewport so the helper reads as "annotation" not
    # geometry. Authors who want to preview the volume's interior pick
    # WIRE_DISPLAY_BOUNDS via the inspector — we don't force it.
    obj.display_type = "WIRE"
    obj.hide_render = True

    # Don't contribute to camera selection by default — clicks in the
    # viewport should land on the actual mesh behind the helper.
    obj.show_in_front = False

    p = obj.ps1godot
    p.mesh_role = "CollisionOnly"
    p.export_mode = "CollisionOnly"
    p.collision_layer = collision_layer
    p.alpha_mode = "Opaque"


def _spawn_at_cursor(context, *, name: str, mesh) -> bpy.types.Object:
    """Wire `mesh` (already constructed) into a new Object positioned at
    the 3D cursor. Returns the new Object."""
    obj = bpy.data.objects.new(name, mesh)
    obj.location = context.scene.cursor.location.copy()
    context.collection.objects.link(obj)

    # Make the new helper the active selection so authors can scale /
    # move immediately without a click.
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    context.view_layer.objects.active = obj
    return obj


def _make_box_mesh(name: str, size: tuple[float, float, float]) -> bpy.types.Mesh:
    """Build a unit-cube mesh scaled to `size` and centered on origin."""
    sx, sy, sz = size[0] * 0.5, size[1] * 0.5, size[2] * 0.5
    verts = [
        (-sx, -sy, -sz), ( sx, -sy, -sz), ( sx,  sy, -sz), (-sx,  sy, -sz),
        (-sx, -sy,  sz), ( sx, -sy,  sz), ( sx,  sy,  sz), (-sx,  sy,  sz),
    ]
    faces = [
        (0, 1, 2, 3),  # bottom
        (4, 7, 6, 5),  # top (reversed for outward normal)
        (0, 4, 5, 1),  # -Y
        (1, 5, 6, 2),  # +X
        (2, 6, 7, 3),  # +Y
        (3, 7, 4, 0),  # -X
    ]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.validate()
    mesh.update()
    return mesh


def _make_sphere_mesh(name: str, radius: float, segments: int = 12, rings: int = 8) -> bpy.types.Mesh:
    """Build a low-poly UV sphere via the bmesh op so we don't have to
    reimplement spherical-coordinates iteration. Author scales after
    spawn if they want a different size."""
    import bmesh
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=radius)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return mesh


# ── Operators ────────────────────────────────────────────────────────


class PS1GODOT_OT_add_player_collision(bpy.types.Operator):
    """Spawn a player-shaped collision box (CollisionOnly, layer=Player) at the 3D cursor."""

    bl_idname = "ps1godot.add_player_collision"
    bl_label = "Add Player Collision Box"
    bl_description = (
        "Spawn a 0.6×0.6×1.8 m wireframe box tagged CollisionOnly with "
        "collision_layer=Player. Use to block player movement against "
        "walls, props, level geometry. Hidden from render."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        name = _next_name("PlayerCollision")
        mesh = _make_box_mesh(name + "_mesh", _PLAYER_DEFAULT_SIZE)
        obj = _spawn_at_cursor(context, name=name, mesh=mesh)
        _configure_helper(obj, "Player")
        self.report({"INFO"}, f"PS1Godot: spawned {name} at cursor.")
        return {"FINISHED"}


class PS1GODOT_OT_add_camera_blocker(bpy.types.Operator):
    """Spawn a camera-only collision box (CollisionOnly, layer=Camera) at the 3D cursor."""

    bl_idname = "ps1godot.add_camera_blocker"
    bl_label = "Add Camera Blocker"
    bl_description = (
        "Spawn a 1×1×1 m wireframe box tagged CollisionOnly with "
        "collision_layer=Camera. Use to keep the camera out of walls "
        "or under-floor space without blocking player movement."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        name = _next_name("CameraBlocker")
        mesh = _make_box_mesh(name + "_mesh", _CAMERA_DEFAULT_SIZE)
        obj = _spawn_at_cursor(context, name=name, mesh=mesh)
        _configure_helper(obj, "Camera")
        self.report({"INFO"}, f"PS1Godot: spawned {name} at cursor.")
        return {"FINISHED"}


class PS1GODOT_OT_add_trigger_volume(bpy.types.Operator):
    """Spawn an event-trigger volume (CollisionOnly, layer=Trigger) at the 3D cursor."""

    bl_idname = "ps1godot.add_trigger_volume"
    bl_label = "Add Trigger Volume"
    bl_description = (
        "Spawn a 2×2×2 m wireframe box tagged CollisionOnly with "
        "collision_layer=Trigger. The runtime fires a Lua event when "
        "the player enters / exits — use for cutscene starts, area "
        "transitions, encounter volumes."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        name = _next_name("TriggerVolume")
        mesh = _make_box_mesh(name + "_mesh", _TRIGGER_DEFAULT_SIZE)
        obj = _spawn_at_cursor(context, name=name, mesh=mesh)
        _configure_helper(obj, "Trigger")
        self.report({"INFO"}, f"PS1Godot: spawned {name} at cursor.")
        return {"FINISHED"}


class PS1GODOT_OT_add_interaction_volume(bpy.types.Operator):
    """Spawn an interaction sphere (CollisionOnly, layer=Interaction) at the 3D cursor."""

    bl_idname = "ps1godot.add_interaction_volume"
    bl_label = "Add Interaction Volume"
    bl_description = (
        "Spawn a 0.75 m radius wireframe sphere tagged CollisionOnly "
        "with collision_layer=Interaction. The runtime shows the "
        "InteractionPromptCanvas when the player is inside; pressing "
        "the interact button fires onInteract on the linked object's "
        "Lua script."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        name = _next_name("InteractionVolume")
        mesh = _make_sphere_mesh(name + "_mesh", _INTERACTION_DEFAULT_RADIUS)
        obj = _spawn_at_cursor(context, name=name, mesh=mesh)
        _configure_helper(obj, "Interaction")
        self.report({"INFO"}, f"PS1Godot: spawned {name} at cursor.")
        return {"FINISHED"}


register, unregister = bpy.utils.register_classes_factory((
    PS1GODOT_OT_add_player_collision,
    PS1GODOT_OT_add_camera_blocker,
    PS1GODOT_OT_add_trigger_volume,
    PS1GODOT_OT_add_interaction_volume,
))
