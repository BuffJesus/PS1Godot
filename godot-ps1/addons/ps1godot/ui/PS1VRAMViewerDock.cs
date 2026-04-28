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
    private OptionButton? _scenePicker;
    // sceneIndex → snapshot. Keeps every scene's snapshot from the most
    // recent export run so authors can flip between them without
    // re-exporting. Cleared by BeginExportRun() at the start of each
    // multi-scene export pass.
    private readonly System.Collections.Generic.Dictionary<int, VramSnapshot> _snapshots = new();

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

        // Top row: header label + scene picker. Picker is hidden until
        // there's more than one snapshot to choose between (i.e. a
        // single-scene export doesn't get a useless dropdown).
        var topRow = new HBoxContainer();
        topRow.AddThemeConstantOverride("separation", 12);
        inner.AddChild(topRow);

        _header = new Label
        {
            Text = "PS1 VRAM — no export yet (run on PSX or use Tools → Export Splashpack)",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _header.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.85f));
        topRow.AddChild(_header);

        _scenePicker = new OptionButton
        {
            Visible = false,
            TooltipText = "Pick which scene's VRAM layout to display.",
        };
        _scenePicker.ItemSelected += OnScenePicked;
        topRow.AddChild(_scenePicker);

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

    // Called by the plugin BEFORE iterating scenes. Clears the picker
    // so stale entries from a previous export don't linger when the
    // current run produces fewer scenes.
    public void BeginExportRun()
    {
        _snapshots.Clear();
        if (_scenePicker != null)
        {
            _scenePicker.Clear();
            _scenePicker.Visible = false;
        }
    }

    public void ApplySnapshot(VramSnapshot snapshot)
    {
        _snapshots[snapshot.SceneIndex] = snapshot;
        RebuildScenePicker();
        // Default to displaying the freshest snapshot — typical author
        // flow is "I just exported, show me what I just did."
        DisplaySnapshot(snapshot);
    }

    private void RebuildScenePicker()
    {
        if (_scenePicker == null) return;
        _scenePicker.Clear();

        // Stable order: ascending sceneIndex matches the runtime's
        // Scene.Load(N) numbering and the on-disc filenames.
        var keys = new System.Collections.Generic.List<int>(_snapshots.Keys);
        keys.Sort();
        foreach (var idx in keys)
        {
            var snap = _snapshots[idx];
            _scenePicker.AddItem($"scene_{idx} — {snap.SceneName}", idx);
        }
        // Hide the dropdown when only one scene's been exported —
        // pointless UI clutter when there's nothing to pick.
        _scenePicker.Visible = _snapshots.Count > 1;
    }

    private void OnScenePicked(long index)
    {
        if (_scenePicker == null) return;
        int sceneIdx = (int)_scenePicker.GetItemId((int)index);
        if (_snapshots.TryGetValue(sceneIdx, out var snap))
            DisplaySnapshot(snap);
    }

    // Push a single snapshot into the visible widgets. Separate from
    // ApplySnapshot so the picker callback can re-display a previously
    // captured snapshot without going through the "this is a new
    // snapshot, rebuild the picker" path.
    private void DisplaySnapshot(VramSnapshot snapshot)
    {
        if (_header == null || _grid == null || _stats == null) return;

        // Sync picker selection with whatever's currently displayed.
        // Skip when the picker isn't visible (single-scene export) —
        // there's nothing to sync.
        if (_scenePicker != null && _scenePicker.Visible)
        {
            for (int i = 0; i < _scenePicker.ItemCount; i++)
            {
                if (_scenePicker.GetItemId(i) == snapshot.SceneIndex)
                {
                    _scenePicker.Selected = i;
                    break;
                }
            }
        }

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
        // MouseFilter.Pass keeps gui_input firing for hover tracking
        // without consuming clicks (we don't have click handlers yet).
        MouseFilter = MouseFilterEnum.Pass;
    }

    // Native Godot 4 dynamic tooltip — fires on hover, no manual
    // timing/positioning. Hit-tests the hover position against our
    // snapshot's rects (textures first since they're nested inside
    // atlases; CLUTs are tiny so they need a generous tolerance).
    // Returns empty string for "no tooltip" — Godot suppresses the
    // popup in that case.
    public override string _GetTooltip(Vector2 atPosition)
    {
        if (_snapshot == null) return "";

        // Map screen-space `atPosition` back to VRAM coords. Inverse
        // of the transform _Draw uses (origin + scale).
        var avail = Size;
        if (avail.X <= 0 || avail.Y <= 0) return "";
        float scale = Mathf.Min(avail.X / VramW, avail.Y / VramH);
        if (scale <= 0) return "";
        var drawSize = new Vector2(VramW * scale, VramH * scale);
        var origin = (avail - drawSize) * 0.5f;

        float vramX = (atPosition.X - origin.X) / scale;
        float vramY = (atPosition.Y - origin.Y) / scale;
        if (vramX < 0 || vramX >= VramW || vramY < 0 || vramY >= VramH) return "";

        // CLUT strips are 1 px tall — hard to hit at small scales.
        // Add a 1-pixel tolerance band so they're hoverable.
        const float ClutHitPadding = 1f;
        foreach (var c in _snapshot.Cluts)
        {
            if (vramX >= c.X && vramX < c.X + c.Length &&
                vramY >= c.Y - ClutHitPadding && vramY < c.Y + 1 + ClutHitPadding)
            {
                return $"CLUT — {c.OwnerTextureName}\n" +
                       $"VRAM ({c.X},{c.Y}) · {c.Length} entries · {BppLabel(c.BitDepth)}";
            }
        }

        // Textures next — they sit on top of atlas footprints in
        // _Draw, so the hit ordering should match.
        foreach (var t in _snapshot.Textures)
        {
            if (vramX >= t.X && vramX < t.X + t.Width &&
                vramY >= t.Y && vramY < t.Y + t.Height)
            {
                int srcW = SourcePixelWidth(t);
                return $"Texture — {t.Name}\n" +
                       $"VRAM ({t.X},{t.Y}) · {srcW}×{t.Height} px · {BppLabel(t.BitDepth)}\n" +
                       $"{t.Width} VRAM word(s) wide";
            }
        }

        // Atlases last — the "empty" slack inside an atlas tooltips
        // as the atlas itself, which is exactly what authors want
        // when scanning for "where's the room to grow."
        foreach (var a in _snapshot.Atlases)
        {
            if (vramX >= a.X && vramX < a.X + a.Width &&
                vramY >= a.Y && vramY < a.Y + a.Height)
            {
                return $"Atlas — {BppLabel(a.BitDepth)}\n" +
                       $"VRAM ({a.X},{a.Y}) · {a.Width}×{a.Height}";
            }
        }

        // Reserved region tooltips — useful for authors trying to
        // place something in the wrong spot ("why can't I put art
        // here? oh, that's the framebuffer").
        if (vramX < 320 && vramY < 240)
            return "Reserved — framebuffer A (320×240)";
        if (vramX < 320 && vramY >= 256 && vramY < 256 + 240)
            return "Reserved — framebuffer B (320×240)";
        if (vramX >= 960)
            return "Reserved — font column (system + custom fonts)";

        return "";
    }

    // Source pixel width = QuantizedWidth × pack ratio. 4bpp packs 4
    // source pixels per VRAM word; 8bpp packs 2; 16bpp is 1:1. Lets
    // the tooltip show authors the original art dimensions, not the
    // packed VRAM cells.
    private static int SourcePixelWidth(VramSnapshot.TextureRect t) => t.BitDepth switch
    {
        PSXBPP.TEX_4BIT => t.Width * 4,
        PSXBPP.TEX_8BIT => t.Width * 2,
        _ => t.Width,
    };

    private static string BppLabel(PSXBPP bpp) => bpp switch
    {
        PSXBPP.TEX_4BIT => "4bpp",
        PSXBPP.TEX_8BIT => "8bpp",
        PSXBPP.TEX_16BIT => "16bpp",
        _ => bpp.ToString(),
    };

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
