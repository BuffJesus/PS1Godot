#if TOOLS
using Godot;

namespace PS1Godot.UI;

// WYSIWYG preview + edit surface for PS1UICanvas. Lives as a bottom
// panel tab so it's next to Output / Debugger / Search, and renders
// the selected canvas at integer zoom against a 320×240 PS1 screen
// frame. Theme slots resolve live so swapping the canvas's Theme
// restyles every opted-in element in the preview immediately.
//
// Design tenets (docs/ui-ux-plan.md § G):
//  - Intuitive: selecting a PS1UICanvas or any PS1UIElement child
//    auto-opens this tab on the right canvas; you never hunt for it.
//  - Non-intimidating: default view is zero-config — author drops
//    elements into the hierarchy and sees them immediately.
//  - Modern: integer-zoom nearest-neighbor, 8-px grid overlay, flat
//    chrome. No skeuomorphism.
//  - Beautiful: coherent accent (PS1 red), 8-px spacing, clear
//    hierarchy label so the author always knows what's on screen.
//
// This slice is READ-ONLY preview. Drag/resize handles, anchor
// picker, and multi-select land in the next sub-slice.
[Tool]
public partial class PS1UICanvasEditor : VBoxContainer
{
    // PSX reference resolution. Everything in this view assumes these
    // bounds — the same the runtime renders into.
    public const int PsxWidth = 320;
    public const int PsxHeight = 240;

    // Dark PS1-ish checkered background so transparent elements read.
    private static readonly Color BgA = new(0.09f, 0.09f, 0.12f);
    private static readonly Color BgB = new(0.12f, 0.12f, 0.15f);
    // Screen frame (bright enough to see against the dark bg).
    private static readonly Color FrameColor = new(1f, 1f, 1f, 0.6f);
    // Text element bounds — visible but not intrusive.
    private static readonly Color BoundsColor = new(1f, 1f, 1f, 0.18f);
    // 8-px grid overlay.
    private static readonly Color GridMajor = new(1f, 1f, 1f, 0.08f);
    private static readonly Color GridMinor = new(1f, 1f, 1f, 0.04f);

    private PS1UICanvas? _selectedCanvas;
    private int _zoom = 2;                  // 1×, 2×, 3×, 4×
    private bool _showGrid = true;

    private Label? _headerLabel;
    private OptionButton? _zoomCombo;
    private CheckButton? _gridToggle;
    private Control? _canvasArea;

    public PS1UICanvasEditor()
    {
        Name = "PS1 UI";
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 6);
        BuildUI();
    }

    // Called by PS1GodotPlugin whenever the editor selection changes.
    // Null = no PS1UICanvas in selection (neither a canvas nor an
    // element child of one).
    public void SetSelectedCanvas(PS1UICanvas? canvas)
    {
        if (_selectedCanvas == canvas) return;
        _selectedCanvas = canvas;
        RefreshHeader();
        _canvasArea?.QueueRedraw();
    }

    private void BuildUI()
    {
        // ── Toolbar ─────────────────────────────────────────────────
        var toolbar = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        toolbar.AddThemeConstantOverride("separation", 8);
        AddChild(toolbar);

        // 8-px left pad so the toolbar aligns with the canvas frame.
        toolbar.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });

        _headerLabel = new Label
        {
            Text = "No PS1UICanvas selected",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toolbar.AddChild(_headerLabel);

        var zoomLabel = new Label { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center };
        toolbar.AddChild(zoomLabel);

        _zoomCombo = new OptionButton();
        _zoomCombo.AddItem("1×", 1);
        _zoomCombo.AddItem("2×", 2);
        _zoomCombo.AddItem("3×", 3);
        _zoomCombo.AddItem("4×", 4);
        _zoomCombo.Select(1);                 // default 2×
        _zoomCombo.ItemSelected += OnZoomChanged;
        toolbar.AddChild(_zoomCombo);

        _gridToggle = new CheckButton { Text = "Grid", ButtonPressed = _showGrid };
        _gridToggle.Toggled += OnGridToggled;
        toolbar.AddChild(_gridToggle);

        // 8-px right pad to mirror the left.
        toolbar.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });

        // ── Scrolling canvas area ───────────────────────────────────
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        AddChild(scroll);

        // Center-wrapper keeps the canvas centered when the viewport
        // is wider than the zoomed frame.
        var center = new CenterContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scroll.AddChild(center);

        _canvasArea = new Control();
        UpdateCanvasSize();
        _canvasArea.Draw += OnDrawCanvas;
        center.AddChild(_canvasArea);
    }

    private void OnZoomChanged(long idx)
    {
        _zoom = Mathf.Clamp((int)(_zoomCombo?.GetItemId((int)idx) ?? 2), 1, 8);
        UpdateCanvasSize();
        _canvasArea?.QueueRedraw();
    }

    private void OnGridToggled(bool on)
    {
        _showGrid = on;
        _canvasArea?.QueueRedraw();
    }

    private void UpdateCanvasSize()
    {
        if (_canvasArea == null) return;
        _canvasArea.CustomMinimumSize = new Vector2(PsxWidth * _zoom, PsxHeight * _zoom);
    }

    private void RefreshHeader()
    {
        if (_headerLabel == null) return;
        if (_selectedCanvas == null)
        {
            _headerLabel.Text = "No PS1UICanvas selected";
            return;
        }
        int elementCount = 0;
        foreach (var child in _selectedCanvas.GetChildren())
            if (child is PS1UIElement) elementCount++;
        string name = string.IsNullOrEmpty(_selectedCanvas.CanvasName)
            ? _selectedCanvas.Name
            : _selectedCanvas.CanvasName;
        string themed = _selectedCanvas.Theme != null ? " · themed" : "";
        _headerLabel.Text = $"{name}  —  {elementCount} element(s){themed}";
    }

    // ─── Drawing ────────────────────────────────────────────────────

    private void OnDrawCanvas()
    {
        if (_canvasArea == null) return;
        int z = _zoom;
        var sz = new Vector2(PsxWidth * z, PsxHeight * z);

        // 16-px checkered background so the PSX frame reads even
        // against dark themes.
        for (int y = 0; y < PsxHeight; y += 16)
        {
            for (int x = 0; x < PsxWidth; x += 16)
            {
                bool checker = ((x / 16) + (y / 16)) % 2 == 0;
                _canvasArea.DrawRect(
                    new Rect2(x * z, y * z, 16 * z, 16 * z),
                    checker ? BgA : BgB,
                    filled: true);
            }
        }

        if (_showGrid) DrawGrid(z);

        // Frame last so it sits on top of the bg but under elements.
        _canvasArea.DrawRect(new Rect2(Vector2.Zero, sz), FrameColor, filled: false, width: 1f);

        if (_selectedCanvas == null) return;

        foreach (var child in _selectedCanvas.GetChildren())
            if (child is PS1UIElement el && el.VisibleOnLoad)
                DrawElement(el, z);
    }

    private void DrawGrid(int z)
    {
        if (_canvasArea == null) return;
        // Minor grid every 8 px, major every 32 px. Matches the 8-px
        // spacing rhythm from docs/ui-ux-plan.md § Visual language.
        for (int x = 0; x <= PsxWidth; x += 8)
        {
            bool major = x % 32 == 0;
            _canvasArea.DrawLine(
                new Vector2(x * z, 0),
                new Vector2(x * z, PsxHeight * z),
                major ? GridMajor : GridMinor,
                1f);
        }
        for (int y = 0; y <= PsxHeight; y += 8)
        {
            bool major = y % 32 == 0;
            _canvasArea.DrawLine(
                new Vector2(0, y * z),
                new Vector2(PsxWidth * z, y * z),
                major ? GridMajor : GridMinor,
                1f);
        }
    }

    private void DrawElement(PS1UIElement el, int z)
    {
        if (_canvasArea == null) return;
        var color = ResolveElementColor(el);
        var rect = new Rect2(el.X * z, el.Y * z, el.Width * z, el.Height * z);

        switch (el.Type)
        {
            case PS1UIElementType.Box:
                _canvasArea.DrawRect(rect, color, filled: true);
                break;

            case PS1UIElementType.Text:
                // Faint bounds rect so the author sees the layout box
                // even when the text doesn't fill it.
                _canvasArea.DrawRect(rect, BoundsColor, filled: false, width: 1f);

                // Use Godot's default editor font at a size that reads
                // at the current zoom. PSX glyphs are ~8 px tall; we
                // approximate with zoom-scaled font size so the preview
                // feels proportional. Per-glyph PSX-font rasterization
                // is a later polish (docs/ui-ux-plan.md § G "Live
                // PS1-quantized preview").
                var font = ThemeDB.FallbackFont;
                int fontSize = Mathf.Max(8, 8 * z);
                string text = string.IsNullOrEmpty(el.Text) ? "(empty)" : el.Text;
                var textColor = string.IsNullOrEmpty(el.Text) ? new Color(color, 0.4f) : color;

                // Wrap to element width so authors see overflow early.
                _canvasArea.DrawMultilineString(
                    font,
                    new Vector2(el.X * z + 2, el.Y * z + fontSize),
                    text,
                    HorizontalAlignment.Left,
                    el.Width * z,
                    fontSize,
                    maxLines: -1,
                    modulate: textColor);
                break;
        }
    }

    // Mirrors Exporter.SceneCollector.ResolveElementColor so the
    // preview matches what actually ships. When the ThemeSlot is
    // Custom or no Theme is assigned, falls back to the authored
    // Color (same contract as the exporter).
    private static Color ResolveElementColor(PS1UIElement el)
    {
        if (el.ThemeSlot == PS1UIThemeSlot.Custom) return el.Color;
        if (el.GetParent() is not PS1UICanvas parent || parent.Theme is null)
            return el.Color;
        var t = parent.Theme;
        return el.ThemeSlot switch
        {
            PS1UIThemeSlot.Text      => t.TextColor,
            PS1UIThemeSlot.Accent    => t.AccentColor,
            PS1UIThemeSlot.Bg        => t.BgColor,
            PS1UIThemeSlot.BgBorder  => t.BgBorderColor,
            PS1UIThemeSlot.Highlight => t.HighlightColor,
            PS1UIThemeSlot.Warning   => t.WarningColor,
            PS1UIThemeSlot.Danger    => t.DangerColor,
            PS1UIThemeSlot.Neutral   => t.NeutralColor,
            _ => el.Color,
        };
    }

    // Periodic redraw catches inspector edits without needing to
    // subscribe to every property. 10 Hz is plenty for eyeball
    // feedback and keeps editor CPU in the noise floor.
    private double _redrawAccum;
    public override void _Process(double delta)
    {
        _redrawAccum += delta;
        if (_redrawAccum >= 0.1)
        {
            _redrawAccum = 0;
            RefreshHeader();
            _canvasArea?.QueueRedraw();
        }
    }
}
#endif
