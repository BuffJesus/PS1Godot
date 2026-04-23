using Godot;

namespace PS1Godot;

// Z-stacks all children at the same rect — first child renders behind, last
// child on top (matching scene-tree order). Useful for layering a Box
// background under a Text label, or stacking icons on a button.
//
// Each child's slot fields control how it's pinned within the overlay rect.
// Default H/VAlign is Fill (child stretches to overlay size), but Center is
// often what you want for icons.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_ui_canvas.svg")]
public partial class PS1UIOverlay : Node
{
    [ExportGroup("Identity")]
    [Export] public string ContainerName { get; set; } = "";

    [ExportGroup("Layout")]
    [Export(PropertyHint.Range, "-256,576,1,suffix:px")]
    public int X { get; set; } = 0;
    [Export(PropertyHint.Range, "-256,576,1,suffix:px")]
    public int Y { get; set; } = 0;
    [Export(PropertyHint.Range, "0,576,1,suffix:px")]
    public int Width { get; set; } = 100;
    [Export(PropertyHint.Range, "0,576,1,suffix:px")]
    public int Height { get; set; } = 32;

    [Export] public PS1UIAnchor Anchor { get; set; } = PS1UIAnchor.Custom;

    [Export] public Vector4I Padding { get; set; } = Vector4I.Zero;

    [Export] public PS1UISlotAlign DefaultHAlign { get; set; } = PS1UISlotAlign.Fill;
    [Export] public PS1UISlotAlign DefaultVAlign { get; set; } = PS1UISlotAlign.Fill;

    [ExportGroup("Slot (when nested inside another container)")]
    [Export] public PS1UISlotAlign SlotHAlign { get; set; } = PS1UISlotAlign.Inherit;
    [Export] public PS1UISlotAlign SlotVAlign { get; set; } = PS1UISlotAlign.Inherit;
    [Export(PropertyHint.Range, "0,16,1")] public int SlotFlex { get; set; } = 0;
    [Export] public Vector4I SlotPadding { get; set; } = Vector4I.Zero;
}
