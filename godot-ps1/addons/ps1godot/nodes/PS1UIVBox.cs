using Godot;

namespace PS1Godot;

// Vertical layout container. Children pack top-to-bottom with `Spacing`
// pixels between each. `Padding` insets the column from this container's
// own rect (Left/Top/Right/Bottom).
//
// Per-child slot fields:
//   - SlotFlex == 0 → child uses its own Height.
//   - SlotFlex  > 0 → child takes proportional share of leftover height.
//   - SlotHAlign  → cross-axis pin (Start/Center/End/Fill).
//   - SlotVAlign  → ignored when flex > 0; otherwise pins child within slot
//     row (rare in VBox).
//
// See PS1UIHBox for the rest of the model — this is the same code with
// axes swapped.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_ui_canvas.svg")]
public partial class PS1UIVBox : Node
{
    [ExportGroup("Identity")]
    [Export] public string ContainerName { get; set; } = "";

    [ExportGroup("Layout")]
    [Export(PropertyHint.Range, "-256,576,1,suffix:px")]
    public int X { get; set; } = 0;
    [Export(PropertyHint.Range, "-256,576,1,suffix:px")]
    public int Y { get; set; } = 0;
    [Export(PropertyHint.Range, "0,576,1,suffix:px")]
    public int Width { get; set; } = 200;
    [Export(PropertyHint.Range, "0,576,1,suffix:px")]
    public int Height { get; set; } = 240;

    [Export] public PS1UIAnchor Anchor { get; set; } = PS1UIAnchor.Custom;

    [Export] public Vector4I Padding { get; set; } = Vector4I.Zero;

    [Export(PropertyHint.Range, "0,64,1,suffix:px")]
    public int Spacing { get; set; } = 4;

    [Export] public PS1UISlotAlign DefaultHAlign { get; set; } = PS1UISlotAlign.Center;
    [Export] public PS1UISlotAlign DefaultVAlign { get; set; } = PS1UISlotAlign.Start;

    [ExportGroup("Slot (when nested inside another container)")]
    [Export] public PS1UISlotAlign SlotHAlign { get; set; } = PS1UISlotAlign.Inherit;
    [Export] public PS1UISlotAlign SlotVAlign { get; set; } = PS1UISlotAlign.Inherit;
    [Export(PropertyHint.Range, "0,16,1")] public int SlotFlex { get; set; } = 0;
    [Export] public Vector4I SlotPadding { get; set; } = Vector4I.Zero;
}
