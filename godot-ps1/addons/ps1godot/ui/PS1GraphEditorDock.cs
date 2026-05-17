#if TOOLS
using Godot;
using PS1Godot.Graph;

namespace PS1Godot.UI;

// PS1Graph editor — slice 3.
//
// Bottom-panel dock that hosts a Godot GraphEdit widget on top of a
// PS1GraphResource. Slice-3 additions over slice 2:
//   - Cycle detection in ConnectionRequest — DFS from the proposed
//     target back via existing edges; if it can already reach the
//     source, refuse the new edge with a PushWarning. Prevents
//     authoring uncompilable loops at connect time.
//   - Two more node kinds: "Branch (if/else)" — three rows of typed
//     pins (Exec in / Exec out true; right-only Exec out false; Bool
//     in) — and "Comment" (pinless decoration with a LineEdit
//     payload). Together with Print these exercise the typed-pin
//     model across Exec / String / Bool plus a pinless kind.
//
// Author actions:
//   - New / Load / Save / Save As… in the toolbar.
//   - Right-click empty GraphEdit → palette → spawn a node.
//   - Drag a pin to another pin → connect (rejected if types differ
//     OR if the edge would close a cycle).
//   - Select + Delete → remove node(s) and cascade-prune connections.
//
// Slice 3 still omits: compile-to-Lua (no compiler), dangling-input
// warnings, node search / palette filter, undo/redo integration,
// group nodes. Those land alongside D1 (Dialogue) once the compiler
// shape is decided, since the compiler's needs drive validation
// granularity (e.g. exec-only cycle rejection vs the current "any
// cycle is rejected" stance).
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
        new NodeKindEntry("print",   "Print"),
        new NodeKindEntry("branch",  "Branch (if/else)"),
        new NodeKindEntry("comment", "Comment"),
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
        AddToolbarButton(toolbar, "Compile",   OnCompilePressed);

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

    private void OnCompilePressed()
    {
        SyncVisualPositionsBackToResource();
        string lua = PS1GraphCompiler.Compile(_resource);
        GD.Print("[PS1Godot] PS1Graph: compiled to Lua —");
        GD.Print(lua);
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
        "print"  => "Hello PSX",
        "branch" => "true",   // Branch's literal-condition default — see BuildVisualBody row 3.
        _        => "",
    };

    // Parse Branch's Payload back to a bool. Tolerant of legacy "" /
    // unset payloads → defaults to true (matches DefaultPayloadFor),
    // so existing graphs saved before this checkbox landed will read
    // as condition=true on next load rather than silently flipping.
    private static bool ParseBoolPayload(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return true;
        return string.Equals(payload, "true", System.StringComparison.OrdinalIgnoreCase);
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
            case "branch":
            {
                // Row 0: Exec in (left) + Exec out true (right).
                g.AddChild(new Label { Text = "exec / true" });
                g.SetSlot(0, true,  (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true,  (int)PinType.Exec, s_pinColors[PinType.Exec]);

                // Row 1: no left pin, Exec out false (right only). The
                // compiler will branch on the Bool input below; this row
                // is the false-arm continuation.
                g.AddChild(new Label { Text = "false" });
                g.SetSlot(1, false, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true,  (int)PinType.Exec, s_pinColors[PinType.Exec]);

                // Row 2: Bool in (left only). The decision input.
                g.AddChild(new Label { Text = "condition" });
                g.SetSlot(2, true,  (int)PinType.Bool, s_pinColors[PinType.Bool],
                             false, (int)PinType.Bool, s_pinColors[PinType.Bool]);

                // Row 3: literal-value fallback. Pinless — used when
                // the Bool input on row 2 is disconnected (which is
                // every Branch in slice 4a, since no Bool-producing
                // kind exists yet). Stored in Payload as "true"/"false"
                // strings; the compiler parses on emit. New Branches
                // default to "true" so the obvious wiring (Hello to
                // the true-exec-out) actually prints Hello at runtime.
                var defaultCheck = new CheckBox
                {
                    Text = "default condition",
                    ButtonPressed = ParseBoolPayload(n.Payload),
                };
                defaultCheck.Toggled += pressed => n.Payload = pressed ? "true" : "false";
                g.AddChild(defaultCheck);
                // No SetSlot for row 3 — pinless row.
                break;
            }
            case "comment":
            {
                // Pinless decoration node. Used to label graph regions
                // ("// player damage", "TODO rebalance") without
                // affecting connection topology. The Payload field
                // holds the text.
                var commentEdit = new LineEdit { Text = n.Payload, PlaceholderText = "comment…" };
                commentEdit.TextChanged += text => n.Payload = text;
                g.AddChild(commentEdit);
                // No SetSlot — both sides remain disabled, no pin caps drawn.
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

        // Cycle check: would a connection from `fromId` to `toId` close
        // a directed loop in the connection graph? Walk existing edges
        // backwards from `fromId` — if we reach `toId`, adding this edge
        // would let us return to it. Conservative: rejects ALL cycles,
        // including data-only ones (String→String chains looping back).
        // Data-cycle false-positives are rare in practice; the slice-4
        // compiler will tighten this to "exec-only" if we hit a real
        // case that's blocked unnecessarily.
        if (WouldFormCycle(fromId, toId))
        {
            GD.PushWarning(
                $"[PS1Godot] PS1Graph: refusing connection from node #{fromId} → node #{toId} — " +
                $"would form a cycle. Cycles aren't compilable to straight-line Lua; break the " +
                $"loop with a Branch (if/else) or restructure the flow.");
            return;
        }

        _resource.Connections.Add(new PS1GraphConnection
        {
            FromNodeId = fromId,
            FromPort   = (int)fromPort,
            ToNodeId   = toId,
            ToPort     = (int)toPort,
        });
        _graphEdit.ConnectNode(fromNode, (int)fromPort, toNode, (int)toPort);
    }

    // DFS over the connection list to determine whether adding
    // `from → to` would form a cycle. A cycle exists iff `to` can
    // already reach `from` via existing edges. Visits each node at
    // most once via the `seen` set; O(edges) per call which is fine
    // for the graph sizes authors hand-edit (slice-3 hand-authored
    // graphs are realistically dozens of nodes, not thousands).
    private bool WouldFormCycle(int from, int to)
    {
        if (from == to) return true;  // self-loop is a degenerate cycle.

        var seen = new System.Collections.Generic.HashSet<int>();
        var stack = new System.Collections.Generic.Stack<int>();
        stack.Push(to);
        while (stack.Count > 0)
        {
            int cur = stack.Pop();
            if (!seen.Add(cur)) continue;
            if (cur == from) return true;
            foreach (var c in _resource.Connections)
            {
                if (c.FromNodeId == cur)
                {
                    stack.Push(c.ToNodeId);
                }
            }
        }
        return false;
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

    // Crash report 2026-05-17: deleting a node took the editor down.
    // Two suspected interactions with GraphEdit's mid-emit state:
    //   1. Calling _graphEdit.DisconnectNode(...) inside DeleteNodesRequest
    //      raced with GraphEdit's own connection-cleanup pass — GraphEdit
    //      auto-removes any connection touching a freed child GraphNode,
    //      so the explicit DisconnectNode was redundant AND ran while
    //      GraphEdit's connection table was being mutated.
    //   2. QueueFree on a still-selected GraphNode during signal emit
    //      can use-after-free on the selection model.
    //
    // Fix: skip the manual DisconnectNode (GraphEdit handles it), and
    // defer all visual mutations to a CallDeferred pass so GraphEdit
    // finishes its current emit cycle before we touch its children.
    // try/catch around the handler so any future failure prints an
    // actionable error instead of crashing the editor.
    private void OnDeleteNodesRequest(Godot.Collections.Array<StringName> nodeNames)
    {
        if (_graphEdit == null) return;

        try
        {
            // Snapshot the doomed Ids up front. The actual deletion runs
            // deferred (next idle frame), but we extract from the signal
            // payload now in case Godot recycles the Array.
            var doomedIds = new System.Collections.Generic.HashSet<int>();
            foreach (var n in nodeNames)
            {
                int id = ExtractIdFromVisualName(n.ToString());
                if (id >= 0) doomedIds.Add(id);
            }
            if (doomedIds.Count == 0) return;

            // Pass through Variant boxing so CallDeferred accepts the
            // HashSet. Godot.Collections.Array<int> would also work but
            // copying into a Godot array per delete is wasteful; the
            // wrapper PerformDeferredDelete unboxes via the field below.
            _pendingDeleteIds = doomedIds;
            CallDeferred(MethodName.PerformDeferredDelete);
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[PS1Godot] PS1Graph: delete-nodes handler threw: {ex}");
        }
    }

    // Set by OnDeleteNodesRequest, consumed by PerformDeferredDelete
    // on the next idle frame. Single-shot; cleared after consumption.
    private System.Collections.Generic.HashSet<int>? _pendingDeleteIds;

    private void PerformDeferredDelete()
    {
        var doomedIds = _pendingDeleteIds;
        _pendingDeleteIds = null;
        if (doomedIds == null || doomedIds.Count == 0) return;
        if (_graphEdit == null) return;

        try
        {
            // 1. Drop connections from the resource. GraphEdit will
            //    auto-remove the visual lines when the GraphNode is
            //    freed below — no DisconnectNode calls needed.
            for (int i = _resource.Connections.Count - 1; i >= 0; i--)
            {
                var c = _resource.Connections[i];
                if (doomedIds.Contains(c.FromNodeId) || doomedIds.Contains(c.ToNodeId))
                {
                    _resource.Connections.RemoveAt(i);
                }
            }

            // 2. Drop resource nodes.
            for (int i = _resource.Nodes.Count - 1; i >= 0; i--)
            {
                if (doomedIds.Contains(_resource.Nodes[i].Id))
                {
                    _resource.Nodes.RemoveAt(i);
                }
            }

            // 3. Free visuals + clean the id→name map. Resolve the
            //    name BEFORE removing from the map so we still know
            //    which GraphNode to free.
            foreach (var id in doomedIds)
            {
                if (_idToVisualName.TryGetValue(id, out var name))
                {
                    _idToVisualName.Remove(id);
                    var visual = _graphEdit.GetNodeOrNull<GraphNode>(name);
                    visual?.QueueFree();
                }
            }
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[PS1Godot] PS1Graph: deferred-delete pass threw: {ex}");
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
