#if TOOLS
using Godot;

namespace PS1Godot.UI;

// Header banner for resource types that are still scaffolded — the
// authoring side ships, but the runtime hasn't caught up yet. Surfaces
// the implementation status at the top of the inspector so authors
// don't fill out a macro/family, hit F5, and wonder why nothing fires.
//
// Today: PS1SoundMacro and PS1SoundFamily are the only Phase 5 Stage A
// scaffolds. Add a case here whenever a new authoring-only resource
// lands; remove the case when the runtime catches up.
public partial class PS1ExperimentalBadgeInspector : EditorInspectorPlugin
{
    private static readonly Color BadgeBg     = new(0.50f, 0.40f, 0.10f, 0.90f);
    private static readonly Color BadgeBorder = new(0.95f, 0.75f, 0.25f, 1.00f);
    private static readonly Color BadgeText   = new(1.00f, 0.95f, 0.80f, 1.00f);

    public override bool _CanHandle(GodotObject obj)
        => obj is PS1SoundMacro or PS1SoundFamily;

    public override void _ParseBegin(GodotObject obj)
    {
        string text = obj switch
        {
            PS1SoundMacro =>
                "⚠ Experimental — Phase 5 Stage A. Authoring works end-to-end; the runtime " +
                "sequencer ships in Stage B. Sound.PlayMacro currently no-ops; the macro will " +
                "trigger from Lua once the dispatch path lands.",
            PS1SoundFamily =>
                "⚠ Experimental — Phase 5 Stage A. Authoring works end-to-end; the runtime " +
                "dispatch ships in Stage B. Sound.PlayFamily currently no-ops; variation pools " +
                "will resolve once the dispatch path lands.",
            _ => "⚠ Experimental — runtime not yet wired.",
        };
        AddCustomControl(BuildBadge(text));
    }

    private static Control BuildBadge(string text)
    {
        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        var sb = new StyleBoxFlat
        {
            BgColor               = BadgeBg,
            BorderColor           = BadgeBorder,
            BorderWidthLeft       = 3,
            BorderWidthTop        = 1,
            BorderWidthRight      = 1,
            BorderWidthBottom     = 1,
            CornerRadiusBottomLeft  = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft     = 4,
            CornerRadiusTopRight    = 4,
            ContentMarginLeft     = 8,
            ContentMarginRight    = 8,
            ContentMarginTop      = 4,
            ContentMarginBottom   = 4,
        };
        panel.AddThemeStyleboxOverride("panel", sb);

        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        label.AddThemeColorOverride("font_color", BadgeText);
        label.AddThemeFontSizeOverride("font_size", 11);
        panel.AddChild(label);

        return panel;
    }
}
#endif
