# Phase 4 — vertex-color lighting bake operators.
#
# Vertex colors are the cheapest "real" lighting path on PS1: the GPU
# Gouraud-shades textured triangles by interpolating the per-vertex RGB,
# so a static lighting bake costs literally one byte per channel per
# vertex with zero runtime overhead. The integration plan calls these
# tools out as "one of the most important Blender-side features" (§ 4.5).
#
# Four operators in this module:
#   create_vc_layer       — add a "Col" attribute, white-fill it. Has
#                            to exist before the others can paint.
#   ambient_tint          — multiply every vertex by a chosen color.
#                            Use for night-mode washes, faction palettes,
#                            base lighting before a directional bake.
#   bake_directional      — saturate(dot(normal, sun_dir)) * sun_color
#                            + ambient_color * ambient_strength. The
#                            "lit from sun" basic look most PS1 worlds use.
#   clear_vc              — reset every vertex to (1, 1, 1). Lets authors
#                            redo a bake without piling artifacts.
#
# All operators work on the active selection; if multiple meshes are
# selected the bake runs across all of them. Edit-mode is exited
# automatically (Blender bake APIs work in object mode).
#
# Coordinate convention: world-space normals. Authors set sun direction
# in world space too (UI prop `vc_sun_dir`), so the bake "looks the
# same" no matter how the mesh is parented or rotated.
#
# PSX clamping: results are clamped to [0, 1] before write, then
# multiplied by 255 for the byte-channel attribute. Out-of-gamut
# additions are not supported here (would need HDR vertex colors,
# which the PS1 GPU doesn't have anyway).

import bpy
import math
from mathutils import Vector


VERTEX_COLOR_LAYER_NAME = "Col"

# PSX 2× semi-trans blend can blow out a vertex color past 1.0 if the
# bake hits 1.0 to begin with — SplashEdit caps its bake at 0.8 to
# leave that headroom (Runtime/PSXLightingBaker.cs:79-83 clamps the
# float result before quantizing to byte). We match the same ceiling
# so a mesh switched to Translucent later doesn't suddenly white-out.
PSX_VERTEX_BAKE_CEILING = 0.8


def _meshes_in_selection(context):
    """Yield every Mesh data that the active selection's mesh objects
    point at. Multiple objects can share the same Mesh (linked dupes);
    we paint the underlying data once, not per-instance."""
    seen = set()
    for obj in context.selected_objects:
        if obj.type != "MESH" or obj.data is None:
            continue
        if obj.data.name in seen:
            continue
        seen.add(obj.data.name)
        yield obj, obj.data


def _ensure_layer(mesh: bpy.types.Mesh) -> bpy.types.ByteColorAttribute | None:
    """Get-or-create a ByteColor attribute named 'Col' at corner domain.

    Corner-domain (per-loop) colors are what gltf2 / Godot consume.
    Per-vertex would also work but corner-domain mirrors what Blender's
    own Vertex Paint mode writes by default, so manual paint and our
    bakes interoperate cleanly.
    """
    if mesh is None:
        return None
    layer = mesh.color_attributes.get(VERTEX_COLOR_LAYER_NAME)
    if layer is None:
        layer = mesh.color_attributes.new(
            name=VERTEX_COLOR_LAYER_NAME,
            type="BYTE_COLOR",
            domain="CORNER",
        )
    return layer


def _saturate(x: float) -> float:
    """Clamp to [0, PSX_VERTEX_BAKE_CEILING].

    The 0.8 ceiling matches SplashEdit's PSXLightingBaker behaviour and
    leaves headroom for the PSX hardware 2× semi-trans blend (which
    multiplies vertex color × 2 on translucent draws). Bake to 1.0 and
    a mesh tagged Translucent later will white-out at runtime.
    """
    if x < 0.0: return 0.0
    if x > PSX_VERTEX_BAKE_CEILING: return PSX_VERTEX_BAKE_CEILING
    return x


# ── Operators ────────────────────────────────────────────────────────


def _white_fill_value() -> float:
    """White-fill matches the bake ceiling so a "blank" layer is
    visually consistent with what a maxed-out directional bake would
    produce. Authors used to seeing 1.0 in vertex paint won't notice
    the difference, and meshes switched to Translucent stay safe."""
    return PSX_VERTEX_BAKE_CEILING


class PS1GODOT_OT_vc_create_layer(bpy.types.Operator):
    """Add a 'Col' vertex-color layer to selected meshes (white-filled)."""

    bl_idname = "ps1godot.vc_create_layer"
    bl_label = "Create Vertex Color Layer"
    bl_description = (
        "Create a 'Col' byte-color attribute at corner domain on every "
        "selected mesh that doesn't already have one. White-fills it so "
        "subsequent bakes have a known starting point."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        touched = 0
        for obj, mesh in _meshes_in_selection(context):
            had = mesh.color_attributes.get(VERTEX_COLOR_LAYER_NAME) is not None
            layer = _ensure_layer(mesh)
            if layer is None:
                continue
            if not had:
                # Newly-created layer — white-fill at the PSX ceiling
                # (0.8) so subsequent translucent draws don't blow out.
                v = _white_fill_value()
                for c in layer.data:
                    c.color = (v, v, v, 1.0)
                touched += 1
        if touched == 0:
            self.report({"INFO"}, "PS1Godot: no new layers added — every selected mesh already has 'Col'.")
        else:
            self.report({"INFO"}, f"PS1Godot: created 'Col' on {touched} mesh(es).")
        return {"FINISHED"}


class PS1GODOT_OT_vc_ambient(bpy.types.Operator):
    """Multiply selected meshes' vertex colors by the scene's Ambient Color."""

    bl_idname = "ps1godot.vc_ambient_tint"
    bl_label = "Apply Ambient Tint"
    bl_description = (
        "Multiply every vertex color in selected meshes by the Ambient "
        "Color. Existing data is dimmed, not overwritten — apply a "
        "directional bake first if you want the bright key, then this "
        "wash for global mood."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        tint = context.scene.ps1godot.vc_ambient_color
        tr, tg, tb = tint[0], tint[1], tint[2]
        touched = 0
        for obj, mesh in _meshes_in_selection(context):
            layer = _ensure_layer(mesh)
            if layer is None:
                continue
            for c in layer.data:
                r, g, b, a = c.color
                c.color = (r * tr, g * tg, b * tb, a)
            touched += 1
        self.report({"INFO"}, f"PS1Godot: tinted {touched} mesh(es) by ambient color.")
        return {"FINISHED"}


class PS1GODOT_OT_vc_bake_directional(bpy.types.Operator):
    """Bake a directional + ambient lighting term into selected meshes' vertex colors."""

    bl_idname = "ps1godot.vc_bake_directional"
    bl_label = "Bake Directional Light"
    bl_description = (
        "For each vertex: max(dot(normal, sun_dir), 0) × sun_color + "
        "ambient_color × ambient_strength. Overwrites existing vertex "
        "color data — clear or re-export the source mesh if you want "
        "the previous bake back."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        s = context.scene.ps1godot
        sun = Vector(s.vc_sun_dir)
        if sun.length_squared < 1e-8:
            self.report({"ERROR"}, "PS1Godot: Sun Direction is zero-length — set a non-zero direction in the Vertex Lighting panel.")
            return {"CANCELLED"}
        sun.normalize()

        sun_r, sun_g, sun_b = s.vc_sun_color[0], s.vc_sun_color[1], s.vc_sun_color[2]
        amb_r, amb_g, amb_b = s.vc_ambient_color[0], s.vc_ambient_color[1], s.vc_ambient_color[2]
        amb_w = s.vc_ambient_strength

        baked = 0
        for obj, mesh in _meshes_in_selection(context):
            layer = _ensure_layer(mesh)
            if layer is None:
                continue

            # World-space normal per vertex. Use object's current
            # rotation; we don't bake at bind pose because authors
            # care about the lighting that matches the scene they see.
            world = obj.matrix_world.to_3x3()

            # Pre-compute per-vertex world normals so the per-loop
            # walk below is cheap. Smooth shading reads vertex
            # normals; flat-shaded faces will look stepped (PS1
            # default and what you usually want).
            n_world = [None] * len(mesh.vertices)
            for i, v in enumerate(mesh.vertices):
                wn = world @ v.normal
                if wn.length_squared > 1e-12:
                    wn.normalize()
                n_world[i] = wn

            # Walk loops (corner domain). Each loop maps to one
            # vertex via its `vertex_index`.
            for li, loop in enumerate(mesh.loops):
                wn = n_world[loop.vertex_index]
                ndotl = wn.dot(sun) if wn is not None else 0.0
                if ndotl < 0.0:
                    ndotl = 0.0

                r = _saturate(ndotl * sun_r + amb_r * amb_w)
                g = _saturate(ndotl * sun_g + amb_g * amb_w)
                b = _saturate(ndotl * sun_b + amb_b * amb_w)
                # Keep alpha at whatever the channel holds (Blender
                # uses 1.0 by default; gltf carries it through).
                a = layer.data[li].color[3]
                layer.data[li].color = (r, g, b, a)
            baked += 1

        self.report({"INFO"}, f"PS1Godot: baked directional light into {baked} mesh(es).")
        return {"FINISHED"}


def _kelvin_to_rgb(kelvin: float) -> tuple[float, float, float]:
    """Convert color temperature in Kelvin to linear RGB in [0, 1].

    Tanner Helland's piecewise approximation — accurate enough for
    artist intuition (warm/neutral/cool) and matches what Blender's
    own Cycles uses internally for the temperature->RGB conversion.
    """
    t = max(1000.0, min(40000.0, kelvin)) / 100.0

    if t <= 66.0:
        r = 1.0
    else:
        r = (329.698727446 * ((t - 60.0) ** -0.1332047592)) / 255.0

    if t <= 66.0:
        g = (99.4708025861 * math.log(t) - 161.1195681661) / 255.0
    else:
        g = (288.1221695283 * ((t - 60.0) ** -0.0755148492)) / 255.0

    if t >= 66.0:
        b = 1.0
    elif t <= 19.0:
        b = 0.0
    else:
        b = (138.5177312231 * math.log(t - 10.0) - 305.0447927307) / 255.0

    return (max(0.0, min(1.0, r)), max(0.0, min(1.0, g)), max(0.0, min(1.0, b)))


def _resolve_light_color(ldata, use_temp: bool) -> tuple[float, float, float]:
    """Read the effective RGB color of a Blender Light, optionally
    overriding via its Cycles temperature property when the addon's
    color-temperature toggle is on.

    Blender 4.x exposes use_temperature + temperature on the Light
    datablock (cycles-side). We guard with hasattr so older versions
    or builds without Cycles don't crash."""
    if use_temp and hasattr(ldata, "use_temperature") and ldata.use_temperature:
        if hasattr(ldata, "temperature"):
            return _kelvin_to_rgb(float(ldata.temperature))
    return (ldata.color[0], ldata.color[1], ldata.color[2])


class PS1GODOT_OT_vc_bake_scene_lights(bpy.types.Operator):
    """Bake vertex lighting from Blender's scene Light objects, with optional shadow casting."""

    bl_idname = "ps1godot.vc_bake_scene_lights"
    bl_label = "Bake from Scene Lights"
    bl_description = (
        "Walk every visible SUN/POINT/SPOT light in the scene and "
        "accumulate Lambertian diffuse per vertex. Optional bake-time "
        "shadow raycasting (PS1 hardware doesn't do runtime shadows; "
        "this bakes them as darker bytes in the vertex color — Silent "
        "Hill / FFIX / MGS used the exact same technique). AREA lights "
        "are skipped with a warning. Result is clamped to 0.8 for PSX "
        "2x semi-trans headroom."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        s = context.scene.ps1godot
        cast_shadows = bool(s.vc_cast_shadows)
        shadow_bias = float(s.vc_shadow_bias)
        use_temp = bool(s.vc_use_color_temperature)

        # Gather lights. AREA gets a one-time warning; PSX has no
        # analogue and silently dropping it would surprise authors.
        lights = []
        skipped_area = 0
        for o in context.scene.objects:
            if o.type != "LIGHT":
                continue
            if o.hide_get() or o.hide_render:
                continue
            data = o.data
            if data is None:
                continue
            if data.type == "AREA":
                skipped_area += 1
                continue
            if data.type not in {"SUN", "POINT", "SPOT"}:
                continue
            w = o.matrix_world
            pos = w.translation.copy()
            # Blender lights point along -Z by convention. The vector
            # FROM the surface TO the sun is the rotated +Z.
            dir_from = (w.to_3x3() @ Vector((0.0, 0.0, 1.0))).normalized()
            color = _resolve_light_color(data, use_temp)
            lights.append((data, pos, dir_from, color))

        if skipped_area > 0:
            self.report({"WARNING"},
                        f"PS1Godot: skipped {skipped_area} AREA light(s) — "
                        f"PSX has no analogue. Use SUN / POINT / SPOT instead.")

        if not lights:
            self.report({"WARNING"}, "PS1Godot: no SUN/POINT/SPOT lights in the scene — nothing to bake.")
            return {"CANCELLED"}

        # Track bake stats for the final report.
        shadow_rays = 0
        baked = 0
        for obj, mesh in _meshes_in_selection(context):
            layer = _ensure_layer(mesh)
            if layer is None:
                continue

            world_mtx = obj.matrix_world
            world_rot = world_mtx.to_3x3()

            # Pre-compute per-vertex world-space position + normal.
            n_world = [None] * len(mesh.vertices)
            p_world = [None] * len(mesh.vertices)
            for i, v in enumerate(mesh.vertices):
                wn = world_rot @ v.normal
                if wn.length_squared > 1e-12:
                    wn.normalize()
                n_world[i] = wn
                p_world[i] = world_mtx @ v.co

            # Per-vertex aggregation cache — multiple loops on the same
            # vertex compute the same lighting term, so cache once per
            # vertex instead of re-raycasting per loop. Big speedup on
            # corner-domain meshes (3-4 loops per vertex on average).
            vertex_color_cache: dict[int, tuple[float, float, float]] = {}

            for li, loop in enumerate(mesh.loops):
                vi = loop.vertex_index
                cached = vertex_color_cache.get(vi)
                if cached is None:
                    wn = n_world[vi]
                    wp = p_world[vi]
                    if wn is None or wp is None:
                        cached = (0.0, 0.0, 0.0)
                    else:
                        cached = self._compute_vertex_color(
                            wp, wn, lights,
                            cast_shadows=cast_shadows,
                            shadow_bias=shadow_bias,
                            scene=context.scene,
                            shadow_ray_counter=lambda: None,  # placeholder — see below
                        )
                    vertex_color_cache[vi] = cached

                r, g, b = cached
                a = layer.data[li].color[3]
                layer.data[li].color = (r, g, b, a)

            # Shadow ray count: vertex_count × light_count when shadows
            # are on; that's the upper bound (some skip on early-out).
            if cast_shadows:
                shadow_rays += len(vertex_color_cache) * len(lights)
            baked += 1

        msg = (f"PS1Godot: baked {len(lights)} light(s) into {baked} mesh(es) "
               f"(clamped to {PSX_VERTEX_BAKE_CEILING:.1f} for PSX 2x semi-trans headroom).")
        if cast_shadows:
            msg += f" Shadow rays cast: {shadow_rays}."
        if use_temp:
            msg += " Color temperature applied to use_temperature=True lights."
        self.report({"INFO"}, msg)
        return {"FINISHED"}

    def _compute_vertex_color(
        self,
        wp: Vector, wn: Vector,
        lights: list,
        *,
        cast_shadows: bool,
        shadow_bias: float,
        scene,
        shadow_ray_counter,
    ) -> tuple[float, float, float]:
        """Sum every light's contribution at this world-space vertex.

        When `cast_shadows` is on, fires one ray per (vertex, light)
        pair from `wp + wn*shadow_bias` toward the light. If anything
        blocks the ray before it reaches the light's distance (or
        anything at all for SUN), that light's contribution is zero.
        """
        r_acc = g_acc = b_acc = 0.0

        # Scoot the ray origin off the surface so it doesn't immediately
        # self-intersect at the source vertex.
        origin = wp + wn * shadow_bias

        for ldata, lpos, ldir_from, lcolor in lights:
            contrib = self._light_contribution(ldata, lpos, ldir_from, wp, wn)
            if contrib <= 0.0:
                continue

            if cast_shadows:
                if not self._light_visible(scene, origin, ldata, lpos, ldir_from):
                    continue

            energy = ldata.energy
            r_acc += lcolor[0] * energy * contrib
            g_acc += lcolor[1] * energy * contrib
            b_acc += lcolor[2] * energy * contrib

        return (_saturate(r_acc), _saturate(g_acc), _saturate(b_acc))

    @staticmethod
    def _light_visible(scene, origin: Vector, ldata, lpos: Vector, ldir_from: Vector) -> bool:
        """Return True if there's nothing between `origin` and the
        light source. Direction + max distance differ by light type:
          SUN: shoot toward the light direction; any hit = shadowed.
          POINT/SPOT: shoot toward the light position; hit closer than
                      the light = shadowed (hits past the light don't
                      block).
        """
        depsgraph = bpy.context.evaluated_depsgraph_get()
        if ldata.type == "SUN":
            direction = ldir_from
            max_dist = 1.0e6   # effectively infinite
        else:
            to_light = lpos - origin
            max_dist = to_light.length
            if max_dist < 1e-6:
                return True   # light is at the surface — treat as visible
            direction = to_light / max_dist

        try:
            hit, _location, _normal, _index, _obj, _matrix = scene.ray_cast(
                depsgraph, origin, direction, distance=max_dist
            )
        except Exception:
            # ray_cast can throw on degenerate inputs in older Blender
            # builds; fall through as "visible" rather than blackening
            # the whole mesh.
            return True
        return not hit

    @staticmethod
    def _light_contribution(ldata, lpos: Vector, ldir_from: Vector,
                            world_pos: Vector, world_normal: Vector) -> float:
        """Compute the unitless intensity multiplier this single light
        contributes to a vertex. Caller multiplies by light.color * energy.

        SUN: pure dot(normal, direction-from-light), clamped to [0, 1].
        POINT: dot(normal, to-light) × inverse-square-ish falloff.
        SPOT: POINT × cone-cutoff factor.
        """
        if ldata.type == "SUN":
            return max(0.0, world_normal.dot(ldir_from))

        # POINT / SPOT — direction is from surface to the light.
        to_light = lpos - world_pos
        dist = to_light.length
        if dist < 1e-6:
            return 0.0
        to_light = to_light / dist

        ndotl = world_normal.dot(to_light)
        if ndotl <= 0.0:
            return 0.0

        # Soft inverse-square in Blender's convention: 1 / (1 + d^2/r^2),
        # zero past `cutoff_distance`. Blender's POINT.cutoff_distance
        # defaults to 0 (no cutoff); we treat 0 as "no cutoff."
        radius = max(getattr(ldata, "shadow_soft_size", 0.25), 0.01)
        falloff = 1.0 / (1.0 + (dist * dist) / (radius * radius))

        cutoff = getattr(ldata, "cutoff_distance", 0.0)
        if cutoff > 0.0 and dist > cutoff:
            return 0.0

        intensity = ndotl * falloff

        if ldata.type == "SPOT":
            # cos(spot_angle / 2) at edge; cos(spot_angle / 2 * (1 - blend))
            # at the inner cone. Blender's spot_size is the FULL cone
            # angle in radians.
            cone_dot = -to_light.dot(ldir_from)  # cos(angle from spot axis)
            half_angle = ldata.spot_size * 0.5
            cos_outer = math.cos(half_angle)
            cos_inner = math.cos(half_angle * (1.0 - max(0.0, ldata.spot_blend)))
            if cone_dot <= cos_outer:
                return 0.0
            if cone_dot >= cos_inner:
                spot_factor = 1.0
            else:
                spot_factor = (cone_dot - cos_outer) / max(1e-6, cos_inner - cos_outer)
            intensity *= spot_factor

        return intensity


class PS1GODOT_OT_vc_clear(bpy.types.Operator):
    """Reset selected meshes' vertex colors to white."""

    bl_idname = "ps1godot.vc_clear"
    bl_label = "Clear Vertex Lighting"
    bl_description = (
        "Reset every vertex color in selected meshes to (1, 1, 1, 1). "
        "Use to redo a bake from scratch without piled-up multiplies."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        cleared = 0
        for obj, mesh in _meshes_in_selection(context):
            layer = mesh.color_attributes.get(VERTEX_COLOR_LAYER_NAME)
            if layer is None:
                continue
            for c in layer.data:
                c.color = (1.0, 1.0, 1.0, 1.0)
            cleared += 1
        self.report({"INFO"}, f"PS1Godot: cleared vertex lighting on {cleared} mesh(es).")
        return {"FINISHED"}


register, unregister = bpy.utils.register_classes_factory((
    PS1GODOT_OT_vc_create_layer,
    PS1GODOT_OT_vc_ambient,
    PS1GODOT_OT_vc_bake_directional,
    PS1GODOT_OT_vc_bake_scene_lights,
    PS1GODOT_OT_vc_clear,
))
