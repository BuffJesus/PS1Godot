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

    private Label? _sceneNameLabel;
    private Label? _sceneStatsLabel;
    private BudgetRow? _triRow;
    private BudgetRow? _vramRow;
    private BudgetRow? _spuRow;

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

        // ── Scene section ───────────────────────────────────────────────
        AddSectionHeader(inner, "Scene");

        _sceneNameLabel = new Label
        {
            Text = "— no scene open —",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _sceneNameLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.85f));
        inner.AddChild(_sceneNameLabel);

        _sceneStatsLabel = new Label
        {
            Text = "Open a scene with a PS1Scene node to see budgets here.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _sceneStatsLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.55f));
        inner.AddChild(_sceneStatsLabel);

        // Budget rows — triangle count, VRAM, SPU. Each gets a label
        // above its bar with "used / max" text and the bar colored per
        // BudgetColor(). Rows hide entirely until scene stats are valid.
        _triRow = new BudgetRow("Triangles");
        inner.AddChild(_triRow.Label);
        inner.AddChild(_triRow.Bar);

        _vramRow = new BudgetRow("VRAM");
        inner.AddChild(_vramRow.Label);
        inner.AddChild(_vramRow.Bar);

        _spuRow = new BudgetRow("SPU");
        inner.AddChild(_spuRow.Label);
        inner.AddChild(_spuRow.Bar);

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

    // Called by the plugin on scene-change or manual refresh.
    // When the scene lacks a PS1Scene node, the stats section reverts
    // to a hint so the author knows what's missing.
    public void ApplySceneStats(SceneStats.Result stats)
    {
        if (_sceneNameLabel == null || _sceneStatsLabel == null ||
            _triRow == null || _vramRow == null || _spuRow == null) return;

        if (!stats.HasPS1Scene)
        {
            _sceneNameLabel.Text = "— no PS1Scene —";
            _sceneStatsLabel.Text = "Add a PS1Scene node to see budgets here.";
            _triRow.Hide();
            _vramRow.Hide();
            _spuRow.Hide();
            return;
        }

        _sceneNameLabel.Text = stats.SceneName ?? "scene";
        _sceneStatsLabel.Text =
            $"{stats.MeshCount} meshes · {stats.UniqueTextureCount} textures · {stats.AudioClipCount} audio clips";

        if (stats.TargetTriangles > 0)
        {
            _triRow.Show(
                $"Triangles  {stats.TriangleCount} / {stats.TargetTriangles}",
                stats.TriangleCount, stats.TargetTriangles);
        }
        else
        {
            _triRow.ShowLabelOnly($"Triangles  {stats.TriangleCount} (no budget set)");
        }

        _vramRow.Show(
            $"VRAM  {FormatKb(stats.VramEstimateBytes)} / {FormatKb(SceneStats.VramBudgetBytes)}",
            stats.VramEstimateBytes, SceneStats.VramBudgetBytes);

        _spuRow.Show(
            $"SPU  {FormatKb(stats.SpuEstimateBytes)} / {FormatKb(SceneStats.SpuBudgetBytes)}",
            stats.SpuEstimateBytes, SceneStats.SpuBudgetBytes);
    }

    private static string FormatKb(long bytes) => $"{bytes / 1024} KB";

    // Green up to 80 %, amber to 95 %, red above. Matches the palette
    // the docs/ui-ux-plan.md § B dock sketch calls out.
    private static Color BudgetColor(double ratio)
    {
        if (ratio < 0.80) return new Color(0.35f, 0.80f, 0.40f);
        if (ratio < 0.95) return new Color(0.95f, 0.75f, 0.25f);
        return new Color(0.90f, 0.25f, 0.25f);
    }

    // A label + thin colored progress bar, controlled as a pair. Lives
    // inside the dock so the instantiation order stays obvious: the
    // label always renders directly above its bar.
    private sealed class BudgetRow
    {
        public readonly Label Label;
        public readonly ProgressBar Bar;

        public BudgetRow(string initialText)
        {
            Label = new Label { Text = initialText, Visible = false };
            Label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.70f));
            Bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = 0,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 10),
                Visible = false,
            };
        }

        public void Show(string text, long used, long max)
        {
            Label.Text = text;
            Label.Visible = true;
            Bar.MaxValue = max;
            Bar.Value = System.Math.Min(used, max);
            Bar.SelfModulate = BudgetColor((double)used / max);
            Bar.Visible = true;
        }

        public void ShowLabelOnly(string text)
        {
            Label.Text = text;
            Label.Visible = true;
            Bar.Visible = false;
        }

        public void Hide()
        {
            Label.Visible = false;
            Bar.Visible = false;
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
