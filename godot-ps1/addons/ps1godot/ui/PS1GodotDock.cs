#if TOOLS
using Godot;

namespace PS1Godot.UI;

// The PS1Godot dock panel — canonical home for the plugin's primary
// actions. See docs/ui-ux-plan.md § "Surfaces → B". Minimal skeleton
// for now: a big Run-on-PSX button + three secondary actions + a
// placeholder Scene section. Budget bars and Setup detection arrive
// as Phase 3 items wire in; the structure below is sized for them.
[Tool]
public partial class PS1GodotDock : VBoxContainer
{
    // Signals — the owning plugin wires these to its action handlers.
    // Kept as signals (not direct method calls) so the dock doesn't
    // need a reference to the plugin and stays unit-testable.
    [Signal] public delegate void RunOnPsxRequestedEventHandler();
    [Signal] public delegate void BuildPsxsplashRequestedEventHandler();
    [Signal] public delegate void LaunchEmulatorRequestedEventHandler();
    [Signal] public delegate void AnalyzeTexturesRequestedEventHandler();
    [Signal] public delegate void ExportOnlyRequestedEventHandler();

    // PS1 red — the branded accent from docs/ui-ux-plan.md § Visual language.
    private static readonly Color AccentRed = new(0xCE / 255f, 0x21 / 255f, 0x27 / 255f);

    private Label? _sceneLabel;

    public PS1GodotDock()
    {
        Name = "PS1Godot";
        // 8-pixel grid per the UI plan. Padding and gaps land on multiples.
        CustomMinimumSize = new Vector2(220, 0);
        AddThemeConstantOverride("separation", 8);
        SizeFlagsVertical = SizeFlags.ExpandFill;

        BuildUI();
    }

    private void BuildUI()
    {
        // Outer margin so content doesn't kiss the dock edges.
        var margin = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        AddChild(margin);

        var inner = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        inner.AddThemeConstantOverride("separation", 12);
        margin.AddChild(inner);

        // ── Primary CTA ─────────────────────────────────────────────────
        var run = new Button
        {
            Text = "▶ Run on PSX",
            TooltipText = "Export splashpack, rebuild runtime if needed, launch PCSX-Redux.",
            CustomMinimumSize = new Vector2(0, 40),
        };
        run.AddThemeColorOverride("font_color", Colors.White);
        run.AddThemeColorOverride("font_hover_color", Colors.White);
        run.AddThemeColorOverride("font_pressed_color", Colors.White);
        ApplyAccentStyle(run);
        run.Pressed += () => EmitSignal(SignalName.RunOnPsxRequested);
        inner.AddChild(run);

        // ── Secondary action row ────────────────────────────────────────
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 4);
        inner.AddChild(actions);

        var build = MakeSecondary("Build", "Compile the psxsplash MIPS runtime (requires toolchain).");
        build.Pressed += () => EmitSignal(SignalName.BuildPsxsplashRequested);
        actions.AddChild(build);

        var launch = MakeSecondary("Launch", "Launch PCSX-Redux without re-exporting the scene.");
        launch.Pressed += () => EmitSignal(SignalName.LaunchEmulatorRequested);
        actions.AddChild(launch);

        var export = MakeSecondary("Export", "Export the current scene to a splashpack without launching the emulator.");
        export.Pressed += () => EmitSignal(SignalName.ExportOnlyRequested);
        actions.AddChild(export);

        var analyze = MakeSecondary("Analyze", "Scan res:// textures and report PS1 compliance (bit depth, VRAM cost).");
        analyze.Pressed += () => EmitSignal(SignalName.AnalyzeTexturesRequested);
        actions.AddChild(analyze);

        // ── Scene section (placeholder until dry-run stats land) ────────
        AddSectionHeader(inner, "Scene");
        _sceneLabel = new Label
        {
            Text = "Open a scene with a PS1Scene node to see budgets here.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _sceneLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.55f));
        inner.AddChild(_sceneLabel);

        // ── Setup section (placeholder until Phase 0.5 detection lands) ─
        AddSectionHeader(inner, "Setup");
        var setupHint = new Label
        {
            Text = "Dependency detection arrives with Phase 0.5. For now, see SETUP.md.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        setupHint.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.55f));
        inner.AddChild(setupHint);

        // Spacer pushes everything to the top.
        var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        inner.AddChild(spacer);
    }

    // Hook for future wiring — plugin will call this with scene stats
    // after an export dry-run so the Scene section shows budgets.
    public void SetSceneInfo(string text)
    {
        if (_sceneLabel != null)
        {
            _sceneLabel.Text = text;
        }
    }

    private static Button MakeSecondary(string text, string tooltip)
    {
        var b = new Button
        {
            Text = text,
            TooltipText = tooltip,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        return b;
    }

    private static void ApplyAccentStyle(Button button)
    {
        // StyleBoxFlat for each button state. The accent drifts slightly
        // lighter on hover and darker on press — matches the "modern"
        // tenet of the UI/UX plan (subtle motion instead of big shifts).
        var normal = MakeAccentStyle(AccentRed);
        var hover = MakeAccentStyle(AccentRed.Lightened(0.08f));
        var pressed = MakeAccentStyle(AccentRed.Darkened(0.12f));
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeStyleboxOverride("focus", hover);
    }

    private static StyleBoxFlat MakeAccentStyle(Color c)
    {
        var s = new StyleBoxFlat
        {
            BgColor = c,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        return s;
    }

    private static void AddSectionHeader(Container parent, string title)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 8);
        parent.AddChild(h);

        var label = new Label { Text = title };
        label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.75f));
        h.AddChild(label);

        var rule = new HSeparator { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        h.AddChild(rule);
    }
}
#endif
