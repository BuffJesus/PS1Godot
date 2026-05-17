#if TOOLS
using Godot;
using PS1Godot.Graph;

namespace PS1Godot.UI;

// PS1Graph editor — slice 2.
//
// Bottom-panel dock that hosts a Godot GraphEdit widget on top of a
// PS1GraphResource. Slice-2 additions over slice 1:
//   - Typed pins. Each node Kind declares its slot layout (pin type per
//     row, per side); GraphEdit's slot-type compatibility check enforces
//     "Exec connects to Exec, String connects to String" automatically.
//     Colours match the PinType enum (Exec white, String yellow, …).
//   - Right-click palette. Right-clicking empty GraphEdit space opens a
//     PopupMenu listing every registered node Kind; selection drops a
//     fresh node at the click position. The slice-1 "+ Print" toolbar
//     button is gone — the palette is the canonical "add node" UX.
//
// Author actions:
//   - New / Load / Save / Save As… in the toolbar.
//   - Right-click empty GraphEdit → "Print" (etc.) to add a node.
//   - Drag a pin to another pin → connect (rejected if types differ).
//   - Select + Delete → remove node(s) and cascade-prune connections.
//
// Slice 2 still omits: compile-to-Lua (no compiler), live validation
// (cycle detection / dangling-input warnings), node search / palette
// filter, undo/redo integration, group / comment nodes. Those land in
// slice 3 alongside D1 (the first concrete graph kind, Dialogue), when
// real use cases tell us which framework polish actually hurts.
[Tool]
public partial class PS1GraphEditorDock : VBoxContainer
{
    private PS1GraphResource _resource = new();
    private GraphEdit? _graphEdit;
    private Label? _pathLabel;
    private EditorFileDialog? _openDialog;
    private EditorFileDialog? _saveDialog;
    private PopupMenu? _palettePopup;

    // Stashed at right-click time so the menu's IdPressed handler knows
    // where to drop the spawned node. GraphEdit hands PopupRequest a
    // position in screen-local coordinates; we convert to graph-canvas
    // coordinates (ScrollOffset + position / zoom) at spawn time.
    private Vector2 _paletteSpawnCanvasPos = Vector2.Zero;

    // Map resource node Id → visual GraphNode child name. Names are
    // "n{Id}" so the connection signals (which give us node names) can
    // round-trip back to Ids cheaply. Cleared on every reload.
    private readonly System.Collections.Generic.Dictionary<int, string> _idToVisualName = new();

    // ── Node-kind registry ───────────────────────────────────────────
    //
    // Each entry describes how to render + edit one node Kind. Adding
    // a new kind = appending one record + handling its kind string in
    // BuildVisualBody / future compiler dispatch. The palette popup
    // iterates this list, so registration order = display order.
    //
    // Slot definitions are baked into the visual at materialise time,
    // not stored in the resource — changing a kind's pin layout in
    // future doesn't invalidate saved graphs (existing connections to
    // removed pins are dropped silently at load by the cascade-prune).
    private record NodeKindEntry(string Kind, string DisplayName);
    private static readonly NodeKindEntry[] s_kinds = new[]
    {
        new NodeKindEntry("print", "Print"),
    };

    // Slice-2 palette: per-pin-type colours. Picked to match common
    // node-graph conventions (Blueprint / Houdini / ShaderForge):
    // exec=white, strings=yellow, numbers=green, bools=red, vectors=blue,
    // entity-refs=purple. Drawn on the GraphNode pin caps via SetSlot.
    private static readonly System.Collections.Generic.Dictionary<PinType, Color> s_pinColors = new()
    {
        { PinType.Exec,      new Color(1.00f, 1.00f, 1.00f) },
        { PinType.String,    new Color(1.00f, 0.82f, 0.18f) },
        { PinType.Number,    new Color(0.36f, 0.85f, 0.41f) },
        { PinType.Bool,      new Color(0.91f, 0.30f, 0.30f) },
        { PinType.Vec3,      new Color(0.34f, 0.55f, 0.95f) },
        { PinType.EntityRef, new Color(0.69f, 0.40f, 0.92f) },
    };

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
            // Right-click pan / scroll-wheel zoom come for free; we wire
            // PopupRequest to open the palette and connection hooks for
            // round-trip into the resource.
        };
        _graphEdit.ConnectionRequest    += OnConnectionRequest;
        _graphEdit.DisconnectionRequest += OnDisconnectionRequest;
        _graphEdit.DeleteNodesRequest   += OnDeleteNodesRequest;
        _graphEdit.PopupRequest         += OnGraphPopupRequest;
        AddChild(_graphEdit);

        // Right-click palette. Built once + reused; entries match
        // s_kinds 1:1 so adding a node Kind = one line in the registry.
        _palettePopup = new PopupMenu { Name = "PS1GraphPalette" };
        for (int i = 0; i < s_kinds.Length; i++)
        {
            _palettePopup.AddItem(s_kinds[i].DisplayName, i);
        }
        _palettePopup.IdPressed += OnPaletteItemPressed;
        AddChild(_palettePopup);
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

    // ── Palette ──────────────────────────────────────────────────────

    private void OnGraphPopupRequest(Vector2 atPosition)
    {
        if (_graphEdit == null || _palettePopup == null) return;

        // PopupRequest's at_position is GraphEdit-local (already in
        // widget coordinates, not screen). Convert to graph-canvas
        // coordinates for the spawn site so the node lands under the
        // cursor regardless of pan/zoom: canvas = (local + scroll) / zoom.
        _paletteSpawnCanvasPos = (atPosition + _graphEdit.ScrollOffset) / _graphEdit.Zoom;

        // Popup at the global screen position so the menu appears under
        // the cursor — GetScreenPosition() gives GraphEdit's screen
        // origin; add the local click position to land at the cursor.
        Vector2 screenPos = _graphEdit.GetScreenPosition() + atPosition;
        _palettePopup.Position = new Vector2I((int)screenPos.X, (int)screenPos.Y);
        _palettePopup.Popup();
    }

    private void OnPaletteItemPressed(long id)
    {
        int idx = (int)id;
        if (idx < 0 || idx >= s_kinds.Length) return;
        SpawnNode(s_kinds[idx].Kind, _paletteSpawnCanvasPos);
    }

    private void SpawnNode(string kind, Vector2 canvasPosition)
    {
        int id = _resource.AllocateId();
        var node = new PS1GraphNode
        {
            Id = id,
            Kind = kind,
            Position = canvasPosition,
            Payload = DefaultPayloadFor(kind),
        };
        _resource.Nodes.Add(node);
        AddVisualForNode(node);
    }

    private static string DefaultPayloadFor(string kind) => kind switch
    {
        "print" => "Hello PSX",
        _       => "",
    };

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

        BuildVisualBody(g, n);

        _graphEdit.AddChild(g);
        _idToVisualName[n.Id] = visualName;
    }

    // Per-kind body + slot layout. Each branch must AddChild one
    // Control per row (in row order) AND call SetSlot for each row that
    // carries pins. Slot index = row index = port index in the
    // PS1GraphConnection FromPort/ToPort fields, so order is part of
    // the contract — append new rows at the bottom of existing kinds
    // rather than reordering.
    private static void BuildVisualBody(GraphNode g, PS1GraphNode n)
    {
        switch (n.Kind)
        {
            case "print":
            {
                // Row 0: Exec in (left) + Exec out (right). Top of the
                // node so the control-flow line reads naturally.
                g.AddChild(new Label { Text = "exec" });
                g.SetSlot(0, true, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                // Row 1: String in (left) + String out (right). The
                // LineEdit holds the literal value used when the String
                // input is disconnected. The String output passes the
                // same value through so a chain of Prints can share a
                // message — convenient for slice 2; the compiler in a
                // later slice may revisit the semantics.
                var payloadEdit = new LineEdit { Text = n.Payload, PlaceholderText = "message…" };
                payloadEdit.TextChanged += text => n.Payload = text;
                g.AddChild(payloadEdit);
                g.SetSlot(1, true, (int)PinType.String, s_pinColors[PinType.String],
                             true, (int)PinType.String, s_pinColors[PinType.String]);
                break;
            }
            default:
            {
                // Fallback: untyped + Payload as a plain label. Lets
                // saved graphs survive a kind being renamed / removed
                // — connections to gone-pins drop, but the node still
                // appears so the author can manually re-route or delete.
                g.AddChild(new Label { Text = $"(unknown kind '{n.Kind}')" });
                g.AddChild(new Label { Text = n.Payload });
                break;
            }
        }
    }

    private static string TitleFor(PS1GraphNode n) =>
        string.IsNullOrEmpty(n.Kind) ? $"node #{n.Id}" : $"{n.Kind} #{n.Id}";

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
