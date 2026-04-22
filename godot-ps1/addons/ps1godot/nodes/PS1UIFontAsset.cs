using Godot;

namespace PS1Godot;

// A custom PS1 UI font. Drop in a TTF/OTF (or any Godot FontFile),
// set a pixel size, run "PS1Godot: Generate font bitmap" — the
// plugin rasterizes a 256-wide atlas + proportional advance widths
// that the exporter packs into VRAM and the runtime renders as-is.
//
// Source of truth for byte layout on the splashpack side:
//   - psxsplash-main/src/uisystem.hh (UIFontDesc, 112 bytes)
//   - splashedit-main/Runtime/PSXFontAsset.cs (reference ingest)
//
// VRAM placement (per uisystem.hh / PSXUIExporter.cs):
//   - Font slot 1 → (960,   0), max 256 px tall
//   - Font slot 2 → (960, 256), max 208 px tall
//   - System font  → (960, 464), baked into the runtime
// Up to 2 custom fonts per splashpack; a third is rejected at export.
//
// Fields are split into two groups:
//   - Authored: SourceFont, FontSize, FontName. The author fills these.
//   - Generated: GlyphWidth, GlyphHeight, Bitmap, AdvanceWidths.
//     Populated by PS1FontGenerator (thin C# wrapper over the
//     PS1FontRasterizer GDExtension class; see scripting/src/).
//     [Export]'d so they round-trip through .tres saves.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_ui_font_asset.svg")]
public partial class PS1UIFontAsset : Resource
{
    [ExportGroup("Source")]
    // Source TTF/OTF (or any Godot FontFile). Required before
    // generation.
    [Export] public FontFile? SourceFont { get; set; }

    // Rendering size in pixels. Matches the font's intended PSX
    // on-screen height. Range mirrors SplashEdit's PSXFontAsset.
    [Export(PropertyHint.Range, "6,32,1,suffix:px")]
    public int FontSize { get; set; } = 12;

    // Lua-facing identifier. Defaults to the resource file name
    // (minus extension) when left blank. Must be unique within a
    // scene's font set.
    [Export] public string FontName { get; set; } = "";

    // Alpha threshold passed to the rasterizer — source TTF pixels
    // below this alpha count as transparent (PS1 can't reproduce
    // gradients). 0.3 matches SplashEdit. Lower = chunkier strokes;
    // higher = thinner / dropped strokes at small sizes.
    [Export(PropertyHint.Range, "0.05,0.95,0.05")]
    public float AlphaThreshold { get; set; } = 0.3f;

    [ExportGroup("Generated (do not hand-edit)")]
    // Glyph cell dimensions on the atlas. Cell width is auto-picked
    // from {4, 8, 16, 32} (must divide 256 evenly for PSX UV wrap).
    [Export(PropertyHint.Range, "0,32,1,suffix:px")]
    public int GlyphWidth { get; set; } = 0;
    [Export(PropertyHint.Range, "0,32,1,suffix:px")]
    public int GlyphHeight { get; set; } = 0;

    // 256-wide atlas, RGBA8 (will pack to 4bpp + 2-entry CLUT at
    // export). Null until Generate is run.
    [Export] public Image? Bitmap { get; set; }

    // Per-glyph horizontal advance in pixels for ASCII 0x20..0x7F.
    // Index = char - 0x20. byte[96] matches the runtime's
    // UIFontDesc.advanceWidths exactly.
    [Export] public byte[] AdvanceWidths { get; set; } = new byte[96];

    // True when Bitmap + AdvanceWidths are populated. Exporter
    // checks this; un-generated fonts skip export with a warning.
    public bool IsGenerated => Bitmap != null && GlyphWidth > 0 && GlyphHeight > 0;
}
