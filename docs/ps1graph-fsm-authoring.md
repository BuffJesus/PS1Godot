# PS1Graph FSM Authoring

How to author a finite-state machine in the PS1Graph dock and consume
it from Lua. Slice D3-1 ships the compiler + node palette. The
runtime `FSM.new` helper that walks the compiled table lands in
slice D3-2; until then you drive the table manually.

## TL;DR — the five steps

1. **Author the graph.** PS1 Graph dock → set `Kind: FSM (state machine)`
   → *New* → right-click → drop **State** and **Transition** nodes.
2. **Wire it up.**
   - One **Transition** = one event-driven edge between two states.
   - State exec-out → Transition exec-in (source state of this event).
   - Transition exec-out → State exec-in (destination state).
3. **Name everything.** State Payload = state name (e.g. `patrol`,
   `chase`). Transition Payload = event name (e.g. `see_player`).
   Empty names get skipped at compile.
4. **Save it.** A sibling `<basename>.lua` is written next to the
   `.tres`, containing `_G.fsm_<basename> = { initial=…, states=…, transitions=… }`.
5. **Ship the .lua.** Drop the path into `PS1Scene.UserScripts`. F5
   runs the chunk at scene init, installing the table on `_G`.

## What it compiles to

```lua
_G.fsm_my_bot = {
    initial = "patrol",
    states = { "patrol", "chase", "attack" },
    transitions = {
        { from = "patrol", event = "see_player", to = "chase" },
        { from = "chase",  event = "in_range",   to = "attack" },
        { from = "attack", event = "lost_sight", to = "patrol" },
    },
}
```

Initial-state rule: the **lowest-Id state node with a name** is
chosen as initial. If you want a different state to be initial,
delete it and re-add (new node gets a higher Id) or reorder
manually in the .tres. A future slice will add an explicit
"is initial" checkbox.

Cycles are allowed and expected — FSM back-edges are how state
machines model "go back to idle" / "loop into the same state on
event X." The PS1Graph cycle guard is skipped for `Kind = "fsm"`.

## Driving it from Lua (until FSM.new ships)

Until the runtime helper lands, you drive the table by hand:

```lua
-- In any scene script:
local current = _G.fsm_my_bot.initial

local function send(event)
    for _, t in ipairs(_G.fsm_my_bot.transitions) do
        if t.from == current and t.event == event then
            current = t.to
            return true
        end
    end
    return false  -- event doesn't apply in this state
end

function onUpdate(self)
    if see_player(self) then send("see_player") end
    if in_range(self)   then send("in_range")   end
    -- ...
end
```

The state machine logic is ~10 lines of Lua per entity. Once
`FSM.new` ships (slice D3-2), you'll get `:Send(event)`, `:Current()`,
and per-state `onEnter` / `onExit` / `onUpdate` callbacks without
hand-rolling the loop.

## Node-kind reference

| Editor node | Pins | Payload | Compiles to |
|---|---|---|---|
| **State** | Exec in + Exec out | State name | `"patrol"` entry in `states` |
| **Transition** | Exec in + Exec out | Event name | `{ from=…, event=…, to=… }` in `transitions` |

State exec-out fans out — drive multiple transitions from one state
by connecting its exec-out to several Transition exec-ins.

## Limits to know about

- **No per-state Lua snippets yet.** Slice D3-2 will add
  `onEnter` / `onExit` / `onUpdate` payloads.
- **No initial-state checkbox.** Lowest-Id state wins.
- **No runtime helper.** Drive the table from your own Lua for now;
  the 10-line walker above is the recipe.
- **Transition events are strings.** No type system — typos compile
  silently. Plan to add an event-vocabulary validation slice.

## Where the code lives

| Concern | File |
|---|---|
| Dock node palette + visual body | `godot-ps1/addons/ps1godot/ui/PS1GraphEditorDock.cs` (`state` / `transition` cases) |
| Compiler (`CompileFsm`) | `godot-ps1/addons/ps1godot/graph/PS1GraphCompiler.cs` |
| Resource types | `godot-ps1/addons/ps1godot/graph/PS1Graph{Resource,Node,Connection}.cs` (shared with all kinds) |
| Companion: dialogue graphs | `docs/ps1graph-dialogue-authoring.md` |
