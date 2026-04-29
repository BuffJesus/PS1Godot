#if TOOLS
using Godot;

namespace PS1Godot.UI;

// docs/ui-ux-plan.md Surface C — translucent budget read-out at the
// top of the 3D viewport. Authors see live tri / VRAM / SPU pressure
// while modeling without flipping over to the dock. Color-coded to
// match the dock's budget bars: green at <80%, amber 80–95%, red 95+%.
//
// Registers via AddControlToContainer(ContainerSpatialEditorMenu).
// Updates on every SceneChanged signal, same hook the dock uses.
// Toggleable via the tiny "i" button per ui-ux-plan.md.
[Tool]
public partial class PS1ViewportOverlay : HBoxContainer
{
    private Label _label = null!;
    private Button _toggleBtn = null!;
    private bool _statsVisible = true;

    public PS1ViewportOverlay()
    {
        // Matches the 8 px grid + accent style. MarginContainer-style
        // padding via separation/theme overrides keeps the row tight
        // against neighbouring viewport controls.
        AddThemeConstantOverride("separation", 4);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;

        _toggleBtn = new Button
        {
            Text = "i",
            Flat = true,
            TooltipText = "Toggle PS1 budget readout in the viewport.",
            CustomMinimumSize = new Vector2(20, 20),
        };
        _toggleBtn.AddThemeFontSizeOverride("font_size", 11);
        _toggleBtn.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.55f));
        _toggleBtn.Pressed += OnToggle;
        AddChild(_toggleBtn);

        _label = new Label
        {
            Text = "PS1: —",
            VerticalAlignment = VerticalAlignment.Center,
            TooltipText = "Live PS1 scene budget readout. tri / VRAM / SPU / texture-page count " +
                          "vs. the PS1Scene caps. Click the dock for a more detailed breakdown.",
        };
        // Slightly faded so it doesn't fight the editor toolbar buttons.
        _label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.85f));
        AddChild(_label);
    }

    private void OnToggle()
    {
        _statsVisible = !_statsVisible;
        _label.Visible = _statsVisible;
        _toggleBtn.AddThemeColorOverride("font_color",
            _statsVisible ? new Color(1, 1, 1, 0.55f) : new Color(1, 1, 1, 0.30f));
    }

    // Called by the plugin on SceneChanged + after each export.
    public void ApplyStats(SceneStats.Result stats)
    {
        if (!_statsVisible) return;

        if (!stats.HasPS1Scene)
        {
            _label.Text = "PS1: no PS1Scene";
            _label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.45f));
            return;
        }

        long vramBudget = SceneStats.VramBudgetBytes;
        long spuBudget = SceneStats.SpuBudgetBytes;
        int triBudget = stats.TargetTriangles > 0 ? stats.TargetTriangles : 1;

        // Worst-of so the color tracks the most over-budget axis.
        float vramPct = (float)stats.VramEstimateBytes / vramBudget;
        float spuPct  = (float)stats.SpuEstimateBytes  / spuBudget;
        float triPct  = stats.TargetTriangles > 0 ? (float)stats.TriangleCount / triBudget : 0f;
        float tpPct   = stats.MaxTexturePages > 0 ? (float)stats.TexturePageEstimate / stats.MaxTexturePages : 0f;
        float worst = Mathf.Max(triPct, Mathf.Max(vramPct, Mathf.Max(spuPct, tpPct)));

        Color col = worst < 0.80f ? new Color(0.55f, 0.85f, 0.55f)
                  : worst < 0.95f ? new Color(0.95f, 0.75f, 0.35f)
                  :                 new Color(0.95f, 0.45f, 0.45f);
        _label.AddThemeColorOverride("font_color", col);

        _label.Text = string.Format(
            "PS1: {0} tri  ·  VRAM {1}  ·  SPU {2}  ·  {3} texpages",
            stats.TriangleCount,
            FormatKb(stats.VramEstimateBytes),
            FormatKb(stats.SpuEstimateBytes),
            stats.TexturePageEstimate);
    }

    private static string FormatKb(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        return $"{bytes / 1024} KB";
    }
}
#endif
