#if TOOLS
using Godot;
using PS1Godot.Exporter;

namespace PS1Godot.UI;

// Dockable VRAM viewer panel. Renders the 1024×512 PSX VRAM grid as it
// stands at the end of the most recent export — reserved framebuffer +
// font regions, atlas footprints colored by bit depth, per-texture
// sub-rects, CLUT strips. Lets authors see at a glance which atlases
// are sparse (room for more textures), which textures pulled in their
// own atlas (single-use offenders), and how much VRAM is actually free.
//
// MVP scope: shows the most recently exported scene's snapshot.
// Multi-scene picker dropdown, hover tooltips, and per-tpage zoom are
// v2 follow-ups (see ROADMAP).
[Tool]
public partial class PS1VRAMViewerDock : VBoxContainer
{
    private Label? _header;
    private Label? _stats;
    private PS1VRAMGrid? _grid;
    private VramSnapshot? _snapshot;

    public PS1VRAMViewerDock()
    {
        Name = "PS1 VRAM";
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 6);

        BuildUI();
    }

    private void BuildUI()
    {
        var margin = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        AddChild(margin);

        var inner = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        inner.AddThemeConstantOverride("separation", 6);
        margin.AddChild(inner);

        _header = new Label
        {
            Text = "PS1 VRAM — no export yet (run on PSX or use Tools → Export Splashpack)",
        };
        _header.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.85f));
        inner.AddChild(_header);

        _grid = new PS1VRAMGrid
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        inner.AddChild(_grid);

        _stats = new Label { Text = "" };
        _stats.AddThemeFontSizeOverride("font_size", 11);
        _stats.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.55f));
        inner.AddChild(_stats);

        // Legend row — color chip + label per bit depth + reserved + CLUT.
        var legend = new HBoxContainer();
        legend.AddThemeConstantOverride("separation", 12);
        legend.AddChild(MakeLegendChip(PS1VRAMGrid.Color4bpp, "4bpp"));
        legend.AddChild(MakeLegendChip(PS1VRAMGrid.Color8bpp, "8bpp"));
        legend.AddChild(MakeLegendChip(PS1VRAMGrid.Color16bpp, "16bpp"));
        legend.AddChild(MakeLegendChip(PS1VRAMGrid.ColorClut, "CLUT"));
        legend.AddChild(MakeLegendChip(PS1VRAMGrid.ColorReserved, "Reserved"));
        inner.AddChild(legend);
    }

    private static Control MakeLegendChip(Color c, string label)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        var swatch = new ColorRect
        {
            Color = c,
            CustomMinimumSize = new Vector2(12, 12),
        };
        row.AddChild(swatch);
        var text = new Label { Text = label };
        text.AddThemeFontSizeOverride("font_size", 11);
        text.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.65f));
        row.AddChild(text);
        return row;
    }

    public void ApplySnapshot(VramSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (_header == null || _grid == null || _stats == null) return;

        if (snapshot.IsEmpty)
        {
            _header.Text = $"PS1 VRAM — {snapshot.SceneName}: no textures";
            _stats.Text = "";
            _grid.SetSnapshot(snapshot); // still draws the reserved regions
            return;
        }

        // Total usable VRAM = 1024*512 - reserved (2 framebuffers + font column).
        // Framebuffers: 2 × 320×240 = 153 600.  Font column: 64×512 = 32 768.
        // Total reserved = 186 368.  Usable = 524 288 - 186 368 = 337 920.
        const long UsablePixels = 1024L * 512L - (2L * 320 * 240) - (64L * 512);
        double pct = 100.0 * snapshot.UsedPixels / (double)UsablePixels;

        _header.Text = $"PS1 VRAM — {snapshot.SceneName} ({pct:F1}% of {UsablePixels / 1024} K usable px)";
        _stats.Text = $"{snapshot.Atlases.Count} atlas(es), {snapshot.Textures.Count} texture(s), {snapshot.Cluts.Count} CLUT(s)";
        _grid.SetSnapshot(snapshot);
    }
}

// Custom Control that draws the 1024×512 VRAM grid. Lives inside the
// dock; takes its size from the parent and scales the VRAM coordinate
// space to fit while preserving aspect ratio. Doing this in _Draw
// (rather than rendering to an Image + TextureRect) keeps the door
// open for hover-tooltips and zoom in v2 — both of which want
// per-pixel hit-testing against the source rects.
[Tool]
public partial class PS1VRAMGrid : Control
{
    public const int VramW = 1024;
    public const int VramH = 512;

    // Color palette for the layout. Tuned for low-saturation tints on a
    // dark dock background — 4bpp/8bpp/16bpp follow the spectrum
    // (cheap-to-expensive), CLUT is yellow as the conventional palette
    // accent, reserved is medium gray.
    public static readonly Color Color4bpp = new(0.40f, 0.78f, 0.45f);   // green
    public static readonly Color Color8bpp = new(0.42f, 0.65f, 0.95f);   // blue
    public static readonly Color Color16bpp = new(0.95f, 0.55f, 0.30f);  // orange
    public static readonly Color ColorClut = new(0.95f, 0.85f, 0.30f);   // yellow
    public static readonly Color ColorReserved = new(0.45f, 0.45f, 0.50f);
    public static readonly Color ColorBackground = new(0.10f, 0.10f, 0.12f);
    public static readonly Color ColorBorder = new(1f, 1f, 1f, 0.15f);

    private VramSnapshot? _snapshot;

    public PS1VRAMGrid()
    {
        // VRAM is exactly 2:1. Pin a sensible minimum so the dock isn't
        // a sliver before the user has resized; the actual draw scales
        // to whatever size the parent gives us.
        CustomMinimumSize = new Vector2(512, 256);
    }

    public void SetSnapshot(VramSnapshot? s)
    {
        _snapshot = s;
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Fit the VRAM rectangle inside the control bounds, preserving
        // the 2:1 aspect ratio. Letterbox horizontally or vertically as
        // needed; centre the rendered VRAM in whichever axis has slack.
        var avail = Size;
        if (avail.X <= 0 || avail.Y <= 0) return;

        float scale = Mathf.Min(avail.X / VramW, avail.Y / VramH);
        var drawSize = new Vector2(VramW * scale, VramH * scale);
        var origin = (avail - drawSize) * 0.5f;

        // Background
        DrawRect(new Rect2(origin, drawSize), ColorBackground);

        // Reserved regions — framebuffer A, framebuffer B, font column.
        // Same coords as VRAMPacker's _reserved list.
        DrawScaledRect(origin, scale, 0,    0,   320,  240, ColorReserved);
        DrawScaledRect(origin, scale, 0,    256, 320,  240, ColorReserved);
        DrawScaledRect(origin, scale, 960,  0,   64,   VramH, ColorReserved);

        if (_snapshot != null)
        {
            // Atlas footprints first — faint fill so per-texture rects
            // pop on top.
            foreach (var a in _snapshot.Atlases)
            {
                Color c = ColorForBpp(a.BitDepth);
                c.A = 0.25f;
                DrawScaledRect(origin, scale, a.X, a.Y, a.Width, a.Height, c);
            }

            // Texture sub-rects — solid fills inside their atlas. One
            // pixel of border per rect helps adjacency reading on tight
            // packings.
            foreach (var t in _snapshot.Textures)
            {
                Color c = ColorForBpp(t.BitDepth);
                DrawScaledRect(origin, scale, t.X, t.Y, t.Width, t.Height, c);
                // Outline only when the rect is large enough that a
                // 1-pixel border doesn't eat the fill.
                if (t.Width * scale >= 4 && t.Height * scale >= 4)
                {
                    DrawScaledRectOutline(origin, scale, t.X, t.Y, t.Width, t.Height, ColorBorder);
                }
            }

            // CLUTs — 1px tall on the source. Force a 2-pixel screen
            // height so they don't disappear at small scales.
            foreach (var c in _snapshot.Cluts)
            {
                float screenH = Mathf.Max(2f, scale);
                var rect = new Rect2(
                    origin.X + c.X * scale,
                    origin.Y + c.Y * scale,
                    c.Length * scale,
                    screenH);
                DrawRect(rect, ColorClut);
            }
        }

        // Outer border so the VRAM region is visually distinct from the
        // dock chrome around it.
        DrawRect(new Rect2(origin, drawSize), ColorBorder, filled: false);
    }

    private void DrawScaledRect(Vector2 origin, float scale, int x, int y, int w, int h, Color c)
    {
        var rect = new Rect2(
            origin.X + x * scale,
            origin.Y + y * scale,
            w * scale,
            h * scale);
        DrawRect(rect, c);
    }

    private void DrawScaledRectOutline(Vector2 origin, float scale, int x, int y, int w, int h, Color c)
    {
        var rect = new Rect2(
            origin.X + x * scale,
            origin.Y + y * scale,
            w * scale,
            h * scale);
        DrawRect(rect, c, filled: false);
    }

    private static Color ColorForBpp(PSXBPP bpp) => bpp switch
    {
        PSXBPP.TEX_4BIT => Color4bpp,
        PSXBPP.TEX_8BIT => Color8bpp,
        PSXBPP.TEX_16BIT => Color16bpp,
        _ => ColorReserved,
    };
}
#endif
