using Godot;
using PS1Godot.Exporter;

namespace PS1Godot;

// Wraps a subtree of MeshInstance3D nodes (typically the pieces of an
// imported FBX) and exports them as a SINGLE PSX GameObject.
//
// Why: PS1AnimationTrack.Rotation targets one object by name and rotates
// it around its own origin. FBX importers decompose a rigged mesh into
// multiple MeshInstance3D children (body / legs / eyes / …) — each would
// normally export as its own GameObject, so a single rotation track can't
// spin the whole thing as a unit. Dropping a PS1MeshGroup above the FBX
// instance tells the exporter "treat everything under this as one object."
//
// Authoring:
//   PS1MeshGroup (ObjectName=HeadSpider)
//   └── HeadSpider.fbx (instanced PackedScene)
//       ├── Bug
//       ├── FrontLegs
//       ├── Head
//       ├── LeftEye
//       └── RightEye
//
// At export, SceneCollector walks every descendant MeshInstance3D, bakes
// each one's local-to-group transform into its triangle verts, concatenates
// them into one PSXMesh (per-surface texture indices preserved so body +
// eyes still use different atlas regions), and emits one GameObject.
//
// Non-goals: skinned meshes aren't supported (PS1SkinnedMesh already
// handles the bone-driven case). A PS1Player ancestor short-circuits this
// path — skinned avatars stay in their own pipeline.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_mesh_group.svg")]
public partial class PS1MeshGroup : Node3D
{
    public enum CollisionKind
    {
        None,
        Static,
        Dynamic,
    }

    [ExportGroup("PS1 / Look")]
    // Applied to every descendant mesh's textures. Authors typically leave
    // this at 8bpp (256-color CLUT) — same default as PS1MeshInstance.
    [Export] public PSXBPP BitDepth { get; set; } = PSXBPP.TEX_8BIT;

    // Per-vertex tint multiplied into the texture sample at runtime. White
    // = no tint, which is what you want for a textured FBX. Dim it for a
    // shadowed "night mode" look without editing textures.
    [Export] public Color FlatColor { get; set; } = new Color(1f, 1f, 1f, 1f);

    [ExportGroup("PS1 / Collision")]
    [Export] public CollisionKind Collision { get; set; } = CollisionKind.None;
    [Export(PropertyHint.Range, "0,255,1")]
    public int LayerMask { get; set; } = 0xFF;

    [ExportGroup("PS1 / Scripting")]
    // Lua script attached to the exported GameObject. onCreate / onUpdate /
    // etc. fire exactly as they would on a PS1MeshInstance.
    [Export(PropertyHint.File, "*.lua")]
    public string ScriptFile { get; set; } = "";

    // Gameplay tag — see PS1MeshInstance.Tag. Used by GameObject.Spawn for
    // pool lookups.
    [Export(PropertyHint.Range, "0,65535,1")]
    public int Tag { get; set; } = 0;

    // Export the group but start with flags.isActive = 0 — see
    // PS1MeshInstance.StartsInactive.
    [Export] public bool StartsInactive { get; set; } = false;

    // PSX hardware semi-trans + alpha-keyed CLUT[0]=0x0000 — see
    // PS1MeshInstance.Translucent.
    [Export] public bool Translucent { get; set; } = false;

    // Mute the UV out-of-range linter — see PS1MeshInstance.TilingUV.
    // PSX still doesn't wrap; flag is for authoring intent only. Apply
    // when the underlying FBX ships with UVs scaled past [0, 1] AND
    // chaotic rasterisation is acceptable (e.g. small props where the
    // mistexture isn't visible at PSX resolution).
    [Export] public bool TilingUV { get; set; } = false;

    // ── Slot C metadata (round-trip with Blender add-on) ────────────
    // Same vocabulary as PS1MeshInstance — see that node + exporter/
    // PS1Metadata.cs for the wire-identifier-stability contract.
    [ExportGroup("PS1 / Metadata")]
    [Export] public MeshRole MeshRole { get; set; } = MeshRole.StaticWorld;
    [Export] public ExportMode ExportMode { get; set; } = ExportMode.MergeStatic;
    [Export] public DrawPhase DrawPhase { get; set; } = DrawPhase.OpaqueStatic;
    [Export] public ShadingMode ShadingMode { get; set; } = ShadingMode.FlatColor;
    [Export] public AlphaMode AlphaMode { get; set; } = AlphaMode.Opaque;
    [Export] public AtlasGroup AtlasGroup { get; set; } = AtlasGroup.World;
    [Export] public Residency Residency { get; set; } = Residency.Scene;

    [Export] public string AssetId { get; set; } = "";
    [Export] public string MeshId { get; set; } = "";
    [Export] public string ChunkId { get; set; } = "";
    [Export] public string RegionId { get; set; } = "";
    [Export] public string AreaArchiveId { get; set; } = "";

    [ExportGroup("PS1 / Naming")]
    // Exported GameObject name. Empty → falls back to the node's own Name,
    // which is what most scenes want. Set explicitly only when you need
    // Lua / animation tracks to reference a stable name independent of
    // the scene-tree label.
    [Export] public string ObjectName { get; set; } = "";
}
