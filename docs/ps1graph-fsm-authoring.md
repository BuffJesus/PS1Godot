# PS1Graph FSM Authoring

How to author a finite-state machine in the PS1Graph dock and consume
it from Lua. Slice D3-1 shipped the compiler + node palette;
slice D3-2 added the `FSM.new` runtime helper so you don't hand-roll
the walker. Per-state Lua callbacks (`on_enter` / `on_exit` /
`on_update`) are aspirational — the helper checks for them
defensively, the compiler doesn't populate them yet (D3-3).

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

## Driving it from Lua

`FSM.new` is built into the runtime as of slice D3-2 — it lives in
`_G.FSM` before any scene script runs, no setup required.

```lua
-- Construct an instance from the compiled table.
local bot = FSM.new(_G.fsm_my_bot)

-- Query state.
bot:Current()         --> "patrol"
bot:Is("patrol")      --> true

-- Drive transitions by sending events.
function onUpdate(self)
    if see_player(self) then bot:Send("see_player") end
    if in_range(self)   then bot:Send("in_range")   end
end

-- Per-frame tick — if you wire per-state on_update callbacks
-- (D3-3) you'll call this to dispatch them.
bot:Update(dt)
```

`Send(event)` returns `true` if a transition fired,
`false` if no transition matches `(current, event)` (so you can
detect "event ignored").

### Multiple instances

`FSM.new` produces independent instances. One FSM table can back
many entities — every enemy with the same brain shares one definition
but tracks its own state.

```lua
local def = _G.fsm_my_bot
local enemy1 = FSM.new(def)
local enemy2 = FSM.new(def)
-- enemy1 and enemy2 transition independently.
```

### Optional per-state callbacks (D3-3, opt-in today)

The helper checks for `def.on_enter[state]`, `def.on_exit[state]`,
`def.on_update[state]` and invokes them when present. Today the
compiler doesn't populate these (D3-3 will add per-state Lua-snippet
fields on the State node), but you can hand-attach them yourself if
you want the hook now:

```lua
local def = _G.fsm_my_bot
def.on_enter = {
    chase = function(self, event) Audio.PlaySfx("growl") end,
}
local bot = FSM.new(def)  -- enter(initial) NOT fired since initial is set up before callbacks.
```

## Node-kind reference

| Editor node | Pins | Payload | Compiles to |
|---|---|---|---|
| **State** | Exec in + Exec out | State name | `"patrol"` entry in `states` |
| **Transition** | Exec in + Exec out | Event name | `{ from=…, event=…, to=… }` in `transitions` |

State exec-out fans out — drive multiple transitions from one state
by connecting its exec-out to several Transition exec-ins.

## Limits to know about

- **No per-state Lua snippets compiled in yet.** D3-3 will add
  Payload slots on the State node for `on_enter` / `on_exit` /
  `on_update` snippets that the compiler emits into the `on_*`
  lookup tables. Until then, hand-attach callbacks as shown above
  if you need them.
- **No initial-state checkbox.** Lowest-Id state wins.
- **Transition events are strings.** No type system — typos compile
  silently. Plan to add an event-vocabulary validation slice (warn
  on `Send("typo")` calls that don't match any transition).
- **No `:Update(dt)` self-tick.** You call `bot:Update(dt)` from your
  own per-frame Lua (e.g. PS1Player.onUpdate). The helper doesn't
  hook the runtime per-frame tick on its own.

## Where the code lives

| Concern | File |
|---|---|
| Dock node palette + visual body | `godot-ps1/addons/ps1godot/ui/PS1GraphEditorDock.cs` (`state` / `transition` cases) |
| Compiler (`CompileFsm`) | `godot-ps1/addons/ps1godot/graph/PS1GraphCompiler.cs` |
| Resource types | `godot-ps1/addons/ps1godot/graph/PS1Graph{Resource,Node,Connection}.cs` (shared with all kinds) |
| Companion: dialogue graphs | `docs/ps1graph-dialogue-authoring.md` |
