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

    [ExportGroup("PS1 / Interactable")]
    // When true, the runtime treats this mesh as an interactable. Pressing
    // InteractButton within InteractionRadiusMeters fires onInteract on
    // this object's attached script. Disabled by default — most meshes
    // are not interactive.
    [Export] public bool Interactable { get; set; } = false;
    [Export(PropertyHint.Range, "0.1,10.0,0.1")]
    public float InteractionRadiusMeters { get; set; } = 1.5f;
    // PSX controller button ids — must match psyqo::AdvancedPad::Button:
    //   0=Select  1=L3       2=R3       3=Start
    //   4=Up      5=Right    6=Down     7=Left
    //   8=L2      9=R2      10=L1      11=R1
    //   12=Triangle 13=Circle 14=Cross 15=Square
    // Cross (14) is the conventional "interact" button; default to that.
    [Export(PropertyHint.Range, "0,15,1")]
    public int InteractButton { get; set; } = 14;
    // Repeatable = fires every time within cooldown. Non-repeatable = once, then locked.
    [Export] public bool InteractionRepeatable { get; set; } = true;
    [Export(PropertyHint.Range, "0,600,1")]
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
        // Pad the cull volume so Godot keeps drawing while the snap fudges
        // pixels. 2 world units is overkill for a 320×240 framebuffer but cheap.
        if (ExtraCullMargin < 2.0f) ExtraCullMargin = 2.0f;
    }
}
