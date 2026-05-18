#if TOOLS
using System.Collections.Generic;
using Godot;

namespace PS1Godot.UI;

// PS1 Doctor — unified validator dock.
//
// The PS1GodotDock surfaces a one-line export summary + an 8-row
// offender list capped for compactness. Authors with complex scenes
// hit the cap regularly and have to read raw warnings out of the
// Output panel.
//
// PS1 Doctor is the "show me everything" view: full offender list,
// grouped by category, severity-filterable, with the same click-to-
// focus affordance as the small dock. Data source is the same
// LastExportSummary — Plugin pushes the snapshot via
// ApplyLastExportSummary after every export run.
//
// Slice 1 (MVP): inline category classification by parsing the
// Reason field, three severity checkboxes, scrollable list, click
// node names to highlight in the SceneTree. No standalone refresh —
// the dock updates whenever an export runs.
//
// Future slices: extend `LastExportSummary.Offender` with an
// explicit Category field, add a Refresh button that re-runs
// validators on the current scene without writing the splashpack,
// add severity-promotion rules (e.g. "VRAM 95%+ promotes Warning →
// Blocker before shipping"), and tab-style sections per the
// production-tooling RFC.
public partial class PS1DoctorDock : VBoxContainer
{
    private Label?           _headerLabel;
    private CheckBox?        _showErrors;
    private CheckBox?        _showWarnings;
    private Label?           _emptyLabel;
    private VBoxContainer?   _categoryList;

    // Stash the most recent summary so toggling filters re-renders
    // without needing a fresh export.
    private LastExportSummary? _summary;

    private enum Category
    {
        Uv,        // UV out-of-range — PSX will misrender
        Mesh,      // vertex-reuse / mesh-cleanup candidates
        Texture,   // VRAM / atlas / bpp warnings
        Audio,     // SPU / XA / CDDA routing or budget
        Animation, // keyframe / fps / clip-size warnings
        Decal,     // overlap / stack-depth warnings
        Other,     // catch-all for unclassified
    }

    public PS1DoctorDock()
    {
        Name = "PS1 Doctor";
        SizeFlagsVertical   = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        BuildUi();
    }

    private void BuildUi()
    {
        AddThemeConstantOverride("separation", 6);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        AddChild(header);

        _headerLabel = new Label
        {
            Text = "Run an export to populate.",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.AddChild(_headerLabel);

        _showErrors = new CheckBox { Text = "Errors", ButtonPressed = true };
        _showErrors.Toggled += _ => Rebuild();
        header.AddChild(_showErrors);

        _showWarnings = new CheckBox { Text = "Warnings", ButtonPressed = true };
        _showWarnings.Toggled += _ => Rebuild();
        header.AddChild(_showWarnings);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        AddChild(scroll);

        _categoryList = new VBoxContainer
        {
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _categoryList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_categoryList);

        _emptyLabel = new Label
        {
            Text = "No issues to show.",
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _emptyLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.85f, 0.55f));
        AddChild(_emptyLabel);
    }

    // Called from PS1GodotPlugin once per export, with the snapshot
    // accumulated across all scenes. Stash + render.
    public void ApplyLastExportSummary(LastExportSummary? summary)
    {
        _summary = summary;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_headerLabel == null || _categoryList == null || _emptyLabel == null) return;

        foreach (var child in _categoryList.GetChildren()) child.QueueFree();

        if (_summary == null || _summary.ScenesExported == 0)
        {
            _headerLabel.Text = "Run an export to populate.";
            _headerLabel.RemoveThemeColorOverride("font_color");
            _emptyLabel.Visible = false;
            return;
        }

        // Header mirrors the small dock's headline but slightly more
        // verbose since we have horizontal real estate here.
        _headerLabel.Text = $"{_summary.ScenesExported} scene(s)  •  " +
                            $"{_summary.Errors} error(s), {_summary.Warnings} warning(s)";
        Color headerColor = _summary.Severity switch
        {
            SummarySeverity.Error   => new Color(0.90f, 0.40f, 0.40f),
            SummarySeverity.Warning => new Color(0.95f, 0.80f, 0.35f),
            _                       => new Color(0.55f, 0.85f, 0.55f),
        };
        _headerLabel.AddThemeColorOverride("font_color", headerColor);

        // Partition offenders by category. Inline classification by
        // parsing Reason — works for slice 1; refactor target is an
        // explicit Category enum on the Offender record.
        var byCategory = new Dictionary<Category, List<LastExportSummary.Offender>>();
        foreach (var off in _summary.Offenders)
        {
            if (off.Tier == SummarySeverity.Error   && !(_showErrors?.ButtonPressed   ?? true)) continue;
            if (off.Tier == SummarySeverity.Warning && !(_showWarnings?.ButtonPressed ?? true)) continue;
            var cat = ClassifyOffender(off);
            if (!byCategory.TryGetValue(cat, out var list))
            {
                list = new List<LastExportSummary.Offender>();
                byCategory[cat] = list;
            }
            list.Add(off);
        }

        // Render categories in a fixed display order regardless of
        // first-seen order — keeps the layout stable across runs.
        var order = new[] { Category.Uv, Category.Texture, Category.Audio,
                            Category.Mesh, Category.Animation, Category.Decal,
                            Category.Other };

        int totalShown = 0;
        foreach (var cat in order)
        {
            if (!byCategory.TryGetValue(cat, out var list) || list.Count == 0) continue;
            _categoryList.AddChild(BuildCategoryHeader(cat, list.Count));
            foreach (var off in list)
            {
                _categoryList.AddChild(BuildOffenderRow(off));
                totalShown++;
            }
        }

        _emptyLabel.Visible = totalShown == 0;
    }

    private static Category ClassifyOffender(LastExportSummary.Offender off)
    {
        string r = off.Reason?.ToLowerInvariant() ?? "";
        if (r.Contains("uv "))                      return Category.Uv;
        if (r.Contains("vertex reuse"))             return Category.Mesh;
        if (r.Contains("merge-by-distance"))        return Category.Mesh;
        if (r.Contains("texture") || r.Contains("vram") ||
            r.Contains("atlas")   || r.Contains("4bpp") || r.Contains("8bpp")) return Category.Texture;
        if (r.Contains("audio") || r.Contains("spu") || r.Contains("xa") ||
            r.Contains("cdda")  || r.Contains("adpcm"))  return Category.Audio;
        if (r.Contains("animation") || r.Contains("keyframe") ||
            r.Contains("clip")      || r.Contains("fps"))     return Category.Animation;
        if (r.Contains("decal") || r.Contains("translucent")) return Category.Decal;
        return Category.Other;
    }

    private static string CategoryLabel(Category c) => c switch
    {
        Category.Uv        => "UV",
        Category.Mesh      => "Mesh quality",
        Category.Texture   => "Texture / VRAM",
        Category.Audio     => "Audio",
        Category.Animation => "Animation",
        Category.Decal     => "Decal stack",
        _                  => "Other",
    };

    private static Control BuildCategoryHeader(Category cat, int count)
    {
        var lbl = new Label
        {
            Text = $"{CategoryLabel(cat)}  —  {count}",
        };
        lbl.AddThemeColorOverride("font_color", new Color(0.75f, 0.85f, 1.00f));
        // Slight top margin so categories visually breathe apart.
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 4);
        margin.AddChild(lbl);
        return margin;
    }

    private static Control BuildOffenderRow(LastExportSummary.Offender off)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        string glyph = off.Tier == SummarySeverity.Error ? "✗" : "▲";
        Color tier = off.Tier == SummarySeverity.Error
            ? new Color(0.90f, 0.40f, 0.40f)
            : new Color(0.95f, 0.80f, 0.35f);
        var glyphLabel = new Label { Text = glyph, CustomMinimumSize = new Vector2(16, 0) };
        glyphLabel.AddThemeColorOverride("font_color", tier);
        row.AddChild(glyphLabel);

        var link = new LinkButton
        {
            Text = off.Name,
            TooltipText = off.Reason,
            CustomMinimumSize = new Vector2(220, 0),
        };
        link.Underline = LinkButton.UnderlineMode.OnHover;
        link.AddThemeColorOverride("font_color", tier);
        link.Pressed += () => FocusNodeByName(off.Name);
        row.AddChild(link);

        var reason = new Label
        {
            Text = off.Reason,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        reason.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        row.AddChild(reason);

        return row;
    }

    // Same click-to-focus that PS1GodotDock uses. Duplicated here
    // rather than shared via a helper because the small dock's
    // implementation lives in #if TOOLS too and crossing dock
    // boundaries for one method isn't worth the indirection.
    private static void FocusNodeByName(string name)
    {
        var root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root == null) return;
        var match = FindByName(root, name);
        if (match == null) return;
        EditorInterface.Singleton.GetSelection().Clear();
        EditorInterface.Singleton.GetSelection().AddNode(match);
        EditorInterface.Singleton.EditNode(match);
    }

    private static Node? FindByName(Node node, string name)
    {
        if (node.Name == name) return node;
        foreach (var child in node.GetChildren())
        {
            var found = FindByName(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
#endif
