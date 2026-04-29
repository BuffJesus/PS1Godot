using Godot;
using PS1Godot.Exporter;

namespace PS1Godot;

// A MeshInstance3D that auto-applies the PS1 shader and carries export-time
// metadata: how to colorize at export, what bit-depth to quantize textures to,
// collision kind, and an optional behavior script.
//
// Defaults are chosen so a fresh node "just works" without configuration:
//   - 8bpp textures (256-color CLUT, the conventional middle ground)
//   - Baked vertex lighting (uses whatever Godot has computed)
//   - No collision (opt in when you actually want it)
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_mesh_instance.svg")]
public partial class PS1MeshInstance : MeshInstance3D
{
    public enum CollisionKind
    {
        None,
        Static,   // Participates in BVH/world collision
        Dynamic,  // Per-object AABB collider
    }

    public enum ColorMode
    {
        FlatColor,        // Every vertex gets FlatColor (default — works today)
        BakedLighting,    // Walk the scene's Light nodes, shade per-vertex (Phase 2.5)
        MeshVertexColors, // Use the mesh's COLOR channel if present (Phase 2.5)
    }

    [ExportGroup("PS1 / Look")]
    /// <summary>
    /// Texture bit depth at export. 4bpp = 16-color CLUT (best VRAM, harshest
    /// quantize), 8bpp = 256-color CLUT (default; safe middle ground), 16bpp =
    /// direct color (no palette but eats 2× VRAM). Pick 4bpp for decals/sprites,
    /// 8bpp for world geometry, 16bpp only for skies / cinematics.
    /// </summary>
    [Export] public PSXBPP BitDepth { get; set; } = PSXBPP.TEX_8BIT;
    /// <summary>
    /// How vertex colors get filled. FlatColor = every vertex gets the same
    /// FlatColor field (default — works without baking). MeshVertexColors =
    /// use the mesh's COLOR attribute. BakedLighting = walk scene lights.
    /// </summary>
    [Export] public ColorMode VertexColorMode { get; set; } = ColorMode.FlatColor;
    /// <summary>
    /// Color stamped on every vertex when VertexColorMode = FlatColor. Default
    /// 0.5 gray = PSYQo neutral (runtime 2× modulate yields texture untinted).
    /// </summary>
    [Export] public Color FlatColor { get; set; } = new Color(0.5f, 0.5f, 0.5f, 1.0f);

    [ExportGroup("PS1 / Collision")]
    /// <summary>
    /// None = no collision (decals, fog cards). Static = participates in BVH /
    /// world push-back (floors, walls). Dynamic = per-object AABB collider
    /// (moving platforms, doors).
    /// </summary>
    [Export] public CollisionKind Collision { get; set; } = CollisionKind.None;
    /// <summary>
    /// Bitmask deciding which layers this collider participates in. Default
    /// 0xFF = collides with everything. Use bits to separate player vs.
    /// camera vs. trigger collision channels.
    /// </summary>
    [Export(PropertyHint.Range, "0,255,1")]
    public int LayerMask { get; set; } = 0xFF;

    [ExportGroup("PS1 / Scripting")]
    /// <summary>
    /// Optional Lua script attached to this object. Runtime calls
    /// onCreate/onUpdate/onCollideWithPlayer/onInteract on it as events fire.
    /// Empty = no script. See lua/templates/ for starter snippets.
    /// </summary>
    [Export(PropertyHint.File, "*.lua")]
    public string ScriptFile { get; set; } = "";

    /// <summary>
    /// Gameplay tag (0 = untagged). GameObject.Spawn(tag, pos) scans for an
    /// inactive object with this tag and activates it. Pool template instances
    /// share a Tag + set StartsInactive=true; scripts call Spawn to draw from
    /// the pool.
    /// </summary>
    [Export(PropertyHint.Range, "0,65535,1")]
    public int Tag { get; set; } = 0;

    /// <summary>
    /// When true, the object exports but boots inactive. Pair with a non-zero
    /// Tag and let GameObject.Spawn activate it at runtime. Used for pre-placed
    /// pools (bullets, particles, drop items).
    /// </summary>
    [Export] public bool StartsInactive { get; set; } = false;

    /// <summary>
    /// Render textured tris with PSX hardware semi-trans (alpha-keyed via
    /// CLUT[0]=0). Use for decals, foliage planes, hair cards, glass. Exporter
    /// writes CLUT[0]=0 automatically when the source PNG has alpha.
    /// Untextured tris ignore this flag.
    /// </summary>
    [Export] public bool Translucent { get; set; } = false;

    /// <summary>
    /// Silence the MeshLinter's "UV out of [0,1]" warning. PSX hardware does
    /// NOT wrap or clamp — out-of-range UVs sample garbage from neighbouring
    /// VRAM regardless of this flag. Set true ONLY on meshes whose authoring
    /// genuinely expects UV repeats AND you've subdivided at integer UV edges;
    /// otherwise the sampling looks chaotic.
    /// </summary>
    [Export] public bool TilingUV { get; set; } = false;

    // ── Slot C metadata (round-trip with Blender add-on) ────────────
    //
    // These fields mirror tools/blender-addon/.../properties.py per-
    // object enums verbatim and are the contract for the Phase 2 JSON
    // sidecar reader. Defaults are picked so existing scenes don't
    // break: StaticWorld + MergeStatic + OpaqueStatic match what the
    // exporter has been doing implicitly all along. Today these fields
    // are recorded but don't change runtime behavior; Slot D
    // render-group batching + the sidecar reader consume them.
    //
    // Don't rename the enum members — they ARE the wire identifiers
    // (see exporter/PS1Metadata.cs).
    [ExportGroup("PS1 / Metadata")]
    /// <summary>
    /// Authoring role this mesh plays. Drives Slot D batching + chunk
    /// residency decisions. StaticWorld = walls/floors/props that don't
    /// move. DynamicRigid = moving doors/elevators. Skinned = characters.
    /// CollisionOnly = invisible mesh that supplies collision only (e.g.
    /// for fixed-camera scenes — see CollisionOnly in ExportMode below).
    /// </summary>
    [Export] public MeshRole MeshRole { get; set; } = MeshRole.StaticWorld;
    /// <summary>
    /// How this mesh ships. MergeStatic = batched into static draw lists.
    /// KeepSeparate = own GameObject (needed for animation/scripts/Lua).
    /// CollisionOnly = collision only, NO render (for fixed-camera /
    /// pre-rendered backdrop scenes where the room is in the BG image).
    /// Ignore = skip entirely.
    /// </summary>
    [Export] public ExportMode ExportMode { get; set; } = ExportMode.MergeStatic;
    /// <summary>
    /// Render-order bucket. Runtime draws phases in order: OpaqueStatic →
    /// OpaqueDynamic → Characters → CutoutDecals → SemiTrans. Cutout/decal
    /// meshes (foliage, blood splatters) need CutoutDecals; UI overlays
    /// belong in their own UICanvas.
    /// </summary>
    [Export] public DrawPhase DrawPhase { get; set; } = DrawPhase.OpaqueStatic;
    /// <summary>
    /// PSX shading mode. FlatColor = single color per face. VertexColor =
    /// per-vertex Gouraud (use MeshVertexColors / BakedLighting). CelShaded
    /// = stepped ramp (Phase 4 stretch). MeshVertexColors needs the mesh's
    /// COLOR channel populated (Vertex Lighting baker writes it).
    /// </summary>
    [Export] public ShadingMode ShadingMode { get; set; } = ShadingMode.FlatColor;
    /// <summary>
    /// Opaque = no transparency. Cutout = alpha-tested via CLUT[0]=0
    /// (decals, foliage). SemiTransparent = additive 50/50 blend (smoke,
    /// glass). Wired into the texture quantize step so the right CLUT
    /// entries are reserved.
    /// </summary>
    [Export] public AlphaMode AlphaMode { get; set; } = AlphaMode.Opaque;
    /// <summary>
    /// Texture-page grouping hint. World = packed into the world atlas
    /// (default). Characters = own atlas (skin/face textures group). UI =
    /// uses the UI atlas. Different groups can swap independently for
    /// chunk streaming.
    /// </summary>
    [Export] public AtlasGroup AtlasGroup { get; set; } = AtlasGroup.World;
    /// <summary>
    /// When does this mesh's data live in memory. Scene = always loaded
    /// (default). Chunk = loaded with the chunk it belongs to. Prefetch =
    /// loaded ahead of time before chunk transition.
    /// </summary>
    [Export] public Residency Residency { get; set; } = Residency.Scene;

    /// <summary>
    /// Stable string ID for cross-language references (Lua, save data,
    /// Blender sidecars). Auto-populated by the Blender import; empty =
    /// downstream code falls back to node Name.
    /// </summary>
    [Export] public string AssetId { get; set; } = "";
    /// <summary> Stable mesh-resource ID (separate from AssetId so the same
    /// mesh shared across instances stays one ID). </summary>
    [Export] public string MeshId { get; set; } = "";
    /// <summary> Chunk this mesh belongs to. Empty = scene-resident; non-empty
    /// = participates in the chunk-streaming residency tier. </summary>
    [Export] public string ChunkId { get; set; } = "";
    /// <summary> Logical region (room/area) the mesh sits in. Used for
    /// per-region budget rollups + camera-zone matching. </summary>
    [Export] public string RegionId { get; set; } = "";
    /// <summary> Multi-disc archive this mesh ships in. Empty = single-disc
    /// project; non-empty = per-disc residency tracking (Phase 4 multi-
    /// disc). </summary>
    [Export] public string AreaArchiveId { get; set; } = "";

    /// <summary>
    /// Per-material PS1 metadata. One entry per surface that wants
    /// texture-page / CLUT / atlas-group overrides. Match by
    /// PS1MaterialMetadata.MaterialName ⇄ surface Material.ResourceName.
    /// Empty = mesh-level defaults apply to every surface.
    /// </summary>
    [Export] public Godot.Collections.Array<PS1MaterialMetadata> Materials { get; set; } = new();

    /// <summary>
    /// Per-instance vertex-color override. Vertex Lighting + AO bakers
    /// write here. Empty = use the mesh's COLOR channel as-is. Populated =
    /// these colors override at export. Same mesh in two scenes can have
    /// two lighting setups. Survives .glb re-import (lives on the .tscn).
    /// Use "PS1Godot: Bake / Clear BakedColors" to reset.
    /// </summary>
    [Export] public Color[] BakedColors { get; set; } = System.Array.Empty<Color>();

    [ExportGroup("PS1 / Interactable")]
    /// <summary>
    /// When true, runtime treats this mesh as interactable. Player presses
    /// InteractButton within InteractionRadiusMeters → fires onInteract on
    /// the attached ScriptFile. Most meshes are NOT interactive — opt in.
    /// </summary>
    [Export] public bool Interactable { get; set; } = false;
    /// <summary>
    /// Distance in meters at which the interact prompt becomes available.
    /// 1.5 m ≈ arm's reach.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,10.0,0.1,suffix:m")]
    public float InteractionRadiusMeters { get; set; } = 1.5f;
    /// <summary>
    /// PSX controller button id. 12=Triangle (default — clear of jump and
    /// sprint), 13=Circle, 14=Cross (RESERVED for jump on digital pad),
    /// 15=Square (RESERVED for sprint). Other ids are direction/start/
    /// shoulder buttons — see code comment for the full list.
    /// </summary>
    [Export(PropertyHint.Range, "0,15,1")]
    public int InteractButton { get; set; } = 12;
    /// <summary>
    /// True = fires every time the button is pressed within cooldown
    /// (looped dialogue, repeated examines). False = fires once, then
    /// the trigger locks (one-shot pickups, story flags).
    /// </summary>
    [Export] public bool InteractionRepeatable { get; set; } = true;
    /// <summary>
    /// Frames between successive interact firings (60 fps PAL / NTSC). 30
    /// frames ≈ 0.5 s. Set 0 to disable cooldown (every poll fires).
    /// </summary>
    [Export(PropertyHint.Range, "0,600,1,suffix:frames")]
    public int InteractionCooldownFrames { get; set; } = 30;
    /// <summary>
    /// PS1UICanvas name to show as a "Press X to ..." prompt while in
    /// range. Empty = no prompt. Wire-up pending; the field is stored but
    /// runtime ignores missing canvases.
    /// </summary>
    [Export] public string InteractionPromptCanvas { get; set; } = "";

    // _EnterTree runs every time the node enters the scene tree — both at
    // runtime and on every editor scene-open. _Ready can miss hot-reload
    // re-instantiation in [Tool] scripts, so do the lifecycle setup here.
    public override void _EnterTree()
    {
        if (MaterialOverride == null)
        {
            var mat = ResourceLoader.Load<Material>(
                "res://addons/ps1godot/shaders/ps1_default.tres");
            if (mat != null) MaterialOverride = mat;
        }

        // Godot frustum-culls based on the mesh's exact AABB. The PS1 vertex
        // snap moves verts in screen space, which sometimes pushes a triangle's
        // rendered footprint slightly outside the original AABB — Godot then
        // skips drawing the whole mesh once a corner crosses the frustum edge,
        // so the cube vanishes at certain orbit angles.
        //
        // Scale the pad to the mesh size so a 0.1 m prop isn't wrapped in a
        // 4 m halo (which looks like a giant yellow cage around the player
        // when the mesh is a humanoid). 10 % of the largest AABB edge covers
        // the snap's pixel-scale displacement at any reasonable zoom, and the
        // clamp keeps absurdly tiny or absurdly huge meshes sane. Only raise
        // the margin — never lower a hand-set value.
        if (Mesh != null)
        {
            var size = Mesh.GetAabb().Size;
            float maxEdge = Mathf.Max(size.X, Mathf.Max(size.Y, size.Z));
            float dynamicMargin = Mathf.Clamp(maxEdge * 0.1f, 0.1f, 2.0f);
            if (ExtraCullMargin < dynamicMargin) ExtraCullMargin = dynamicMargin;
        }
    }
}
