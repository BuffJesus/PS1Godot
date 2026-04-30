using Godot;

namespace PS1Godot;

// Single-child wrapper that forces an explicit size on its content. Use
// when a child container's natural size doesn't match the layout you want
// (typical: pin a sub-canvas to a fixed 200×80 box regardless of how
// many elements it grows to hold).
//
// Width/HeightOverride < 0 means "take the child's own Width/Height" — the
// SizeBox becomes a pure padding wrapper.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_ui_sizebox.svg")]
public partial class PS1UISizeBox : Node
{
    [ExportGroup("Identity")]
    [Export] public string ContainerName { get; set; } = "";

    [ExportGroup("Layout")]
    [Export(PropertyHint.Range, "-256,576,1,suffix:px")]
    public int X { get; set; } = 0;
    [Export(PropertyHint.Range, "-256,576,1,suffix:px")]
    public int Y { get; set; } = 0;
    /// <summary>
    /// -1 = use child's own Width.
    /// </summary>
    [Export(PropertyHint.Range, "-1,576,1,suffix:px")]
    public int WidthOverride { get; set; } = -1;
    /// <summary>
    /// -1 = use child's own Height.
    /// </summary>
    [Export(PropertyHint.Range, "-1,576,1,suffix:px")]
    public int HeightOverride { get; set; } = -1;

    [Export] public PS1UIAnchor Anchor { get; set; } = PS1UIAnchor.Custom;

    [Export] public Vector4I Padding { get; set; } = Vector4I.Zero;

    [ExportGroup("Slot (when nested inside another container)")]
    [Export] public PS1UISlotAlign SlotHAlign { get; set; } = PS1UISlotAlign.Inherit;
    [Export] public PS1UISlotAlign SlotVAlign { get; set; } = PS1UISlotAlign.Inherit;
    [Export(PropertyHint.Range, "0,16,1")] public int SlotFlex { get; set; } = 0;
    [Export] public Vector4I SlotPadding { get; set; } = Vector4I.Zero;
}
