using Godot;
using PS1Godot.Exporter;

namespace PS1Godot;

// A single widget inside a PS1UICanvas. Must be a direct child of a
// PS1UICanvas to be picked up by the exporter. Widget type selects
// which fields are meaningful:
//   - Text: renders `Text` in `Color`. Built-in system font (slot 0) by
//     default, or a generated PS1UIFontAsset assigned to `Font`.
//   - Box: solid-color rectangle in `Color`.
//   - Image: textured quad. Set `Texture` (Texture2D), `UVRect`
//     (sub-region within the texture; default = full), `BitDepth`
//     (4/8/16). `Color` modulates as a tint (white = unmodified).
//   - Progress: runtime supports these but exporter MVP skips them.
//
// Enum values match the runtime's UIElementType in
// psxsplash-main/src/uisystem.hh — do not renumber.
//
// Coordinates are in PS1 screen pixels (320×240). See the `Anchor`
// property's doc for how X/Y are interpreted — Custom (default) means
// "absolute top-left," other values turn X/Y into insets from the
// anchored edges / offsets from the anchored center.
public enum PS1UIElementType
{
    Image = 0,
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

// Horizontal alignment of a Text element's content inside its
// Width box. Computed per line at runtime using the selected font's
// advance widths, so author-supplied `\n` or the future auto-wrap
// both produce correctly-aligned lines.
public enum PS1UITextAlign
{
    Left = 0,
    Center = 1,
    Right = 2,
}

// Vertical alignment of a Text element's content inside its Height
// box. Same render-time computation as horizontal — stack of lines
// gets shifted as a unit, not per line.
public enum PS1UITextVAlign
{
    Top = 0,
    Middle = 1,
    Bottom = 2,
}

[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_ui_element.svg")]
public partial class PS1UIElement : Node
{
    [ExportGroup("Identity")]
    [Export] public string ElementName { get; set; } = "";
    [Export] public PS1UIElementType Type { get; set; } = PS1UIElementType.Text;
    [Export] public bool VisibleOnLoad { get; set; } = true;

    [ExportGroup("Layout")]
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
    [Export(PropertyHint.Range, "-256,576,1,suffix:px")]
    public int X { get; set; } = 16;
    [Export(PropertyHint.Range, "-256,576,1,suffix:px")]
    public int Y { get; set; } = 16;

    // Width/height in pixels. For Text elements, used only when the
    // element needs layout (wrapping — not in MVP). For Box, the
    // rectangle extent.
    [Export(PropertyHint.Range, "0,576,1,suffix:px")]
    public int Width { get; set; } = 100;
    [Export(PropertyHint.Range, "0,576,1,suffix:px")]
    public int Height { get; set; } = 16;

    [ExportGroup("Appearance")]
    // Tint. Text = foreground color, Box = fill color.
    [Export] public Color Color { get; set; } = new Color(1f, 1f, 1f, 1f);

    // When non-Custom AND the owning PS1UICanvas has a Theme assigned,
    // the exporter uses `theme.<Slot>Color` instead of `Color`. If the
    // slot has no match (or no theme), falls back to `Color`. Change the
    // theme → every opted-in element restyles.
    [Export] public PS1UIThemeSlot ThemeSlot { get; set; } = PS1UIThemeSlot.Custom;

    [ExportGroup("Text")]
    // Text body (Type == Text). UTF-8 bytes; runtime buffer is 64 B,
    // so authored text should stay under ~60 visible characters. Default
    // "Text" so a newly-added element is visible by default; clear it
    // and author your own.
    [Export(PropertyHint.MultilineText)]
    public string Text { get; set; } = "Text";

    // Custom font for Text elements. null → the built-in system font
    // (fontIndex 0). Assigning a generated PS1UIFontAsset makes this
    // element use that font at runtime. Ignored for non-Text types.
    // Max 2 distinct custom fonts per scene (runtime cap); the
    // exporter errors on a third.
    [Export] public PS1UIFontAsset? Font { get; set; }

    // Horizontal alignment of Text content inside the element's
    // Width box. Runtime shifts each line's starting X by the
    // measured line width so `\n` and auto-wrap both align correctly.
    // Ignored for non-Text types.
    [Export] public PS1UITextAlign TextAlign { get; set; } = PS1UITextAlign.Left;

    // Vertical alignment of the text stack inside the Height box.
    // Ignored for non-Text types.
    [Export] public PS1UITextVAlign TextVAlign { get; set; } = PS1UITextVAlign.Top;

    [ExportGroup("Image (when Type = Image)")]
    // Source texture for Image-type elements. Must have a resource path
    // (i.e. saved as an asset, not generated in-memory) so the exporter
    // can dedupe across elements + meshes. Ignored for non-Image types.
    [Export] public Texture2D? Texture { get; set; }

    // Sub-region of `Texture` to display, normalized 0..1 with origin
    // top-left. Default (0,0,1,1) shows the whole texture. Useful for
    // packing multiple icons into one source PNG and addressing each
    // by UV. Ignored for non-Image types.
    [Export] public Rect2 UVRect { get; set; } = new Rect2(0f, 0f, 1f, 1f);

    // PSX bit-depth this image gets quantized to: 4bpp = 16-color
    // palette (cheapest VRAM), 8bpp = 256-color, 16bpp = direct RGB
    // (no CLUT, ~most VRAM). Match the source asset's color complexity:
    // CRT bezels and HUD icons usually fit 4bpp comfortably; photo-
    // realistic textures want 8bpp+. Ignored for non-Image types.
    [Export] public PSXBPP BitDepth { get; set; } = PSXBPP.TEX_8BIT;

    [ExportGroup("Slot (when nested inside a container)")]
    // These fields are read by PS1UIHBox / PS1UIVBox / PS1UISizeBox /
    // PS1UIOverlay parents at export time. Ignored when the parent is a
    // PS1UICanvas (use Anchor + X/Y in that case).
    [Export] public PS1UISlotAlign SlotHAlign { get; set; } = PS1UISlotAlign.Inherit;
    [Export] public PS1UISlotAlign SlotVAlign { get; set; } = PS1UISlotAlign.Inherit;
    // 0 → use Width/Height as-is. >0 → take this proportional share of the
    // leftover space on the parent's main axis (HBox: horizontal,
    // VBox: vertical). Three flex-1 children split free space equally.
    [Export(PropertyHint.Range, "0,16,1")] public int SlotFlex { get; set; } = 0;
    // Inset margin around this element inside its slot. CSS order:
    // X=Left, Y=Top, Z=Right, W=Bottom.
    [Export] public Vector4I SlotPadding { get; set; } = Vector4I.Zero;
}
