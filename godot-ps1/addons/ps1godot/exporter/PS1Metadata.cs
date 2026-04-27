namespace PS1Godot.Exporter;

// Slot C metadata enums (asset pipeline plan § C1–C4). The Godot side
// of the round-trip contract with the Blender add-on at
// tools/blender-addon/ps1godot_blender/properties.py.
//
// **The C# enum-member names here are the wire identifiers** — they
// appear verbatim in the Blender JSON sidecars
// (`<mesh_id>.ps1meshmeta.json`) as `"mesh_role": "StaticWorld"` etc.
// Don't rename a member once a project has shipped: every saved
// `.blend` and every JSON sidecar already on disk encodes the old
// spelling. Adding new members is fine (additive); renaming is not.
//
// PascalCase here matches the Blender-side wire strings character-for-
// character; we serialize with `ToString()` and parse back with
// `Enum.TryParse`. If you need a display name different from the wire
// id, use a separate string at the inspector level — don't add a
// pretty-name layer here.
//
// Cross-references:
//   docs/ps1_asset_pipeline_plan.md           — § Slot C policy
//   tools/blender-addon/.../properties.py     — Blender wire spellings
//   docs/ps1godot_blender_addon_integration_plan.md § 4–5 — round-trip rules

// ── How PS1Godot should treat this mesh at export. ────────────────
// Defaults to StaticWorld because that's what most authored meshes
// are — letting the exporter infer this from existing scene shape
// keeps current scenes building without per-node tagging.
public enum MeshRole
{
    StaticWorld,
    DynamicRigid,
    Skinned,
    Segmented,
    SpriteBillboard,
    CollisionOnly,
    EditorOnly,
}

// Whether to merge into a static render group or keep as a separate
// GameObject. MergeStatic is the cheaper path; KeepSeparate is required
// when the mesh has animation, scripts, or is referenced by name from
// Lua. Slot D render-group batching reads this to decide which meshes
// fold into the static draw list.
public enum ExportMode
{
    MergeStatic,
    KeepSeparate,
    CollisionOnly,
    Ignore,
}

// Render-order bucket. The runtime draws the phases in this order, so
// transparent FX always land after opaque world geo regardless of OT
// insertion order. Slot D batching keys partly off DrawPhase so meshes
// in the same phase can share a draw list.
public enum DrawPhase
{
    OpaqueStatic,
    OpaqueDynamic,
    Characters,
    CutoutDecals,
    TransparentEffects,
    UI,
}

// How vertex/material colors interact with lighting at runtime.
// Mirrors PS1MeshInstance.ColorMode but with the wire spelling matching
// Blender. Today's pipeline still consumes the legacy ColorMode field;
// ShadingMode here is the go-forward authoring vocabulary that Slot D
// + the Phase 2 sidecar reader will switch to.
public enum ShadingMode
{
    Unlit,
    FlatColor,
    VertexColor,
    BakedLighting,
}

// Alpha-blending policy. Cutout uses CLUT[0]=0x0000 for the
// alpha-keyed quads pattern (decals, foliage, hair cards).
// SemiTransparent / Additive set the PSX semi-trans bit; the runtime
// honours the four PSX hardware blend modes via PrimSemiTrans flag.
//
// NOTE: legacy `Translucent: bool` on PS1MeshInstance / PS1MeshGroup
// continues to drive runtime behavior for now. AlphaMode is the
// canonical authoring field for Phase 2 round-trip; the SceneCollector
// derives Translucent from AlphaMode at export.
public enum AlphaMode
{
    Opaque,
    Cutout,
    SemiTransparent,
    Additive,
    UI,
}

// Soft hint for the atlas packer (per-material rather than per-mesh
// in the integration plan, but exposed at mesh level here as a
// fallback while PS1Material doesn't exist yet).
public enum AtlasGroup
{
    World,
    UI,
    Character,
    FX,
    Decal,
    Cutscene,
}

// Texture residency policy — when does the GPU keep this asset's
// pixels in VRAM. Drives per-asset budget warnings (e.g. "16bpp
// non-Cutscene asset" → WARN). Phase 2 sidecar reader populates
// these on import; SplashpackWriter doesn't yet branch on them.
public enum Residency
{
    Always,
    Scene,
    Chunk,
    OnDemand,
    Cutscene,
}

// Texture bit-depth override. `Auto` lets PS1Godot pick from texture
// content (alpha → 4bpp cutout, opaque ≤16 colours → 4bpp, otherwise
// 8bpp). Authors set 4bpp/8bpp/16bpp explicitly when they want to
// override. 16bpp must pair with material.Approved16bpp = true to
// silence the validator warning.
public enum TextureFormat
{
    Auto,
    Bpp4,    // Wire identifier: "4bpp" — see TextureFormatNames below
    Bpp8,    // Wire identifier: "8bpp"
    Bpp16,   // Wire identifier: "16bpp"
}

// TextureFormat is the one enum where wire identifiers don't match
// C# enum-member names — leading digits aren't legal in C# identifiers.
// The serializer special-cases these via the maps below.
public static class TextureFormatNames
{
    public const string Auto  = "Auto";
    public const string Bpp4  = "4bpp";
    public const string Bpp8  = "8bpp";
    public const string Bpp16 = "16bpp";

    public static string ToWire(TextureFormat fmt) => fmt switch
    {
        TextureFormat.Bpp4  => Bpp4,
        TextureFormat.Bpp8  => Bpp8,
        TextureFormat.Bpp16 => Bpp16,
        _                   => Auto,
    };

    public static TextureFormat FromWire(string wire) => wire switch
    {
        Bpp4  => TextureFormat.Bpp4,
        Bpp8  => TextureFormat.Bpp8,
        Bpp16 => TextureFormat.Bpp16,
        _     => TextureFormat.Auto,
    };
}
