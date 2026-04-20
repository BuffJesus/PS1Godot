using Godot;

namespace PS1Godot;

// A single widget inside a PS1UICanvas. Must be a direct child of a
// PS1UICanvas to be picked up by the exporter. Widget type selects
// which fields are meaningful:
//   - Text: renders `Text` in `Color`. Font is the runtime's built-in
//     system font in MVP (custom fonts = Phase 2 of this bullet).
//   - Box: solid-color rectangle in `Color`.
//   - Image / Progress: runtime supports these but exporter MVP skips
//     them.
//
// Coordinates are in PS1 screen pixels (320×240). Anchor is fixed to
// top-left (0,0,0,0) in MVP — authors use absolute X/Y. Anchor support
// lands when the exporter needs it (HUD pinned bottom-right, etc.).
public enum PS1UIElementType
{
    Box = 1,
    Text = 2,
}

[Tool]
[GlobalClass]
public partial class PS1UIElement : Node
{
    [Export] public string ElementName { get; set; } = "";

    [Export] public PS1UIElementType Type { get; set; } = PS1UIElementType.Text;

    [Export] public bool VisibleOnLoad { get; set; } = true;

    // Top-left corner in PS1 screen pixels. 320 wide × 240 tall.
    [Export(PropertyHint.Range, "-256,576,1")]
    public int X { get; set; } = 16;
    [Export(PropertyHint.Range, "-256,576,1")]
    public int Y { get; set; } = 16;

    // Width/height in pixels. For Text elements, used only when the
    // element needs layout (wrapping — not in MVP). For Box, the
    // rectangle extent.
    [Export(PropertyHint.Range, "0,576,1")]
    public int Width { get; set; } = 100;
    [Export(PropertyHint.Range, "0,576,1")]
    public int Height { get; set; } = 16;

    // Tint. Text = foreground color, Box = fill color.
    [Export] public Color Color { get; set; } = new Color(1f, 1f, 1f, 1f);

    // Text body (Type == Text). UTF-8 bytes; runtime buffer is 64 B,
    // so authored text should stay under ~60 visible characters.
    [Export(PropertyHint.MultilineText)]
    public string Text { get; set; } = "";
}
