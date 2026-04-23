using Godot;

namespace PS1Godot;

// A 3D model rendered in a screen-space rect on top of the main scene.
// Lives as a child of PS1UICanvas. At runtime the renderer adds a
// "HUD model pass" after the main scene render: for each active
// PS1UIModel, it swaps the camera matrix to the model's orbit transform
// and re-renders the referenced GameObject's polys on top.
//
// Use cases: title-screen logo rotating at full-screen, inventory item
// preview in a corner while gameplay continues behind it, character
// portrait in a dialog, spinning trophy in an end-of-round screen.
//
// Author workflow:
// 1. Place the mesh somewhere in the scene as a normal PS1MeshInstance
//    (the "template" — it's still exported as a GameObject).
// 2. Mark it StartsInactive = true if you don't want it visible as part
//    of the main scene (most of the time: you don't — it's only meant
//    for the HUD pass).
// 3. Drop a PS1UIModel child under the target PS1UICanvas. Set Target
//    to the mesh node. Set the screen rect (X, Y, W, H) where the
//    model should appear.
// 4. Tune OrbitDistance so the model's bounding sphere fits the rect.
//    The `PS1Godot: Frame Selected Model in Viewport` tool computes a
//    good initial distance.
// 5. At runtime, Lua can Show/Hide individual models, swap the target,
//    or spin them via UI.SetModelOrbit.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_ui_canvas.svg")]
public partial class PS1UIModel : Node
{
    [ExportGroup("Identity")]
    // Unique name per canvas. Used by Lua: UI.SetModelVisible("canvas", "slotName", true).
    [Export] public string ModelName { get; set; } = "";

    [Export] public bool VisibleOnLoad { get; set; } = true;

    [ExportGroup("Target")]
    // NodePath to a PS1MeshInstance / PS1MeshGroup elsewhere in the scene.
    // That GameObject's polygons are re-rendered in this widget's rect.
    [Export] public NodePath Target { get; set; } = new NodePath();

    [ExportGroup("Screen Rect")]
    [Export] public PS1UIAnchor Anchor { get; set; } = PS1UIAnchor.Center;
    [Export(PropertyHint.Range, "-256,576,1,suffix:px")]
    public int X { get; set; } = 0;
    [Export(PropertyHint.Range, "-256,576,1,suffix:px")]
    public int Y { get; set; } = 0;
    [Export(PropertyHint.Range, "0,576,1,suffix:px")]
    public int Width { get; set; } = 128;
    [Export(PropertyHint.Range, "0,576,1,suffix:px")]
    public int Height { get; set; } = 128;

    [ExportGroup("Camera")]
    // Yaw around the model's Y axis. Animate from Lua for a rotating
    // preview: UI.SetModelOrbit(canvas, name, newYaw, pitch).
    [Export(PropertyHint.Range, "-360,360,0.5,suffix:°")]
    public float OrbitYawDegrees { get; set; } = 0f;

    // Pitch above/below the model's horizon (negative = look up at it).
    [Export(PropertyHint.Range, "-89,89,0.5,suffix:°")]
    public float OrbitPitchDegrees { get; set; } = 0f;

    // Distance from the model's pivot along the camera's forward axis.
    // Use the Frame Model tool to compute a good starting value for the
    // target screen rect.
    [Export(PropertyHint.Range, "0.1,100,0.1,suffix:m")]
    public float OrbitDistance { get; set; } = 3f;

    // Projection H per-model. Auto-derived at export from Width +
    // OrbitDistance + target-mesh extent so the model fills the rect;
    // this value is only used as a fallback when the exporter can't
    // resolve the target's bounding box.
    [Export(PropertyHint.Range, "1,1024,1")]
    public int ProjectionH { get; set; } = 240;

    [ExportGroup("Slot (when nested inside a container)")]
    [Export] public PS1UISlotAlign SlotHAlign { get; set; } = PS1UISlotAlign.Inherit;
    [Export] public PS1UISlotAlign SlotVAlign { get; set; } = PS1UISlotAlign.Inherit;
    [Export(PropertyHint.Range, "0,16,1")] public int SlotFlex { get; set; } = 0;
    [Export] public Vector4I SlotPadding { get; set; } = Vector4I.Zero;
}
