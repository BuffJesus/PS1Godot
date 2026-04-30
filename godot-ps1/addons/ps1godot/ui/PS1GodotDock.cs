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
    private BudgetRow? _texPageRow;
    private Label? _pipelineStatusLabel;
    private Label? _configWarningsLabel;
    private VBoxContainer? _configWarningsRows;
    private Label? _lastExportLabel;
    private VBoxContainer? _lastExportRows;   // click-to-focus list
    private VBoxContainer? _setupBox;
    private Label? _setupSummary;

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
            TooltipText = "Export splashpack, rebuild runtime if needed, launch PCSX-Redux.\nShortcut: F5 (Godot Play button is intercepted by the plugin).",
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

        // ── Preview toggle ──────────────────────────────────────────────
        // Flips the shared ps1.gdshader's quantize+dither uniforms so
        // authors can compare PSX-quantized output to the un-quantized
        // source without editing the .tres files. Works by mutating the
        // ShaderMaterial parameters in place — every PS1MeshInstance
        // sharing ps1_default.tres / ps1_skinned.tres picks it up
        // immediately.
        var previewToggle = new CheckBox
        {
            Text = "PSX preview (5-bit quantize + dither)",
            ButtonPressed = true,
            TooltipText = "When ON: viewport matches shipped PSX look (5-bit/channel + 4×4 Bayer dither). OFF: source colors, no banding/dither.",
        };
        previewToggle.Toggled += pressed => SetPsxPreviewEnabled(pressed);
        inner.AddChild(previewToggle);

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

        // Budget rows — triangle count, VRAM, SPU, texture pages. Each
        // gets a label above its bar with "used / max" text and the bar
        // colored per BudgetColor(). Rows hide entirely until scene
        // stats are valid.
        _triRow = new BudgetRow("Triangles");
        inner.AddChild(_triRow.Label);
        inner.AddChild(_triRow.Bar);

        _vramRow = new BudgetRow("VRAM");
        inner.AddChild(_vramRow.Label);
        inner.AddChild(_vramRow.Bar);

        _spuRow = new BudgetRow("SPU");
        inner.AddChild(_spuRow.Label);
        inner.AddChild(_spuRow.Bar);

        _texPageRow = new BudgetRow("Tex Pages");
        inner.AddChild(_texPageRow.Label);
        inner.AddChild(_texPageRow.Bar);

        // Pipeline status line — shows "Exporting…" / "Building…" /
        // "Launching…" during the Run-on-PSX flow. Hidden when idle.
        _pipelineStatusLabel = new Label
        {
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _pipelineStatusLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.85f, 1.0f));
        inner.AddChild(_pipelineStatusLabel);

        // Live configuration-warning aggregator. Walks the scene tree on
        // every refresh, lists nodes whose _GetConfigurationWarnings is
        // non-empty. Catches authoring drift between exports — e.g.,
        // "PS1Player has no NavRegion" surfaces here as soon as you
        // delete the nav, not after the next F5.
        _configWarningsLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass,
        };
        _configWarningsLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.75f, 0.25f));
        inner.AddChild(_configWarningsLabel);

        _configWarningsRows = new VBoxContainer { Visible = false };
        _configWarningsRows.AddThemeConstantOverride("separation", 2);
        inner.AddChild(_configWarningsRows);

        // Last-export summary headline + click-to-focus row list.
        // Headline carries severity-coded one-line state; rows below
        // expand to per-mesh entries the author can click to jump to
        // the offending node in the scene tree.
        _lastExportLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass,
        };
        _lastExportLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.70f));
        inner.AddChild(_lastExportLabel);

        _lastExportRows = new VBoxContainer { Visible = false };
        _lastExportRows.AddThemeConstantOverride("separation", 2);
        inner.AddChild(_lastExportRows);

        // ── Setup section ───────────────────────────────────────────────
        AddSectionHeader(inner, "Setup");

        _setupSummary = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _setupSummary.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.75f));
        inner.AddChild(_setupSummary);

        _setupBox = new VBoxContainer();
        _setupBox.AddThemeConstantOverride("separation", 2);
        inner.AddChild(_setupBox);

        var recheckBtn = new Button
        {
            Text = "Re-check all",
            TooltipText = "Re-probe all dependencies (toolchain, emulator, submodules). " +
                          "Useful after installing something the panel flagged as missing.",
        };
        recheckBtn.Pressed += RefreshSetupStatus;
        inner.AddChild(recheckBtn);

        RefreshSetupStatus();

        // Spacer pushes everything to the top.
        var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        inner.AddChild(spacer);
    }

    public void RefreshSetupStatus()
    {
        if (_setupBox == null || _setupSummary == null) return;

        foreach (var child in _setupBox.GetChildren())
        {
            child.QueueFree();
        }

        var rows = SetupDetector.Detect();
        int ok = 0, missing = 0;
        foreach (var row in rows)
        {
            if (row.Status == SetupDetector.Status.Ok) ok++;
            else if (row.Status == SetupDetector.Status.Missing) missing++;

            _setupBox.AddChild(BuildSetupRow(row));
        }

        _setupSummary.Text = missing == 0
            ? $"All {rows.Count} dependencies found."
            : $"{missing} of {rows.Count} dependencies missing — hover for details.";
        _setupSummary.AddThemeColorOverride(
            "font_color",
            missing == 0 ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.95f, 0.75f, 0.35f));
    }

    private static Control BuildSetupRow(SetupDetector.Row row)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 6);

        string glyph = row.Status switch
        {
            SetupDetector.Status.Ok => "✓",
            SetupDetector.Status.Missing => "✗",
            _ => "·",
        };
        Color glyphColor = row.Status switch
        {
            SetupDetector.Status.Ok => new Color(0.45f, 0.85f, 0.50f),
            SetupDetector.Status.Missing => new Color(0.95f, 0.45f, 0.45f),
            _ => new Color(1, 1, 1, 0.55f),
        };

        var glyphLabel = new Label { Text = glyph, CustomMinimumSize = new Vector2(14, 0) };
        glyphLabel.AddThemeColorOverride("font_color", glyphColor);
        h.AddChild(glyphLabel);

        var name = new Label
        {
            Text = row.Name,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = row.Detail,
        };
        h.AddChild(name);

        // For missing deps, show a "Copy" button with the install hint.
        if (row.Status == SetupDetector.Status.Missing)
        {
            string? hint = GetInstallHint(row.Name);
            if (hint != null)
            {
                var copyBtn = new Button
                {
                    Text = "Copy",
                    TooltipText = $"Copy to clipboard: {hint}",
                    CustomMinimumSize = new Vector2(50, 0),
                };
                copyBtn.Pressed += () => DisplayServer.ClipboardSet(hint);
                h.AddChild(copyBtn);
            }
        }

        return h;
    }

    private static string? GetInstallHint(string depName) => depName switch
    {
        "MIPS toolchain" => "powershell -File pcsx-redux-main/mips.ps1 install 14.2.0",
        "make"           => "winget install MSYS2.MSYS2",
        "PCSX-Redux"     => "winget install grumpycoders.pcsx-redux",
        "Blender"        => "winget install BlenderFoundation.Blender",
        "psxsplash submodules" => "git submodule update --init --recursive",
        _ => null,
    };

    // Toggle the PSX preview pass on every PS1 ShaderMaterial that
    // shares ps1.gdshader. Loads the two stock .tres files directly
    // (PS1MeshInstance default + PS1SkinnedMesh default) and mutates
    // their uniforms — author-overridden materials with their own
    // ShaderMaterial copy are unaffected (intentional: power users get
    // their own toggle on their own material).
    private static void SetPsxPreviewEnabled(bool enabled)
    {
        int bits = enabled ? 5 : 0;
        SetUniformOnMaterial("res://addons/ps1godot/shaders/ps1_default.tres", bits, enabled);
        SetUniformOnMaterial("res://addons/ps1godot/shaders/ps1_skinned.tres", bits, enabled);
    }

    private static void SetUniformOnMaterial(string resPath, int quantizeBits, bool dither)
    {
        var mat = ResourceLoader.Load<ShaderMaterial>(resPath);
        if (mat == null) return;
        mat.SetShaderParameter("preview_quantize_bits", quantizeBits);
        mat.SetShaderParameter("preview_dither_enabled", dither);
    }

    // Called by the plugin on scene-change or manual refresh.
    // When the scene lacks a PS1Scene node, the stats section reverts
    // to a hint so the author knows what's missing.
    public void ApplySceneStats(SceneStats.Result stats)
    {
        if (_sceneNameLabel == null || _sceneStatsLabel == null ||
            _triRow == null || _vramRow == null || _spuRow == null ||
            _texPageRow == null) return;

        if (!stats.HasPS1Scene)
        {
            _sceneNameLabel.Text = "— no PS1Scene —";
            _sceneStatsLabel.Text = "Add a PS1Scene node to see budgets here.";
            _triRow.Hide();
            _vramRow.Hide();
            _spuRow.Hide();
            _texPageRow.Hide();
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
            $"SPU  {FormatKb(stats.SpuEstimateBytes)} / {FormatKb(SceneStats.SpuBudgetBytes)}  (gameplay-resident)",
            stats.SpuEstimateBytes, SceneStats.SpuBudgetBytes);

        if (stats.MaxTexturePages > 0)
        {
            _texPageRow.Show(
                $"Tex Pages  {stats.TexturePageEstimate} / {stats.MaxTexturePages}",
                stats.TexturePageEstimate, stats.MaxTexturePages);
        }
        else
        {
            _texPageRow.ShowLabelOnly($"Tex Pages  {stats.TexturePageEstimate} (no budget set)");
        }
    }

    /// <summary>
    /// Walk the active scene tree, collect every node whose
    /// _GetConfigurationWarnings() returns a non-empty array, and render
    /// the result as a click-to-focus list. Called from OnSceneChanged and
    /// after each export. Hidden when there are no warnings.
    /// </summary>
    public void RefreshConfigWarnings()
    {
        if (_configWarningsLabel == null || _configWarningsRows == null) return;

        var root = EditorInterface.Singleton?.GetEditedSceneRoot();
        var hits = new System.Collections.Generic.List<(Node Node, string[] Warnings)>();
        if (root != null) CollectConfigWarnings(root, hits);

        foreach (var child in _configWarningsRows.GetChildren()) child.QueueFree();

        if (hits.Count == 0)
        {
            _configWarningsLabel.Visible = false;
            _configWarningsRows.Visible = false;
            return;
        }

        int total = 0;
        foreach (var (_, w) in hits) total += w.Length;
        _configWarningsLabel.Text = hits.Count == 1
            ? $"⚠  1 node needs attention ({total} warning{(total == 1 ? "" : "s")})"
            : $"⚠  {hits.Count} nodes need attention ({total} warnings)";
        _configWarningsLabel.Visible = true;

        foreach (var (node, warnings) in hits)
        {
            string nodeName = (string)node.Name;
            foreach (string msg in warnings)
            {
                _configWarningsRows.AddChild(BuildConfigWarningRow(nodeName, msg));
            }
        }
        _configWarningsRows.Visible = true;
    }

    private static void CollectConfigWarnings(Node n, System.Collections.Generic.List<(Node, string[])> acc)
    {
        // Native Node.Call("_get_configuration_warnings") raises a hard
        // engine error on nodes that don't override it (the GDVIRTUAL
        // dispatch only activates for overrides). Gate with HasMethod so
        // we don't spam the Output panel for every unrelated MeshInstance3D
        // / AnimationKeyframe in the tree.
        if (n.HasMethod("_get_configuration_warnings"))
        {
            try
            {
                string[] warnings = n.Call("_get_configuration_warnings").AsStringArray();
                if (warnings != null && warnings.Length > 0) acc.Add((n, warnings));
            }
            catch { /* defensive — broken overrides shouldn't crash the dock */ }
        }
        foreach (var c in n.GetChildren())
            if (c is Node child) CollectConfigWarnings(child, acc);
    }

    private static Control BuildConfigWarningRow(string nodeName, string message)
    {
        // Truncate long warnings in the row label; full text in tooltip.
        const int maxRowChars = 100;
        string preview = message.Length > maxRowChars
            ? message.Substring(0, maxRowChars - 1) + "…"
            : message;
        var btn = new Button
        {
            Text = $"  {nodeName} — {preview}",
            TooltipText = $"{nodeName}\n\n{message}\n\nClick to focus this node in the scene tree.",
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            ClipText = true,
        };
        btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.75f, 0.25f, 0.95f));
        btn.Pressed += () => FocusNodeByName(nodeName);
        return btn;
    }

    /// <summary>
    /// Show a transient status line during the Run-on-PSX pipeline.
    /// Pass null or empty to hide when the pipeline is done.
    /// </summary>
    public void SetPipelineStatus(string? status)
    {
        if (_pipelineStatusLabel == null) return;
        if (string.IsNullOrEmpty(status))
        {
            _pipelineStatusLabel.Visible = false;
            return;
        }
        _pipelineStatusLabel.Text = status;
        _pipelineStatusLabel.Visible = true;
    }

    // Push the validation summary built during the last
    // OnExportEmptySplashpack run. Color-codes the line: green when no
    // issues, amber when there are. Tooltip carries the multi-line
    // breakdown.
    public void ApplyLastExportSummary(LastExportSummary? summary)
    {
        if (_lastExportLabel == null || _lastExportRows == null) return;
        if (summary == null || summary.ScenesExported == 0)
        {
            _lastExportLabel.Visible = false;
            _lastExportRows.Visible = false;
            return;
        }

        _lastExportLabel.Text = summary.LabelText;
        _lastExportLabel.TooltipText = summary.TooltipText;
        Color headColor = summary.Severity switch
        {
            SummarySeverity.Error   => new Color(0.90f, 0.25f, 0.25f),
            SummarySeverity.Warning => new Color(0.95f, 0.75f, 0.25f),
            _                       => new Color(0.55f, 0.85f, 0.55f),
        };
        _lastExportLabel.AddThemeColorOverride("font_color", headColor);
        _lastExportLabel.Visible = true;

        // Rebuild the click-to-focus rows. Cap at 8 so the dock stays
        // compact; longer reports send the rest to the Output panel.
        foreach (var child in _lastExportRows.GetChildren()) child.QueueFree();
        int rowsShown = 0;
        foreach (var off in summary.Offenders)
        {
            if (rowsShown >= 8) break;
            _lastExportRows.AddChild(BuildOffenderRow(off));
            rowsShown++;
        }
        _lastExportRows.Visible = rowsShown > 0;
    }

    // One row in the click-to-focus list. LinkButton renders as
    // underlined text and feels obviously interactive without needing
    // a button-frame style. Hover tooltip carries the full reason.
    private Control BuildOffenderRow(LastExportSummary.Offender off)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);

        string glyph = off.Tier == SummarySeverity.Error ? "✗" : "▲";
        Color tierColor = off.Tier == SummarySeverity.Error
            ? new Color(0.90f, 0.40f, 0.40f)
            : new Color(0.95f, 0.80f, 0.35f);
        var glyphLabel = new Label { Text = glyph, CustomMinimumSize = new Vector2(14, 0) };
        glyphLabel.AddThemeColorOverride("font_color", tierColor);
        row.AddChild(glyphLabel);

        var link = new LinkButton
        {
            Text = off.Name,
            TooltipText = off.Reason,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        link.Underline = LinkButton.UnderlineMode.OnHover;
        link.AddThemeColorOverride("font_color", tierColor);
        link.Pressed += () => FocusNodeByName(off.Name);
        row.AddChild(link);

        return row;
    }

    // Walks the active scene root looking for a node whose Name
    // matches `name`. First hit wins. Selection update + EditNode
    // brings the scene tree to focus on the offender; the user can
    // press F to frame it in the 3D viewport.
    private static void FocusNodeByName(string name)
    {
        var root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root == null) return;
        var match = FindByName(root, name);
        if (match == null)
        {
            GD.PushWarning($"[PS1Godot] Click-to-focus: no node named '{name}' in the active scene.");
            return;
        }
        var sel = EditorInterface.Singleton.GetSelection();
        sel.Clear();
        sel.AddNode(match);
        EditorInterface.Singleton.EditNode(match);
    }

    private static Node? FindByName(Node n, string name)
    {
        if ((string)n.Name == name) return n;
        foreach (var child in n.GetChildren())
        {
            var hit = FindByName(child, name);
            if (hit != null) return hit;
        }
        return null;
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
