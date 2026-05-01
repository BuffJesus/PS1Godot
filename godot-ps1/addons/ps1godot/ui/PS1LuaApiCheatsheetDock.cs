#if TOOLS
using Godot;
using PS1Godot.Tools;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PS1Godot.UI;

// Bottom-panel "PS1 Lua API" tab — searchable cheatsheet rendered
// from the same parser the EmmyLua stub generator uses, so what
// authors browse here matches what their external editor's
// completion knows about (no skew).
//
// Layout: search box at top + "Reload" button. Below: a Tree grouped
// by namespace (Audio / Camera / Entity / Scene / Sound / UI / …).
// Selecting a method shows its full docstring + a "Copy signature"
// button on the right.
[Tool]
public partial class PS1LuaApiCheatsheetDock : VBoxContainer
{
    private LineEdit _search = null!;
    private Tree     _tree   = null!;
    private RichTextLabel _detail = null!;
    private Label    _summary = null!;
    private Button   _copyBtn = null!;

    private List<LuaApiStubGenerator.Bind> _binds = new();
    // Selected bind — drives the detail panel + copy button payload.
    // Null when the user has selected a namespace header instead.
    private LuaApiStubGenerator.Bind? _selected;

    public PS1LuaApiCheatsheetDock()
    {
        Name = "PS1 Lua API";
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

        // ── Top row: search + reload ────────────────────────────────
        var topRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        topRow.AddThemeConstantOverride("separation", 6);
        inner.AddChild(topRow);

        _search = new LineEdit
        {
            PlaceholderText = "Search by name, namespace, or doc text…",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClearButtonEnabled = true,
        };
        _search.TextChanged += _ => RebuildTree();
        topRow.AddChild(_search);

        var reload = new Button
        {
            Text = "↻",
            TooltipText = "Re-parse psxsplash-main/src/luaapi.hh. Use after pulling " +
                          "runtime changes that add new Lua bindings.",
        };
        reload.Pressed += LoadBinds;
        topRow.AddChild(reload);

        // ── Summary line ────────────────────────────────────────────
        _summary = new Label { Text = "" };
        _summary.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.65f));
        _summary.AddThemeFontSizeOverride("font_size", 11);
        inner.AddChild(_summary);

        // ── Split: tree (left) + detail (right) ─────────────────────
        var split = new HSplitContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            // 4.7 deprecated SplitOffset (single int) in favour of
            // SplitOffsets[] for n-way splits. We're a 2-way split so
            // the array has one entry.
            SplitOffsets = new[] { 320 },
        };
        inner.AddChild(split);

        _tree = new Tree
        {
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Single,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _tree.ItemSelected += OnTreeItemSelected;
        split.AddChild(_tree);

        var rightCol = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        rightCol.AddThemeConstantOverride("separation", 6);
        split.AddChild(rightCol);

        _detail = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = false,
            ScrollActive = true,
            SelectionEnabled = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Text = "Select a method on the left to see its signature, docstring, and a copy button.",
        };
        rightCol.AddChild(_detail);

        _copyBtn = new Button
        {
            Text = "Copy signature",
            TooltipText = "Copy the selected method's signature (e.g. Audio.PlayMusic(name, loop)) " +
                          "to the clipboard for paste into a Lua script.",
            Disabled = true,
        };
        _copyBtn.Pressed += OnCopySignature;
        rightCol.AddChild(_copyBtn);

        // Initial parse — keep dock usable on first open.
        LoadBinds();
    }

    // Public entry point for the Tools-menu lookup action: prime the
    // search box with `filter`, refocus it, rebuild the tree. Caller
    // is responsible for making the bottom-panel tab visible so the
    // dock is on screen when this fires.
    public void FocusAndFilter(string filter)
    {
        if (_search == null) return;
        _search.Text = filter ?? "";
        _search.GrabFocus();
        if (filter != null && filter.Length > 0)
        {
            _search.CaretColumn = filter.Length;
            _search.SelectAll();
        }
        RebuildTree();
    }

    private void LoadBinds()
    {
        try
        {
            string projectRoot = LuaApiStubGenerator.ResolveProjectRoot();
            string headerPath = Path.Combine(projectRoot, LuaApiStubGenerator.LuaApiRelPath);
            if (!File.Exists(headerPath))
            {
                _summary.Text = $"luaapi.hh not found at {headerPath}";
                _binds = new List<LuaApiStubGenerator.Bind>();
            }
            else
            {
                _binds = LuaApiStubGenerator.Parse(File.ReadAllLines(headerPath));
                int nsCount = _binds.Select(b => b.Namespace).Distinct().Count();
                _summary.Text = $"{_binds.Count} bindings across {nsCount} namespaces  ({headerPath})";
            }
        }
        catch (System.Exception e)
        {
            _summary.Text = $"Failed to parse luaapi.hh: {e.Message}";
            _binds = new List<LuaApiStubGenerator.Bind>();
        }
        RebuildTree();
    }

    private void RebuildTree()
    {
        _tree.Clear();
        var root = _tree.CreateItem();

        string filter = (_search.Text ?? "").Trim();
        bool hasFilter = !string.IsNullOrEmpty(filter);

        // Namespace bucketing — sorted alpha so the tree reads
        // predictably across reloads.
        var byNs = _binds
            .Where(b => MatchesFilter(b, filter))
            .GroupBy(b => b.Namespace)
            .OrderBy(g => g.Key, System.StringComparer.OrdinalIgnoreCase);

        int matched = 0;
        foreach (var group in byNs)
        {
            var nsItem = _tree.CreateItem(root);
            int n = 0;
            foreach (var bind in group.OrderBy(b => b.Name, System.StringComparer.OrdinalIgnoreCase))
            {
                var item = _tree.CreateItem(nsItem);
                item.SetText(0, $"{bind.Name}({TruncateArgs(bind.RawArgs)})");
                item.SetMetadata(0, _binds.IndexOf(bind));
                n++;
                matched++;
            }
            nsItem.SetText(0, $"{group.Key}  ({n})");
            // Expand all when a filter is active so matches are visible.
            nsItem.Collapsed = !hasFilter && _binds.Count > 30;
        }

        if (hasFilter)
            _summary.Text = $"{matched} match(es) for \"{filter}\"";
    }

    private static bool MatchesFilter(LuaApiStubGenerator.Bind b, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        if (Contains(b.Namespace, filter)) return true;
        if (Contains(b.Name, filter))      return true;
        if (Contains(b.RawArgs, filter))   return true;
        if (Contains(b.RawReturn, filter)) return true;
        foreach (var d in b.Doc) if (Contains(d, filter)) return true;
        return false;

        static bool Contains(string s, string needle)
            => !string.IsNullOrEmpty(s) && s.Contains(needle, System.StringComparison.OrdinalIgnoreCase);
    }

    private static string TruncateArgs(string args)
    {
        if (string.IsNullOrEmpty(args)) return "";
        return args.Length <= 30 ? args : args[..27] + "…";
    }

    private void OnTreeItemSelected()
    {
        var item = _tree.GetSelected();
        if (item == null) { _selected = null; _copyBtn.Disabled = true; return; }
        var meta = item.GetMetadata(0);
        if (meta.VariantType != Variant.Type.Int)
        {
            // Namespace header selected.
            _selected = null;
            _detail.Text = "[i]Namespace[/i] — pick a method below to see its details.";
            _copyBtn.Disabled = true;
            return;
        }
        int idx = meta.AsInt32();
        if (idx < 0 || idx >= _binds.Count) return;

        _selected = _binds[idx];
        _detail.Clear();
        var sb = new System.Text.StringBuilder();
        sb.Append("[b][color=#80c0ff]");
        sb.Append(EscapeBb(_selected.Namespace)).Append('.').Append(EscapeBb(_selected.Name));
        sb.Append("[/color][/b][color=#a0a0a0](").Append(EscapeBb(_selected.RawArgs)).Append(')');
        sb.Append("[/color]");
        if (!string.IsNullOrEmpty(_selected.RawReturn))
            sb.Append(" [color=#a0a0a0]→ ").Append(EscapeBb(_selected.RawReturn)).Append("[/color]");
        sb.Append("\n\n");
        if (_selected.Doc.Count == 0)
            sb.Append("[i](no docstring)[/i]");
        else
            foreach (var line in _selected.Doc) sb.Append(EscapeBb(line)).Append('\n');
        _detail.AppendText(sb.ToString());
        _copyBtn.Disabled = false;
    }

    private void OnCopySignature()
    {
        if (_selected == null) return;
        string sig = $"{_selected.Namespace}.{_selected.Name}({_selected.RawArgs})";
        DisplayServer.ClipboardSet(sig);
        GD.Print($"[PS1Godot] Copied to clipboard: {sig}");
    }

    private static string EscapeBb(string s)
        => s.Replace("[", "[lb]");
}
#endif
