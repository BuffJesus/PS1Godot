using Godot;

namespace PS1Godot;

// Horizontal layout container. Children pack left-to-right with `Spacing`
// pixels between each. `Padding` insets the whole row from this container's
// own rect (Left/Top/Right/Bottom).
//
// Per-child slot fields (read off PS1UIElement / nested containers):
//   - SlotFlex == 0 → child uses its own Width.
//   - SlotFlex  > 0 → child takes (flex/totalFlex) of the leftover space
//     after fixed-width children + spacing + padding are removed.
//   - SlotVAlign  → cross-axis pin: Start/Center/End/Fill (Fill stretches
//     to container height minus padding).
//   - SlotHAlign  → ignored when flex > 0 (stretched); when flex = 0, it
//     pins the fixed-size child within its slot column (rarely useful in
//     HBox; mostly Inherit/Start).
//
// Layout is resolved at export time by SceneCollector — the splashpack
// stores absolute X/Y just like a hand-authored PS1UICanvas, so this
// container is purely an authoring convenience.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_ui_hbox.svg")]
public partial class PS1UIHBox : Node
{
    [ExportGroup("Identity")]
    [Export] public string ContainerName { get; set; } = "";

    /// <summary>
    /// Container rect in canvas-relative coords. The exporter places this
    /// rect at (X, Y) with size (Width, Height) inside the parent canvas;
    /// children pack inside it after Padding insets.
    /// </summary>
    [ExportGroup("Layout")]
    [Export(PropertyHint.Range, "-256,576,1,suffix:px")]
    public int X { get; set; } = 0;
    [Export(PropertyHint.Range, "-256,576,1,suffix:px")]
    public int Y { get; set; } = 0;
    [Export(PropertyHint.Range, "0,576,1,suffix:px")]
    public int Width { get; set; } = 320;
    [Export(PropertyHint.Range, "0,576,1,suffix:px")]
    public int Height { get; set; } = 24;

    [Export] public PS1UIAnchor Anchor { get; set; } = PS1UIAnchor.Custom;

    /// <summary>
    /// Vector4I: X=Left, Y=Top, Z=Right, W=Bottom (CSS order).
    /// </summary>
    [Export] public Vector4I Padding { get; set; } = Vector4I.Zero;

    /// <summary>
    /// Pixels between adjacent children.
    /// </summary>
    [Export(PropertyHint.Range, "0,64,1,suffix:px")]
    public int Spacing { get; set; } = 4;

    /// <summary>
    /// Defaults for children whose SlotHAlign/VAlign are Inherit.
    /// </summary>
    [Export] public PS1UISlotAlign DefaultHAlign { get; set; } = PS1UISlotAlign.Start;
    [Export] public PS1UISlotAlign DefaultVAlign { get; set; } = PS1UISlotAlign.Center;

    /// <summary>
    /// Used when this HBox is itself a child of another HBox/VBox/SizeBox.
    /// </summary>
    [ExportGroup("Slot (when nested inside another container)")]
    [Export] public PS1UISlotAlign SlotHAlign { get; set; } = PS1UISlotAlign.Inherit;
    [Export] public PS1UISlotAlign SlotVAlign { get; set; } = PS1UISlotAlign.Inherit;
    [Export(PropertyHint.Range, "0,16,1")] public int SlotFlex { get; set; } = 0;
    [Export] public Vector4I SlotPadding { get; set; } = Vector4I.Zero;
}
