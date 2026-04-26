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
    [Export] public PSXBPP BitDepth { get; set; } = PSXBPP.TEX_8BIT;
    [Export] public ColorMode VertexColorMode { get; set; } = ColorMode.FlatColor;
    [Export] public Color FlatColor { get; set; } = new Color(0.5f, 0.5f, 0.5f, 1.0f);

    [ExportGroup("PS1 / Collision")]
    [Export] public CollisionKind Collision { get; set; } = CollisionKind.None;
    [Export(PropertyHint.Range, "0,255,1")]
    public int LayerMask { get; set; } = 0xFF;

    [ExportGroup("PS1 / Scripting")]
    [Export(PropertyHint.File, "*.lua")]
    public string ScriptFile { get; set; } = "";

    // Gameplay tag. 0 = untagged. GameObject.Spawn(tag, pos) scans for an
    // INACTIVE object with this tag to activate — so template instances you
    // want to pool should share a Tag and set StartsInactive = true, and
    // scripts call GameObject.Spawn(MY_TAG, pos) to draw from the pool.
    [Export(PropertyHint.Range, "0,65535,1")]
    public int Tag { get; set; } = 0;

    // When true, the object is exported but starts with flags.isActive = 0.
    // Intended for pre-placed pool instances: author drops N copies of a
    // "bullet" template, sets StartsInactive + matching Tag on each, and
    // scripts activate them via GameObject.Spawn at runtime.
    [Export] public bool StartsInactive { get; set; } = false;

    // Render this mesh's textured tris with PSX hardware semi-trans
    // (alpha-keyed via CLUT[0]=0x0000). Use for decals (blood splatter,
    // graffiti), foliage planes, hair cards, glass overlays. The
    // exporter writes CLUT[0]=0 automatically when the source texture
    // has alpha; authors just set Translucent=true on the mesh node.
    // Untextured tris are unaffected — PS1 hardware can't alpha them
    // without a texture sample.
    [Export] public bool Translucent { get; set; } = false;

    // The PS1 GPU rasterises UVs as 8-bit texel coords within the bound
    // texture page — out-of-range source UVs sample neighbouring atlas
    // data as garbage (no wrap, no clamp). The MeshLinter warns by
    // default. Set TilingUV=true on meshes whose authoring genuinely
    // expects UV repeats (e.g. some Kenney FBX furniture ships with
    // tiled-atlas UVs scaled past [0, 1]) to silence the warning.
    //
    // NOTE: this only mutes the diagnostic. PSX still doesn't wrap, so
    // the rendered sampling is whatever the per-vertex linear UV interp
    // happens to land on — usually visibly chaotic. Real PS1 tiling
    // requires subdividing the mesh at integer UV boundaries; that's
    // not done here. Use the flag knowingly.
    [Export] public bool TilingUV { get; set; } = false;

    [ExportGroup("PS1 / Interactable")]
    // When true, the runtime treats this mesh as an interactable. Pressing
    // InteractButton within InteractionRadiusMeters fires onInteract on
    // this object's attached script. Disabled by default — most meshes
    // are not interactive.
    [Export] public bool Interactable { get; set; } = false;
    [Export(PropertyHint.Range, "0.1,10.0,0.1,suffix:m")]
    public float InteractionRadiusMeters { get; set; } = 1.5f;
    // PSX controller button ids — must match psyqo::AdvancedPad::Button:
    //   0=Select  1=L3       2=R3       3=Start
    //   4=Up      5=Right    6=Down     7=Left
    //   8=L2      9=R2      10=L1      11=R1
    //   12=Triangle 13=Circle 14=Cross 15=Square
    // Runtime reserves Cross (14) for jump and Square (15) for sprint on a
    // digital pad, so those collide if used for interact. Default to
    // Triangle (12) — PS1-era convention for "action/menu" buttons and
    // clear of both reservations. Circle (13) is also free.
    [Export(PropertyHint.Range, "0,15,1")]
    public int InteractButton { get; set; } = 12;
    // Repeatable = fires every time within cooldown. Non-repeatable = once, then locked.
    [Export] public bool InteractionRepeatable { get; set; } = true;
    [Export(PropertyHint.Range, "0,600,1,suffix:frames")]
    public int InteractionCooldownFrames { get; set; } = 30;
    // UI canvas to show as a "Press X to ..." prompt while in range. Empty
    // = no prompt. Wire-up lands when the UI bullet ships; for now the
    // field is stored but the runtime just ignores missing canvases.
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
