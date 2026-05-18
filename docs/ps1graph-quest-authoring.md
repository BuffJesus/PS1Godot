# PS1Graph Quest Authoring

How to author a quest in the PS1Graph dock. Slice D2-1 shipped the
compiler + node palette; slice D2-2 added the `Quest.new` runtime
helper with full prereq resolution + save/load; slice D2-3 wires
per-objective and per-outcome Lua callbacks (`on_activate` /
`on_complete` / `on_trigger`) so quest progress can drive game-side
behaviour without polling.

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

## Driving it from Lua

`Quest.new` is built into the runtime as of slice D2-2 — it lives in
`_G.Quest` before any scene script runs.

```lua
-- Construct an instance from the compiled table. Auto-activates
-- the initial objectives on construction.
local q = Quest.new(_G.quest_save_the_village)

q:IsActive("find_npc")     --> true
q:IsActive("talk_to_npc")  --> false (prereq not met yet)

-- Player completes "find_npc". Returns the list of newly-unlocked
-- objective ids so you can pop a "New objective: ..." HUD.
local unlocked = q:Complete("find_npc")
-- unlocked == { "talk_to_npc" }

q:IsActive("talk_to_npc")  --> true

-- Check for an outcome — returns the first outcome whose prereqs
-- are all complete, or nil.
q:Outcome()                --> nil (defeat_orc not done)
q:Complete("talk_to_npc")
q:Complete("defeat_orc")
q:Outcome()                --> "victory"
```

### Save / load via Persist

```lua
-- Snapshot the completed set for persistence.
local snap = q:Save()                -- { completed = {"find_npc", ...} }
Persist.SetTable("quest_village", snap)

-- ... later, on game load:
local restored = Quest.new(_G.quest_save_the_village)
restored:Load(Persist.GetTable("quest_village"))
-- Active set recomputes deterministically from completed.
```

### Multiple instances

`Quest.new` produces independent instances — useful when one quest
template backs multiple repeatable variants (escort missions,
delivery contracts).

### Full API surface

| Method | Returns | Notes |
|---|---|---|
| `:Activate()` | array of newly-unlocked ids | Idempotent; called once on construction |
| `:Complete(id)` | array of newly-unlocked ids | No-op if id is already complete or not in objectives |
| `:IsActive(id)` | bool | |
| `:IsComplete(id)` | bool | |
| `:ActiveSet()` | array of ids | Order unspecified |
| `:Outcome()` | string or nil | First satisfied outcome |
| `:Save()` | snapshot table | `{ completed = {ids} }` |
| `:Load(snap)` | array of newly-unlocked ids | Replaces internal state, recomputes active |

## Node-kind reference

| Editor node | Pins | Payloads | Compiles to |
|---|---|---|---|
| **Objective** | Exec in + Exec out | [0] id, [1] display title, [2] on_activate, [3] on_complete | entry in `objectives[id]` + `on_activate[id]` / `on_complete[id]` for non-empty snippets |
| **Outcome** | Exec in only | [0] id, [1] on_trigger | entry in `outcomes` array + `on_trigger[id]` for non-empty snippet |

### Per-objective / per-outcome Lua snippets

Each Objective node has two optional snippet fields below the title:

- **on_activate** — runs when the objective becomes active (initial
  state OR a prereq just completed unlocking it). Signature:
  `function(self)` where `self` is the Quest instance.
- **on_complete** — runs when the author calls `q:Complete(id)`.
  Signature: `function(self)`.

Outcome nodes have one snippet field below the id:

- **on_trigger** — runs **once** when the outcome's prereqs first
  become satisfied. `Quest.new` tracks a fired-outcome set so the
  callback doesn't double-fire on subsequent `:Complete()` calls or
  across `:Load()` cycles.

All snippets are single-line Lua. Chain statements with `;`.

Example:

```
Objective "find_npc":
  on_activate: HUD.PopBanner("Find the village elder")
  on_complete: Audio.PlaySfx("ding")

Outcome "victory":
  on_trigger:  Persist.Set("chapter1_done", true); Cutscene.Play("villagers_cheer")
```

`self` is the Quest instance — set fields like `self._gold_earned`
to hold per-quest scratch state.

## Limits to know about

- **Snippets are single-line.** Chain with `;` for multiple
  statements. Multi-line text-area authoring is a later polish slice.
- **Cycle guard is ON for quests** — a back-edge would mean
  "completing A depends on B which depends on A," always a logic
  bug. Use multiple objectives for genuinely parallel branches.
- **OR-of-prereqs needs structural workaround** — add an intermediate
  objective with no prereqs of its own and trigger it explicitly from
  your game code from either branch.
- **Save snapshots are `{ completed, fired_outcomes }`.** Active set
  is always recomputed from completed + initial; never persisted
  directly. `fired_outcomes` is tracked so `on_trigger` callbacks
  don't double-fire after a save/load.

## Where the code lives

| Concern | File |
|---|---|
| Dock palette + visual body | `godot-ps1/addons/ps1godot/ui/PS1GraphEditorDock.cs` (`objective` / `outcome` cases) |
| Compiler (`CompileQuest`) | `godot-ps1/addons/ps1godot/graph/PS1GraphCompiler.cs` |
| Companions | `docs/ps1graph-dialogue-authoring.md`, `docs/ps1graph-fsm-authoring.md` |
