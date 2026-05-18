# PS1Graph Behavior Tree Authoring

How to author a Behavior Tree in the PS1Graph dock and tick it from
Lua. Fifth graph kind alongside Dialogue / FSM / Quest / Untyped.
Fills the "decide what to do this tick" niche that FSM (state +
transition) doesn't cover well.

When to use which:

- **FSM** — entity has a current state (patrol / chase / attack)
  and explicit events trigger transitions ("see_player" sends
  patrol → chase). Compact for 3-6 state archetypes.
- **BT** — entity re-decides what to do each tick by walking a
  prioritised tree. Better for: "try to attack if I can, else
  flank if I can, else patrol." Scales better than nested-FSM
  conditions when an entity has many fallback behaviours.

## TL;DR — five steps

1. **Author the tree.** PS1 Graph dock → `Kind: Behavior Tree` →
   *New* → right-click → drop **BT Sequence** / **BT Selector** /
   **BT Leaf** nodes.
2. **Wire children.** Composites have 6 child exec-out slots. Wire
   each used slot to a child node. Empty slots compile out.
3. **Author leaf snippets.** Each Leaf's Lua must `return "success"`,
   `"failure"`, or `"running"`. Receives `self` (the BT instance).
4. **Save.** Sibling `<basename>.lua` is auto-written with
   `_G.bt_<basename>`.
5. **Tick from Lua.** From your scene's per-frame Lua:

   ```lua
   local bot = bot or BT.new(_G.bt_my_bot)
   bot:Tick(self)
   ```

## What it compiles to

```lua
_G.bt_my_bot = {
    root = "n0",
    nodes = {
        n0 = { kind = "selector", children = {"n1", "n4"} },
        n1 = { kind = "sequence", children = {"n2", "n3"} },
        n2 = { kind = "leaf", fn = function(self) return self.actor:CanSeePlayer() and "success" or "failure" end },
        n3 = { kind = "leaf", fn = function(self) return self.actor:Attack() end },
        n4 = { kind = "leaf", fn = function(self) self.actor:Patrol(); return "running" end },
    },
}
```

The author reads top-to-bottom:
1. Selector tries the attack sequence first (n1).
2. n1 (Sequence) requires can-see-player AND attack to both
   succeed.
3. If either fails, the Selector falls back to n4 (patrol),
   which returns `"running"` to indicate "still working on it."

## Composite semantics

**Sequence** (`bt_sequence`): walks children left-to-right.
- First failed child → return `"failure"` immediately.
- First child returning `"running"` → return `"running"`; next
  Tick resumes from the same child.
- All succeed → return `"success"`.

**Selector** (`bt_selector`, a.k.a. "fallback"): walks
left-to-right.
- First succeeded child → return `"success"`.
- First child returning `"running"` → return `"running"`; resume
  from the same child next Tick.
- All fail → return `"failure"`.

## Leaf API

The Lua snippet on a Leaf node MUST return one of the three result
strings. Anything else is treated as `"failure"`. The walker
wraps the snippet as:

```lua
function(self) return (<your snippet>) end
```

So `return` is implicit — write an expression that evaluates to
the result string:

```
self.actor:HasTarget() and "success" or "failure"
```

`self` is the BT instance. Useful fields:

- `self.actor` — whatever was passed to `bt:Tick(actor)` this
  frame.
- `self._scratch` — empty table you can write to for per-instance
  state ("how long have I been chasing this target?").
- `self:Reset()` — clears all in-flight `"running"` state on
  composites so the next tick restarts each subtree from its
  first child.

## Multiple instances

`BT.new(def)` creates an independent instance. One tree
definition can back many entities — each tracks its own
`_running` + `_scratch`:

```lua
local def = _G.bt_my_bot
local enemy_a = BT.new(def); enemy_a.id = "a"
local enemy_b = BT.new(def); enemy_b.id = "b"
-- a and b advance independently.
```

## Limits to know about

- **6 children per composite.** Authors with more chain composites
  (nest a Sequence inside another Selector slot). Variable-pin
  GraphNodes are a Godot framework limitation.
- **No decorator nodes (yet).** Common BT primitives like Inverter
  / Repeater / Cooldown aren't shipped. Workaround: wrap the
  condition into the Leaf's snippet (`return not <expr>` for
  inverter; track tick counts in `self._scratch` for repeat /
  cooldown).
- **Leaf snippets are single-line.** Same as the dialogue / FSM /
  quest pattern. Multi-statement chains via `;`; the right-pane
  Node Details inspector gives a multi-line TextEdit if you need
  it.
- **No tick rate control.** `bt:Tick(self)` is called once per
  frame from your Lua. Want a tick every N frames? Wrap the call
  in your own counter.

## Where the code lives

| Concern | File |
|---|---|
| Dock palette + visual body | `godot-ps1/addons/ps1godot/ui/PS1GraphEditorDock.cs` (`bt_sequence` / `bt_selector` / `bt_leaf` cases) |
| Compiler (`CompileBt`) | `godot-ps1/addons/ps1godot/graph/PS1GraphCompiler.cs` |
| Runtime `BT.new` | `psxsplash-main/src/lua.cpp` (`kBtHelperSrc` block in `Lua::Init`) |
| Companion docs | `docs/ps1graph-dialogue-authoring.md`, `docs/ps1graph-fsm-authoring.md`, `docs/ps1graph-quest-authoring.md` |
