#if TOOLS
using System.IO;
using Godot;

namespace PS1Godot.UI;

// docs/ui-ux-plan.md Surface A — first-run panel that auto-opens
// when the plugin detects a fresh project or missing dependencies.
// Replaces "open SETUP.md, follow 7 steps" with one popup that shows
// status + (eventually) [Install] buttons per dep + an "Open demo
// scene" CTA.
//
// Phase 0.5 minimum: status checklist + open-demo + skip. The
// [Install] buttons proper come later (need careful HTTP download +
// extract per dep). For now the rows show ✓/✗/· with detail strings
// the author can act on manually — same data the dock's Setup
// section uses.
//
// Trigger semantics (handled by PS1GodotPlugin):
//   - Show once per Godot session if SetupDetector returns any
//     Missing rows.
//   - Author can "Skip" to dismiss; project-local flag in
//     ProjectSettings persists the choice. Re-show via Tools →
//     PS1Godot: Audit → Show First-Run Panel.
[Tool]
public partial class PS1FirstRunPanel : Window
{
    public PS1FirstRunPanel()
    {
        Title = "PS1Godot — Welcome";
        InitialPosition = WindowInitialPosition.CenterMainWindowScreen;
        Size = new Vector2I(560, 480);
        Unresizable = false;
        Transient = true;
        Exclusive = true;
        CloseRequested += () => QueueFree();

        BuildUI();
    }

    private void BuildUI()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top",    16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        margin.AddThemeConstantOverride("margin_left",   24);
        margin.AddThemeConstantOverride("margin_right",  24);
        margin.AnchorRight = 1; margin.AnchorBottom = 1;
        AddChild(margin);

        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        margin.AddChild(v);

        // ── Welcome ─────────────────────────────────────────────────
        var welcome = new Label
        {
            Text = "Welcome to PS1Godot — author PS1 games in Godot.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        welcome.AddThemeFontSizeOverride("font_size", 18);
        v.AddChild(welcome);

        var sub = new Label
        {
            Text = "Quick checklist of dependencies. Green = ready, red = needs setup. " +
                   "Once all green, click \"Open demo scene\" to see a working PS1 export.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        sub.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.75f));
        v.AddChild(sub);

        v.AddChild(new HSeparator());

        // ── Dependency rows ─────────────────────────────────────────
        var rows = SetupDetector.Detect();
        foreach (var row in rows)
        {
            v.AddChild(BuildRow(row));
        }

        v.AddChild(new HSeparator());

        // ── Action row ──────────────────────────────────────────────
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        v.AddChild(actions);

        var demoBtn = new Button
        {
            Text = "▶  Open demo scene",
            CustomMinimumSize = new Vector2(180, 40),
        };
        demoBtn.Pressed += OnOpenDemo;
        actions.AddChild(demoBtn);

        var docsBtn = new Button
        {
            Text = "📖  Open SETUP.md",
            CustomMinimumSize = new Vector2(160, 40),
        };
        docsBtn.Pressed += OnOpenDocs;
        actions.AddChild(docsBtn);

        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        actions.AddChild(spacer);

        var skip = new Button
        {
            Text = "Skip — I know what I'm doing",
            Flat = true,
        };
        skip.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.6f));
        skip.Pressed += () => { MarkSkipped(); QueueFree(); };
        actions.AddChild(skip);
    }

    private static Control BuildRow(SetupDetector.Row row)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);

        string glyph = row.Status switch
        {
            SetupDetector.Status.Ok       => "✓",
            SetupDetector.Status.Missing  => "✗",
            _                             => "·",
        };
        Color glyphColor = row.Status switch
        {
            SetupDetector.Status.Ok       => new Color(0.45f, 0.85f, 0.50f),
            SetupDetector.Status.Missing  => new Color(0.95f, 0.45f, 0.45f),
            _                             => new Color(1, 1, 1, 0.55f),
        };
        var g = new Label { Text = glyph, CustomMinimumSize = new Vector2(20, 0) };
        g.AddThemeColorOverride("font_color", glyphColor);
        g.AddThemeFontSizeOverride("font_size", 18);
        h.AddChild(g);

        var name = new Label
        {
            Text = row.Name,
            CustomMinimumSize = new Vector2(140, 0),
        };
        h.AddChild(name);

        var detail = new Label
        {
            Text = row.Detail,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        detail.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.7f));
        h.AddChild(detail);

        return h;
    }

    private void OnOpenDemo()
    {
        // Demo template ships in the plugin. Open it as a fresh editor
        // tab; author saves a duplicate to their project to start.
        EditorInterface.Singleton.OpenSceneFromPath(
            "res://addons/ps1godot/templates/demo_template.tscn");
        QueueFree();
    }

    private void OnOpenDocs()
    {
        OS.ShellOpen("https://github.com/BuffJesus/PS1Godot/blob/main/SETUP.md");
    }

    // ── Skip persistence ────────────────────────────────────────────
    private const string SkipFlagPath = "user://ps1godot_skip_first_run";

    public static bool ShouldShow()
    {
        // Don't show if user already skipped.
        if (Godot.FileAccess.FileExists(SkipFlagPath)) return false;
        // Don't show if all deps healthy.
        var rows = SetupDetector.Detect();
        foreach (var r in rows)
        {
            if (r.Status == SetupDetector.Status.Missing) return true;
        }
        return false;
    }

    private static void MarkSkipped()
    {
        using var f = Godot.FileAccess.Open(SkipFlagPath, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString("skipped");
    }
}
#endif
