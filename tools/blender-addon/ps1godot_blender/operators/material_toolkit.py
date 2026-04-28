# Phase 5 — PS1 Material toolkit.
#
# Two halves:
#
#   create_material
#     One-click spawns a StandardMaterial3D-equivalent (Blender's
#     Principled BSDF default) pre-tagged with sensible PS1 defaults
#     and a fresh material_id. Same skip-the-tag-dance pattern as the
#     collision helpers.
#
#   preview_4bpp / preview_8bpp
#     The showpiece. Median-cut quantize the active material's albedo
#     texture to 16 / 256 colors and write the result back to a new
#     Blender Image. Authors open it in the image editor and see what
#     the PSX hardware will actually output — no more "ship to emulator
#     to find out the dithering looks wrong" round-trips.
#
# PSX texture format reality check:
#   - 4bpp = 16-color CLUT. Conventional layout reserves index 0 for
#     transparent (CLUT[0]=0x0000) so cutout decals can alpha-key
#     against a single palette entry. We honour this by keeping
#     transparent pixels transparent in the preview.
#   - 8bpp = 256-color CLUT. No index reservation by convention; alpha
#     is preserved as-is.
#   - Quantization is RGB-only (alpha is a binary keep-or-drop based
#     on the source mask, not part of the palette distance metric).
#
# Implementation: pure numpy median cut + nearest-color mapping. Numpy
# ships in Blender's bundled Python, so no external deps. ~80 lines of
# vectorized code; ~1 second for a 256×256 source.

import bpy
import numpy as np


# ── Material defaults ────────────────────────────────────────────────


class PS1GODOT_OT_create_material(bpy.types.Operator):
    """Create a Principled BSDF material pre-tagged with PS1 metadata + assign to selected objects."""

    bl_idname = "ps1godot.create_material"
    bl_label = "Create PS1 Material"
    bl_description = (
        "Create a new material with the Principled BSDF default and "
        "PS1Godot metadata pre-tagged (atlas_group=World, "
        "texture_format=Auto, alpha_mode=Opaque, force_no_filter=True). "
        "If objects are selected, assigns the new material to their "
        "first material slot. Author clicks once instead of right-click "
        "→ New Material → set 9 PropertyGroup fields."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        name = self._next_name("PS1_Material")
        mat = bpy.data.materials.new(name=name)
        mat.use_nodes = True
        # Principled BSDF is the default node Blender ships; we don't
        # touch the shader graph beyond renaming so authors can still
        # plug their own preview shader in.
        mat.diffuse_color = (0.8, 0.8, 0.8, 1.0)

        p = mat.ps1godot
        p.material_id     = name
        p.atlas_group     = "World"
        p.texture_format  = "Auto"
        p.alpha_mode      = "Opaque"
        p.force_no_filter = True   # PSX never filters; preview should match
        p.approved_16bpp  = False

        # Assign to selected objects' first slot. Quietly skip non-mesh
        # objects + objects that don't have any material slots yet.
        assigned = 0
        for obj in context.selected_objects:
            if obj.type != "MESH":
                continue
            if not obj.material_slots:
                obj.data.materials.append(mat)
            else:
                obj.material_slots[0].material = mat
            assigned += 1

        msg = f"PS1Godot: created '{name}'"
        if assigned:
            msg += f" + assigned to {assigned} object(s)"
        else:
            msg += " (no selected meshes — author manually via the material panel)"
        self.report({"INFO"}, msg)
        return {"FINISHED"}

    @staticmethod
    def _next_name(prefix: str) -> str:
        n = 1
        while f"{prefix}_{n:02d}" in bpy.data.materials:
            n += 1
        return f"{prefix}_{n:02d}"


# ── PSX-quantization preview ─────────────────────────────────────────


# PSX page max — anything above this would auto-downscale at Godot
# import. We do the same here so authors see what the engine will see.
_PSX_PAGE_MAX = 256

# Alpha threshold for cutout treatment — pixels at or above this are
# considered opaque, the rest are dropped to fully-transparent. Mirrors
# how the existing TextureValidationReport thinks about cutouts.
_CUTOUT_ALPHA_THRESHOLD = 0.5


def _albedo_image_for_active_material(context) -> bpy.types.Image | None:
    """Walk the active material's node tree looking for a TEX_IMAGE
    node whose output feeds the Base Color of the Principled BSDF (or
    any TEX_IMAGE if no Principled is present)."""
    obj = context.active_object
    if obj is None or not obj.material_slots:
        return None
    slot = obj.material_slots[obj.active_material_index] if obj.material_slots else None
    mat = slot.material if slot else None
    if mat is None or not mat.use_nodes or mat.node_tree is None:
        return None

    # Prefer the node feeding Base Color; fall back to first TEX_IMAGE.
    base_color_image = None
    fallback = None
    for node in mat.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image is not None:
            if fallback is None:
                fallback = node.image
            for output in node.outputs:
                for link in output.links:
                    to_node = link.to_node
                    if to_node.type == "BSDF_PRINCIPLED" and link.to_socket.name == "Base Color":
                        base_color_image = node.image
                        break
                if base_color_image is not None:
                    break
        if base_color_image is not None:
            break
    return base_color_image or fallback


def _load_pixels_rgba(img: bpy.types.Image) -> np.ndarray:
    """Read `img.pixels` into an (H, W, 4) float32 numpy array. Blender
    stores bottom-row-first; we leave that orientation alone since we
    write back in the same convention."""
    w, h = img.size[0], img.size[1]
    flat = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(flat)
    return flat.reshape(h, w, 4)


def _downscale(rgba: np.ndarray, max_dim: int) -> np.ndarray:
    """Box-filter downscale to max_dim on each axis (preserving aspect).
    Authors can still preview a 1024×1024 texture; we show what the
    256×256 it'll be auto-downscaled to looks like."""
    h, w = rgba.shape[:2]
    if w <= max_dim and h <= max_dim:
        return rgba
    scale = max_dim / max(w, h)
    new_w = max(1, int(w * scale))
    new_h = max(1, int(h * scale))
    # Naive nearest-neighbor — Blender's importer's auto-downscale is
    # also nearest-neighbor (texel-precise sampling on PSX), so this
    # matches what gets shipped.
    ys = (np.arange(new_h) * (h / new_h)).astype(int)
    xs = (np.arange(new_w) * (w / new_w)).astype(int)
    return rgba[ys[:, None], xs[None, :], :]


def _median_cut(pixels: np.ndarray, num_colors: int) -> np.ndarray:
    """Median-cut palette generation. `pixels` is (N, 3) float32 in
    [0, 1]. Returns (≤num_colors, 3) palette in [0, 1].

    Buckets are split repeatedly along their longest channel, at the
    median. Each final bucket's mean becomes one palette entry.
    """
    if len(pixels) == 0:
        return np.zeros((0, 3), dtype=np.float32)

    buckets: list[np.ndarray] = [pixels]
    while len(buckets) < num_colors:
        # Find the bucket with the largest single-channel range.
        best_idx = -1
        best_range = -1.0
        for i, b in enumerate(buckets):
            if len(b) < 2:
                continue
            r = b.max(axis=0) - b.min(axis=0)
            mr = float(r.max())
            if mr > best_range:
                best_range = mr
                best_idx = i
        if best_idx < 0:
            break  # every bucket is a single pixel — no more splits available

        b = buckets.pop(best_idx)
        ch = int((b.max(axis=0) - b.min(axis=0)).argmax())
        sorted_b = b[b[:, ch].argsort()]
        mid = len(sorted_b) // 2
        buckets.append(sorted_b[:mid])
        buckets.append(sorted_b[mid:])

    return np.array([b.mean(axis=0) for b in buckets], dtype=np.float32)


def _map_to_palette(pixels: np.ndarray, palette: np.ndarray) -> np.ndarray:
    """For each row in `pixels` (N, 3), return the index of the nearest
    row in `palette` (K, 3) by squared Euclidean distance."""
    if len(pixels) == 0 or len(palette) == 0:
        return np.zeros(len(pixels), dtype=np.int32)
    # (N, 1, 3) - (1, K, 3) → (N, K, 3); square + sum → (N, K)
    diff = pixels[:, None, :] - palette[None, :, :]
    dist = (diff * diff).sum(axis=-1)
    return dist.argmin(axis=-1).astype(np.int32)


def _quantize(rgba: np.ndarray, num_colors: int, reserve_transparent: bool) -> np.ndarray:
    """Median-cut quantize an (H, W, 4) RGBA image. RGB only —
    transparent pixels are passed through as (0, 0, 0, 0). Returns the
    same shape, float32."""
    h, w = rgba.shape[:2]
    flat = rgba.reshape(-1, 4)
    rgb = flat[:, :3]
    a = flat[:, 3]

    # Treat alpha as cutout — opaque pixels feed the palette, transparent
    # pixels are passed through as (0,0,0,0). For 4bpp, reserving a slot
    # for transparent matches the conventional CLUT[0]=0x0000 layout.
    opaque_mask = a >= _CUTOUT_ALPHA_THRESHOLD
    has_alpha = bool((~opaque_mask).any())

    palette_size = num_colors
    if reserve_transparent and has_alpha:
        palette_size = max(1, num_colors - 1)

    opaque_rgb = rgb[opaque_mask]
    palette = _median_cut(opaque_rgb, palette_size)

    out = np.zeros_like(flat)
    if len(palette) > 0:
        idx = _map_to_palette(opaque_rgb, palette)
        out[opaque_mask, :3] = palette[idx]
        out[opaque_mask, 3] = 1.0
    return out.reshape(h, w, 4)


def _write_preview_image(name: str, rgba: np.ndarray) -> bpy.types.Image:
    """Create-or-reuse a Blender Image of the given name and stamp
    `rgba` into its pixel buffer. Returns the Image."""
    h, w = rgba.shape[:2]
    img = bpy.data.images.get(name)
    if img is not None:
        # Reuse the existing image so re-running the preview replaces
        # in-place instead of piling _001 / _002 variants in the data.
        img.scale(w, h)
    else:
        img = bpy.data.images.new(name=name, width=w, height=h, alpha=True)
        img.colorspace_settings.name = "sRGB"
    img.pixels.foreach_set(rgba.flatten())
    img.update()
    return img


def _open_in_image_editor(img: bpy.types.Image, context) -> None:
    """Find any IMAGE_EDITOR area + bind `img` to it. Quiet no-op when
    no image editor is open (most authoring layouts have one; some
    don't)."""
    for window in context.window_manager.windows:
        for area in window.screen.areas:
            if area.type == "IMAGE_EDITOR":
                for space in area.spaces:
                    if space.type == "IMAGE_EDITOR":
                        space.image = img
                        return


def _do_preview(context, num_colors: int, suffix: str, reserve_transparent: bool) -> tuple[bpy.types.Image | None, str]:
    """Shared preview implementation. Returns (image, error_msg)."""
    img = _albedo_image_for_active_material(context)
    if img is None:
        return None, "no albedo texture on the active material"
    if img.size[0] == 0 or img.size[1] == 0:
        return None, f"image '{img.name}' has zero size"

    rgba = _load_pixels_rgba(img)
    rgba = _downscale(rgba, _PSX_PAGE_MAX)
    quantized = _quantize(rgba, num_colors, reserve_transparent)

    base = img.name.rsplit(".", 1)[0] if "." in img.name else img.name
    preview_name = f"{base}_{suffix}"
    out = _write_preview_image(preview_name, quantized)
    _open_in_image_editor(out, context)
    return out, ""


class PS1GODOT_OT_preview_4bpp(bpy.types.Operator):
    """Quantize the active material's texture to 16 colors (4bpp) and show in the image editor."""

    bl_idname = "ps1godot.preview_4bpp"
    bl_label = "Preview as 4bpp"
    bl_description = (
        "Median-cut quantize the active material's albedo texture to "
        "15 + 1-transparent colors (PSX 4bpp CLUT layout). Result is "
        "auto-downscaled to 256x256 first to match Godot's import "
        "auto-downscale. New image opens in any visible image editor."
    )
    bl_options = {"REGISTER"}

    def execute(self, context):
        out, err = _do_preview(context, num_colors=16, suffix="psx4bpp", reserve_transparent=True)
        if out is None:
            self.report({"ERROR"}, f"PS1Godot: {err}")
            return {"CANCELLED"}
        self.report({"INFO"}, f"PS1Godot: 4bpp preview '{out.name}' ({out.size[0]}x{out.size[1]}).")
        return {"FINISHED"}


class PS1GODOT_OT_preview_8bpp(bpy.types.Operator):
    """Quantize the active material's texture to 256 colors (8bpp) and show in the image editor."""

    bl_idname = "ps1godot.preview_8bpp"
    bl_label = "Preview as 8bpp"
    bl_description = (
        "Median-cut quantize the active material's albedo texture to "
        "256 colors (PSX 8bpp CLUT). Result is auto-downscaled to "
        "256x256 first. Use to spot banding before shipping; if the "
        "preview looks identical to the source, downgrade to 4bpp for "
        "75 percent VRAM savings."
    )
    bl_options = {"REGISTER"}

    def execute(self, context):
        # 8bpp doesn't reserve a transparent index — the CLUT layout
        # carries alpha per-entry, not at index 0.
        out, err = _do_preview(context, num_colors=256, suffix="psx8bpp", reserve_transparent=False)
        if out is None:
            self.report({"ERROR"}, f"PS1Godot: {err}")
            return {"CANCELLED"}
        self.report({"INFO"}, f"PS1Godot: 8bpp preview '{out.name}' ({out.size[0]}x{out.size[1]}).")
        return {"FINISHED"}


register, unregister = bpy.utils.register_classes_factory((
    PS1GODOT_OT_create_material,
    PS1GODOT_OT_preview_4bpp,
    PS1GODOT_OT_preview_8bpp,
))
