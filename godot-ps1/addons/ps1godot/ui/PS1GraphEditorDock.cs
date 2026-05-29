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

    // Node Details inspector (UE port-plan pick #1). Right pane of
    // an HSplitContainer; populated on NodeSelected, cleared on
    // NodeDeselected. Holds multi-line TextEdits for each payload
    // slot so authors can write long Lua snippets without packing
    // statements into a one-line LineEdit.
    private VBoxContainer? _inspectorPanel;
    private int _inspectorNodeId = -1;
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
        new NodeKindEntry("reroute",        "Reroute",          AvailableInAll: true),
        new NodeKindEntry("line",           "Line",             GraphKind: "dialogue"),
        new NodeKindEntry("choice",         "Choice",           GraphKind: "dialogue"),
        new NodeKindEntry("set_flag",       "Set Flag",         GraphKind: "dialogue"),
        new NodeKindEntry("condition",      "Condition (flag)", GraphKind: "dialogue"),
        new NodeKindEntry("play_sound",     "Play Sound",       GraphKind: "dialogue"),
        new NodeKindEntry("start_cutscene", "Start Cutscene",   GraphKind: "dialogue"),
        new NodeKindEntry("lua_snippet",    "Lua Snippet",      GraphKind: "dialogue"),
        new NodeKindEntry("lua_condition",  "Lua Condition",    GraphKind: "dialogue"),
        new NodeKindEntry("sub_dialogue",   "Sub-Dialogue",     GraphKind: "dialogue"),
        new NodeKindEntry("state",          "State",            GraphKind: "fsm"),
        new NodeKindEntry("transition",     "Transition",       GraphKind: "fsm"),
        new NodeKindEntry("objective",      "Objective",        GraphKind: "quest"),
        new NodeKindEntry("outcome",        "Outcome",          GraphKind: "quest"),
        new NodeKindEntry("bt_sequence",    "BT Sequence",      GraphKind: "bt"),
        new NodeKindEntry("bt_selector",    "BT Selector",      GraphKind: "bt"),
        new NodeKindEntry("bt_leaf",        "BT Leaf",          GraphKind: "bt"),
        new NodeKindEntry("bossbt_config",  "Boss Config",      GraphKind: "bossbt"),
        new NodeKindEntry("bossbt_phase",   "Boss Phase",       GraphKind: "bossbt"),
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
        ("bt",       "Behavior Tree"),
        ("bossbt",   "Boss BT (Combat.MeleeBoss config)"),
    };

    // Per-kind metadata for tooltips, palette categorisation, and the
    // forthcoming corner-icon / title-bar tint (UE port plan picks #5,
    // #6). Indexed by the same Kind string s_kinds uses. Missing
    // entries fall back to "(no description)" so adding a kind doesn't
    // need an entry to compile.
    private record KindMeta(string Category, string Tooltip);

    // Category-tint table for the GraphNode title bar (UE port-plan
    // pick #5). Each category gets a distinct hue: Dialogue = blue,
    // FSM = teal, Quest = orange-amber, Untyped = neutral grey, Meta =
    // muted purple. Picked low-saturation so the title strip reads as
    // a category tag, not a primary-color landmine.
    private static readonly System.Collections.Generic.Dictionary<string, Color> s_categoryTints = new()
    {
        ["Dialogue"] = new Color(0.30f, 0.55f, 0.85f),  // blue
        ["FSM"]      = new Color(0.30f, 0.75f, 0.70f),  // teal
        ["Quest"]    = new Color(0.85f, 0.60f, 0.30f),  // amber
        ["Untyped"]  = new Color(0.55f, 0.55f, 0.55f),  // grey
        ["Meta"]     = new Color(0.65f, 0.50f, 0.75f),  // muted purple
        ["BossBT"]   = new Color(0.80f, 0.35f, 0.40f),  // muted crimson — souls boss vibe
    };

    // Per-kind payload-slot labels for the Node Details inspector
    // (UE port-plan pick #1). Each entry maps Kind → array of human-
    // readable slot labels; the inspector renders one TextEdit per
    // label, indexed by position. Kinds without an entry get a
    // generic "Payload[N]" fallback. Inspector edits write back to
    // PS1GraphNode.Payloads — on the next graph reload the on-canvas
    // LineEdits re-read these values.
    private static readonly System.Collections.Generic.Dictionary<string, string[]> s_kindPayloadLabels = new()
    {
        ["print"]          = new[] { "message" },
        ["branch"]         = new[] { "default condition (true/false)" },
        ["bool_literal"]   = new[] { "value (true/false)" },
        ["comment"]        = new[] { "comment text" },
        ["reroute"]        = new string[0],   // no payloads — passthrough only
        ["line"]           = new[] { "text", "speaker", "audio clip", "skippable (true/false)", "notifies (frame:lua | ...)", "reveal mode (none / typewriter)", "reveal rate (chars/sec, default 30)" },
        ["choice"]         = new[] { "option 1 text", "option 2 text", "option 3 text" },
        ["set_flag"]       = new[] { "flag name", "value (true/false)" },
        ["condition"]      = new[] { "flag name" },
        ["play_sound"]     = new[] { "clip name" },
        ["start_cutscene"] = new[] { "cutscene id" },
        ["lua_snippet"]    = new[] { "Lua snippet" },
        ["lua_condition"]  = new[] { "Lua expression (no 'return ' prefix)" },
        ["sub_dialogue"]   = new[] { "target dialogue basename" },
        ["state"]          = new[] { "state name", "on_enter Lua", "on_update Lua", "on_exit Lua" },
        ["transition"]     = new[] { "event name" },
        ["objective"]      = new[] { "objective id", "display title", "on_activate Lua", "on_complete Lua" },
        ["outcome"]        = new[] { "outcome id", "on_trigger Lua" },
        ["bt_sequence"]    = new string[0],   // composite — no payloads
        ["bt_selector"]    = new string[0],
        ["bt_leaf"]        = new[] { "Lua snippet (return 'success' / 'failure' / 'running')" },
        ["bossbt_config"]  = new[]
        {
            "encounter_id (Persist key prefix; pair with PS1Encounter)",
            "aggro_radius (world units; e.g. 8)",
            "attack_radius (world units; e.g. 2)",
            "tell_frames (windup before swing; e.g. 30)",
            "hit_frames (swing-active window; e.g. 12)",
            "recover_frames (post-swing chase window; e.g. 30)",
            "swing_damage (e.g. 18)",
            "swing_range (world units; e.g. 2)",
            "hp_canvas (PS1UICanvas name; e.g. boss_hp)",
            "hp_element (PS1UIElement fill name; e.g. boss_hp_fill)",
            "on_tell Lua (e.g. Camera.ShakeRaw(82, 4))",
            "on_hit_land Lua (params: self, entity, hit, applied)",
            "on_death Lua (e.g. Camera.LockOff())",
            "swing_y_below (vertical reach below attacker; blank = swing_range)",
            "swing_y_above (vertical reach above attacker; blank = swing_range)",
            "iframes (per-hit invuln frames; e.g. 6; blank = no iframes)",
            "iframes_phase_change (long invuln during phase transition; e.g. 60)",
        },
        ["bossbt_phase"]   = new[]
        {
            "hp_ratio (0..1; e.g. 0.5 for half HP)",
            "tell_frames override (optional; blank = base)",
            "recover_frames override (optional; blank = base)",
            "on_enter Lua (e.g. Camera.ShakeRaw(900, 30))",
        },
    };

    // Corner-icon glyph table for the GraphNode title (UE port-plan
    // pick #5 companion). Side-effect / state-mutating kinds get a
    // visual flag the eye can scan: ▶ for actions (advance), ⚡ for
    // power-user Lua escape hatches, ✱ for outcomes / state-mutating.
    // Empty = no glyph (the common "render a value" case).
    private static readonly System.Collections.Generic.Dictionary<string, string> s_kindGlyphs = new()
    {
        ["set_flag"]       = "✱",
        ["play_sound"]     = "♪",
        ["start_cutscene"] = "▶",
        ["lua_snippet"]    = "⚡",
        ["lua_condition"]  = "⚡",
        ["sub_dialogue"]   = "↪",
        ["transition"]     = "→",
        ["outcome"]        = "🏁",
        ["bossbt_config"]  = "⚔",
        ["bossbt_phase"]   = "⚠",
    };
    private static readonly System.Collections.Generic.Dictionary<string, KindMeta> s_kindMeta = new()
    {
        ["print"]          = new("Untyped",  "Print one Lua expression to the debug console. Slice-1 placeholder kind."),
        ["branch"]         = new("Untyped",  "if/else split on a Bool input. Two exec outs (true/false)."),
        ["bool_literal"]   = new("Untyped",  "Constant Bool source. Feeds a Branch condition."),
        ["comment"]        = new("Meta",     "Pinless decoration. Compiles to nothing."),
        ["reroute"]        = new("Meta",     "Pinless 1-in/1-out passthrough — bends exec edges without affecting flow. Compiler chases through it like a Disabled node, so the runtime never sees it."),
        ["line"]           = new("Dialogue", "Display one line of dialogue. Optional audio clip; optional skippable flag; pipe-separated notify markers fire timed Lua snippets while the line is active."),
        ["choice"]         = new("Dialogue", "Branch on player input. Up to 3 options; D-pad navigates, X confirms. Walker prefixes selected option with > when no cursor element is on the canvas."),
        ["set_flag"]       = new("Dialogue", "Persist.Set(name, bool). Auto-advances."),
        ["condition"]      = new("Dialogue", "Branch on Persist.Get(flag) == true. Two exec outs (true/false)."),
        ["play_sound"]     = new("Dialogue", "Audio.PlaySfx(clip). Auto-advances. Use the Line node's audio field for voiced dialogue instead — this is for one-shot SFX between lines."),
        ["start_cutscene"] = new("Dialogue", "Cutscene.Play(id). Auto-advances; cutscene runs concurrently. Put at the end of the line chain (next=nil) if you want the dialogue to wait."),
        ["lua_snippet"]    = new("Dialogue", "Power-user: runs arbitrary Lua, auto-advances. Walker pcalls so syntax errors print rather than crash."),
        ["lua_condition"]  = new("Dialogue", "Power-user: branch on `return (<expr>)`. Empty expression compiles to `return false`."),
        ["sub_dialogue"]   = new("Dialogue", "Call into another dialogue table (target = .tres basename). Pushes a stack frame, walks the sub, returns at the exec-out on the sub's nil-next. Depth capped at 4."),
        ["state"]          = new("FSM",      "FSM state. Lowest-Id state is the initial state. Per-state Lua snippets (on_enter / on_update / on_exit) get dispatched by FSM.new."),
        ["transition"]     = new("FSM",      "Event-driven edge: source state's exec-out → transition's exec-in (event name) → destination state's exec-in. Send the event with instance:Send(name)."),
        ["objective"]      = new("Quest",    "Quest task. Incoming objective edges become prereqs (AND-merged). On-activate / on-complete snippets dispatch via Quest.new."),
        ["outcome"]        = new("Quest",    "Terminal quest node. Fires when all incoming objectives complete; Quest:Outcome() returns its id. on_trigger snippet fires once via fired-outcome tracking."),
        ["bt_sequence"]    = new("BT",       "Behavior Tree Sequence: ticks children left-to-right. Stops + returns 'failure' on first failed child; returns 'running' if any child returns 'running' (resumes next tick); returns 'success' only if all children succeed."),
        ["bt_selector"]    = new("BT",       "Behavior Tree Selector (fallback): ticks children left-to-right. Stops + returns 'success' on first succeeded child; returns 'running' if any child returns 'running'; returns 'failure' only if all children fail."),
        ["bt_leaf"]        = new("BT",       "Behavior Tree Leaf: author-supplied Lua snippet that must return 'success', 'failure', or 'running'. Snippet receives `self` (the BT instance) as parameter. Use self._scratch to hold per-tick state."),
        ["bossbt_config"]  = new("BossBT",   "Boss base config — one per BossBT graph. Compiles to the top-level Combat.MeleeBoss fields. Pair the encounter_id with a PS1Encounter node of the same id so the gate flag derives correctly. Empty payloads omitted from the compiled table (effective(key) fallback fills in)."),
        ["bossbt_phase"]   = new("BossBT",   "Boss phase override — zero or more per graph. hp_ratio is the trigger threshold (0..1 of MaxHP). Phases sort by descending hp_ratio in the compiled output so highest threshold fires first as HP drops. Override fields blank = inherit from base config."),
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

        // HSplit (UE port-plan pick #1) — GraphEdit on the left, Node
        // Details inspector on the right. SplitOffset sets the
        // inspector's width from the right edge; -300 keeps it compact
        // while still fitting a multi-line TextEdit for snippet
        // authoring. Author can drag the splitter to taste.
        var splitter = new HSplitContainer
        {
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SplitOffset = -300,
        };
        AddChild(splitter);

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
        _graphEdit.NodeSelected         += OnGraphNodeSelected;
        _graphEdit.NodeDeselected       += OnGraphNodeDeselected;
        splitter.AddChild(_graphEdit);

        // Node Details inspector (right pane).
        _inspectorPanel = new VBoxContainer
        {
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _inspectorPanel.AddThemeConstantOverride("separation", 6);
        splitter.AddChild(_inspectorPanel);
        ShowInspectorEmpty();

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
        // Don't assign ResourcePath on the reconstructed graph —
        // Godot's resource cache already holds the original (bare)
        // Resource at this path, and SetPath emits a native ERROR
        // ("Another resource is loaded from path") that the C# try/
        // catch can't suppress (the error fires inside C++ before any
        // C# exception path). _loadedFromPath below is the dock-side
        // source of truth; UpdatePathLabel / OnSavePressed / Compile
        // all funnel through EffectivePath() which prefers ResourcePath
        // when set and falls back to _loadedFromPath otherwise. The
        // fast-path generic load already had ResourcePath set by
        // ResourceLoader, so the reconstruct path is the only one that
        // needs the fallback.
        _resource = loaded;
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
        // Thread EffectivePath() so the preview matches what Save
        // would write — without it, reconstructed-loaded graphs (which
        // lose ResourcePath to the Godot 4.7-dev5 binding quirk)
        // compile as `_G.dialogue_unnamed` "from (unsaved)" and the
        // author thinks the dock is broken.
        string path = EffectivePath();
        string lua = PS1GraphCompiler.Compile(_resource, path);
        GD.Print("[PS1Godot] PS1Graph: compiled to Lua —");
        GD.Print(lua);
        if (string.IsNullOrEmpty(path))
        {
            GD.PushWarning("[PS1Godot] PS1Graph: graph has no saved path — sibling .lua not written. Use Save As first.");
            return;
        }
        WriteCompiledLuaSibling(path);
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
            int itemIdx = 0;
            for (int i = 0; i < s_kinds.Length; i++)
            {
                var k = s_kinds[i];
                if (!k.AvailableInAll && k.GraphKind != currentGraphKind) continue;
                _palettePopup.AddItem(k.DisplayName, i);
                // Hover-tooltip from s_kindMeta (UE port-plan pick #6).
                // Per-item tooltip on Godot PopupMenu is set by item
                // index, NOT by id — use the running itemIdx, not i.
                if (s_kindMeta.TryGetValue(k.Kind, out var meta))
                {
                    _palettePopup.SetItemTooltip(itemIdx, meta.Tooltip);
                }
                itemIdx++;
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
        // Tooltip on the node title — UE port-plan pick #6. Falls back
        // to "(no description)" so kinds that pre-date a metadata entry
        // still render a tooltip rather than nothing.
        if (s_kindMeta.TryGetValue(n.Kind, out var nodeMeta))
        {
            g.TooltipText = nodeMeta.Tooltip;

            // UE port-plan pick #5 — category-tinted title bar.
            // Godot's `title_color` theme entry only colors the title
            // TEXT, not the bar background — so overriding it alone
            // produces tinted text on a grey strip, which doesn't read
            // as a "category tag" at the dock's zoom level. Override
            // the `titlebar` StyleBox instead (and `titlebar_selected`
            // for the focused state) so the strip itself takes the
            // category color. Keep title text white for contrast on
            // the mid-saturation tints. Disabled nodes get a
            // desaturated grey strip (UE port-plan pick #2 feedback).
            Color titleTint = n.IsDisabled
                ? new Color(0.35f, 0.35f, 0.35f)
                : (s_categoryTints.TryGetValue(nodeMeta.Category, out var t) ? t : new Color(0.55f, 0.55f, 0.55f));
            var titleBar = new StyleBoxFlat
            {
                BgColor = titleTint,
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 4,
                ContentMarginBottom = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
            };
            var titleBarSelected = new StyleBoxFlat
            {
                BgColor = titleTint.Lightened(0.15f),
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 4,
                ContentMarginBottom = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderColor = Colors.White,
            };
            g.AddThemeStyleboxOverride("titlebar", titleBar);
            g.AddThemeStyleboxOverride("titlebar_selected", titleBarSelected);
            // Title text color — picked by relative luminance (Rec. 709)
            // so light tints (teal, amber, untyped grey, meta purple)
            // get dark text and dark tints (Disabled) get white.
            //
            // Godot 4.7-dev5 GraphNode keeps the title in an internal
            // Label themed as `GraphNodeTitleLabel`; the `title_color`
            // theme entry on the GraphNode itself doesn't reach the
            // Label. Override the Label's `font_color` directly via
            // GetTitlebarHbox so the contrast switch actually applies.
            float lum = 0.2126f * titleTint.R + 0.7152f * titleTint.G + 0.0722f * titleTint.B;
            Color titleTextColor = lum > 0.5f ? new Color(0.10f, 0.10f, 0.10f) : Colors.White;
            var titlebarHbox = g.GetTitlebarHBox();
            foreach (Node child in titlebarHbox.GetChildren())
            {
                if (child is Label titleLabel)
                {
                    titleLabel.AddThemeColorOverride("font_color", titleTextColor);
                    break;
                }
            }

            // Optional corner glyph — prepend to the title so it sits
            // left of the kind name. Cheaper than overlaying an icon
            // Control on the title bar (which GraphNode doesn't expose
            // a clean slot for) and reads fine at the dock's zoom range.
            string prefix = "";
            if (n.IsDisabled)             prefix = "[OFF] ";
            else if (n.IsDevelopmentOnly) prefix = "[DEV] ";
            else if (s_kindGlyphs.TryGetValue(n.Kind, out var glyph)) prefix = $"{glyph}  ";
            if (prefix.Length > 0)
            {
                g.Title = $"{prefix}{g.Title}";
            }
        }
        else
        {
            g.TooltipText = $"({n.Kind}) — no description registered in s_kindMeta.";
        }

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
            case "reroute":
            {
                // UE port-plan pick #3 — pinless 1-in/1-out passthrough
                // for bending exec edges. Body is a single bullet so
                // the node stays visually tiny. Compiler treats it as
                // transparent (chase-through, same as Disabled).
                g.AddChild(new Label
                {
                    Text = " • ",
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                g.SetSlot(0, true, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true, (int)PinType.Exec, s_pinColors[PinType.Exec]);
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
                // Dialogue Line — speaker + text + optional audio +
                // skippable flag. Compiles to:
                //   { kind="line", speaker=..., text=..., audio=...,
                //     skippable=..., next=... }
                //
                // Payloads:
                //   [0] text          [1] speaker
                //   [2] audio clip    [3] "true"/"false" (skippable)
                //
                // Row 0: Exec in + Exec out (standard dialogue advance).
                // Row 1: speaker LineEdit.
                // Row 2: text LineEdit.
                // Row 3: audio LineEdit (clip name; D1h).
                // Row 4: skippable CheckBox (D1h).
                g.AddChild(new Label { Text = "exec / next" });
                g.SetSlot(0, true, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var speakerEdit = new LineEdit
                {
                    Text = n.GetPayload(1),
                    PlaceholderText = "speaker…",
                };
                speakerEdit.TextChanged += text => n.SetPayload(1, text);
                g.AddChild(speakerEdit);

                var textEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "text…",
                };
                textEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(textEdit);

                var audioEdit = new LineEdit
                {
                    Text = n.GetPayload(2),
                    PlaceholderText = "audio clip name (optional)…",
                };
                audioEdit.TextChanged += text => n.SetPayload(2, text);
                g.AddChild(audioEdit);

                // Skippable defaults to true; empty Payload[3] (legacy
                // graphs from before D1h) reads as default-true so old
                // .tres files behave identically to pre-D1h.
                var skippableCheck = new CheckBox
                {
                    Text = "skippable",
                    ButtonPressed = !string.Equals(n.GetPayload(3), "false",
                                                    System.StringComparison.OrdinalIgnoreCase),
                };
                skippableCheck.Toggled += pressed => n.SetPayload(3, pressed ? "true" : "false");
                g.AddChild(skippableCheck);

                // Row 5: notifies LineEdit (D1i). Pipe-separated
                // "frame:lua | frame:lua" — fires each Lua snippet
                // when the line's frame counter reaches the threshold.
                // Up to 8 notifies per line (runtime cap).
                var notifiesEdit = new LineEdit
                {
                    Text = n.GetPayload(4),
                    PlaceholderText = "notifies: 12:Audio.PlaySfx(\"x\") | 30:...",
                };
                notifiesEdit.TextChanged += text => n.SetPayload(4, text);
                g.AddChild(notifiesEdit);
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
            case "sub_dialogue":
            {
                // Slice D1j — call into another dialogue table.
                // Payload[0] = target basename (e.g. "shopkeeper_greeting"
                // resolves at runtime to _G.dialogue_shopkeeper_greeting).
                // Exec in / out — out fires when the sub-dialogue
                // returns (hits a node with nil-next).
                g.AddChild(new Label { Text = "exec / return" });
                g.SetSlot(0, true, (int)PinType.Exec, s_pinColors[PinType.Exec],
                             true, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var targetEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "target dialogue basename (e.g. shopkeeper)…",
                };
                targetEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(targetEdit);
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
            case "bt_sequence":
            case "bt_selector":
            {
                // BT composite — N child exec-outs. Sequence runs them
                // left-to-right requiring all-success; Selector runs
                // them left-to-right requiring any-success. Same UI
                // shape (mirrors Choice's 3-option pattern but with 6
                // slots, since BT trees commonly fan wider).
                //
                // Row 0: Exec in (left only).
                // Rows 1..6: Exec out (right only) — child N at port (N-1)
                //            on the right side per GraphEdit indexing.
                string composite = n.Kind == "bt_sequence" ? "sequence" : "selector";
                g.AddChild(new Label { Text = $"exec in ({composite})" });
                g.SetSlot(0, true,  (int)PinType.Exec, s_pinColors[PinType.Exec],
                             false, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                const int kBtChildSlots = 6;
                for (int i = 0; i < kBtChildSlots; i++)
                {
                    g.AddChild(new Label { Text = $"child {i + 1}" });
                    g.SetSlot(i + 1, false, (int)PinType.Exec, s_pinColors[PinType.Exec],
                                      true,  (int)PinType.Exec, s_pinColors[PinType.Exec]);
                }
                break;
            }
            case "bt_leaf":
            {
                // BT Leaf — single Lua snippet returning the BT result
                // string. No exec-out (leaves are terminal in BT
                // semantics; the composite parent moves to the next
                // child / decides outcome based on this return value).
                //
                // Row 0: Exec in.
                // Row 1: snippet LineEdit (Payloads[0]).
                g.AddChild(new Label { Text = "exec in" });
                g.SetSlot(0, true,  (int)PinType.Exec, s_pinColors[PinType.Exec],
                             false, (int)PinType.Exec, s_pinColors[PinType.Exec]);

                var snippetEdit = new LineEdit
                {
                    Text = n.GetPayload(0),
                    PlaceholderText = "return 'success' / 'failure' / 'running'…",
                };
                snippetEdit.TextChanged += text => n.SetPayload(0, text);
                g.AddChild(snippetEdit);
                break;
            }
            case "objective":
            {
                // Quest Objective — one task the player must complete.
                // Payloads[0] = id (Lua table key + Persist key),
                // Payloads[1] = display title for HUD / journal,
                // Payloads[2] = on_activate Lua snippet (D2-3),
                // Payloads[3] = on_complete Lua snippet (D2-3).
                //
                // Exec in = AND of upstream objectives must complete
                // before this one becomes active. Exec out = "this one
                // gates downstream nodes." An objective with no incoming
                // exec edge is an "initial objective."
                //
                // Row 0: Exec in (left) + Exec out (right).
                // Row 1: id LineEdit (Payloads[0]).
                // Row 2: title LineEdit (Payloads[1]).
                // Row 3: on_activate LineEdit (Payloads[2]).
                // Row 4: on_complete LineEdit (Payloads[3]).
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

                var actEdit = new LineEdit
                {
                    Text = n.GetPayload(2),
                    PlaceholderText = "on_activate Lua (optional)…",
                };
                actEdit.TextChanged += text => n.SetPayload(2, text);
                g.AddChild(actEdit);

                var completeEdit = new LineEdit
                {
                    Text = n.GetPayload(3),
                    PlaceholderText = "on_complete Lua (optional)…",
                };
                completeEdit.TextChanged += text => n.SetPayload(3, text);
                g.AddChild(completeEdit);
                // Rows 1..4 pinless.
                break;
            }
            case "outcome":
            {
                // Quest Outcome — terminal node. Payloads[0] = outcome id,
                // Payloads[1] = on_trigger Lua snippet (D2-3, runs once
                // when the outcome first becomes satisfied).
                //
                // Row 0: Exec in only.
                // Row 1: outcome id LineEdit (Payloads[0]).
                // Row 2: on_trigger LineEdit (Payloads[1]).
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

                var triggerEdit = new LineEdit
                {
                    Text = n.GetPayload(1),
                    PlaceholderText = "on_trigger Lua (optional)…",
                };
                triggerEdit.TextChanged += text => n.SetPayload(1, text);
                g.AddChild(triggerEdit);
                // Rows 1..2 pinless.
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
            case "bossbt_config":
            {
                // BossBT base config (RFC docs/internal/rfc/bossbt-graph-kind.md).
                // 13 payload slots → 13 LineEdits. No pins — phases are
                // gathered by Kind, not by exec connections, so this
                // node is purely a parameter carrier.
                //
                // Payload slot layout matches PS1GraphCompiler.CompileBossBt.
                // Mismatched indices = silent compiler emission of the
                // wrong field, so keep the order in lockstep with both
                // s_kindPayloadLabels["bossbt_config"] and CompileBossBt.
                g.AddChild(new Label { Text = "boss config (drive Combat.MeleeBoss)" });
                EmitBossBtPayloadEdit(g, n, 0,  "encounter_id… (e.g. smoke_boss)");
                EmitBossBtPayloadEdit(g, n, 1,  "aggro_radius (units, e.g. 8)");
                EmitBossBtPayloadEdit(g, n, 2,  "attack_radius (units, e.g. 2)");
                EmitBossBtPayloadEdit(g, n, 3,  "tell_frames (e.g. 30)");
                EmitBossBtPayloadEdit(g, n, 4,  "hit_frames (e.g. 12)");
                EmitBossBtPayloadEdit(g, n, 5,  "recover_frames (e.g. 30)");
                EmitBossBtPayloadEdit(g, n, 6,  "swing_damage (e.g. 18)");
                EmitBossBtPayloadEdit(g, n, 7,  "swing_range (units, e.g. 2)");
                EmitBossBtPayloadEdit(g, n, 8,  "hp_canvas (e.g. boss_hp)");
                EmitBossBtPayloadEdit(g, n, 9,  "hp_element (e.g. boss_hp_fill)");
                EmitBossBtPayloadEdit(g, n, 10, "on_tell Lua…");
                EmitBossBtPayloadEdit(g, n, 11, "on_hit_land Lua…");
                EmitBossBtPayloadEdit(g, n, 12, "on_death Lua…");
                // Advanced fields (slots 13-16) — asymmetric swing AABB
                // + i-frame knobs. Blank = library default (symmetric
                // swing_range, no iframes). Appended slots so older
                // .tres files still load + compile correctly.
                EmitBossBtPayloadEdit(g, n, 13, "swing_y_below (blank = swing_range)");
                EmitBossBtPayloadEdit(g, n, 14, "swing_y_above (blank = swing_range)");
                EmitBossBtPayloadEdit(g, n, 15, "iframes per hit (e.g. 6)");
                EmitBossBtPayloadEdit(g, n, 16, "iframes during phase change (e.g. 60)");
                break;
            }
            case "bossbt_phase":
            {
                // BossBT phase override (RFC). 4 payload slots → 4
                // LineEdits. Pinless like the config node — phases are
                // gathered by Kind and sorted by descending hp_ratio
                // at compile, so no exec edges are needed.
                //
                // hp_ratio is the entry threshold (0..1 of MaxHP);
                // tell_frames / recover_frames override the base
                // config values; on_enter fires once when the phase
                // becomes active.
                g.AddChild(new Label { Text = "phase override" });
                EmitBossBtPayloadEdit(g, n, 0, "hp_ratio (0..1, e.g. 0.5)");
                EmitBossBtPayloadEdit(g, n, 1, "tell_frames override (blank = base)");
                EmitBossBtPayloadEdit(g, n, 2, "recover_frames override (blank = base)");
                EmitBossBtPayloadEdit(g, n, 3, "on_enter Lua…");
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

    // Helper for BossBT config / phase node bodies — adds one LineEdit
    // hooked to PS1GraphNode.SetPayload(payloadIdx, …). Used per payload
    // slot in both cases to keep BuildVisualBody readable. No SetSlot
    // call — BossBT nodes are pinless (config / phase data carriers
    // gathered by Kind, not exec edges).
    private static void EmitBossBtPayloadEdit(GraphNode g, PS1GraphNode n,
        int payloadIdx, string placeholder)
    {
        var edit = new LineEdit
        {
            Text = n.GetPayload(payloadIdx),
            PlaceholderText = placeholder,
        };
        edit.TextChanged += text => n.SetPayload(payloadIdx, text);
        g.AddChild(edit);
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

    // ── Node Details inspector (UE port-plan pick #1) ──────────────

    private void OnGraphNodeSelected(Node selected)
    {
        if (selected is not GraphNode gn) return;
        int id = ExtractIdFromVisualName(gn.Name);
        if (id < 0) return;
        PS1GraphNode? node = null;
        foreach (var n in _resource.Nodes)
        {
            if (n.Id == id) { node = n; break; }
        }
        if (node == null) return;
        BuildInspectorFor(node);
        _inspectorNodeId = id;
    }

    private void OnGraphNodeDeselected(Node deselected)
    {
        if (deselected is not GraphNode gn) return;
        int id = ExtractIdFromVisualName(gn.Name);
        // Only clear if the deselected node is the one currently shown
        // — multi-select would otherwise wipe the panel on every click.
        if (id != _inspectorNodeId) return;
        ShowInspectorEmpty();
        _inspectorNodeId = -1;
    }

    private void ShowInspectorEmpty()
    {
        if (_inspectorPanel == null) return;
        foreach (var child in _inspectorPanel.GetChildren())
        {
            _inspectorPanel.RemoveChild(child);
            child.QueueFree();
        }
        var hint = new Label
        {
            Text = "Select a node to edit its payloads here.\n\nMulti-line snippets supported — Enter inserts a newline.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
        _inspectorPanel.AddChild(hint);
    }

    private void BuildInspectorFor(PS1GraphNode node)
    {
        if (_inspectorPanel == null) return;
        foreach (var child in _inspectorPanel.GetChildren())
        {
            _inspectorPanel.RemoveChild(child);
            child.QueueFree();
        }

        // Header — kind + id + tooltip text (so the author always has
        // the kind's description visible while editing).
        var title = new Label
        {
            Text = $"{TitleFor(node)}  (id #{node.Id})",
        };
        title.AddThemeColorOverride("font_color", new Color(0.75f, 0.85f, 1.00f));
        _inspectorPanel.AddChild(title);

        // Enabled tri-state (UE port-plan pick #2). Sits at the top of
        // the inspector so the state toggle is always one click away
        // when debugging a graph — flip a node Disabled to mute it
        // without deleting + re-authoring it, then flip back.
        var stateRow = new HBoxContainer();
        stateRow.AddThemeConstantOverride("separation", 6);
        stateRow.AddChild(new Label { Text = "State:", VerticalAlignment = VerticalAlignment.Center });
        var stateBtn = new OptionButton();
        stateBtn.AddItem("Enabled",          (int)PS1GraphNode.NodeEnabledState.Enabled);
        stateBtn.AddItem("Disabled",         (int)PS1GraphNode.NodeEnabledState.Disabled);
        stateBtn.AddItem("DevelopmentOnly",  (int)PS1GraphNode.NodeEnabledState.DevelopmentOnly);
        stateBtn.Selected = (int)node.EnabledState;
        stateBtn.TooltipText =
            "Enabled: compiles normally.\n" +
            "Disabled: skipped at compile — surrounding graph's exec edges chase through.\n" +
            "DevelopmentOnly: emits with a marker comment; future slice can strip via build flag.";
        stateBtn.ItemSelected += idx =>
        {
            node.EnabledState = (PS1GraphNode.NodeEnabledState)(int)idx;
            // Refresh the canvas so the title prefix + tint update
            // immediately. ReloadGraphView is a hammer but the dock's
            // node counts are small and selection loss is acceptable.
            ReloadGraphView();
        };
        stateRow.AddChild(stateBtn);
        _inspectorPanel.AddChild(stateRow);

        if (s_kindMeta.TryGetValue(node.Kind, out var meta))
        {
            var desc = new Label
            {
                Text = meta.Tooltip,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            desc.AddThemeColorOverride("font_color", new Color(0.70f, 0.70f, 0.70f));
            _inspectorPanel.AddChild(desc);
            _inspectorPanel.AddChild(new HSeparator());
        }

        // Resolve the labels list. Kinds without an entry render
        // payloads as "Payload[0]", "Payload[1]" etc. up to a sensible
        // cap (4 slots) so a new kind is still editable.
        string[] labels = s_kindPayloadLabels.TryGetValue(node.Kind, out var l)
            ? l
            : new[] { "Payload[0]", "Payload[1]", "Payload[2]", "Payload[3]" };

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _inspectorPanel.AddChild(scroll);

        var rows = new VBoxContainer
        {
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        rows.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(rows);

        for (int i = 0; i < labels.Length; i++)
        {
            int slot = i;  // capture
            rows.AddChild(new Label { Text = labels[i] });
            var edit = new TextEdit
            {
                Text = node.GetPayload(slot),
                CustomMinimumSize = new Vector2(0, 64),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ScrollFitContentHeight = true,
                WrapMode = TextEdit.LineWrappingMode.Boundary,
            };
            edit.TextChanged += () => node.SetPayload(slot, edit.Text ?? "");
            rows.AddChild(edit);
        }
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
