#if TOOLS
using System.Collections.Generic;
using Godot;

namespace PS1Godot.UI;

// PS1 Reference Viewer (UE editor port plan pick #2). Cross-project
// "who uses this asset?" — pick a resource path, scan every .tscn /
// .tres / .lua under res:// for references to it, list the hits.
//
// The dock backs both manual workflows (paste a path, click Find)
// and the editor selection signal (when the author selects an asset
// in FileSystem the dock auto-populates).
//
// References we detect:
//   - UID references: `uid="uid://abc123"` or `ExtResource("..."`
//     with the matching UID, in .tscn / .tres files.
//   - Path-string references: literal "res://..." in .tres / .lua
//     (catches Lua snippets like `Audio.PlaySfx("hum_low")` only
//     when a path is in the string, not when the name is — slice 2
//     can layer name-based search via PS1Scene.AudioClips's name
//     table).
//
// Click-to-jump highlights the referencing file in the FileSystem
// dock, mirroring the PS1 Graph Find pattern.
public partial class PS1ReferenceViewerDock : VBoxContainer
{
    private LineEdit?      _targetEdit;
    private Label?         _statusLabel;
    private VBoxContainer? _resultsList;
    private CheckBox?      _autoFollowSelection;

    private sealed record Reference(string ReferencingPath, int LineNumber, string Snippet);

    public PS1ReferenceViewerDock()
    {
        Name = "PS1 References";
        SizeFlagsVertical   = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        BuildUi();
    }

    public override void _Ready()
    {
        // Auto-follow the editor's FileSystem selection when enabled.
        // The signal fires whenever the user clicks a different file
        // in the FileSystem dock.
        EditorInterface.Singleton.GetFileSystemDock().FilesMoved += (_, _) => RefreshIfAuto();
    }

    private void BuildUi()
    {
        AddThemeConstantOverride("separation", 6);

        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 6);
        AddChild(toolbar);

        toolbar.AddChild(new Label { Text = "Target:", VerticalAlignment = VerticalAlignment.Center });

        _targetEdit = new LineEdit
        {
            PlaceholderText = "res://assets/audio/hum_low.tres",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _targetEdit.TextSubmitted += _ => RunSearch();
        toolbar.AddChild(_targetEdit);

        var findBtn = new Button { Text = "Find references" };
        findBtn.Pressed += RunSearch;
        toolbar.AddChild(findBtn);

        var fromSelectionBtn = new Button { Text = "↩ From FileSystem selection" };
        fromSelectionBtn.Pressed += PopulateFromSelection;
        toolbar.AddChild(fromSelectionBtn);

        _autoFollowSelection = new CheckBox { Text = "Auto-follow selection", ButtonPressed = false };
        toolbar.AddChild(_autoFollowSelection);

        _statusLabel = new Label { Text = "Pick an asset (paste path or use selection button)." };
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

    private void PopulateFromSelection()
    {
        var selected = EditorInterface.Singleton.GetSelectedPaths();
        if (selected == null || selected.Length == 0)
        {
            if (_statusLabel != null)
                _statusLabel.Text = "Nothing selected in the FileSystem dock.";
            return;
        }
        if (_targetEdit != null) _targetEdit.Text = selected[0];
        RunSearch();
    }

    private void RefreshIfAuto()
    {
        if (_autoFollowSelection == null || !_autoFollowSelection.ButtonPressed) return;
        PopulateFromSelection();
    }

    private void RunSearch()
    {
        if (_targetEdit == null || _resultsList == null || _statusLabel == null) return;
        string targetPath = (_targetEdit.Text ?? "").Trim();
        if (string.IsNullOrEmpty(targetPath))
        {
            _statusLabel.Text = "Target path is empty.";
            ClearResults();
            return;
        }

        ClearResults();

        // Look up the target's UID so .tscn ExtResource references
        // (which key off uid://abc instead of the path string) get
        // matched too.
        string targetUid = "";
        var rid = ResourceLoader.GetResourceUid(targetPath);
        if (rid != ResourceUid.InvalidId)
        {
            targetUid = ResourceUid.Singleton.IdToText(rid);
        }

        var hits = new List<Reference>();
        int filesScanned = 0;
        foreach (var p in EnumerateSearchable("res://"))
        {
            if (p == targetPath) continue;  // skip the target file itself
            filesScanned++;
            ScanFile(p, targetPath, targetUid, hits);
        }

        _statusLabel.Text = filesScanned == 0
            ? "No searchable files found under res://."
            : $"Scanned {filesScanned} file(s) — {hits.Count} reference(s) to {targetPath}.";

        foreach (var h in hits) _resultsList.AddChild(BuildHitRow(h));
        if (hits.Count == 0)
        {
            var none = new Label
            {
                Text = "No references found. The asset may be unused, dynamically loaded (Lua " +
                       "Audio.PlaySfx by name), or referenced only through an indirection " +
                       "(PS1Scene.AudioClips name lookup).",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            none.AddThemeColorOverride("font_color", new Color(0.60f, 0.60f, 0.60f));
            _resultsList.AddChild(none);
        }
    }

    private static void ScanFile(string path, string targetPath, string targetUid,
                                  List<Reference> hits)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        int lineNo = 0;
        while (!f.EofReached())
        {
            string line = f.GetLine();
            lineNo++;
            if (line == null) continue;
            bool match = line.Contains(targetPath, System.StringComparison.OrdinalIgnoreCase);
            if (!match && !string.IsNullOrEmpty(targetUid))
            {
                match = line.Contains(targetUid, System.StringComparison.OrdinalIgnoreCase);
            }
            if (match)
            {
                hits.Add(new Reference(path, lineNo, line.Trim()));
            }
        }
    }

    private Control BuildHitRow(Reference r)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        var link = new LinkButton
        {
            Text = $"{r.ReferencingPath}:{r.LineNumber}",
            TooltipText = r.Snippet,
        };
        link.Underline = LinkButton.UnderlineMode.OnHover;
        link.Pressed += () =>
        {
            EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(r.ReferencingPath);
            EditorInterface.Singleton.GetFileSystemDock()?.NavigateToPath(r.ReferencingPath);
        };
        row.AddChild(link);

        var snippet = new Label
        {
            Text = TruncateForRow(r.Snippet),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        snippet.AddThemeColorOverride("font_color", new Color(0.80f, 0.80f, 0.80f));
        row.AddChild(snippet);

        return row;
    }

    private static string TruncateForRow(string s)
    {
        if (s == null) return "";
        const int max = 96;
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

    // Walks res:// recursively for files we know how to grep. .tres /
    // .tscn cover Godot resource references; .lua catches runtime
    // path-string references. Skips Godot's metadata + the build/
    // output (which would otherwise hit the export's own .lua copies).
    private static IEnumerable<string> EnumerateSearchable(string dir)
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
                if (name == ".godot" || name == ".import" || name == "build") continue;
                foreach (var sub in EnumerateSearchable(path)) yield return sub;
            }
            else if (name.EndsWith(".tres", System.StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith(".tscn", System.StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith(".lua",  System.StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }
}
#endif
