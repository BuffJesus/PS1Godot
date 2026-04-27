# PS1Godot Blender add-on — custom property definitions.
#
# Two PropertyGroups attach to Blender data:
#   PS1GodotSceneProps   → bpy.types.Scene.ps1godot
#   PS1GodotObjectProps  → bpy.types.Object.ps1godot
#
# The enum values here are the source of truth for the Blender side of
# the round-trip; matching enums live in:
#   godot-ps1/addons/ps1godot/nodes/PS1MeshInstance.cs (PS1Godot side)
#   docs/ps1_asset_pipeline_plan.md § C1–C4 (the shared schema)
#
# Identifier strings (the first item of each enum tuple) are the wire
# format. Don't rename them once a project ships — they end up in the
# JSON sidecars and the .blend file's saved scene state. Display names
# (second item) and descriptions (third) can be edited freely.

import bpy
from bpy.props import (
    StringProperty,
    EnumProperty,
    IntProperty,
    BoolProperty,
    PointerProperty,
)


# ── Enums ────────────────────────────────────────────────────────────
#
# These mirror docs/ps1_asset_pipeline_plan.md § Slot C verbatim. Keep
# the lists in alphabetical-by-display-order so the dropdowns stay
# readable; the wire identifier is what travels through metadata JSON.

MESH_ROLE_ITEMS = [
    ("StaticWorld",   "Static World",   "Bakeable into a static render group; never moves."),
    ("DynamicRigid",  "Dynamic Rigid",  "Whole-object transform animation (doors, props)."),
    ("Skinned",       "Skinned",        "Bone-driven deformable mesh (humanoid, etc.)."),
    ("Segmented",     "Segmented",      "Body parts as separate meshes parented to bones."),
    ("SpriteBillboard","Sprite/Billboard","Atlas-driven 2D imposter (Phase 3 D4)."),
    ("CollisionOnly", "Collision Only", "Not rendered; contributes to collision/nav only."),
    ("EditorOnly",    "Editor Only",    "Skipped at export (gizmos, pivots, layout aids)."),
]

EXPORT_MODE_ITEMS = [
    ("MergeStatic",   "Merge Static",   "Combine into the static render group at export."),
    ("KeepSeparate",  "Keep Separate",  "Force a discrete GameObject (animated / scripted)."),
    ("CollisionOnly", "Collision Only", "Export as collider/nav only; no draw."),
    ("Ignore",        "Ignore",         "Skip entirely (paired with EditorOnly role)."),
]

DRAW_PHASE_ITEMS = [
    ("OpaqueStatic",       "Opaque Static",       "World geometry, drawn first."),
    ("OpaqueDynamic",      "Opaque Dynamic",      "Animated rigids, drawn after static."),
    ("Characters",         "Characters",          "Skinned/segmented; eats the most fillrate."),
    ("CutoutDecals",       "Cutout Decals",       "Alpha-keyed quads (blood, graffiti, webs)."),
    ("TransparentEffects", "Transparent Effects", "Semi-transparent FX; drawn last."),
    ("UI",                 "UI",                  "HUD and overlays; drawn over everything."),
]

SHADING_MODE_ITEMS = [
    ("Unlit",         "Unlit",          "Texture only, no lighting."),
    ("FlatColor",     "Flat Color",     "Per-tri vertex color (PSX-default Gouraud-off)."),
    ("VertexColor",   "Vertex Color",   "Per-vert color (PSX Gouraud); cheapest 'lit' look."),
    ("BakedLighting", "Baked Lighting", "Pre-baked into vertex color via the bake tools."),
]

ALPHA_MODE_ITEMS = [
    ("Opaque",          "Opaque",          "Solid; no alpha test."),
    ("Cutout",          "Cutout",          "1-bit alpha via CLUT[0]=0x0000."),
    ("SemiTransparent", "Semi-Transparent","PSX semi-trans bit (50/50, additive, etc.)."),
    ("Additive",        "Additive",        "Additive blending (sparks, lights)."),
    ("UI",              "UI",              "UI alpha rules (handled by the UI exporter)."),
]

TEXTURE_FORMAT_ITEMS = [
    ("Auto",  "Auto",  "Let PS1Godot pick based on texture content."),
    ("4bpp",  "4bpp",  "16-color CLUT — preferred for decals + small textures."),
    ("8bpp",  "8bpp",  "256-color CLUT — preferred for world atlases."),
    ("16bpp", "16bpp", "Direct color — cutscene/menu only; consumes 4× VRAM."),
]

RESIDENCY_ITEMS = [
    ("Always",    "Always",    "Resident for the whole game (fonts, UI atlas)."),
    ("Scene",     "Scene",     "Loaded with the current scene."),
    ("Chunk",     "Chunk",     "Streamed with the current world chunk."),
    ("OnDemand",  "On Demand", "Pulled in by explicit Lua call."),
    ("Cutscene",  "Cutscene",  "Resident only during a cutscene."),
]


# ── Scene-level metadata ────────────────────────────────────────────

class PS1GodotSceneProps(bpy.types.PropertyGroup):
    """Project-wide settings — where to write sidecars, default IDs, etc."""

    project_root: StringProperty(
        name="Project Root",
        description="Path to the PS1Godot project root (the folder containing godot-ps1/).",
        default="",
        subtype="DIR_PATH",
    )
    output_subdir: StringProperty(
        name="Output Subdir",
        description="Folder under project_root where this addon writes sidecars (Phase 2+).",
        default="ps1godot_assets/blender_sources",
    )
    default_chunk_id: StringProperty(
        name="Default ChunkId",
        description="Fallback ChunkId stamped on objects that don't override.",
        default="",
    )
    default_disc_id: IntProperty(
        name="Default DiscId",
        description="Fallback DiscId stamped on objects that don't override.",
        default=1,
        min=1,
    )
    metadata_version: IntProperty(
        name="Metadata Version",
        description="PS1GodotBlenderMetadataVersion stamped into exported JSON. Bump when changing schema.",
        default=1,
        min=1,
    )


# ── Object-level metadata ───────────────────────────────────────────

class PS1GodotObjectProps(bpy.types.PropertyGroup):
    """Per-object PS1 authoring fields. Empty IDs mean 'inherit from scene defaults'."""

    asset_id: StringProperty(
        name="Asset ID",
        description="Stable cross-tool identifier. Auto-generated on first export if empty.",
        default="",
    )
    mesh_id: StringProperty(
        name="Mesh ID",
        description="Logical mesh name (e.g. 'town_crate_01'). Empty = derive from object name.",
        default="",
    )
    chunk_id: StringProperty(
        name="Chunk ID",
        description="World chunk this object belongs to; blank = scene default.",
        default="",
    )
    region_id: StringProperty(
        name="Region ID",
        description="Larger region (groups chunks); blank = inherit.",
        default="",
    )
    area_archive_id: StringProperty(
        name="Area Archive ID",
        description="Streaming archive (e.g. 'AREA_TOWN_NORTH'); blank = inherit.",
        default="",
    )

    mesh_role: EnumProperty(
        name="Mesh Role",
        description="How PS1Godot should treat this mesh at export.",
        items=MESH_ROLE_ITEMS,
        default="StaticWorld",
    )
    export_mode: EnumProperty(
        name="Export Mode",
        description="Whether to merge into a static group or keep as a separate GameObject.",
        items=EXPORT_MODE_ITEMS,
        default="MergeStatic",
    )
    draw_phase: EnumProperty(
        name="Draw Phase",
        description="Render-order bucket. Picked automatically from mesh_role + alpha_mode if left default.",
        items=DRAW_PHASE_ITEMS,
        default="OpaqueStatic",
    )
    shading_mode: EnumProperty(
        name="Shading Mode",
        description="How vertex/material colors interact with lighting at runtime.",
        items=SHADING_MODE_ITEMS,
        default="FlatColor",
    )
    alpha_mode: EnumProperty(
        name="Alpha Mode",
        description="Alpha-blending policy. Cutout uses CLUT[0]=transparent; SemiTransparent/Additive set the PSX semi-trans bit.",
        items=ALPHA_MODE_ITEMS,
        default="Opaque",
    )

    collision_layer: StringProperty(
        name="Collision Layer",
        description="Optional collision layer name (Phase 6 — collision helpers).",
        default="",
    )

    # Authoring-side helpers — do NOT travel into PS1Godot. The export
    # validators read them; the round-trip importer rewrites them from
    # the .blend file alone.
    note: StringProperty(
        name="Author Note",
        description="Free-form note for the author. Not exported.",
        default="",
    )


# ── Material-level metadata ─────────────────────────────────────────

class PS1GodotMaterialProps(bpy.types.PropertyGroup):
    """Per-material PS1 metadata. Filled in once a material is assigned a texture page / CLUT."""

    material_id: StringProperty(
        name="Material ID",
        description="Stable material identifier; auto-derived from material name if empty.",
        default="",
    )
    texture_page_id: StringProperty(
        name="Texture Page ID",
        description="VRAM page this material's texture sits on.",
        default="",
    )
    clut_id: StringProperty(
        name="CLUT ID",
        description="Palette identifier; multiple materials can share a CLUT.",
        default="",
    )
    palette_group: StringProperty(
        name="Palette Group",
        description="Logical palette family (e.g. 'town_day', 'dungeon_dark').",
        default="",
    )
    atlas_group: StringProperty(
        name="Atlas Group",
        description="Atlas the texture belongs to (World/UI/Character/FX/Decal/Cutscene).",
        default="World",
    )
    texture_format: EnumProperty(
        name="Texture Format",
        description="Override PS1Godot's auto bit-depth selection.",
        items=TEXTURE_FORMAT_ITEMS,
        default="Auto",
    )
    alpha_mode: EnumProperty(
        name="Alpha Mode",
        description="Material's alpha behaviour.",
        items=ALPHA_MODE_ITEMS,
        default="Opaque",
    )
    force_no_filter: BoolProperty(
        name="Force No Filter",
        description="Disable any preview filtering Blender would apply.",
        default=False,
    )
    approved_16bpp: BoolProperty(
        name="Approved 16bpp",
        description="Author has explicitly OK'd 16bpp residency for this material (silences the validator warning).",
        default=False,
    )


# ── Registration ────────────────────────────────────────────────────
#
# We use bpy.utils.register_classes_factory (the canonical Blender
# pattern, see scripts/addons_core/hydra_storm/properties.py) to
# generate matched register() / unregister() functions. The
# PointerProperty attachments to bpy.types.Scene / Object / Material
# happen inside register() / unregister() right after the factory call
# so the typed PointerProperty can resolve the just-registered
# PropertyGroup classes.

_classes = (
    PS1GodotSceneProps,
    PS1GodotObjectProps,
    PS1GodotMaterialProps,
)

_register_classes, _unregister_classes = bpy.utils.register_classes_factory(_classes)


# (host, attribute) pairs the addon installs on Blender data types.
_pointer_targets = (
    (bpy.types.Scene,    "ps1godot", PS1GodotSceneProps),
    (bpy.types.Object,   "ps1godot", PS1GodotObjectProps),
    (bpy.types.Material, "ps1godot", PS1GodotMaterialProps),
)


def register():
    _register_classes()
    for host, attr, prop_type in _pointer_targets:
        setattr(host, attr, PointerProperty(type=prop_type))


def unregister():
    # Detach pointers in reverse so the host types are clean before the
    # PropertyGroup classes themselves go away. Guard each delete: hot-
    # reload during development can leave the pointers in inconsistent
    # shapes; we'd rather silently move on than block re-registration.
    for host, attr, _prop_type in reversed(_pointer_targets):
        if hasattr(host, attr):
            try:
                delattr(host, attr)
            except (AttributeError, RuntimeError):
                pass
    _unregister_classes()
