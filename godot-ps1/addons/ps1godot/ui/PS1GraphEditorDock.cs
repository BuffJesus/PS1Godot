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
    // Fallback path tracking: Godot 4.7-dev5 sometimes rejects setting
    // ResourcePath on a reconstructed PS1GraphResource ("Another
    // resource is loaded from path"). When that happens we still need
    // to remember the path the author loaded from so subsequent
    // Save → no Save As dialog, and the label / compile use it via
    // EffectivePath().
    private string _loadedFromPath = "";
    private GraphEdit? _graphEdit;
    private Label? _pathLabel;
    private EditorFileDialog? _openDialog;
    private EditorFileDialog? _saveDialog;
    private PopupMenu? _palettePopup;

    // Always-visible kind dropdown in the toolbar so the workflow is
    // discoverable at a glance — earlier popup-on-mouse positioning
    // sometimes opened off-screen depending on dock layout.
    private OptionButton? _kindDropdown;

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
    // Each node kind belongs to ONE graph kind unless flagged AvailableInAll.
    // GraphKind matches resource.Kind: "" = untyped/script, "dialogue" =
    // PS1DialogueGraph, future kinds same pattern. Comment is the only
    // wildcard — labels are useful in every graph kind. Print/Branch/
    // BoolLit live in untyped because they compile to top-level
    // statements, which the dialogue compile path silently drops;
    // hiding them in dialogue mode avoids the "I authored this and it
    // didn't compile, why?" confusion.
    private record NodeKindEntry(string Kind, string DisplayName, string GraphKind = "", bool AvailableInAll = false);
    private static readonly NodeKindEntry[] s_kinds = new[]
    {
        new NodeKindEntry("print",          "Print",            GraphKind: ""),
        new NodeKindEntry("branch",         "Branch (if/else)", GraphKind: ""),
        new NodeKindEntry("bool_literal",   "Bool Literal",     GraphKind: ""),
        new NodeKindEntry("comment",        "Comment",          AvailableInAll: true),
        new NodeKindEntry("line",           "Line",             GraphKind: "dialogue"),
        new NodeKindEntry("choice",         "Choice",           GraphKind: "dialogue"),
        new NodeKindEntry("set_flag",       "Set Flag",         GraphKind: "dialogue"),
        new NodeKindEntry("condition",      "Condition (flag)", GraphKind: "dialogue"),
        new NodeKindEntry("play_sound",     "Play Sound",       GraphKind: "dialogue"),
        new NodeKindEntry("start_cutscene", "Start Cutscene",   GraphKind: "dialogue"),
        new NodeKindEntry("lua_snippet",    "Lua Snippet",      GraphKind: "dialogue"),
        new NodeKindEntry("lua_condition",  "Lua Condition",    GraphKind: "dialogue"),
        new NodeKindEntry("state",          "State",            GraphKind: "fsm"),
        new NodeKindEntry("transition",     "Transition",       GraphKind: "fsm"),
        new NodeKindEntry("objective",      "Objective",        GraphKind: "quest"),
        new NodeKindEntry("outcome",        "Outcome",          GraphKind: "quest"),
    };

    // Available graph kinds, surfaced when the author hits New.
    // Empty Kind = "Untyped / Script" (the generic statement-compile
    // model used through slice 4); "dialogue" = table-compile to a
    // _G.dialogue_<name> table for a runtime walker (D1b).
    private static readonly (string Kind, string DisplayName)[] s_graphKinds = new[]
    {
        ("",         "Untyped / Script"),
        ("dialogue", "Dialogue"),
        ("fsm",      "FSM (state machine)"),
        ("quest",    "Quest"),
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

        // Kind dropdown comes first so the workflow reads left-to-right:
        // pick a kind → hit New (or Load existing). Tooltip explains
        // what each kind compiles to.
        toolbar.AddChild(new Label
        {
            Text = "Kind:",
            VerticalAlignment = VerticalAlignment.Center,
        });
        _kindDropdown = new OptionButton();
        for (int i = 0; i < s_graphKinds.Length; i++)
        {
            _kindDropdown.AddItem(s_graphKinds[i].DisplayName, i);
        }
        _kindDropdown.TooltipText =
            "Graph kind for new graphs. Untyped → flat Lua statements; " +
            "Dialogue → table assigned to _G.dialogue_<basename> for a " +
            "runtime walker.";
        toolbar.AddChild(_kindDropdown);

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

        // Right-click palette. Items rebuilt on each popup based on
        // the current graph's Kind — kinds with GraphKind == "" are
        // always shown; kinds with GraphKind == resource.Kind are
        // shown only for matching graph kinds.
        _palettePopup = new PopupMenu { Name = "PS1GraphPalette" };
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
        try
        {
            // Read the toolbar dropdown for the kind to seed. Defaults to
            // index 0 ("Untyped / Script") if no selection has been made.
            int idx = _kindDropdown?.Selected ?? 0;
            if (idx < 0 || idx >= s_graphKinds.Length) idx = 0;
            var (kind, displayName) = s_graphKinds[idx];

            _resource = new PS1GraphResource { Kind = kind };
            _loadedFromPath = "";
            ReloadGraphView();
            UpdatePathLabel();
            GD.Print($"[PS1Godot] PS1Graph: new {displayName} graph (unsaved). " +
                     $"Right-click in the canvas to add nodes; the palette filters by kind.");
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[PS1Godot] PS1Graph: New threw: {ex}");
        }
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
        // RobustLoad handles the Godot 4.7-dev5 binding-quirk fallback —
        // see PS1GraphCompiler.RobustLoad for the full story. Same
        // helper is used by SceneCollector's F5 auto-recompile so the
        // two paths stay in lockstep.
        var loaded = PS1GraphCompiler.RobustLoad(path);
        if (loaded == null)
        {
            GD.PushError($"[PS1Godot] PS1Graph: failed to load '{path}' (file missing or not a PS1GraphResource).");
            return;
        }
        // Wrap ResourcePath assignment: when the load fell back to a
        // reconstructed resource, Godot's resource cache already holds
        // the original bare Resource at this path and setting our
        // reconstructed one's path emits "Another resource is loaded"
        // ERROR. The reconstructed object isn't registered in the
        // cache, so it can't claim the path. Path threading below
        // (Save / Compile) takes a path parameter explicitly so we
        // don't need ResourcePath to stick for correctness — just for
        // the dock label and saved .tres metadata.
        try
        {
            loaded.ResourcePath = path;
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"[PS1Godot] PS1Graph: couldn't claim ResourcePath '{path}' on reconstructed graph: {ex.Message}. Save / Compile paths handle this via explicit pathOverride; label may show '(unsaved)' until next Save.");
        }
        _resource = loaded;
        // Track the path on the dock so UpdatePathLabel / OnSavePressed
        // see it even when ResourcePath didn't stick on the resource.
        _loadedFromPath = path;

        // Sync the toolbar Kind dropdown to whatever the loaded graph
        // says. Otherwise the right-click palette filters against the
        // *previous* selection (e.g. "Untyped") and dialogue-kind nodes
        // won't appear in the palette after loading a dialogue graph.
        if (_kindDropdown != null)
        {
            for (int i = 0; i < s_graphKinds.Length; i++)
            {
                if (s_graphKinds[i].Kind == _resource.Kind)
                {
                    _kindDropdown.Selected = i;
                    break;
                }
            }
        }

        ReloadGraphView();
        UpdatePathLabel();
        GD.Print($"[PS1Godot] PS1Graph: loaded '{path}' " +
                 $"(kind='{_resource.Kind}', {_resource.Nodes.Count} node(s), {_resource.Connections.Count} connection(s)).");
    }

    private void OnSavePressed()
    {
        SyncVisualPositionsBackToResource();

        string path = EffectivePath();
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
        NudgeFileSystem(path);
        GD.Print($"[PS1Godot] PS1Graph: saved '{path}'.");
        WriteCompiledLuaSibling(path);
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
        try { _resource.ResourcePath = path; }
        catch (System.Exception ex)
        {
            GD.PushWarning($"[PS1Godot] PS1Graph: couldn't claim ResourcePath on save: {ex.Message} — using path-tracked fallback.");
        }
        _loadedFromPath = path;
        var err = ResourceSaver.Save(_resource, path);
        if (err != Error.Ok)
        {
            GD.PushError($"[PS1Godot] PS1Graph: save failed ({err}) at '{path}'.");
            return;
        }
        NudgeFileSystem(path);
        UpdatePathLabel();
        GD.Print($"[PS1Godot] PS1Graph: saved '{path}'.");
        WriteCompiledLuaSibling(path);
    }

    // Tell the editor's FileSystem dock + resource cache that this path
    // just changed on disk. Without it, the file is silently up-to-date
    // but the dock + ResourceLoader return the prior cached version
    // until the editor restarts — looks like "Save didn't work" to the
    // author. Mirrors the same nudge used after the sibling .lua write.
    private static void NudgeFileSystem(string path)
    {
        try
        {
            EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(path);
        }
        catch (System.Exception ex)
        {
            GD.PushWarning($"[PS1Godot] PS1Graph: UpdateFile('{path}') failed: {ex.Message}. " +
                           "File is saved on disk; FileSystem dock may not refresh until next focus.");
        }
    }

    // Each Save also writes a sibling .lua holding the compiled output.
    // Authors attach that .lua to a node via the existing Lua-script
    // pipeline — no graph-aware code in the exporter / runtime needed.
    // If the compile fails (empty graph, malformed kind), the .lua
    // still gets written with the header comment + a TODO body so the
    // sibling exists and dependent script-list references don't break.
    private void WriteCompiledLuaSibling(string tresPath)
    {
        if (string.IsNullOrEmpty(tresPath)) return;
        string luaPath = System.IO.Path.ChangeExtension(tresPath, ".lua");
        // Pass tresPath as pathOverride: Godot 4.7-dev5 sometimes drops
        // a reconstructed PS1GraphResource's ResourcePath, which would
        // otherwise make the compiler emit `_G.dialogue_unnamed` from
        // basename "unnamed" — silently breaking every author script
        // that calls `Dialog.RunGraph(_G.dialogue_<correct_name>)`.
        string content = PS1GraphCompiler.Compile(_resource, tresPath);

        using (var f = Godot.FileAccess.Open(luaPath, Godot.FileAccess.ModeFlags.Write))
        {
            if (f == null)
            {
                var openErr = Godot.FileAccess.GetOpenError();
                GD.PushError($"[PS1Godot] PS1Graph: failed to open '{luaPath}' for write ({openErr}).");
                return;
            }
            f.StoreString(content);
        }

        // Nudge the editor's filesystem dock to pick up the new file
        // immediately. Without this, FileAccess writes to res:// don't
        // surface in the dock until the next manual rescan / editor
        // restart — the file is on disk, just invisible. UpdateFile on
        // a single path is much cheaper than a full Scan().
        if (luaPath.StartsWith("res://"))
        {
            try
            {
                EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(luaPath);
            }
            catch (System.Exception ex)
            {
                GD.PushWarning($"[PS1Godot] PS1Graph: UpdateFile('{luaPath}') failed: {ex.Message}. " +
                               "File is written; manually refresh the FileSystem dock to see it.");
            }
        }

        GD.Print($"[PS1Godot] PS1Graph: wrote compiled Lua to '{luaPath}'.");
    }

    // ── Palette ──────────────────────────────────────────────────────

    private void OnGraphPopupRequest(Vector2 atPosition)
    {
        try
        {
            if (_graphEdit == null || _palettePopup == null) return;

            // PopupRequest's at_position is GraphEdit-local (already in
            // widget coordinates, not screen). Convert to graph-canvas
            // coordinates for the spawn site so the node lands under the
            // cursor regardless of pan/zoom: canvas = (local + scroll) / zoom.
            _paletteSpawnCanvasPos = (atPosition + _graphEdit.ScrollOffset) / _graphEdit.Zoom;

            // Rebuild palette items filtered by the current graph's Kind.
            // Item id = index into s_kinds (so spawn matches the chosen kind),
            // not a contiguous menu index — using AddItem(..., id) keeps that
            // mapping stable even with hidden entries. AvailableInAll = true
            // is the wildcard escape hatch (Comment); everything else gates
            // on GraphKind == resource.Kind.
            _palettePopup.Clear();
            string currentGraphKind = _resource?.Kind ?? "";
            for (int i = 0; i < s_kinds.Length; i++)
            {
                var k = s_kinds[i];
                if (!k.AvailableInAll && k.GraphKind != currentGraphKind) continue;
                _palettePopup.AddItem(k.DisplayName, i);
            }

            // Popup at the global screen position so the menu appears under
            // the cursor — GetScreenPosition() gives GraphEdit's screen
            // origin; add the local click position to land at the cursor.
            Vector2 screenPos = _graphEdit.GetScreenPosition() + atPosition;
            _palettePopup.Position = new Vector2I((int)screenPos.X, (int)screenPos.Y);
            _palettePopup.Popup();
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[PS1Godot] PS1Graph: right-click palette threw: {ex}");
        }
    }

    private void OnPaletteItemPressed(long id)
    {
        try
        {
            int idx = (int)id;
            if (idx < 0 || idx >= s_kinds.Length) return;
            SpawnNode(s_kinds[idx].Kind, _paletteSpawnCanvasPos);
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[PS1Godot] PS1Graph: spawn node threw: {ex}");
        }
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
        "print"        => "Hello PSX",
        "branch"       => "true",   // Branch's literal-condition default — see BuildVisualBody row 3.
        "bool_literal" => "true",   // Same default for the literal Bool source.
        "set_flag"     => "true",   // Default flag value (Payloads[1]); Payloads[0] is name.
        _              => "",
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
        // Drop visual GraphElement children only. GraphEdit holds an
        // internal "connection_layer" child that draws all the
        // connection lines — freeing it puts GraphEdit in a state
        // where every subsequent frame errors "connections_layer is
        // missing", and any input that touches connections crashes.
        //
        // Use RemoveChild + QueueFree (not just QueueFree) so the names
        // free up immediately. Plain QueueFree defers disposal to the
        // next idle frame; if we then AddChild new GraphNodes with the
        // same names (n0, n1, ...) in this same call, Godot auto-
        // suffixes them ("n0@2") because the originals are still in the
        // tree. The auto-suffixed names break the ConnectNode lookup
        // below, leaving a populated tree with zero rendered nodes —
        // which looks like "load did nothing" to the author.
        var toRemove = new System.Collections.Generic.List<Node>();
        foreach (var child in _graphEdit.GetChildren())
        {
            if (child is GraphElement) toRemove.Add(child);
        }
        foreach (var child in toRemove)
        {
            _graphEdit.RemoveChild(child);
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
            case "bool_literal":
            {
                // Single-row pure-data source. No left pin; one right
                // pin (Bool out). CheckBox writes "true"/"false" to
                // Payload so the compiler can read the literal on emit.
                // Connecting this node's output to Branch's row-2 Bool
                // input drives Branch's condition with a connection,
                // overriding Branch's own CheckBox-literal fallback.
                var valueCheck = new CheckBox
                {
                    Text = "value",
                    ButtonPressed = ParseBoolPayload(n.Payload),
                };
                valueCheck.Toggled += pressed => n.Payload = pressed ? "true" : "false";
                g.AddChild(valueCheck);
                g.SetSlot(0, false, (int)PinType.Bool, s_pinColors[PinType.Bool],
                             true,  (int)PinType.Bool, s_pinColors[PinType.Bool]);
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
            case "line":
            {
                // Dialogue Line — speaker + text, single exec out (the
                // "next line" link). Compiles to a Lua table entry of
                // shape { kind="line", speaker=..., text=..., next=... }.
                //
                // Row 0: Exec in (left) + Exec out (right). Standard
                // dialogue advance.
                g.AddChild(new Label { Text = "exec / next" });
                g.SetSlot(0, true, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                // Row 1: speaker LineEdit. Pinless — slice D1a stores
                // the literal directly. Future slice can promote to a
                // typed String input if upstream Speaker sources become
                // useful.
                var speakerEdit = new LineEdit
                {
                    Text = n.GetPayload(1),
                    PlaceholderText = "speaker…",
                };
                speakerEdit.TextChanged += text => n.SetPayload(1, text);
                g.AddChild(speakerEdit);
                // No SetSlot — pinless.

                // Row 2: text LineEdit. Same story — pinless literal.
                var textEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "text…",
                };
                textEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(textEdit);
                // No SetSlot — pinless.
                break;
            }
            case "set_flag":
            {
                // Persist.Set("<name>", <bool>) — sets a save-state flag.
                // Row 0: Exec in/out. Row 1: flag-name LineEdit (Payloads[0]).
                // Row 2: value CheckBox (Payloads[1] as "true"/"false").
                g.AddChild(new Label { Text = "exec / next" });
                g.SetSlot(0, true, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var nameEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "flag name…",
                };
                nameEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(nameEdit);
                // No SetSlot — pinless.

                var valueCheck = new CheckBox
                {
                    Text = "value",
                    ButtonPressed = ParseBoolPayload(n.GetPayload(1)),
                };
                valueCheck.Toggled += pressed => n.SetPayload(1, pressed ? "true" : "false");
                g.AddChild(valueCheck);
                // No SetSlot — pinless.
                break;
            }
            case "condition":
            {
                // Branches on Persist.Get("<flag>") == true.
                // Row 0: Exec in (left) + Exec out true (right).
                // Row 1: Exec out false (right only).
                // Row 2: flag-name LineEdit (Payloads[0], pinless).
                g.AddChild(new Label { Text = "exec / true" });
                g.SetSlot(0, true,  (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true,  (int)PinType.Exec, s_pinColors[PinType.Exec]);

                g.AddChild(new Label { Text = "false" });
                g.SetSlot(1, false, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true,  (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var flagEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "flag name…",
                };
                flagEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(flagEdit);
                // No SetSlot — pinless.
                break;
            }
            case "play_sound":
            {
                // Audio.PlaySfx("<clip>") — fires an SFX and advances.
                // Row 0: Exec in/out. Row 1: clip-name LineEdit (Payloads[0]).
                g.AddChild(new Label { Text = "exec / next" });
                g.SetSlot(0, true, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var clipEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "audio clip name…",
                };
                clipEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(clipEdit);
                // No SetSlot — pinless.
                break;
            }
            case "start_cutscene":
            {
                // Cutscene.Play("<id>") — kicks a cutscene and advances.
                // The dialogue keeps running concurrently; if the author
                // wants the dialogue to wait, they should put this node
                // at the END of the line/choice chain (next = nil).
                g.AddChild(new Label { Text = "exec / next" });
                g.SetSlot(0, true, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var idEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "cutscene id…",
                };
                idEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(idEdit);
                // No SetSlot — pinless.
                break;
            }
            case "lua_snippet":
            {
                // Power-user action node — runs an arbitrary author-
                // supplied Lua snippet and auto-advances via the exec
                // edge. Same compile/runtime shape as set_flag /
                // play_sound (compiles to kind="action"), but the body
                // is whatever the author types.
                //
                // Row 0: Exec in/out.
                // Row 1: Lua snippet LineEdit (Payloads[0]).
                g.AddChild(new Label { Text = "exec / next" });
                g.SetSlot(0, true, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var snippetEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "Lua snippet (e.g. Audio.PlaySfx(\"x\"); Persist.Set(\"y\", true))…",
                };
                snippetEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(snippetEdit);
                // No SetSlot — pinless.
                break;
            }
            case "lua_condition":
            {
                // Power-user condition node — branches on an arbitrary
                // Lua expression instead of the structured flag-only
                // check the regular Condition node ships. Same exec
                // shape as condition (true on row 0, false on row 1),
                // and compiles to the same runtime kind="condition"
                // with a "return <expr>" body.
                //
                // Row 0: Exec in (left) + Exec out true (right).
                // Row 1: Exec out false (right only).
                // Row 2: expression LineEdit (Payloads[0], pinless).
                g.AddChild(new Label { Text = "exec / true" });
                g.SetSlot(0, true,  (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true,  (int)PinType.Exec, s_pinColors[PinType.Exec]);

                g.AddChild(new Label { Text = "false" });
                g.SetSlot(1, false, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true,  (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var exprEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "Lua expression returning bool (e.g. Persist.Get(\"x\") > 5)…",
                };
                exprEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(exprEdit);
                // No SetSlot — pinless.
                break;
            }
            case "choice":
            {
                // Dialogue Choice — N option rows, each with an option
                // text LineEdit + an Exec out for that option's
                // continuation. Compiles to a Lua table entry of shape
                // { kind="choice", options = { {text=..., next=...}, … } }.
                //
                // Slice D1a fixes the option count at 3 (covers most
                // dialogue branches; chain Choices for trees that need
                // wider fanout). Variable-pin support is a future
                // polish slice — Godot's GraphNode SetSlot model
                // doesn't grow rows dynamically.
                //
                // Row 0: Exec in (left only). No exec out at the top
                // — each option row carries its own.
                g.AddChild(new Label { Text = "exec in" });
                g.SetSlot(0, true,  (int)PinType.Exec, s_pinColors[PinType.Exec],
                             false, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                for (int opt = 0; opt < 3; opt++)
                {
                    int payloadIdx = opt; // option texts live in Payloads[0..2]
                    var optEdit = new LineEdit
                    {
                        Text = n.GetPayload(payloadIdx),
                        PlaceholderText = $"option {opt + 1}…",
                    };
                    optEdit.TextChanged += text => n.SetPayload(payloadIdx, text);
                    g.AddChild(optEdit);
                    // Row (opt+1): right pin only — Exec out for this option.
                    g.SetSlot(opt + 1, false, (int)PinType.Exec, s_pinColors[PinType.Exec],
                                       true,  (int)PinType.Exec, s_pinColors[PinType.Exec]);
                }
                break;
            }
            case "state":
            {
                // FSM State — represents one FSM state. Payloads[0] is
                // the state name (used as the Lua table key). Exec in
                // is the entry point; the lowest-Id state is initial.
                // Exec out fans out to one or more transition nodes;
                // GraphEdit allows multiple connections from a single
                // right pin.
                //
                // Per-state Lua snippets in Payloads[1..3] compile to
                // on_enter[name] / on_update[name] / on_exit[name]
                // lookup tables the FSM.new runtime helper dispatches
                // automatically. Single-line statements; multi-line
                // authoring chains with `;` (or use a Lua snippet node
                // pattern in a later slice).
                //
                // Row 0: Exec in (left) + Exec out (right).
                // Row 1: state name LineEdit (Payloads[0]).
                // Row 2: on_enter LineEdit (Payloads[1]).
                // Row 3: on_update LineEdit (Payloads[2]).
                // Row 4: on_exit LineEdit (Payloads[3]).
                g.AddChild(new Label { Text = "exec in / transitions" });
                g.SetSlot(0, true, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var nameEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "state name…",
                };
                nameEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(nameEdit);

                var enterEdit = new LineEdit
                {
                    Text = n.GetPayload(1),
                    PlaceholderText = "on_enter Lua (optional)…",
                };
                enterEdit.TextChanged += text => n.SetPayload(1, text);
                g.AddChild(enterEdit);

                var updateEdit = new LineEdit
                {
                    Text = n.GetPayload(2),
                    PlaceholderText = "on_update Lua (optional, gets dt)…",
                };
                updateEdit.TextChanged += text => n.SetPayload(2, text);
                g.AddChild(updateEdit);

                var exitEdit = new LineEdit
                {
                    Text = n.GetPayload(3),
                    PlaceholderText = "on_exit Lua (optional)…",
                };
                exitEdit.TextChanged += text => n.SetPayload(3, text);
                g.AddChild(exitEdit);
                // Rows 1..4 are pinless — no SetSlot calls.
                break;
            }
            case "objective":
            {
                // Quest Objective — one task the player must complete.
                // Payloads[0] = id (Lua table key + Persist key);
                // Payloads[1] = display title for HUD / journal.
                //
                // Exec in = AND of upstream objectives must complete
                // before this one becomes active. Exec out = "this one
                // gates downstream nodes." An objective with no incoming
                // exec edge is an "initial objective" — active when the
                // quest starts.
                //
                // Row 0: Exec in (left) + Exec out (right).
                // Row 1: id LineEdit (Payloads[0]).
                // Row 2: title LineEdit (Payloads[1]).
                g.AddChild(new Label { Text = "exec / unlocks" });
                g.SetSlot(0, true, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var idEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "objective id (e.g. find_npc)…",
                };
                idEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(idEdit);

                var titleEdit = new LineEdit
                {
                    Text = n.GetPayload(1),
                    PlaceholderText = "display title (HUD / journal)…",
                };
                titleEdit.TextChanged += text => n.SetPayload(1, text);
                g.AddChild(titleEdit);
                // Rows 1..2 pinless.
                break;
            }
            case "outcome":
            {
                // Quest Outcome — terminal node. When all incoming
                // objectives are complete, this outcome fires. Payload[0]
                // is the outcome id (the value `quest:Outcome()` returns
                // for branching downstream — victory / fail / bad_ending).
                //
                // Row 0: Exec in only.
                // Row 1: outcome id LineEdit (Payloads[0]).
                g.AddChild(new Label { Text = "exec in" });
                g.SetSlot(0, true,  (int)PinType.Exec, s_pinColors[PinType.Exec],
                             false, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var outIdEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "outcome id (e.g. victory, fail)…",
                };
                outIdEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(outIdEdit);
                // Row 1 pinless.
                break;
            }
            case "transition":
            {
                // FSM Transition — one event-driven edge between two
                // states. Payloads[0] is the event name. Exec in comes
                // from the source state's exec out; exec out goes to
                // the destination state's exec in.
                //
                // Row 0: Exec in (left) + Exec out (right).
                // Row 1: event name LineEdit (pinless).
                g.AddChild(new Label { Text = "from / to" });
                g.SetSlot(0, true, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var evEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "event name…",
                };
                evEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(evEdit);
                // No SetSlot — pinless.
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
        // FSM graphs MUST allow cycles — every back-edge in a state
        // machine (state A → transition → state B → transition → state
        // A) is a legitimate loop. The cycle guard exists for graph
        // kinds that compile to straight-line control flow (untyped,
        // dialogue actions chained, etc.); skip it for FSM.
        bool allowCycles = _resource?.Kind == "fsm";
        if (!allowCycles && WouldFormCycle(fromId, toId))
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
        string kindTag = "";
        foreach (var (k, display) in s_graphKinds)
        {
            if (k == (_resource?.Kind ?? "")) { kindTag = $"  [{display}]"; break; }
        }
        string path = EffectivePath();
        _pathLabel.Text = string.IsNullOrEmpty(path)
            ? $"(unsaved){kindTag}"
            : $"{path}{kindTag}";
    }

    // Resolves the "where is this graph saved" path with the binding-
    // quirk fallback. Prefer the Godot-managed ResourcePath when it
    // stuck; fall back to whatever Load remembered. Save / label /
    // compile all funnel through here so dialogues like
    // "(unsaved) for an FSM that's actually loaded from disk" don't
    // happen.
    private string EffectivePath()
    {
        var rp = _resource?.ResourcePath;
        if (!string.IsNullOrEmpty(rp)) return rp;
        return _loadedFromPath ?? "";
    }
}
#endif
