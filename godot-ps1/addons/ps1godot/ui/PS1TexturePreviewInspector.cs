#if TOOLS
using Godot;
using PS1Godot.Exporter;

namespace PS1Godot.UI;

// EditorInspectorPlugin that adds a "PSX Preview (quantized)" panel
// after the standard inspector fields on any node that owns a direct
// Texture + BitDepth pair. Lets authors see the PSX-equivalent
// quantization without exporting + launching the emulator — collapses
// the texture-tuning iteration loop from minutes to milliseconds.
//
// Currently attaches to:
//   - PS1Sky               (always — the sky node has one Texture field)
//   - PS1UIElement         (only when Type=Image — Box/Text/Progress
//                           elements don't reference a texture)
//
// Skipped: PS1MeshInstance / PS1MeshGroup. Mesh textures flow through
// material_override or per-surface materials, not a direct field; the
// preview would need to probe a material chain to find the albedo
// texture. Worth doing once the authoring story for mesh materials is
// settled (Phase 3+ — currently authors edit material_override
// manually, so the preview's value-add is smaller).
public partial class PS1TexturePreviewInspector : EditorInspectorPlugin
{
    public override bool _CanHandle(GodotObject obj)
    {
        if (obj is PS1Sky) return true;
        if (obj is PS1UIElement el && el.Type == PS1UIElementType.Image) return true;
        return false;
    }

    public override void _ParseEnd(GodotObject obj)
    {
        // _ParseEnd is called once per inspected object; the returned
        // Control is reparented + owned by the inspector and freed
        // automatically when the selection changes.
        AddCustomControl(new PS1TexturePreviewControl(obj));
    }
}

// The custom Control rendered after the standard inspector fields. Owns
// the TextureRect + meta label and listens for inspector edits so the
// preview updates live as authors tweak BitDepth or swap the source.
public partial class PS1TexturePreviewControl : VBoxContainer
{
    // Cap the preview at 256px on its larger axis. Keeps the inspector
    // compact and matches the PSX TPage size — nothing bigger fits in
    // a single tpage anyway, so authors can see the full asset at 1:1.
    private const int MaxPreviewPx = 256;
    // Fallback minimum so a missing texture doesn't collapse the
    // section to zero height (which makes the empty-state hint
    // unreadable).
    private const int MinPreviewPx = 96;

    private readonly GodotObject _target;
    private TextureRect _source = null!;
    private TextureRect _preview = null!;
    private Label _meta = null!;

    public PS1TexturePreviewControl(GodotObject target)
    {
        _target = target;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 4);
    }

    public override void _Ready()
    {
        // A thin separator + small header label distinguishes the
        // generated preview from the standard property editors above.
        var sep = new HSeparator();
        AddChild(sep);

        var header = new Label
        {
            Text = "PSX Preview (quantized)",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        header.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f));
        AddChild(header);

        // Side-by-side: source on the left, PSX-quantized on the right.
        // Two equally-weighted columns under a single HBox so authors
        // can A/B at a glance instead of mentally diffing against the
        // FileSystem preview thumbnail. Headers above each panel keep
        // the framing obvious even at small sizes.
        var grid = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("separation", 8);
        AddChild(grid);

        var srcCol = MakePanelColumn("Source", out _source);
        var dstCol = MakePanelColumn("PSX (quantized)", out _preview);
        grid.AddChild(srcCol);
        grid.AddChild(dstCol);

        _meta = new Label { Text = "" };
        _meta.AddThemeFontSizeOverride("font_size", 11);
        _meta.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        AddChild(_meta);

        // Live refresh — fires whenever the inspector commits a value.
        // EditorInterface.GetInspector() returns a stable singleton so
        // we can safely connect/disconnect across our lifetime.
        var inspector = EditorInterface.Singleton.GetInspector();
        inspector.PropertyEdited += OnPropertyEdited;

        Refresh();
    }

    public override void _ExitTree()
    {
        var inspector = EditorInterface.Singleton.GetInspector();
        inspector.PropertyEdited -= OnPropertyEdited;
    }

    private void OnPropertyEdited(string property)
    {
        // Only the two fields that affect the preview output. Re-running
        // the quantizer for unrelated edits (Color, X, Y, etc.) would
        // burn CPU on every keystroke for no visual change.
        if (property == "Texture" || property == "BitDepth" || property == "Type")
            Refresh();
    }

    private void Refresh()
    {
        // Hide the whole preview section when the inspected object
        // can't currently use a texture (e.g. PS1UIElement.Type was
        // switched from Image to Box). Otherwise authors see a stale
        // "no texture assigned" panel hanging around under unrelated
        // properties.
        if (_target is PS1UIElement el && el.Type != PS1UIElementType.Image)
        {
            Visible = false;
            return;
        }
        Visible = true;

        var (tex, bpp) = ReadTextureAndBpp(_target);

        if (tex == null)
        {
            _source.Texture = null;
            _preview.Texture = null;
            _meta.Text = "(no texture assigned)";
            return;
        }

        var srcImage = tex.GetImage();
        if (srcImage == null)
        {
            _source.Texture = null;
            _preview.Texture = null;
            _meta.Text = "(texture has no image data)";
            return;
        }

        var quantized = PS1TexturePreview.Build(srcImage, bpp);
        if (quantized == null)
        {
            _source.Texture = null;
            _preview.Texture = null;
            _meta.Text = "(zero-sized texture)";
            return;
        }

        // Cap each panel at MaxPreviewPx on the larger axis so a 4K
        // source doesn't push the inspector to absurd widths. Both
        // panels get the same size so vertical/horizontal extents
        // match across the A/B comparison.
        int w = quantized.GetWidth();
        int h = quantized.GetHeight();
        int displayW = w;
        int displayH = h;
        if (displayW > MaxPreviewPx || displayH > MaxPreviewPx)
        {
            float scale = (float)MaxPreviewPx / System.Math.Max(displayW, displayH);
            displayW = System.Math.Max(MinPreviewPx, (int)(displayW * scale));
            displayH = System.Math.Max(MinPreviewPx, (int)(displayH * scale));
        }
        else
        {
            displayW = System.Math.Max(MinPreviewPx, displayW);
            displayH = System.Math.Max(MinPreviewPx, displayH);
        }

        _source.Texture = tex;
        _source.CustomMinimumSize = new Vector2(displayW, displayH);

        _preview.Texture = ImageTexture.CreateFromImage(quantized);
        _preview.CustomMinimumSize = new Vector2(displayW, displayH);

        string bppLabel = bpp switch
        {
            PSXBPP.TEX_4BIT => "4bpp (16-color CLUT)",
            PSXBPP.TEX_8BIT => "8bpp (256-color CLUT)",
            PSXBPP.TEX_16BIT => "16bpp (direct, 2× VRAM)",
            _ => bpp.ToString(),
        };
        long vramBytes = EstimateVramBytes(w, h, bpp);
        _meta.Text = $"{w}×{h} → {bppLabel}  ·  {FormatBytes(vramBytes)} VRAM";
    }

    private static (Texture2D? tex, PSXBPP bpp) ReadTextureAndBpp(GodotObject obj)
    {
        return obj switch
        {
            PS1Sky sky => (sky.Texture, sky.BitDepth),
            PS1UIElement el => (el.Texture, el.BitDepth),
            _ => (null, PSXBPP.TEX_8BIT),
        };
    }

    private static VBoxContainer MakePanelColumn(string label, out TextureRect rect)
    {
        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        col.AddThemeConstantOverride("separation", 2);
        var h = new Label
        {
            Text = label,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        h.AddThemeFontSizeOverride("font_size", 10);
        h.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.7f));
        col.AddChild(h);

        rect = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(MinPreviewPx, MinPreviewPx),
            // Nearest-neighbor on both panels so the source's pixel art
            // and the quantized output read the same way; bilinear would
            // hide the dither pattern entirely.
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        col.AddChild(rect);
        return col;
    }

    // VRAM cost estimate matching SceneStats.EstimateTextureVramBytes:
    // packed pixel data + one CLUT (0 for 16bpp). The exporter caps
    // texture dimensions at 256×256 (PSX VRAM page max); mirror that
    // here so the inspector reports the same number the dock will.
    private static long EstimateVramBytes(int width, int height, PSXBPP bpp)
    {
        int w = System.Math.Min(width, 256);
        int h = System.Math.Min(height, 256);
        long pixels = (long)w * h;
        long textureBytes = bpp switch
        {
            PSXBPP.TEX_4BIT => pixels / 2,
            PSXBPP.TEX_8BIT => pixels,
            PSXBPP.TEX_16BIT => pixels * 2,
            _ => pixels,
        };
        long clutBytes = bpp switch
        {
            PSXBPP.TEX_4BIT => 16 * 2,
            PSXBPP.TEX_8BIT => 256 * 2,
            _ => 0,
        };
        return textureBytes + clutBytes;
    }

    private static string FormatBytes(long b) =>
        b < 1024 ? $"{b} B" : $"{b / 1024.0:F1} KB";
}
#endif
