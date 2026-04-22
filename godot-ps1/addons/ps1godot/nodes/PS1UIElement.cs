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
// Coordinates are in PS1 screen pixels (320×240). See the `Anchor`
// property's doc for how X/Y are interpreted — Custom (default) means
// "absolute top-left," other values turn X/Y into insets from the
// anchored edges / offsets from the anchored center.
public enum PS1UIElementType
{
    Box = 1,
    Text = 2,
}

// Which `PS1Theme` slot this element should pull its color from at
// export time. `Custom` (the default, 0) means "use the authored
// Color field verbatim" — backward-compatible behavior for scenes
// that don't have a theme yet.
public enum PS1UIThemeSlot
{
    Custom = 0,
    Text,
    Accent,
    Bg,
    BgBorder,
    Highlight,
    Warning,
    Danger,
    Neutral,
}

[Tool]
[GlobalClass]
public partial class PS1UIElement : Node
{
    [Export] public string ElementName { get; set; } = "";

    [Export] public PS1UIElementType Type { get; set; } = PS1UIElementType.Text;

    [Export] public bool VisibleOnLoad { get; set; } = true;

    // Placement mode. Custom = X/Y are the absolute top-left corner
    // (backward-compatible default). Non-Custom = the element snaps
    // to one of the nine PSX-screen reference points and X/Y become
    // insets (edge anchors) or offsets (center anchors). See
    // PS1UIAnchor's doc comment for full semantics.
    [Export] public PS1UIAnchor Anchor { get; set; } = PS1UIAnchor.Custom;

    // Interpretation depends on Anchor:
    //   - Custom / TopLeft: absolute position of the top-left corner.
    //   - TopRight, CenterRight, BottomRight: X is the inset in
    //     pixels from the right edge (positive = onto the screen).
    //   - BottomLeft, BottomCenter, BottomRight: Y is the inset from
    //     the bottom edge.
    //   - Center-aligned axes: the value is an offset from the PSX
    //     center (160 horizontally, 120 vertically).
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

    // When non-Custom AND the owning PS1UICanvas has a Theme assigned,
    // the exporter uses `theme.<Slot>Color` instead of `Color`. If the
    // slot has no match (or no theme), falls back to `Color`. Change the
    // theme → every opted-in element restyles.
    [Export] public PS1UIThemeSlot ThemeSlot { get; set; } = PS1UIThemeSlot.Custom;

    // Text body (Type == Text). UTF-8 bytes; runtime buffer is 64 B,
    // so authored text should stay under ~60 visible characters.
    [Export(PropertyHint.MultilineText)]
    public string Text { get; set; } = "";
}
