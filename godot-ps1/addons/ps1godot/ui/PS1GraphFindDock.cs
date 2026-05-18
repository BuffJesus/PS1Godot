#if TOOLS
using System.Collections.Generic;
using Godot;
using PS1Godot.Graph;

namespace PS1Godot.UI;

// PS1 Graph Find — cross-graph substring search (UE port-plan pick #4,
// mirrors UE's FindInBlueprints).
//
// Quest / dialogue / FSM authoring leans heavily on string keys —
// Persist flag names, FSM event names, audio clip names, outcome
// ids. Typos compile silently: a `Persist.Set("met_bob")` followed
// by a Condition node testing `met_bbo` runs forever without the
// branch ever taking the true arm. The dialogue authoring doc names
// this as the #1 troubleshooting trap.
//
// This dock scans every `.tres` under `res://` for `PS1GraphResource`
// resources, then for each node checks Payload + Payloads[] +
// EnabledState against a search query. Matches list the graph
// path + node id + matching text; clicking a result opens the
// .tres in Godot's FileSystem (no jump-to-node yet — slice 2).
//
// Uses `PS1GraphCompiler.RobustLoad` so the Godot 4.7-dev5 binding
// quirk doesn't drop search results silently.
public partial class PS1GraphFindDock : VBoxContainer
{
    private LineEdit?      _queryEdit;
    private CheckBox?      _caseSensitive;
    private CheckBox?      _includeDisabled;
    private Label?         _statusLabel;
    private VBoxContainer? _resultsList;

    private sealed record Hit(string GraphPath, string Kind, int NodeId, int PayloadSlot, string MatchedText, bool DisabledNode);

    public PS1GraphFindDock()
    {
        Name = "PS1 Graph Find";
        SizeFlagsVertical   = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        BuildUi();
    }

    private void BuildUi()
    {
        AddThemeConstantOverride("separation", 6);

        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 6);
        AddChild(toolbar);

        toolbar.AddChild(new Label { Text = "Find:", VerticalAlignment = VerticalAlignment.Center });

        _queryEdit = new LineEdit
        {
            PlaceholderText = "flag name / event name / line text…",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _queryEdit.TextSubmitted += _ => Search();
        toolbar.AddChild(_queryEdit);

        var searchBtn = new Button { Text = "Search" };
        searchBtn.Pressed += Search;
        toolbar.AddChild(searchBtn);

        _caseSensitive = new CheckBox { Text = "Case sensitive", ButtonPressed = false };
        toolbar.AddChild(_caseSensitive);

        _includeDisabled = new CheckBox { Text = "Include disabled", ButtonPressed = true };
        toolbar.AddChild(_includeDisabled);

        _statusLabel = new Label { Text = "Enter a query and press Search." };
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.70f, 0.70f));
        AddChild(_statusLabel);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        AddChild(scroll);

        _resultsList = new VBoxContainer
        {
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _resultsList.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_resultsList);
    }

    private void Search()
    {
        if (_queryEdit == null || _resultsList == null || _statusLabel == null) return;
        string query = _queryEdit.Text ?? "";
        if (string.IsNullOrEmpty(query))
        {
            _statusLabel.Text = "Empty query.";
            ClearResults();
            return;
        }

        bool caseSensitive  = _caseSensitive?.ButtonPressed   ?? false;
        bool includeDisabled = _includeDisabled?.ButtonPressed ?? true;

        ClearResults();

        var hits = new List<Hit>();
        int graphsScanned = 0;
        foreach (var path in EnumerateTres("res://"))
        {
            var graph = PS1GraphCompiler.RobustLoad(path);
            if (graph == null) continue;
            graphsScanned++;
            foreach (var n in graph.Nodes)
            {
                if (!includeDisabled && n.IsDisabled) continue;
                // Payload (legacy single-string slot) + Payloads[].
                CheckPayload(hits, path, graph.Kind, n, slot: 0, n.Payload, query, caseSensitive);
                if (n.Payloads != null)
                {
                    for (int i = 0; i < n.Payloads.Count; i++)
                    {
                        if (i == 0) continue;  // already covered via legacy Payload mirror
                        CheckPayload(hits, path, graph.Kind, n, slot: i, n.Payloads[i] ?? "", query, caseSensitive);
                    }
                }
            }
        }

        _statusLabel.Text = $"Searched {graphsScanned} graph(s) — {hits.Count} hit(s).";
        foreach (var h in hits) _resultsList.AddChild(BuildHitRow(h));
        if (hits.Count == 0)
        {
            var none = new Label
            {
                Text = "No matches. Try the other case-sensitivity setting, or check the spelling.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            none.AddThemeColorOverride("font_color", new Color(0.60f, 0.60f, 0.60f));
            _resultsList.AddChild(none);
        }
    }

    private static void CheckPayload(List<Hit> hits, string graphPath, string kind,
                                      PS1GraphNode node, int slot, string text,
                                      string query, bool caseSensitive)
    {
        if (string.IsNullOrEmpty(text)) return;
        bool match = caseSensitive
            ? text.Contains(query)
            : text.ToLowerInvariant().Contains(query.ToLowerInvariant());
        if (!match) return;
        hits.Add(new Hit(graphPath, node.Kind, node.Id, slot, text, node.IsDisabled));
    }

    private Control BuildHitRow(Hit h)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        if (h.DisabledNode)
        {
            var disabledTag = new Label { Text = "[OFF]" };
            disabledTag.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
            row.AddChild(disabledTag);
        }

        var link = new LinkButton
        {
            Text = $"{h.GraphPath}  ({h.Kind} #{h.NodeId} Payload[{h.PayloadSlot}])",
            TooltipText = h.MatchedText,
        };
        link.Underline = LinkButton.UnderlineMode.OnHover;
        link.Pressed += () =>
        {
            // Highlight the .tres in the FileSystem dock — the
            // PS1Graph editor dock doesn't yet have a "load this
            // path" API, so this gives the author a one-click path
            // to opening it via the existing Load… button.
            EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(h.GraphPath);
            EditorInterface.Singleton.GetFileSystemDock()?.NavigateToPath(h.GraphPath);
        };
        row.AddChild(link);

        var snippet = new Label
        {
            Text = TruncateForRow(h.MatchedText),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        snippet.AddThemeColorOverride("font_color", new Color(0.80f, 0.80f, 0.80f));
        row.AddChild(snippet);

        return row;
    }

    private static string TruncateForRow(string s)
    {
        if (s == null) return "";
        s = s.Replace("\n", " ⏎ ");
        const int max = 80;
        return s.Length <= max ? s : s.Substring(0, max - 3) + "...";
    }

    private void ClearResults()
    {
        if (_resultsList == null) return;
        foreach (var child in _resultsList.GetChildren())
        {
            _resultsList.RemoveChild(child);
            child.QueueFree();
        }
    }

    // Recursive walk of res:// looking for .tres files. Uses
    // Godot's DirAccess to respect the project's resource filtering
    // (skips .import, .godot, etc. automatically).
    private static IEnumerable<string> EnumerateTres(string dir)
    {
        var d = DirAccess.Open(dir);
        if (d == null) yield break;
        d.IncludeNavigational = false;
        d.IncludeHidden = false;
        d.ListDirBegin();
        var children = new List<string>();
        for (;;)
        {
            string name = d.GetNext();
            if (string.IsNullOrEmpty(name)) break;
            children.Add(name);
        }
        d.ListDirEnd();

        foreach (var name in children)
        {
            string path = dir.EndsWith("/") ? dir + name : dir + "/" + name;
            if (d.DirExists(name))
            {
                // Skip Godot's metadata dirs and the build output.
                if (name == ".godot" || name == ".import" || name == "build") continue;
                foreach (var sub in EnumerateTres(path)) yield return sub;
            }
            else if (name.EndsWith(".tres", System.StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }
}
#endif
