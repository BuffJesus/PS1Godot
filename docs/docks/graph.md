# PS1 Graph editor

The bottom-panel `PS1 Graph` tab — `PS1GraphEditorDock` — hosts the
authoring canvas for the four PS1Graph kinds:

- **Dialogue** — branching conversations with choices, sub-dialogue
  call/return, and Lua-side notifies.
- **FSM** — finite state machines for AI brains, gameplay state.
- **Quest** — objective trees with serialized progression state.
- **Behavior Tree** — selector / sequence / leaf composition for
  more reactive AI than an FSM.

<!-- SCREENSHOT: docks/graph.png — test.tres loaded showing dialogue with all node kinds visible -->

Open by double-clicking a `.tres` whose script extends
`PS1GraphResource` (e.g. `test.tres` for dialogue, `bot_brain.tres`
for FSM, `village_quest.tres` for quest, `bt_test.tres` for BT).
The dock auto-loads the resource and renders its node graph.

For per-kind authoring detail, see:

- [Dialogue graphs](../authoring/graphs/dialogue.md)
- [FSM graphs](../authoring/graphs/fsm.md)
- [Quest graphs](../authoring/graphs/quest.md)
- [Behavior tree graphs](../authoring/graphs/behavior-tree.md)

This page documents the dock itself — affordances common across
all four kinds.

## Color tinting

Each graph kind gets a distinct accent tint so you can tell at a
glance what you're looking at: Dialogue blue, FSM teal, Quest amber,
Behavior Tree green. The tint applies to node title bars, connector
lines, and the canvas grid stripe at the top.

<!-- SCREENSHOT: docks/graph-tints.png — three side-by-side nodes from different kinds, ~600px wide -->

The accent is independent of the editor's purple accent; the dock
respects Godot's light/dark theme toggle.

## Node palette

Right-click on empty canvas → kind-appropriate palette pops up.
Each entry has a hover tooltip with the node's purpose. The palette
filters to the active graph kind — you can't drop an FSM `State`
node onto a Dialogue canvas.

<!-- SCREENSHOT: docks/graph-palette.png — palette open with tooltips -->

## Connections

Drag from an output port to an input port to connect. The dock
runs **cycle detection** on every connection attempt — DFS from the
proposed target back via existing edges, refuses the edge if it
can already reach the source. Prevents authoring uncompilable
loops at connect time; the alternative (catch them at compile time)
gives you a useless error far from the cause.

Different kinds have different connection rules:

- **Dialogue** — DAG with no cycles. Sub-dialogue call edges are
  treated as boundary crossings, not loops.
- **FSM** — cycles required (a state machine that can't loop is
  not interesting). The cycle check is disabled here; transition
  guards (the `condition` payload field) gate which way state
  flows at runtime.
- **Quest** — DAG, mostly. Objective completion can fan out to
  multiple downstream objectives.
- **BT** — strict tree; each child has exactly one parent. Drag
  an existing parent connection to a new parent to move a subtree.

## Compile + lint

A **Compile** button at the top of the dock runs the
`PS1GraphCompiler` on the active graph and writes a sibling
`.lua` file (e.g. `test.tres` produces `test.lua`). The compiler
serializes the graph into the table shape the runtime's
`Dialog.RunGraph` / FSM / Quest / BT walkers expect.

A **Lint** button runs the same validators the exporter uses, but
in isolation against just this graph. Errors land inline in the
canvas (offending node gets a red badge) + as a list in the
status row at the bottom of the dock.

The shipped behavior is: **Compile** always runs **Lint** first;
lint errors block compile. Edit then re-compile.

## Save behavior

`Ctrl+S` saves the `.tres` resource and auto-runs Compile. The
.lua sibling stays in sync with the `.tres` on every save — no
extra step.

`take_over_path` is set on the compiled `.lua` so reloading the
`.tres` in the editor doesn't lose any hand-edits you made to the
`.lua` (you shouldn't be hand-editing the generated file, but the
guard is there for when you do).

## Common affordances

- **Mouse-wheel** — zoom.
- **Middle-mouse drag** — pan.
- **Selection rectangle** — left-click drag on empty canvas.
- **Multi-select** — `Shift+click` adds; `Ctrl+click` toggles.
- **Delete** — Delete key, with confirm for >5 nodes.
- **Undo / Redo** — `Ctrl+Z` / `Ctrl+Y`, in the Godot editor's
  undo history. Connection changes, node moves, payload edits all
  participate.
- **Pin canvas** — top-right pin icon keeps a graph open across
  scene switches (useful while iterating on a Lua script that
  references the graph).

## Related docks

- [PS1 Graph Find](#) — `PS1 Tools → Graph Find` — searches across
  all graph resources in the project for a string match. Useful
  when you remember a dialogue line but not which graph it lives
  in.
- The compile + lint behavior overlaps with **PS1 Doctor**'s
  validation, but Graph's lint runs at edit time per-resource;
  Doctor runs at export time across the whole scene.
