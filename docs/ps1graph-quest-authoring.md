# PS1Graph Quest Authoring

How to author a quest in the PS1Graph dock. Slice D2-1 ships the
compiler + node palette; the `Quest.new` runtime helper that walks
the compiled table lands in slice D2-2. Until then you drive the
table manually (10-line walker recipe below).

## TL;DR

1. **Author the graph.** PS1 Graph dock → `Kind: Quest` → *New* →
   right-click → drop **Objective** and **Outcome** nodes.
2. **Wire prereqs.** Objective A exec-out → Objective B exec-in
   means "complete A before B unlocks." Multiple incoming edges
   into one objective = AND (all must complete). Express OR by
   wiring separate chains to the same downstream objective via
   independent intermediate objectives.
3. **Wire outcomes.** Objective(s) exec-out → Outcome exec-in.
   When all of an outcome's incoming objectives are complete, the
   outcome fires.
4. **Name everything.** Objective Payload[0] = id (Lua key + Persist
   key), Payload[1] = display title. Outcome Payload[0] = outcome id
   (the value `Quest.new` returns from `:Outcome()`).
5. **Save it.** Sibling `<basename>.lua` is auto-written with
   `_G.quest_<basename>`.
6. **Ship the .lua.** Drop into `PS1Scene.UserScripts` and F5 — runs
   at scene init, table sits in `_G` waiting.

## What it compiles to

```lua
_G.quest_save_the_village = {
    initial_objectives = { "find_npc" },
    objectives = {
        find_npc    = { id = "find_npc",    title = "Find the elder",      prereqs = {} },
        talk_to_npc = { id = "talk_to_npc", title = "Speak with the elder", prereqs = { "find_npc" } },
        defeat_orc  = { id = "defeat_orc",  title = "Defeat the orc",      prereqs = { "talk_to_npc" } },
    },
    outcomes = {
        { id = "victory", prereqs = { "defeat_orc" } },
    },
}
```

**Initial objectives** are those with no incoming objective edge.
They're active when the quest starts.

**Prereqs** are the ids of all upstream objective nodes wired into
this node's exec-in. AND-merged at runtime — every prereq must be
complete before the node activates / fires.

**Outcomes** are terminal. A quest with no outcomes is a checklist
(player completes objectives but the quest never "ends").

## Driving it from Lua (until Quest.new ships)

10-line manual walker — sufficient for slice D2-1 testing:

```lua
local q = _G.quest_save_the_village
local completed = {}
local active    = {}
for _, id in ipairs(q.initial_objectives) do active[id] = true end

local function complete(id)
    completed[id] = true
    active[id]    = nil
    for objId, obj in pairs(q.objectives) do
        if not completed[objId] and not active[objId] then
            local ok = true
            for _, p in ipairs(obj.prereqs) do
                if not completed[p] then ok = false; break end
            end
            if ok then active[objId] = true end
        end
    end
end

local function outcome()
    for _, o in ipairs(q.outcomes) do
        local ok = true
        for _, p in ipairs(o.prereqs) do
            if not completed[p] then ok = false; break end
        end
        if ok then return o.id end
    end
    return nil
end
```

Slice D2-2 will ship `Quest.new(table)` returning an instance with
`:Activate()`, `:Complete(id)`, `:IsActive(id)`, `:Outcome()`, and
`:Save() / :Load()` for persistence via `Persist`.

## Node-kind reference

| Editor node | Pins | Payloads | Compiles to |
|---|---|---|---|
| **Objective** | Exec in + Exec out | [0] id, [1] display title | entry in `objectives[id]` with `{ id, title, prereqs }` |
| **Outcome** | Exec in only | [0] id | entry in `outcomes` array with `{ id, prereqs }` |

## Limits to know about

- **No Quest.new runtime helper yet** (D2-2).
- **No save/load tie-in yet** — D2-2 will fold completed-objective
  state into the `Persist` system.
- **Outcomes don't run code.** They're just terminal id markers; the
  author dispatches on `:Outcome()` from their own Lua. A per-outcome
  Lua snippet field is a D2-3 candidate.
- **Cycle guard is ON for quests** — a back-edge would mean
  "completing A depends on B which depends on A," which is always a
  bug. Use multiple objectives for genuinely parallel branches.
- **OR-of-prereqs needs structural workaround** — add an intermediate
  objective that AND-merges nothing (no prereqs of its own) and is
  triggered explicitly by your game code from either branch.

## Where the code lives

| Concern | File |
|---|---|
| Dock palette + visual body | `godot-ps1/addons/ps1godot/ui/PS1GraphEditorDock.cs` (`objective` / `outcome` cases) |
| Compiler (`CompileQuest`) | `godot-ps1/addons/ps1godot/graph/PS1GraphCompiler.cs` |
| Companions | `docs/ps1graph-dialogue-authoring.md`, `docs/ps1graph-fsm-authoring.md` |
