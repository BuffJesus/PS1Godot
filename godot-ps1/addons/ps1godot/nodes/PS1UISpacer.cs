using Godot;

namespace PS1Godot;

// Empty placeholder used to push siblings apart inside HBox / VBox. Two
// modes:
//   - Flex == 0 (default): occupies a fixed (Width, Height) on the main
//     axis. Useful for a constant pixel gap between two children.
//   - Flex  > 0: takes (flex / totalFlex) of leftover space on the main
//     axis after fixed-size siblings are placed. Three flex-1 spacers
//     between two buttons → buttons quartile-spaced across the row.
//
// Renders nothing in the splashpack — the exporter consumes Spacer purely
// for its layout effect on the parent.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_ui_spacer.svg")]
public partial class PS1UISpacer : Node
{
    /// <summary>
    /// Used only when Flex == 0 (fixed-size spacer mode).
    /// </summary>
    [ExportGroup("Layout")]
    [Export(PropertyHint.Range, "0,576,1,suffix:px")]
    public int Width { get; set; } = 8;
    [Export(PropertyHint.Range, "0,576,1,suffix:px")]
    public int Height { get; set; } = 8;

    [Export(PropertyHint.Range, "0,16,1")]
    public int SlotFlex { get; set; } = 0;
}
