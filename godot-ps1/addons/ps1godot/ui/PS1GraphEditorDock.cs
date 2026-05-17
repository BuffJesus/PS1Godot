#if TOOLS
using Godot;
using PS1Godot.Graph;

namespace PS1Godot.UI;

// PS1Graph editor — walking skeleton (slice 1).
//
// Bottom-panel dock that hosts a Godot GraphEdit widget on top of a
// PS1GraphResource. Lets the author:
//   - New     → start a fresh in-memory graph (path unset until save).
//   - Load    → pick a .tres via EditorFileDialog, materialise it.
//   - Save    → ResourceSaver.Save to the resource's existing path, or
//               prompt for a path on first save.
//   - Add Print → drop one "print" node at the GraphEdit viewport
//               centre. (Slice 2 will add a right-click palette with
//               more node kinds; this button is the minimum to exercise
//               the round-trip.)
//   - Drag / connect / disconnect / delete via stock GraphEdit input.
//
// Slice 1 deliberately omits: typed-pin colouring (all pins are slot 0,
// "any" type), compile-to-Lua (no compiler), live validation (cycle
// detection / dangling-input warnings), node search / palette filter,
// undo/redo integration, multi-selection move. Those land alongside D1
// (the first concrete graph kind) once the framework is anchored.
[Tool]
public partial class PS1GraphEditorDock : VBoxContainer
{
    private PS1GraphResource _resource = new();
    private GraphEdit? _graphEdit;
    private Label? _pathLabel;
    private EditorFileDialog? _openDialog;
    private EditorFileDialog? _saveDialog;

    // Map resource node Id → visual GraphNode child name. Names are
    // "n{Id}" so the connection signals (which give us node names) can
    // round-trip back to Ids cheaply. Cleared on every reload.
    private readonly System.Collections.Generic.Dictionary<int, string> _idToVisualName = new();

    public PS1GraphEditorDock()
    {
        Name = "PS1 Graph";
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        BuildUI();
    }

    private void BuildUI()
    {
        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 6);
        AddChild(toolbar);

        AddToolbarButton(toolbar, "New",       OnNewPressed);
        AddToolbarButton(toolbar, "Load…",     OnLoadPressed);
        AddToolbarButton(toolbar, "Save",      OnSavePressed);
        AddToolbarButton(toolbar, "Save As…",  OnSaveAsPressed);

        toolbar.AddChild(new VSeparator());

        AddToolbarButton(toolbar, "+ Print", OnAddPrintPressed);

        toolbar.AddChild(new VSeparator());

        _pathLabel = new Label
        {
            Text = "(unsaved)",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toolbar.AddChild(_pathLabel);

        _graphEdit = new GraphEdit
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            // Right-click pan / scroll-wheel zoom come for free; we only
            // care about wiring the connect/disconnect hooks for slice 1.
        };
        _graphEdit.ConnectionRequest    += OnConnectionRequest;
        _graphEdit.DisconnectionRequest += OnDisconnectionRequest;
        _graphEdit.DeleteNodesRequest   += OnDeleteNodesRequest;
        AddChild(_graphEdit);
    }

    private static void AddToolbarButton(HBoxContainer parent, string label, System.Action handler)
    {
        var b = new Button { Text = label };
        b.Pressed += () => handler();
        parent.AddChild(b);
    }

    // ── Toolbar handlers ─────────────────────────────────────────────

    private void OnNewPressed()
    {
        _resource = new PS1GraphResource();
        ReloadGraphView();
        UpdatePathLabel();
        GD.Print("[PS1Godot] PS1Graph: new graph (unsaved).");
    }

    private void OnLoadPressed()
    {
        if (_openDialog == null)
        {
            _openDialog = new EditorFileDialog
            {
                FileMode = EditorFileDialog.FileModeEnum.OpenFile,
                Access   = EditorFileDialog.AccessEnum.Resources,
            };
            _openDialog.AddFilter("*.tres", "PS1Graph (.tres)");
            _openDialog.FileSelected += OnLoadPathChosen;
            AddChild(_openDialog);
        }
        _openDialog.PopupCentered(new Vector2I(800, 600));
    }

    private void OnLoadPathChosen(string path)
    {
        var loaded = ResourceLoader.Load<PS1GraphResource>(path);
        if (loaded == null)
        {
            GD.PushError($"[PS1Godot] PS1Graph: failed to load '{path}' as PS1GraphResource.");
            return;
        }
        _resource = loaded;
        ReloadGraphView();
        UpdatePathLabel();
        GD.Print($"[PS1Godot] PS1Graph: loaded '{path}' " +
                 $"({_resource.Nodes.Count} node(s), {_resource.Connections.Count} connection(s)).");
    }

    private void OnSavePressed()
    {
        SyncVisualPositionsBackToResource();

        string path = _resource.ResourcePath;
        if (string.IsNullOrEmpty(path))
        {
            // First save — fall through to Save As… so the author picks
            // a destination once and subsequent Saves write there.
            OnSaveAsPressed();
            return;
        }

        var err = ResourceSaver.Save(_resource, path);
        if (err != Error.Ok)
        {
            GD.PushError($"[PS1Godot] PS1Graph: save failed ({err}) at '{path}'.");
            return;
        }
        GD.Print($"[PS1Godot] PS1Graph: saved '{path}'.");
    }

    private void OnSaveAsPressed()
    {
        if (_saveDialog == null)
        {
            _saveDialog = new EditorFileDialog
            {
                FileMode = EditorFileDialog.FileModeEnum.SaveFile,
                Access   = EditorFileDialog.AccessEnum.Resources,
                CurrentFile = "graph.tres",
            };
            _saveDialog.AddFilter("*.tres", "PS1Graph (.tres)");
            _saveDialog.FileSelected += OnSavePathChosen;
            AddChild(_saveDialog);
        }
        _saveDialog.PopupCentered(new Vector2I(800, 600));
    }

    private void OnSavePathChosen(string path)
    {
        SyncVisualPositionsBackToResource();
        _resource.ResourcePath = path;
        var err = ResourceSaver.Save(_resource, path);
        if (err != Error.Ok)
        {
            GD.PushError($"[PS1Godot] PS1Graph: save failed ({err}) at '{path}'.");
            return;
        }
        UpdatePathLabel();
        GD.Print($"[PS1Godot] PS1Graph: saved '{path}'.");
    }

    private void OnAddPrintPressed()
    {
        int id = _resource.AllocateId();
        var node = new PS1GraphNode
        {
            Id = id,
            Kind = "print",
            Position = GetSpawnPosition(),
            Payload = "Hello PSX",
        };
        _resource.Nodes.Add(node);
        AddVisualForNode(node);
    }

    // ── Graph view (re)build ─────────────────────────────────────────

    private void ReloadGraphView()
    {
        if (_graphEdit == null) return;
        _graphEdit.ClearConnections();
        // Drop every existing visual GraphNode; toolbar lives on us, not
        // on the GraphEdit, so it's safe to wipe all of GraphEdit's
        // children here.
        foreach (var child in _graphEdit.GetChildren())
        {
            child.QueueFree();
        }
        _idToVisualName.Clear();

        foreach (var n in _resource.Nodes)
        {
            AddVisualForNode(n);
        }
        foreach (var c in _resource.Connections)
        {
            if (!_idToVisualName.TryGetValue(c.FromNodeId, out var fromName)) continue;
            if (!_idToVisualName.TryGetValue(c.ToNodeId, out var toName))     continue;
            _graphEdit.ConnectNode(fromName, c.FromPort, toName, c.ToPort);
        }
    }

    private void AddVisualForNode(PS1GraphNode n)
    {
        if (_graphEdit == null) return;

        string visualName = $"n{n.Id}";
        var g = new GraphNode
        {
            Name = visualName,
            Title = TitleFor(n),
            PositionOffset = n.Position,
            Resizable = false,
        };

        // Slice-1 pin model: one slot per node carrying both an input
        // pin (left) and an output pin (right). Type 0 = "any" — the
        // typed-pin system in the next slice will widen this.
        var payloadEdit = new LineEdit { Text = n.Payload, PlaceholderText = "payload…" };
        payloadEdit.TextChanged += text => n.Payload = text;
        g.AddChild(payloadEdit);
        g.SetSlot(0, true, 0, Colors.White, true, 0, Colors.White);

        _graphEdit.AddChild(g);
        _idToVisualName[n.Id] = visualName;
    }

    private static string TitleFor(PS1GraphNode n) =>
        string.IsNullOrEmpty(n.Kind) ? $"node #{n.Id}" : $"{n.Kind} #{n.Id}";

    // Drop new nodes at a position that's visible regardless of pan/zoom:
    // top-left of the visible area plus a small offset, then a per-node
    // stagger so successive presses don't pile up.
    private Vector2 GetSpawnPosition()
    {
        if (_graphEdit == null) return Vector2.Zero;
        var scroll = _graphEdit.ScrollOffset;
        var basePos = scroll + new Vector2(40, 40);
        int n = _resource.Nodes.Count;
        return basePos + new Vector2(n * 20, n * 20);
    }

    // ── Visual → resource sync ───────────────────────────────────────

    private void SyncVisualPositionsBackToResource()
    {
        if (_graphEdit == null) return;
        foreach (var child in _graphEdit.GetChildren())
        {
            if (child is GraphNode g)
            {
                int id = ExtractIdFromVisualName(g.Name);
                if (id < 0) continue;
                foreach (var n in _resource.Nodes)
                {
                    if (n.Id == id)
                    {
                        n.Position = g.PositionOffset;
                        break;
                    }
                }
            }
        }
    }

    private static int ExtractIdFromVisualName(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        if (name.Length < 2 || name[0] != 'n') return -1;
        return int.TryParse(name.Substring(1), out var id) ? id : -1;
    }

    // ── GraphEdit signal handlers ────────────────────────────────────

    private void OnConnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
    {
        if (_graphEdit == null) return;
        int fromId = ExtractIdFromVisualName(fromNode);
        int toId   = ExtractIdFromVisualName(toNode);
        if (fromId < 0 || toId < 0) return;

        // Cycle prevention is a slice-2 concern (live validation); for
        // slice 1 we permit any connection the GraphEdit allows, which
        // includes the trivial self-loop. The render layer doesn't mind.
        _resource.Connections.Add(new PS1GraphConnection
        {
            FromNodeId = fromId,
            FromPort   = (int)fromPort,
            ToNodeId   = toId,
            ToPort     = (int)toPort,
        });
        _graphEdit.ConnectNode(fromNode, (int)fromPort, toNode, (int)toPort);
    }

    private void OnDisconnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
    {
        if (_graphEdit == null) return;
        int fromId = ExtractIdFromVisualName(fromNode);
        int toId   = ExtractIdFromVisualName(toNode);
        if (fromId < 0 || toId < 0) return;

        for (int i = _resource.Connections.Count - 1; i >= 0; i--)
        {
            var c = _resource.Connections[i];
            if (c.FromNodeId == fromId && c.FromPort == (int)fromPort
                && c.ToNodeId == toId && c.ToPort == (int)toPort)
            {
                _resource.Connections.RemoveAt(i);
            }
        }
        _graphEdit.DisconnectNode(fromNode, (int)fromPort, toNode, (int)toPort);
    }

    private void OnDeleteNodesRequest(Godot.Collections.Array<StringName> nodeNames)
    {
        if (_graphEdit == null) return;

        // Collect the Ids we're deleting first; we'll need them to prune
        // any connection that touched them on either side.
        var doomedIds = new System.Collections.Generic.HashSet<int>();
        foreach (var n in nodeNames)
        {
            int id = ExtractIdFromVisualName(n.ToString());
            if (id >= 0) doomedIds.Add(id);
        }
        if (doomedIds.Count == 0) return;

        // Prune connections referencing any doomed node (in either
        // direction). Pull connection lines from GraphEdit too so the
        // visual stays consistent without a full reload.
        for (int i = _resource.Connections.Count - 1; i >= 0; i--)
        {
            var c = _resource.Connections[i];
            if (doomedIds.Contains(c.FromNodeId) || doomedIds.Contains(c.ToNodeId))
            {
                if (_idToVisualName.TryGetValue(c.FromNodeId, out var fromName) &&
                    _idToVisualName.TryGetValue(c.ToNodeId,   out var toName))
                {
                    _graphEdit.DisconnectNode(fromName, c.FromPort, toName, c.ToPort);
                }
                _resource.Connections.RemoveAt(i);
            }
        }

        // Drop the resource nodes + their visuals.
        for (int i = _resource.Nodes.Count - 1; i >= 0; i--)
        {
            if (doomedIds.Contains(_resource.Nodes[i].Id))
            {
                _resource.Nodes.RemoveAt(i);
            }
        }
        foreach (var id in doomedIds)
        {
            if (_idToVisualName.TryGetValue(id, out var name))
            {
                var visual = _graphEdit.GetNodeOrNull<GraphNode>(name);
                visual?.QueueFree();
                _idToVisualName.Remove(id);
            }
        }
    }

    private void UpdatePathLabel()
    {
        if (_pathLabel == null) return;
        _pathLabel.Text = string.IsNullOrEmpty(_resource.ResourcePath)
            ? "(unsaved)"
            : _resource.ResourcePath;
    }
}
#endif
