# PS1 scene validator — Phase 1 operator. Walks every mesh object in
# the active scene, evaluates a small fixed ruleset, and reports the
# results via the operator info area.
#
# The ruleset here intentionally overlaps with Godot-side validators
# (TextureValidationReport / AnimationLinter / MeshLinter): catching the
# issue in Blender means it never travels into the Godot pipeline.
#
# Phase 2+ will fan this out into category-specific operators
# (validate_meshes / validate_materials / validate_collision) and emit
# a report markdown file alongside the JSON sidecars.

import bpy


_PSX_PAGE_MAX = 256


class PS1GODOT_OT_validate_scene(bpy.types.Operator):
    """Validate the active scene against PS1 authoring rules and report findings."""

    bl_idname = "ps1godot.validate_scene"
    bl_label = "Validate Scene"
    bl_description = "Walk all PS1Godot-tagged objects and report rule violations to the info area."
    bl_options = {"REGISTER"}

    def execute(self, context):
        warnings = []
        errors = []
        checked = 0

        seen_mesh_ids: dict[str, str] = {}
        seen_asset_ids: dict[str, str] = {}

        for obj in context.scene.objects:
            if obj.type != "MESH":
                continue
            if obj.hide_render:
                continue
            checked += 1
            self._validate_object(obj, warnings, errors, seen_mesh_ids, seen_asset_ids)

            for slot in obj.material_slots:
                mat = slot.material
                if mat is None:
                    continue
                self._validate_material(obj, mat, warnings, errors)

        if not checked:
            self.report({"INFO"}, "PS1Godot: no mesh objects to validate.")
            return {"FINISHED"}

        # Batching candidate hint (Slot D1 mirror) — reports which
        # meshes would collapse into single GameObjects on the Godot
        # side. Authors plan around the optimizer at author time
        # instead of being surprised by it post-export.
        batch_hints = self._compute_batch_hints(context.scene)
        for hint in batch_hints:
            print(f"[PS1Godot HINT] {hint}")
            # INFO severity — these aren't problems, just optimization
            # opportunities. Don't pollute the WARN stream.
            self.report({"INFO"}, hint)

        # Mirror outputs to Blender's info area + console.
        for w in warnings:
            print(f"[PS1Godot WARN] {w}")
            self.report({"WARNING"}, w)
        for e in errors:
            print(f"[PS1Godot ERROR] {e}")
            self.report({"ERROR"}, e)

        if not warnings and not errors:
            self.report({"INFO"}, f"PS1Godot: {checked} object(s) validated, no issues.")
        else:
            self.report(
                {"INFO"},
                f"PS1Godot: {checked} object(s) validated — "
                f"{len(warnings)} warning(s), {len(errors)} error(s).",
            )
        return {"FINISHED"}

    # ── Batching candidate hint (Slot D1 mirror) ────────────────────
    #
    # Bucket eligible static meshes the same way Godot's
    # StaticBatchOptimizer.cs does, then report each multi-member
    # bucket. Authors who tag 30 walls all `MeshRole=StaticWorld +
    # ExportMode=MergeStatic` see "30 → 1 batch (29 GameObjects saved
    # at export)" before they ship — encourages consistent
    # texture_page_id and metadata so the optimizer can actually do
    # its job.

    def _compute_batch_hints(self, scene: bpy.types.Scene) -> list[str]:
        buckets: dict[tuple[str, str, str, str, str, str], list[str]] = {}
        for obj in scene.objects:
            if obj.type != "MESH":
                continue
            if obj.hide_render:
                continue
            if not self._is_batch_eligible(obj):
                continue
            key = self._bucket_key(obj)
            buckets.setdefault(key, []).append(obj.name)

        hints: list[str] = []
        total_saved = 0
        for key, members in buckets.items():
            if len(members) < 2:
                continue
            saved = len(members) - 1
            total_saved += saved
            phase, shading, alpha, atlas, _t_or_o, tpage = key
            tpage_label = tpage if tpage else "(no texture_page_id)"
            preview = ", ".join(members[:3])
            if len(members) > 3:
                preview += f", +{len(members) - 3} more"
            hints.append(
                f"Batch hint: {len(members)} meshes share bucket "
                f"({phase} / {shading} / {alpha} / {atlas} / {tpage_label}) — "
                f"will collapse to 1 GameObject on export, saving {saved}. "
                f"Members: {preview}."
            )
        if total_saved > 0:
            hints.append(
                f"Static-batch summary: {total_saved} GameObject(s) total would be saved at export. "
                f"Set consistent texture_page_id on related meshes to maximize bucketing."
            )
        return hints

    @staticmethod
    def _is_batch_eligible(obj: bpy.types.Object) -> bool:
        """Mirror of Godot StaticBatchOptimizer.IsBatchEligible.

        Only flags author signals visible from Blender — we can't see
        Lua attachments / Tag / Interactable / StartsInactive from the
        Blender side, so we'll over-report (some hinted meshes still
        won't batch on Godot if those fields are non-default). That's
        OK — the hint is a best-case estimate."""
        p = obj.ps1godot
        if p.mesh_role != "StaticWorld":
            return False
        if p.export_mode != "MergeStatic":
            return False
        return True

    @staticmethod
    def _bucket_key(obj: bpy.types.Object) -> tuple[str, str, str, str, str, str]:
        """Mirror of Godot StaticBatchOptimizer.MakeBucketKey.

        First-material's texture_page_id + atlas_group win — the Blender
        side keeps both on Material PropertyGroups (the Godot side has
        atlas_group on the mesh as a fallback). The atlas packer
        keeps materials with the same texture_page_id on one VRAM page,
        so consistent first-slot tagging produces consistent buckets."""
        p = obj.ps1godot

        texture_page_id = ""
        atlas_group = "World"   # falls through when no material assigned
        for slot in obj.material_slots:
            if slot.material is None:
                continue
            mp = slot.material.ps1godot
            if mp.texture_page_id:
                texture_page_id = mp.texture_page_id
            if mp.atlas_group:
                atlas_group = mp.atlas_group
            break

        # Translucent flag isn't a Blender PropertyGroup field today —
        # the Godot side's smart-defaults infer it from alpha_mode ==
        # Cutout. Empty placeholder keeps the key shape matched to the
        # Godot side; in practice alpha_mode already distinguishes
        # opaque from cutout meshes.
        translucent_marker = ""
        return (
            p.draw_phase,
            p.shading_mode,
            p.alpha_mode,
            atlas_group,
            translucent_marker,
            texture_page_id,
        )

    # ── Per-object rules ────────────────────────────────────────────

    def _validate_object(
        self,
        obj: bpy.types.Object,
        warnings: list[str],
        errors: list[str],
        seen_mesh_ids: dict[str, str],
        seen_asset_ids: dict[str, str],
    ):
        props = obj.ps1godot

        # Editor-only / Ignore objects bypass validation entirely so
        # gizmo scaffolds don't trigger "no UVs!" noise.
        if props.mesh_role == "EditorOnly" or props.export_mode == "Ignore":
            return

        # Stable-ID uniqueness. Empty IDs auto-derive at export time
        # (Phase 2) so they're not flagged here.
        mesh_id = props.mesh_id or obj.name
        if mesh_id in seen_mesh_ids and seen_mesh_ids[mesh_id] != obj.name:
            errors.append(
                f"Duplicate mesh_id '{mesh_id}' on '{obj.name}' (also on '{seen_mesh_ids[mesh_id]}')."
            )
        else:
            seen_mesh_ids[mesh_id] = obj.name

        if props.asset_id:
            if props.asset_id in seen_asset_ids and seen_asset_ids[props.asset_id] != obj.name:
                errors.append(
                    f"Duplicate asset_id '{props.asset_id}' on '{obj.name}' "
                    f"(also on '{seen_asset_ids[props.asset_id]}')."
                )
            else:
                seen_asset_ids[props.asset_id] = obj.name

        mesh = obj.data
        if mesh is None or not isinstance(mesh, bpy.types.Mesh):
            return

        # CollisionOnly is exempt from render-side checks (no UVs / no
        # vertex colors). Surface its specific shape requirements only.
        if props.mesh_role == "CollisionOnly":
            if len(mesh.polygons) > 64:
                warnings.append(
                    f"'{obj.name}' is CollisionOnly with {len(mesh.polygons)} faces — "
                    f"PSX collision wants simplified hulls."
                )
            if not props.collision_layer:
                warnings.append(
                    f"'{obj.name}' is CollisionOnly but collision_layer is empty — "
                    f"set Player / Camera / Trigger / Interaction (or use the "
                    f"PS1 Collision Helpers panel which sets it automatically)."
                )
            # Collision helpers carry materials by accident sometimes
            # (Blender clones the active material onto new mesh objects).
            # The runtime ignores them but the GLB carries unused slots.
            if any(s.material is not None for s in obj.material_slots):
                warnings.append(
                    f"'{obj.name}' is CollisionOnly with material slots assigned — "
                    f"clear slots so the GLB doesn't carry unused materials."
                )
            return

        # UV layer presence. Untextured (vertex-color-only) meshes are
        # legitimately UV-less, so don't warn for ShadingMode==FlatColor /
        # VertexColor without a material.
        has_uv = bool(mesh.uv_layers)
        material_count = len([s for s in obj.material_slots if s.material])
        if material_count > 0 and not has_uv:
            warnings.append(f"'{obj.name}' has materials but no UV layer.")

        # UV out-of-range. PSX rasteriser doesn't wrap or clamp; UVs
        # outside [0,1] sample whatever atlas neighbour happens to sit
        # there. Same rule as godot-side MeshLinter.
        if has_uv:
            uv_layer = mesh.uv_layers.active
            far_oob = 0
            for uv in uv_layer.data:
                u, v = uv.uv
                if u < -0.5 or u > 1.5 or v < -0.5 or v > 1.5:
                    far_oob += 1
            if far_oob:
                warnings.append(
                    f"'{obj.name}' has {far_oob} UV(s) far outside [0,1] — "
                    f"PSX won't wrap; will sample neighbouring atlas data."
                )

        # Vertex-color expectation vs reality.
        wants_vc = props.shading_mode in {"VertexColor", "BakedLighting"}
        has_vc = bool(mesh.color_attributes) or bool(mesh.vertex_colors)
        if wants_vc and not has_vc:
            warnings.append(
                f"'{obj.name}' shading_mode={props.shading_mode} but no vertex color layer."
            )

        # Material count. PS1 prefers atlases — multiple materials on
        # one mesh translate into multiple draw calls + tpage flips.
        if material_count > 4:
            warnings.append(
                f"'{obj.name}' has {material_count} materials — atlas cleanup recommended."
            )

        # Static role with no animation/script/collision flags is the
        # MergeStatic candidate. Mirror image of A4 in the asset
        # pipeline plan; reporting only.
        if props.mesh_role == "DynamicRigid" and props.export_mode == "MergeStatic":
            warnings.append(
                f"'{obj.name}' is DynamicRigid with ExportMode=MergeStatic — "
                f"merge will freeze it in place; switch to KeepSeparate."
            )

        # Triangle count sanity. PSX practical per-mesh ceiling depends
        # on draw budget but anything over 1000 is a red flag for
        # static world geometry that should split / LOD.
        if len(mesh.polygons) > 1000:
            warnings.append(
                f"'{obj.name}' has {len(mesh.polygons)} faces — "
                f"split or LOD; PSX render budget is tight."
            )

    # ── Per-material rules ──────────────────────────────────────────

    def _validate_material(
        self,
        obj: bpy.types.Object,
        mat: bpy.types.Material,
        warnings: list[str],
        errors: list[str],
    ):
        m = mat.ps1godot

        # Texture page ID is the cross-tool wire that Godot uses to
        # group materials onto VRAM pages. Empty = Godot will pack as
        # standalone, which usually wastes a slot.
        if not m.texture_page_id:
            warnings.append(
                f"Material '{mat.name}' on '{obj.name}' has no texture_page_id — "
                f"will pack standalone; assign to share a page."
            )

        # 16bpp guard — VRAM is precious on PSX. Authors must opt-in
        # explicitly with approved_16bpp to silence this.
        if m.texture_format == "16bpp" and not m.approved_16bpp:
            warnings.append(
                f"Material '{mat.name}' on '{obj.name}' is 16bpp without approved_16bpp — "
                f"reserve 16bpp for cutscene/menu only."
            )

        # Texture-vs-page sanity. Blender exposes the bound image; if
        # it's bigger than 256 in either dim we know PS1Godot will
        # auto-downscale at export, which is silent VRAM waste.
        for node in (mat.node_tree.nodes if mat.use_nodes and mat.node_tree else ()):
            if node.type == "TEX_IMAGE" and node.image is not None:
                w, h = node.image.size
                if w > _PSX_PAGE_MAX or h > _PSX_PAGE_MAX:
                    warnings.append(
                        f"Material '{mat.name}' texture '{node.image.name}' is {w}×{h} — "
                        f"author at ≤{_PSX_PAGE_MAX}×{_PSX_PAGE_MAX} (PSX page max)."
                    )


register, unregister = bpy.utils.register_classes_factory((PS1GODOT_OT_validate_scene,))
