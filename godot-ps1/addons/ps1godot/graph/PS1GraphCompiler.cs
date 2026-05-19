using System.Collections.Generic;
using System.Text;

namespace PS1Godot.Graph;

// PS1Graph → Lua compiler. Slice 4a: supports print / branch / comment
// from the slice-1..3 framework. Output is a flat sequence of Lua
// statements, one per exec root, emitted in node-Id order.
//
// Slot indices below MUST match the per-kind layout in
// PS1GraphEditorDock.BuildVisualBody — they are the contract between
// the visual and the compiler. A future slice may extract the slot
// layout into a shared per-kind table so the duplication can't drift,
// but slice 4a accepts the duplication with explicit comments at each
// hardcoded port reference.
//
// Data-pin resolution: for each data input we look for an incoming
// connection. If present, walk back through the source node's matching
// output (recursive). If absent, fall back to the input node's own
// Payload string (Print's literal value mode). No Bool-producing kinds
// exist yet, so Branch's condition always resolves to the literal
// `false` — a Bool Literal + comparison nodes are slice-5 work.
public static class PS1GraphCompiler
{
    public static string Compile(PS1GraphResource resource, string? pathOverride = null)
    {
        if (resource?.Nodes == null) return "";

        // pathOverride lets callers thread the file path through even
        // when `resource.ResourcePath` is unreliable — happens with
        // Godot 4.7-dev5's flaky C# binding for custom-script Resources
        // where a freshly-constructed PS1GraphResource won't accept a
        // ResourcePath assignment through the C++ getter. Without this,
        // a dock-save of a reconstructed graph compiles as `_G.dialogue_unnamed`
        // even though the .tres is saved at a real path.
        string effectivePath = !string.IsNullOrEmpty(pathOverride)
            ? pathOverride
            : (resource.ResourcePath ?? "");

        // Dispatch on graph Kind. Kinds compile to different Lua shapes:
        //   "" (untyped) → flat statement sequence (slice 4 model).
        //   "dialogue"   → a _G.dialogue_<basename> table the runtime
        //                  walker (slice D1b) interprets at gameplay
        //                  time, not at scene init.
        //   "fsm"        → a _G.fsm_<basename> table the author drives
        //                  manually (slice D3-1 — no runtime helper
        //                  yet; FSM.new shipping later as slice D3-2).
        //   "quest"      → a _G.quest_<basename> table; objectives +
        //                  outcomes with prereq edges. Author drives
        //                  manually until Quest.new ships (slice D2-2).
        //   "bt"         → a _G.bt_<basename> behavior-tree table.
        //                  Sequence/Selector/Leaf nodes; BT.new ticks
        //                  it once per frame from author Lua.
        return resource.Kind switch
        {
            "dialogue" => CompileDialogue(resource, effectivePath),
            "fsm"      => CompileFsm(resource, effectivePath),
            "quest"    => CompileQuest(resource, effectivePath),
            "bt"       => CompileBt(resource, effectivePath),
            _          => CompileUntyped(resource),
        };
    }

    private static string CompileUntyped(PS1GraphResource resource)
    {
        var sb = new StringBuilder();
        string pathLabel = string.IsNullOrEmpty(resource.ResourcePath) ? "(unsaved)" : resource.ResourcePath;
        sb.AppendLine($"-- Compiled from {pathLabel}");
        sb.AppendLine($"-- {resource.Nodes.Count} node(s), {resource.Connections.Count} connection(s)");
        sb.AppendLine();

        // Index nodes by Id, build the "has incoming exec edge" set so
        // we can identify roots in one pass.
        var byId = new Dictionary<int, PS1GraphNode>();
        foreach (var n in resource.Nodes) byId[n.Id] = n;

        var hasIncomingExec = new HashSet<int>();
        foreach (var c in resource.Connections)
        {
            if (IsExecPort(byId, c.ToNodeId, c.ToPort, isInput: true))
            {
                hasIncomingExec.Add(c.ToNodeId);
            }
        }

        // Roots = exec-bearing nodes whose exec input is unconnected,
        // in Id order. Id order matches creation order, so successive
        // "+ Print" presses produce output in the order the author saw
        // them appear — predictable enough for slice 4a.
        var roots = new List<int>();
        foreach (var n in resource.Nodes)
        {
            if (!HasExecInput(n.Kind)) continue;
            if (hasIncomingExec.Contains(n.Id)) continue;
            roots.Add(n.Id);
        }
        roots.Sort();

        foreach (var rootId in roots)
        {
            EmitNode(sb, byId, resource.Connections, rootId, indent: 0);
        }

        return sb.ToString();
    }

    // Dialogue compile: emit `_G.dialogue_<basename> = { entry=..., nodes={...} }`.
    // The runtime walker (slice D1b — `Dialog.RunGraph(table)` in luaapi)
    // will read .entry, look up nodes[entry], dispatch on .kind, and
    // follow .next / .options[].next for navigation. Authors invoke
    // the walker from their own Lua via the predictable global name.
    //
    // No statements compile to ambient effect — the chunk's top-level
    // is a single assignment, so loading the .lua just installs the
    // table without running anything. That matches the player-driven
    // semantics dialogue needs (no auto-advance).
    private static string CompileDialogue(PS1GraphResource resource, string effectivePath)
    {
        var sb = new StringBuilder();
        string pathLabel = string.IsNullOrEmpty(effectivePath) ? "(unsaved)" : effectivePath;
        string basename = BasenameForGlobal(effectivePath);
        sb.AppendLine($"-- Compiled from {pathLabel} (dialogue)");
        sb.AppendLine($"-- {resource.Nodes.Count} node(s), {resource.Connections.Count} connection(s)");
        sb.AppendLine($"-- Walker: Dialog.RunGraph(_G.dialogue_{basename})");
        sb.AppendLine();

        var byId = new Dictionary<int, PS1GraphNode>();
        foreach (var n in resource.Nodes) byId[n.Id] = n;

        // Entry = the first dialogue node with no incoming exec edge,
        // in Id order. If there's no dialogue-kind node at all, entry
        // is `nil` and the table is essentially empty.
        //
        // Edges from Disabled source nodes don't count — disabling a
        // node falls through its exec edges (FindNextKey already chases
        // past Disabled for downstream navigation), so the entry-picker
        // mirrors that and promotes the next downstream node when the
        // author disables the entry. Without this, disabling the entry
        // would orphan every reachable node and the dialogue dies with
        // `entry = nil` even though the rest of the graph is intact.
        var hasIncomingExec = new HashSet<int>();
        foreach (var c in resource.Connections)
        {
            if (!IsExecPort(byId, c.ToNodeId, c.ToPort, isInput: true)) continue;
            if (byId.TryGetValue(c.FromNodeId, out var src) && src.IsDisabled) continue;
            hasIncomingExec.Add(c.ToNodeId);
        }
        int entryId = -1;
        foreach (var n in resource.Nodes)
        {
            if (!IsDialogueKind(n.Kind)) continue;
            if (n.IsDisabled) continue;  // pick #2 — disabled can't be entry
            if (hasIncomingExec.Contains(n.Id)) continue;
            if (entryId < 0 || n.Id < entryId) entryId = n.Id;
        }

        sb.AppendLine($"_G.dialogue_{basename} = {{");
        sb.AppendLine($"    entry = {(entryId < 0 ? "nil" : "\"n" + entryId + "\"")},");
        sb.AppendLine($"    nodes = {{");

        foreach (var n in resource.Nodes)
        {
            if (!IsDialogueKind(n.Kind)) continue;
            // Disabled nodes (UE port-plan pick #2) are pruned from
            // the compiled table entirely — exec-edge chase logic in
            // FindNextKey routes other nodes past them.
            if (n.IsDisabled) continue;
            if (n.IsDevelopmentOnly)
            {
                sb.AppendLine($"        -- @dev-only n{n.Id}");
            }
            EmitDialogueNode(sb, byId, resource.Connections, n);
        }

        sb.AppendLine("    },");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static bool IsDialogueKind(string kind) => kind switch
    {
        "line"           => true,
        "choice"         => true,
        "set_flag"       => true,
        "condition"      => true,
        "play_sound"     => true,
        "start_cutscene" => true,
        "lua_snippet"    => true,
        "lua_condition"  => true,
        "sub_dialogue"   => true,
        _                => false,
    };

    private static bool IsFsmKind(string kind) => kind switch
    {
        "state"      => true,
        "transition" => true,
        _            => false,
    };

    // FSM compile — emit a Lua table the author drives manually for
    // slice D3-1; slice D3-2 will add a runtime `FSM.new` helper that
    // consumes this exact shape. States become a string-array; each
    // transition node is one entry in the transitions array, addressed
    // by upstream state name + event + downstream state name.
    //
    // Shape:
    //   _G.fsm_<basename> = {
    //       initial = "patrol",
    //       states = { "patrol", "chase", "attack" },
    //       transitions = {
    //           { from = "patrol", event = "see_player", to = "chase" },
    //           ...
    //       },
    //   }
    //
    // Initial-state selection: the lowest-Id state node is chosen as
    // initial. Trivial rule for slice 1; later slices may add an
    // explicit "is initial" checkbox on the state node or a separate
    // Entry node kind. States with no name (empty Payload[0]) get
    // skipped at emit time — they'd produce an invalid Lua key.
    // Transitions with a missing endpoint (no incoming state, no
    // outgoing state, or unresolvable state ref) are skipped with a
    // single comment line so the .lua stays valid.
    private static string CompileFsm(PS1GraphResource resource, string effectivePath)
    {
        var sb = new StringBuilder();
        string pathLabel = string.IsNullOrEmpty(effectivePath) ? "(unsaved)" : effectivePath;
        string basename = BasenameForGlobal(effectivePath);
        sb.AppendLine($"-- Compiled from {pathLabel} (fsm)");
        sb.AppendLine($"-- {resource.Nodes.Count} node(s), {resource.Connections.Count} connection(s)");
        sb.AppendLine($"-- Author: drive via _G.fsm_{basename}.states / .transitions until FSM.new ships.");
        sb.AppendLine();

        var byId = new Dictionary<int, PS1GraphNode>();
        foreach (var n in resource.Nodes) byId[n.Id] = n;

        // States = state nodes with non-empty Payload[0], in Id order
        // (so the "initial = lowest-Id" rule is deterministic).
        var stateNodes = new List<PS1GraphNode>();
        foreach (var n in resource.Nodes)
        {
            if (n.Kind != "state") continue;
            if (n.IsDisabled) continue;  // pick #2 — pruned from `states`
            if (string.IsNullOrEmpty(n.GetPayload(0))) continue;
            stateNodes.Add(n);
        }
        stateNodes.Sort((a, b) => a.Id.CompareTo(b.Id));

        string initial = stateNodes.Count > 0 ? stateNodes[0].GetPayload(0) : "";

        sb.AppendLine($"_G.fsm_{basename} = {{");
        sb.AppendLine($"    initial = {(string.IsNullOrEmpty(initial) ? "nil" : EscapeLuaString(initial))},");

        sb.AppendLine($"    states = {{");
        foreach (var s in stateNodes)
        {
            sb.AppendLine($"        {EscapeLuaString(s.GetPayload(0))},");
        }
        sb.AppendLine($"    }},");

        sb.AppendLine($"    transitions = {{");
        foreach (var n in resource.Nodes)
        {
            if (n.Kind != "transition") continue;
            if (n.IsDisabled) continue;  // pick #2 — pruned from `transitions`
            string ev = n.GetPayload(0);
            if (string.IsNullOrEmpty(ev))
            {
                sb.AppendLine($"        -- skipped transition n{n.Id}: no event name authored.");
                continue;
            }
            // From-state = the state node connected to this transition's
            // exec-in. To-state = the state node this transition's
            // exec-out drives.
            string? fromState = ResolveAdjacentStateName(byId, resource.Connections,
                                                          predicate: c => c.ToNodeId == n.Id && c.ToPort == 0,
                                                          isFromSide: true);
            string? toState   = ResolveAdjacentStateName(byId, resource.Connections,
                                                          predicate: c => c.FromNodeId == n.Id && c.FromPort == 0,
                                                          isFromSide: false);
            if (fromState == null || toState == null)
            {
                sb.AppendLine($"        -- skipped transition n{n.Id} ('{ev}'): missing {(fromState == null ? "from-state" : "to-state")} endpoint.");
                continue;
            }
            sb.AppendLine($"        {{ from = {EscapeLuaString(fromState)}, event = {EscapeLuaString(ev)}, to = {EscapeLuaString(toState)} }},");
        }
        sb.AppendLine($"    }},");

        // Per-state callback tables. Slice D3-3 emits `on_enter`,
        // `on_update`, `on_exit` lookup tables (keyed by state name)
        // wrapping each non-empty snippet in a Lua function with
        // (self, event_or_dt) parameters. Empty tables are emitted
        // even when no states have snippets in that category so the
        // FSM.new helper's defensive `if d.on_enter and d.on_enter[..]`
        // checks short-circuit on miss without a table-not-found.
        EmitFsmCallbackTable(sb, stateNodes, "on_enter",  payloadIdx: 1, paramName: "event");
        EmitFsmCallbackTable(sb, stateNodes, "on_update", payloadIdx: 2, paramName: "dt");
        EmitFsmCallbackTable(sb, stateNodes, "on_exit",   payloadIdx: 3, paramName: "event");

        sb.AppendLine("}");
        return sb.ToString();
    }

    // Emit one FSM callback lookup table. Skips states with no snippet
    // in this slot so the table only contains the entries the author
    // actually wrote — FSM.new dispatches via key-lookup, missing entries
    // are no-ops. The wrapping `function(self, <paramName>) <snippet> end`
    // matches the signature the helper invokes with.
    private static void EmitFsmCallbackTable(StringBuilder sb, List<PS1GraphNode> states,
                                              string tableName, int payloadIdx, string paramName)
    {
        sb.AppendLine($"    {tableName} = {{");
        foreach (var s in states)
        {
            string snippet = s.GetPayload(payloadIdx);
            if (string.IsNullOrEmpty(snippet)) continue;
            string stateName = s.GetPayload(0);
            // Keys with a Lua-safe identifier go through `name = ...`;
            // anything else gets bracketed via `[\"name\"] = ...`. State
            // names from the dock LineEdit can contain spaces / punctuation,
            // so be defensive.
            string keyForm = IsLuaIdentifier(stateName)
                ? stateName
                : $"[{EscapeLuaString(stateName)}]";
            sb.AppendLine($"        {keyForm} = function(self, {paramName}) {snippet} end,");
        }
        sb.AppendLine($"    }},");
    }

    // Quest compile (slice D2-1) — emit a Lua table the author drives
    // manually; slice D2-2 will add a runtime `Quest.new` helper that
    // consumes this exact shape. Shape:
    //
    //   _G.quest_<basename> = {
    //       initial_objectives = { "find_npc" },
    //       objectives = {
    //           find_npc    = { id = "find_npc",    title = "...", prereqs = {} },
    //           talk_to_npc = { id = "talk_to_npc", title = "...", prereqs = { "find_npc" } },
    //       },
    //       outcomes = {
    //           { id = "victory", prereqs = { "defeat_orc" } },
    //       },
    //   }
    //
    // Prereqs come from incoming exec edges on each objective/outcome.
    // Multiple incoming edges = AND (all upstream objectives must be
    // complete before this node activates / fires). OR-of-prereqs is
    // expressed by having multiple objectives drive the same downstream
    // node from different chains.
    //
    // Initial objectives = objective nodes whose exec-in has no
    // incoming edge from another objective. These activate when the
    // quest starts.
    //
    // Outcomes are terminal — the quest helper exposes `:Outcome()`
    // which returns the first outcome whose prereqs are all satisfied,
    // or nil if none yet. Cycle guard stays on for quest graphs (unlike
    // FSM) — a quest with a back-edge would be a logic bug.
    private static string CompileQuest(PS1GraphResource resource, string effectivePath)
    {
        var sb = new StringBuilder();
        string pathLabel = string.IsNullOrEmpty(effectivePath) ? "(unsaved)" : effectivePath;
        string basename = BasenameForGlobal(effectivePath);
        sb.AppendLine($"-- Compiled from {pathLabel} (quest)");
        sb.AppendLine($"-- {resource.Nodes.Count} node(s), {resource.Connections.Count} connection(s)");
        sb.AppendLine($"-- Author: drive _G.quest_{basename}.objectives / .outcomes until Quest.new ships.");
        sb.AppendLine();

        var byId = new Dictionary<int, PS1GraphNode>();
        foreach (var n in resource.Nodes) byId[n.Id] = n;

        // Index objectives by their authored id (Payload[0]) so prereq
        // resolution can produce "objective_a", not "n3". Objectives
        // without an id get skipped — Lua keys can't be empty strings.
        var objectiveNodes = new List<PS1GraphNode>();
        foreach (var n in resource.Nodes)
        {
            if (n.Kind != "objective") continue;
            if (n.IsDisabled) continue;  // pick #2 — pruned from `objectives`
            if (string.IsNullOrEmpty(n.GetPayload(0))) continue;
            objectiveNodes.Add(n);
        }
        objectiveNodes.Sort((a, b) => a.Id.CompareTo(b.Id));

        // Initial objectives = those with no incoming exec edge from
        // any other objective. Outcomes don't count as upstream — an
        // outcome's exec-out is null (it's a terminal node).
        var initialObjectiveIds = new List<string>();
        foreach (var o in objectiveNodes)
        {
            if (HasIncomingObjectiveEdge(o.Id, byId, resource.Connections)) continue;
            initialObjectiveIds.Add(o.GetPayload(0));
        }

        sb.AppendLine($"_G.quest_{basename} = {{");
        sb.Append($"    initial_objectives = {{");
        for (int i = 0; i < initialObjectiveIds.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(EscapeLuaString(initialObjectiveIds[i]));
        }
        sb.AppendLine(" },");

        sb.AppendLine($"    objectives = {{");
        foreach (var o in objectiveNodes)
        {
            string id    = o.GetPayload(0);
            string title = o.GetPayload(1);
            var prereqs  = ResolveUpstreamObjectiveIds(o.Id, byId, resource.Connections);
            string keyForm = IsLuaIdentifier(id) ? id : $"[{EscapeLuaString(id)}]";

            sb.Append($"        {keyForm} = {{ id = {EscapeLuaString(id)}, title = {EscapeLuaString(title)}, prereqs = {{");
            for (int i = 0; i < prereqs.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(EscapeLuaString(prereqs[i]));
            }
            sb.AppendLine(" } },");
        }
        sb.AppendLine($"    }},");

        sb.AppendLine($"    outcomes = {{");
        var outcomeNodes = new List<PS1GraphNode>();
        foreach (var n in resource.Nodes)
        {
            if (n.Kind != "outcome") continue;
            if (n.IsDisabled) continue;  // pick #2 — pruned from `outcomes`
            string id = n.GetPayload(0);
            if (string.IsNullOrEmpty(id))
            {
                sb.AppendLine($"        -- skipped outcome n{n.Id}: no id authored.");
                continue;
            }
            outcomeNodes.Add(n);
            var prereqs = ResolveUpstreamObjectiveIds(n.Id, byId, resource.Connections);
            sb.Append($"        {{ id = {EscapeLuaString(id)}, prereqs = {{");
            for (int i = 0; i < prereqs.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(EscapeLuaString(prereqs[i]));
            }
            sb.AppendLine(" } },");
        }
        sb.AppendLine($"    }},");

        // Per-objective + per-outcome callback tables (slice D2-3).
        // Objective Payloads[2..3] = on_activate / on_complete.
        // Outcome Payload[1]       = on_trigger (fires once when the
        // outcome first becomes satisfied; Quest.new tracks fired set).
        // Empty snippets are skipped — Quest.new dispatches defensively
        // so missing keys are clean no-ops.
        EmitQuestCallbackTable(sb, objectiveNodes, "on_activate", payloadIdx: 2);
        EmitQuestCallbackTable(sb, objectiveNodes, "on_complete", payloadIdx: 3);
        EmitQuestCallbackTable(sb, outcomeNodes,   "on_trigger",  payloadIdx: 1);

        sb.AppendLine("}");
        return sb.ToString();
    }

    // Emit one Quest callback lookup table keyed by node id.
    // Identical structure to FSM's EmitFsmCallbackTable but the
    // function signature is `function(self) ... end` — quest callbacks
    // don't take an event/dt parameter; the id is implied by the table
    // key.
    private static void EmitQuestCallbackTable(StringBuilder sb, List<PS1GraphNode> nodes,
                                                string tableName, int payloadIdx)
    {
        sb.AppendLine($"    {tableName} = {{");
        foreach (var n in nodes)
        {
            string snippet = n.GetPayload(payloadIdx);
            if (string.IsNullOrEmpty(snippet)) continue;
            string id = n.GetPayload(0);
            string keyForm = IsLuaIdentifier(id)
                ? id
                : $"[{EscapeLuaString(id)}]";
            sb.AppendLine($"        {keyForm} = function(self) {snippet} end,");
        }
        sb.AppendLine($"    }},");
    }

    // Does any incoming exec edge to nodeId come from an objective?
    // Used to decide initial-objective status. Edges from non-objective
    // sources (e.g. a future Trigger node kind) don't disqualify.
    private static bool HasIncomingObjectiveEdge(int nodeId,
                                                  Dictionary<int, PS1GraphNode> byId,
                                                  Godot.Collections.Array<PS1GraphConnection> conns)
    {
        foreach (var c in conns)
        {
            if (c.ToNodeId != nodeId) continue;
            if (c.ToPort != 0) continue;  // exec in is left-port 0
            if (!byId.TryGetValue(c.FromNodeId, out var src)) continue;
            if (src.Kind == "objective") return true;
        }
        return false;
    }

    // For a quest node (objective or outcome), collect the ids of all
    // objective nodes whose exec-out drives this node's exec-in. Used
    // for both objective prereqs and outcome trigger sets. Skips
    // non-objective upstream (logically a quest only depends on
    // objectives completing — outcomes don't gate anything).
    private static List<string> ResolveUpstreamObjectiveIds(int nodeId,
                                                              Dictionary<int, PS1GraphNode> byId,
                                                              Godot.Collections.Array<PS1GraphConnection> conns)
    {
        var ids = new List<string>();
        foreach (var c in conns)
        {
            if (c.ToNodeId != nodeId) continue;
            if (c.ToPort != 0) continue;
            if (!byId.TryGetValue(c.FromNodeId, out var src)) continue;
            if (src.Kind != "objective") continue;
            string id = src.GetPayload(0);
            if (string.IsNullOrEmpty(id)) continue;
            ids.Add(id);
        }
        return ids;
    }

    private static bool IsBtKind(string kind) => kind switch
    {
        "bt_sequence" => true,
        "bt_selector" => true,
        "bt_leaf"     => true,
        _             => false,
    };

    // Behavior Tree compile (slice BT-1). Emits a flat node table the
    // BT.new runtime helper walks recursively. Shape:
    //
    //   _G.bt_<basename> = {
    //       root = "n0",
    //       nodes = {
    //           n0 = { kind = "sequence", children = {"n1", "n2"} },
    //           n1 = { kind = "leaf", fn = function(self) return "success" end },
    //           ...
    //       },
    //   }
    //
    // Root = lowest-Id BT node with no incoming exec edge. Composites
    // (sequence / selector) collect their children by walking FindNextKey
    // for ports 1..kBtChildSlots (mirrors the dock's per-row exec-out
    // slots) and dropping nil entries — authors leave unused slots
    // unwired.
    //
    // Leaf snippets are wrapped as `function(self) return <snippet> end`
    // so the walker calls the function directly (avoids a load+pcall
    // per tick). Empty leaf compiles to `return "failure"` — a stable
    // debuggable default rather than a syntax error.
    private static string CompileBt(PS1GraphResource resource, string effectivePath)
    {
        var sb = new StringBuilder();
        string pathLabel = string.IsNullOrEmpty(effectivePath) ? "(unsaved)" : effectivePath;
        string basename = BasenameForGlobal(effectivePath);
        sb.AppendLine($"-- Compiled from {pathLabel} (bt)");
        sb.AppendLine($"-- {resource.Nodes.Count} node(s), {resource.Connections.Count} connection(s)");
        sb.AppendLine($"-- Walker: local bt = BT.new(_G.bt_{basename}); bt:Tick(self)");
        sb.AppendLine();

        var byId = new Dictionary<int, PS1GraphNode>();
        foreach (var n in resource.Nodes) byId[n.Id] = n;

        // Root selection: lowest-Id BT-kind node with no incoming exec
        // edge. Skip Disabled (pick #2) and non-BT kinds.
        var hasIncomingExec = new HashSet<int>();
        foreach (var c in resource.Connections)
        {
            if (IsExecPort(byId, c.ToNodeId, c.ToPort, isInput: true))
                hasIncomingExec.Add(c.ToNodeId);
        }
        int rootId = -1;
        foreach (var n in resource.Nodes)
        {
            if (!IsBtKind(n.Kind) || n.IsDisabled) continue;
            if (hasIncomingExec.Contains(n.Id)) continue;
            if (rootId < 0 || n.Id < rootId) rootId = n.Id;
        }

        sb.AppendLine($"_G.bt_{basename} = {{");
        sb.AppendLine($"    root = {(rootId < 0 ? "nil" : "\"n" + rootId + "\"")},");
        sb.AppendLine($"    nodes = {{");

        const int kBtChildSlots = 6;  // mirrors PS1GraphEditorDock's per-composite slot cap
        foreach (var n in resource.Nodes)
        {
            if (!IsBtKind(n.Kind) || n.IsDisabled) continue;
            if (n.IsDevelopmentOnly) sb.AppendLine($"        -- @dev-only n{n.Id}");

            switch (n.Kind)
            {
                case "bt_sequence":
                case "bt_selector":
                {
                    string kindStr = n.Kind == "bt_sequence" ? "sequence" : "selector";
                    sb.Append($"        n{n.Id} = {{ kind = \"{kindStr}\", children = {{");
                    bool wroteAny = false;
                    for (int i = 0; i < kBtChildSlots; i++)
                    {
                        // Composite child slot N → right-port N (the
                        // exec-out at SetSlot row N+1). FindNextKey
                        // chases through Disabled / reroute already.
                        string child = FindNextKey(resource.Connections, n.Id, byId: byId, fromPort: i);
                        if (child == "nil") continue;
                        if (wroteAny) sb.Append(", ");
                        sb.Append(child);
                        wroteAny = true;
                    }
                    sb.AppendLine(" } },");
                    break;
                }
                case "bt_leaf":
                {
                    // Author writes either a full statement
                    // (`return "success"` — per the handoff docs) or a
                    // bare expression (`Bot.IsHealthy(self)`). Wrapping
                    // a statement with `return (...)` produces
                    // `return (return "success")` which is Lua syntax
                    // error and the BT silently fails to load. Detect
                    // a leading `return` word and emit verbatim; only
                    // wrap when the snippet is genuinely an expression.
                    string snippet = n.GetPayload(0);
                    string body;
                    if (string.IsNullOrWhiteSpace(snippet))
                    {
                        body = "return \"failure\"";
                    }
                    else if (System.Text.RegularExpressions.Regex.IsMatch(snippet, @"^\s*return\b"))
                    {
                        body = snippet;
                    }
                    else
                    {
                        body = $"return ({snippet})";
                    }
                    sb.AppendLine($"        n{n.Id} = {{ kind = \"leaf\", fn = function(self) {body} end }},");
                    break;
                }
            }
        }

        sb.AppendLine("    },");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static bool IsLuaIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        char c0 = s[0];
        if (!(char.IsLetter(c0) || c0 == '_')) return false;
        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        }
        return true;
    }

    // Find the state-node adjacent to a transition along the given
    // connection direction, and return its Payload[0] name. Returns
    // null when there's no such connection or the endpoint isn't a
    // state with a name. `isFromSide`: true picks `c.FromNodeId`
    // (looking for the upstream side); false picks `c.ToNodeId`.
    private static string? ResolveAdjacentStateName(Dictionary<int, PS1GraphNode> byId,
                                                     Godot.Collections.Array<PS1GraphConnection> conns,
                                                     System.Func<PS1GraphConnection, bool> predicate,
                                                     bool isFromSide)
    {
        foreach (var c in conns)
        {
            if (!predicate(c)) continue;
            int otherId = isFromSide ? c.FromNodeId : c.ToNodeId;
            if (!byId.TryGetValue(otherId, out var other)) continue;
            if (other.Kind != "state") continue;
            string name = other.GetPayload(0);
            if (string.IsNullOrEmpty(name)) continue;
            return name;
        }
        return null;
    }

    // Parse the Line node's "notifies" pipe-string (Payload[4]) into
    // a `, notifies = { {at=12, lua="..."}, ... }` Lua table fragment.
    // Format: "<frame>:<lua> | <frame>:<lua> | …" — whitespace around
    // tokens trimmed; entries missing a colon dropped with a `--`
    // comment so the author sees them in the compiled .lua. Returns
    // empty string when no notifies authored, so the line table stays
    // compact.
    private static string CompileLineNotifies(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var entries = raw!.Split('|');
        var sb = new StringBuilder();
        sb.Append(", notifies = { ");
        int written = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            string e = entries[i].Trim();
            if (e.Length == 0) continue;
            int colon = e.IndexOf(':');
            if (colon < 0)
            {
                sb.Append($"--[[malformed notify '{e}' (no colon)]] ");
                continue;
            }
            string atRaw = e.Substring(0, colon).Trim();
            string lua   = e.Substring(colon + 1).Trim();
            if (!int.TryParse(atRaw, out int at) || at < 0)
            {
                sb.Append($"--[[notify frame not int: '{atRaw}']] ");
                continue;
            }
            if (lua.Length == 0)
            {
                sb.Append($"--[[notify frame {at} missing lua]] ");
                continue;
            }
            if (written > 0) sb.Append(", ");
            sb.Append($"{{at = {at}, lua = {EscapeLuaString(lua)}}}");
            written++;
        }
        sb.Append(" }");
        return sb.ToString();
    }

    // Compile the Line node's reveal-mode + rate (Payload[5]/[6]) into
    // a `, reveal_mode = "typewriter", reveal_rate = 30` fragment. Empty
    // / "none" mode emits nothing so existing graphs stay byte-identical
    // and the runtime walker's missing-key default ("none") takes over.
    // Rate parses as an int (chars per second); anything unparseable
    // falls back to 30 and notes the issue inline.
    private static string CompileLineReveal(string? mode, string? rate)
    {
        string m = (mode ?? "").Trim().ToLowerInvariant();
        if (m.Length == 0 || m == "none") return "";
        // Only the modes the C++ walker actually implements are emitted
        // verbatim; anything else compiles with a comment so the author
        // can spot the typo in the .lua diff.
        bool known = m == "typewriter";
        string modeOut = known
            ? EscapeLuaString(m)
            : $"{EscapeLuaString(m)} --[[unknown reveal mode; walker will fall back]]";
        int rateInt = 30;
        string rateNote = "";
        if (!string.IsNullOrWhiteSpace(rate))
        {
            if (!int.TryParse(rate, out rateInt) || rateInt <= 0)
            {
                rateInt = 30;
                rateNote = $" --[[rate '{rate}' not a positive int — using 30]]";
            }
        }
        return $", reveal_mode = {modeOut}, reveal_rate = {rateInt}{rateNote}";
    }

    private static void EmitDialogueNode(StringBuilder sb, Dictionary<int, PS1GraphNode> byId,
                                          Godot.Collections.Array<PS1GraphConnection> conns,
                                          PS1GraphNode n)
    {
        switch (n.Kind)
        {
            case "line":
            {
                // Line: { kind="line", speaker=..., text=..., audio=...,
                //         skippable=..., notifies={...},
                //         reveal_mode=..., reveal_rate=..., next=... }
                //
                // Payloads:
                //   [0] text          [1] speaker
                //   [2] audio clip    [3] "true"/"false" (skippable)
                //   [4] notifies      — pipe-string "frame:lua | frame:lua"
                //   [5] reveal mode   — "none" / "typewriter"
                //   [6] reveal rate   — chars per second (default 30)
                //
                // Empty audio / notifies / reveal fields → omitted key
                // (walker treats missing as none). Skippable defaults
                // to true so pre-D1h graphs keep the legacy advance
                // behaviour. Reveal mode defaults to "none" so the
                // typewriter is opt-in per line.
                string text    = EscapeLuaString(n.GetPayload(0));
                string speaker = EscapeLuaString(n.GetPayload(1));
                string audio   = n.GetPayload(2) ?? "";
                string audioField = string.IsNullOrEmpty(audio)
                    ? ""
                    : $", audio = {EscapeLuaString(audio)}";
                bool skippable = !string.Equals(n.GetPayload(3), "false",
                                                 System.StringComparison.OrdinalIgnoreCase);
                string notifiesField = CompileLineNotifies(n.GetPayload(4));
                string revealField = CompileLineReveal(n.GetPayload(5), n.GetPayload(6));
                string next    = FindNextKey(conns, n.Id, byId: byId, fromPort: 0);
                sb.AppendLine($"        n{n.Id} = {{ kind = \"line\", speaker = {speaker}, text = {text}{audioField}, skippable = {(skippable ? "true" : "false")}{notifiesField}{revealField}, next = {next} }},");
                break;
            }
            case "set_flag":
            {
                // Compiles to a generic action node: a Lua snippet the
                // walker runs on entry, then follows .next. Flag name +
                // value are baked into the snippet at compile time.
                string flag  = EscapeLuaString(n.GetPayload(0));
                string value = string.Equals(n.GetPayload(1), "true",
                                              System.StringComparison.OrdinalIgnoreCase)
                                ? "true" : "false";
                string lua   = $"Persist.Set({flag}, {value})";
                string next  = FindNextKey(conns, n.Id, byId: byId, fromPort: 0);
                sb.AppendLine($"        n{n.Id} = {{ kind = \"action\", lua = {EscapeLuaString(lua)}, next = {next} }},");
                break;
            }
            case "play_sound":
            {
                string clip = EscapeLuaString(n.GetPayload(0));
                string lua  = $"Audio.PlaySfx({clip})";
                string next = FindNextKey(conns, n.Id, byId: byId, fromPort: 0);
                sb.AppendLine($"        n{n.Id} = {{ kind = \"action\", lua = {EscapeLuaString(lua)}, next = {next} }},");
                break;
            }
            case "start_cutscene":
            {
                string id   = EscapeLuaString(n.GetPayload(0));
                string lua  = $"Cutscene.Play({id})";
                string next = FindNextKey(conns, n.Id, byId: byId, fromPort: 0);
                sb.AppendLine($"        n{n.Id} = {{ kind = \"action\", lua = {EscapeLuaString(lua)}, next = {next} }},");
                break;
            }
            case "sub_dialogue":
            {
                // Slice D1j — `{ kind="sub_dialogue", target="<basename>",
                // next="<resume_id>" }`. Runtime walker pushes the
                // current (table, resume) onto its stack, swaps to
                // `_G.dialogue_<target>`, walks until it hits a
                // nil-next then pops back to `next`.
                //
                // BasenameForGlobal normalises the target so authors
                // can type the .tres basename directly (e.g.
                // "shopkeeper-greeting" → "shopkeeper_greeting").
                string target = BasenameForGlobal(n.GetPayload(0));
                string next   = FindNextKey(conns, n.Id, byId: byId, fromPort: 0);
                sb.AppendLine($"        n{n.Id} = {{ kind = \"sub_dialogue\", target = {EscapeLuaString(target)}, next = {next} }},");
                break;
            }
            case "lua_snippet":
            {
                // Power-user action — author-supplied snippet, baked
                // verbatim into the runtime "action" kind. Walker
                // pcalls each snippet so syntax errors print + advance
                // doesn't fire rather than crash the scene.
                string snippet = n.GetPayload(0);
                if (string.IsNullOrEmpty(snippet))
                {
                    // Empty snippet is a no-op action — still emit so the
                    // exec edge resolves. Walker handles empty body fine.
                    snippet = "";
                }
                string next = FindNextKey(conns, n.Id, byId: byId, fromPort: 0);
                sb.AppendLine($"        n{n.Id} = {{ kind = \"action\", lua = {EscapeLuaString(snippet)}, next = {next} }},");
                break;
            }
            case "lua_condition":
            {
                // Power-user condition — author-supplied expression,
                // wrapped in `return (<expr>)` so the walker's pcall
                // gets a boolean stack slot regardless of expression
                // form. Empty expression compiles to `return false` so
                // the false branch always fires (stable, debuggable
                // behaviour rather than a Lua syntax error).
                string expr = n.GetPayload(0);
                string lua  = string.IsNullOrEmpty(expr)
                    ? "return false"
                    : $"return ({expr})";
                string nextTrue  = FindNextKey(conns, n.Id, byId: byId, fromPort: 0);
                string nextFalse = FindNextKey(conns, n.Id, byId: byId, fromPort: 1);
                sb.AppendLine($"        n{n.Id} = {{ kind = \"condition\", lua = {EscapeLuaString(lua)}, next_true = {nextTrue}, next_false = {nextFalse} }},");
                break;
            }
            case "condition":
            {
                // Condition: { kind="condition", lua="return ...", next_true=..., next_false=... }
                // Walker runs the Lua snippet, branches on the boolean
                // result. Slice D1d's structured form reads a single
                // flag; future polish slice could open this up to
                // arbitrary expressions via a dedicated "Lua Condition"
                // node kind.
                string flag      = EscapeLuaString(n.GetPayload(0));
                string lua       = $"return Persist.Get({flag}) == true";
                string nextTrue  = FindNextKey(conns, n.Id, byId: byId, fromPort: 0);
                string nextFalse = FindNextKey(conns, n.Id, byId: byId, fromPort: 1);
                sb.AppendLine($"        n{n.Id} = {{ kind = \"condition\", lua = {EscapeLuaString(lua)}, next_true = {nextTrue}, next_false = {nextFalse} }},");
                break;
            }
            case "choice":
            {
                // Choice: { kind="choice", options = { { text=..., next=... }, … } }
                // Each option = one row. Slice D1a fixes 3 option
                // slots; empty option texts get pruned at emit so the
                // runtime doesn't show blank choices.
                //
                // Connection port indices are per-side. The choice node
                // has its exec-in on the left (one left pin) and three
                // exec-outs on the right — Godot indexes right pins
                // 0..2 in slot order, so option N's outgoing edge is
                // FromPort=N, NOT FromPort=N+1. Getting this wrong
                // ships off-by-one branches that picked "A" but ran B.
                sb.AppendLine($"        n{n.Id} = {{ kind = \"choice\", options = {{");
                for (int opt = 0; opt < 3; opt++)
                {
                    string optText = n.GetPayload(opt);
                    if (string.IsNullOrEmpty(optText)) continue;
                    string text = EscapeLuaString(optText);
                    string next = FindNextKey(conns, n.Id, byId: byId, fromPort: opt);
                    sb.AppendLine($"            {{ text = {text}, next = {next} }},");
                }
                sb.AppendLine("        } },");
                break;
            }
        }
    }

    // Lua key for the node connected to (srcId, fromPort), or `nil`
    // when the exec out is dangling. Returns a quoted string like "n3"
    // so callers can drop it straight into Lua source.
    //
    // If `byId` is supplied, chases through `Disabled` linear-flow
    // nodes — line / set_flag / play_sound / start_cutscene /
    // lua_snippet / sub_dialogue — so the surrounding graph's exec
    // edges skip past disabled segments. Bails out at nil or at a
    // non-linear kind (choice / condition / branch) since multi-out
    // chasing is ambiguous. Cycle guard: chase budget of 32 hops
    // (any genuine graph hits non-disabled or nil long before that).
    // Without byId, retains the slice-1 single-hop behaviour.
    private static string FindNextKey(Godot.Collections.Array<PS1GraphConnection> conns,
                                       int srcId, int fromPort,
                                       Dictionary<int, PS1GraphNode>? byId = null)
    {
        int target = -1;
        foreach (var c in conns)
        {
            if (c.FromNodeId == srcId && c.FromPort == fromPort)
            {
                target = c.ToNodeId;
                break;
            }
        }
        if (target < 0) return "nil";

        if (byId != null)
        {
            int hops = 0;
            // Chase through targets that should be invisible to the
            // runtime: Disabled linear nodes (UE pick #2) AND every
            // reroute knot (UE pick #3). Both compile to nothing;
            // exec edges chase past them transparently.
            while (hops++ < 32 &&
                   byId.TryGetValue(target, out var tn) &&
                   HasSingleExecOut(tn.Kind) &&
                   (tn.IsDisabled || tn.Kind == "reroute"))
            {
                int nextTarget = -1;
                foreach (var c2 in conns)
                {
                    if (c2.FromNodeId == target && c2.FromPort == 0)
                    {
                        nextTarget = c2.ToNodeId;
                        break;
                    }
                }
                if (nextTarget < 0) return "nil";
                target = nextTarget;
            }
        }

        return $"\"n{target}\"";
    }

    // Whether a kind has exactly one exec-out at port 0 — eligible
    // for chase-through when Disabled or always-transparent (reroute).
    private static bool HasSingleExecOut(string kind) => kind switch
    {
        "line"           => true,
        "set_flag"       => true,
        "play_sound"     => true,
        "start_cutscene" => true,
        "lua_snippet"    => true,
        "sub_dialogue"   => true,
        "print"          => true,
        "reroute"        => true,
        _                => false,
    };

    // Derive a Lua-safe identifier from the resource's .tres path.
    // Strips directory + extension, lowercases, replaces anything
    // non-alphanumeric with underscore. Unsaved graphs get "unnamed".
    private static string BasenameForGlobal(string path)
    {
        if (string.IsNullOrEmpty(path)) return "unnamed";
        string raw = System.IO.Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(raw)) return "unnamed";
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw.ToLowerInvariant())
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }
        string result = sb.ToString();
        // Lua identifiers can't start with a digit; prefix if needed.
        if (result.Length > 0 && char.IsDigit(result[0])) result = "_" + result;
        return string.IsNullOrEmpty(result) ? "unnamed" : result;
    }

    // ── Emission ────────────────────────────────────────────────────

    private static void EmitNode(StringBuilder sb, Dictionary<int, PS1GraphNode> byId,
                                  Godot.Collections.Array<PS1GraphConnection> conns, int nodeId, int indent)
    {
        if (!byId.TryGetValue(nodeId, out var n)) return;
        string pad = new string(' ', indent * 4);
        switch (n.Kind)
        {
            case "print":
            {
                // Print: row 0 = Exec in+out (slot 0), row 1 = String
                // in+out (slot 1). Body emits print(<resolved string>).
                string msg = ResolveStringInput(byId, conns, nodeId, slot: 1);
                sb.AppendLine($"{pad}print({msg})");
                EmitExecContinuation(sb, byId, conns, nodeId, slot: 0, indent);
                break;
            }
            case "branch":
            {
                // Branch: row 0 = Exec in + Exec out (true), row 1 =
                // right-only Exec out (false), row 2 = Bool in (left
                // only). Body emits if/then/else.
                string cond = ResolveBoolInput(byId, conns, nodeId, slot: 2);
                sb.AppendLine($"{pad}if {cond} then");
                EmitExecContinuation(sb, byId, conns, nodeId, slot: 0, indent + 1);
                sb.AppendLine($"{pad}else");
                EmitExecContinuation(sb, byId, conns, nodeId, slot: 1, indent + 1);
                sb.AppendLine($"{pad}end");
                break;
            }
            case "comment":
                // Pinless decoration — emit nothing.
                break;
            default:
                sb.AppendLine($"{pad}-- (unknown kind '{n.Kind}')");
                break;
        }
    }

    // Follow the single exec edge originating at (nodeId, slot). Each
    // exec output drives at most one continuation; GraphEdit's pin
    // model permits multiple but slice-4a semantics treat extras as
    // ambiguous (we just take the first match in connection order —
    // future validation slice will warn about multi-outbound exec).
    private static void EmitExecContinuation(StringBuilder sb, Dictionary<int, PS1GraphNode> byId,
                                              Godot.Collections.Array<PS1GraphConnection> conns,
                                              int nodeId, int slot, int indent)
    {
        foreach (var c in conns)
        {
            if (c.FromNodeId == nodeId && c.FromPort == slot)
            {
                EmitNode(sb, byId, conns, c.ToNodeId, indent);
                return;
            }
        }
        // Dangling exec edge — emit nothing. Slice-5 dangling-input
        // warnings would flag this at authoring time.
    }

    // ── Data-pin resolution ─────────────────────────────────────────

    // String input: walk back through any incoming connection, falling
    // back to the consuming node's own Payload literal when the input
    // is unconnected. Tail-recursive through chained String pass-
    // throughs (Print A → Print B → Print C).
    private static string ResolveStringInput(Dictionary<int, PS1GraphNode> byId,
                                              Godot.Collections.Array<PS1GraphConnection> conns,
                                              int nodeId, int slot)
    {
        foreach (var c in conns)
        {
            if (c.ToNodeId == nodeId && c.ToPort == slot)
            {
                return ProduceString(byId, conns, c.FromNodeId, c.FromPort);
            }
        }
        if (byId.TryGetValue(nodeId, out var n))
        {
            return EscapeLuaString(n.Payload);
        }
        return "\"\"";
    }

    // Produce a string value FROM a source node's output port. Each
    // kind that has a String output declares how to compute it here.
    private static string ProduceString(Dictionary<int, PS1GraphNode> byId,
                                         Godot.Collections.Array<PS1GraphConnection> conns,
                                         int srcId, int srcPort)
    {
        if (!byId.TryGetValue(srcId, out var n)) return "\"\"";
        switch (n.Kind)
        {
            case "print":
                // Print's String out (slot 1 right) passes through its
                // resolved String in — same value the node will itself
                // print, available for downstream Prints to forward.
                if (srcPort == 1) return ResolveStringInput(byId, conns, srcId, slot: 1);
                break;
        }
        return "\"\"";
    }

    // Bool input: walk back through any incoming connection (handled
    // by ProduceBool — Bool Literal kind delivers a literal value).
    // When no connection drives this pin, fall back to the consuming
    // node's own Payload literal. For Branch (the only Bool-consumer
    // in slice 4b), the row-3 "default condition" CheckBox stores
    // "true"/"false". Empty/missing payload → "true" (matches the
    // DefaultPayloadFor seed used when new Branches are spawned).
    private static string ResolveBoolInput(Dictionary<int, PS1GraphNode> byId,
                                            Godot.Collections.Array<PS1GraphConnection> conns,
                                            int nodeId, int slot)
    {
        foreach (var c in conns)
        {
            if (c.ToNodeId == nodeId && c.ToPort == slot)
            {
                return ProduceBool(byId, conns, c.FromNodeId, c.FromPort);
            }
        }
        if (byId.TryGetValue(nodeId, out var n))
        {
            if (string.IsNullOrEmpty(n.Payload)) return "true";
            return string.Equals(n.Payload, "true", System.StringComparison.OrdinalIgnoreCase)
                ? "true" : "false";
        }
        return "false";
    }

    // Produce a bool value FROM a source node's output port. Mirrors
    // ProduceString. Each Bool-producing kind declares its output
    // semantics here.
    private static string ProduceBool(Dictionary<int, PS1GraphNode> byId,
                                       Godot.Collections.Array<PS1GraphConnection> conns,
                                       int srcId, int srcPort)
    {
        if (!byId.TryGetValue(srcId, out var n)) return "false";
        switch (n.Kind)
        {
            case "bool_literal":
                // Slot 0 right = Bool out — emit the Payload literal.
                if (srcPort == 0)
                {
                    if (string.IsNullOrEmpty(n.Payload)) return "true";
                    return string.Equals(n.Payload, "true", System.StringComparison.OrdinalIgnoreCase)
                        ? "true" : "false";
                }
                break;
        }
        return "false";
    }

    // ── Per-kind slot metadata ──────────────────────────────────────

    private static bool HasExecInput(string kind) => kind switch
    {
        "print"          => true,
        "branch"         => true,
        "line"           => true,
        "choice"         => true,
        "set_flag"       => true,
        "condition"      => true,
        "play_sound"     => true,
        "start_cutscene" => true,
        "lua_snippet"    => true,
        "lua_condition"  => true,
        "sub_dialogue"   => true,
        "state"          => true,
        "transition"     => true,
        "objective"      => true,
        "outcome"        => true,
        "reroute"        => true,
        "bt_sequence"    => true,
        "bt_selector"    => true,
        "bt_leaf"        => true,
        "comment"        => false,
        _                => false,
    };

    // Is the given port on a node an Exec pin? Used to build the
    // "incoming exec" set and (future) relax cycle rejection to
    // exec-only. Slot indices match BuildVisualBody exactly.
    private static bool IsExecPort(Dictionary<int, PS1GraphNode> byId, int nodeId, int port, bool isInput)
    {
        if (!byId.TryGetValue(nodeId, out var n)) return false;
        return n.Kind switch
        {
            "print"          => port == 0,                            // row 0 in+out
            "branch"         => port == 0 || (!isInput && port == 1), // row 0 in+out, row 1 out-only
            "line"           => port == 0,                            // row 0 in+out
            // choice: row 0 = exec in (left-port 0),
            //         rows 1..3 = exec outs (right-ports 0..2).
            "choice"         => (isInput && port == 0) || (!isInput && port >= 0 && port <= 2),
            "set_flag"       => port == 0,                            // row 0 in+out
            "condition"      => port == 0 || (!isInput && port == 1), // row 0 in+out (true), row 1 out (false)
            "play_sound"     => port == 0,
            "start_cutscene" => port == 0,
            // lua_snippet mirrors set_flag/play_sound — exec in+out at row 0.
            "lua_snippet"    => port == 0,
            // lua_condition mirrors condition — row 0 in+out (true),
            // row 1 out-only (false).
            "lua_condition"  => port == 0 || (!isInput && port == 1),
            // sub_dialogue mirrors line — exec in+out at row 0.
            "sub_dialogue"   => port == 0,
            // state, transition: row 0 in+out (exec in left-port 0,
            // exec out right-port 0). State's exec out can drive many
            // transitions; transition's exec out drives the next state.
            "state"          => port == 0,
            "transition"     => port == 0,
            // objective: row 0 in+out (gates downstream objectives /
            // outcomes). outcome: row 0 in only (terminal).
            "objective"      => port == 0,
            "outcome"        => isInput && port == 0,
            // reroute: row 0 in+out — pinless body but exec slot at 0.
            "reroute"        => port == 0,
            // BT composites: row 0 = exec in (left-port 0); rows 1..6
            // = exec outs (right-ports 0..5).
            "bt_sequence"    => (isInput && port == 0) || (!isInput && port >= 0 && port <= 5),
            "bt_selector"    => (isInput && port == 0) || (!isInput && port >= 0 && port <= 5),
            // BT leaf: row 0 in only — leaves are terminal in BT.
            "bt_leaf"        => isInput && port == 0,
            _                => false,
        };
    }

    // ── Robust .tres → PS1GraphResource loader ──────────────────────
    //
    // Godot 4.7-dev5 sometimes returns custom-script Resources from
    // `ResourceLoader.Load` without attaching the C# wrapper class —
    // the runtime type comes back as plain `Godot.Resource` and the
    // generic `Load<T>` wrapper throws `InvalidCastException` at its
    // internal `(T)resource` cast. RobustLoad does the try-fallback-
    // reconstruct dance so the dock + auto-recompile + any other site
    // that needs to load a PS1GraphResource always gets a wired-up
    // typed object (even when Godot's binding fails).
    //
    // Returns null only when the file doesn't exist or can't be
    // interpreted as a graph at all.
    public static PS1GraphResource? RobustLoad(string tresPath)
    {
        if (string.IsNullOrEmpty(tresPath)) return null;
        if (!Godot.FileAccess.FileExists(tresPath)) return null;

        PS1GraphResource? loaded = null;
        try
        {
            loaded = Godot.ResourceLoader.Load<PS1GraphResource>(tresPath);
        }
        catch (System.InvalidCastException)
        {
            // Binding-quirk; fall through.
        }
        if (loaded != null) return loaded;

        var raw = Godot.ResourceLoader.Load(tresPath);
        if (raw == null) return null;
        return ReconstructGraphFromBareResource(raw);
    }

    // Rebuild a typed PS1GraphResource from a bare Godot.Resource when
    // Godot 4.7-dev5 fails to attach the C# script binding on load.
    // Reads exported properties via Resource.Get; tolerates Nodes /
    // Connections subresources that also lost their bindings by doing
    // the same property-copy on each entry.
    public static PS1GraphResource? ReconstructGraphFromBareResource(Godot.Resource raw)
    {
        if (raw == null) return null;
        var graph = new PS1GraphResource
        {
            Kind = raw.Get("Kind").AsString() ?? "",
            NextNodeId = raw.Get("NextNodeId").AsInt32(),
        };

        var rawNodes = raw.Get("Nodes").AsGodotArray();
        if (rawNodes != null)
        {
            foreach (var item in rawNodes)
            {
                if (item.AsGodotObject() is not Godot.Resource rn) continue;
                if (rn is PS1GraphNode boundNode)
                {
                    graph.Nodes.Add(boundNode);
                    continue;
                }
                // Sub-resource also lost its binding — rebuild manually.
                // Every [Export] property on PS1GraphNode must be copied
                // here, including EnabledState (UE port-plan pick #2) —
                // skipping it silently reverts Disabled/DevelopmentOnly
                // to Enabled on every reload, and downstream compile +
                // runtime behave as if the author never set it.
                var node = new PS1GraphNode
                {
                    Id = rn.Get("Id").AsInt32(),
                    Kind = rn.Get("Kind").AsString() ?? "",
                    Position = rn.Get("Position").AsVector2(),
                    Payload = rn.Get("Payload").AsString() ?? "",
                    EnabledState = (PS1GraphNode.NodeEnabledState)rn.Get("EnabledState").AsInt32(),
                };
                var rawPayloads = rn.Get("Payloads").AsGodotArray<string>();
                if (rawPayloads != null)
                {
                    foreach (var s in rawPayloads) node.Payloads.Add(s ?? "");
                }
                graph.Nodes.Add(node);
            }
        }

        var rawConns = raw.Get("Connections").AsGodotArray();
        if (rawConns != null)
        {
            foreach (var item in rawConns)
            {
                if (item.AsGodotObject() is not Godot.Resource rc) continue;
                if (rc is PS1GraphConnection boundConn)
                {
                    graph.Connections.Add(boundConn);
                    continue;
                }
                graph.Connections.Add(new PS1GraphConnection
                {
                    FromNodeId = rc.Get("FromNodeId").AsInt32(),
                    FromPort   = rc.Get("FromPort").AsInt32(),
                    ToNodeId   = rc.Get("ToNodeId").AsInt32(),
                    ToPort     = rc.Get("ToPort").AsInt32(),
                });
            }
        }

        return graph;
    }

    // ── Escaping ────────────────────────────────────────────────────

    private static string EscapeLuaString(string s)
    {
        if (s == null) return "\"\"";
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:   sb.Append(c); break;
            }
        }
        sb.Append("\"");
        return sb.ToString();
    }
}
