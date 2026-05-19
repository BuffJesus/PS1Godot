# PS1Graph ← UE 5.7 Blueprint node-UX port plan

Mined from UE 5.7 Blueprint editor source: `Engine/Source/Editor/Kismet`,
`KismetWidgets`, `BlueprintGraph`, `GraphEditor`. Six picks worth porting
into PS1Graph plus four explicit rejects. Each pick has scope estimate
and a why-not so this plan stays honest about which UE patterns assume
scale we don't have.

Sampled files:
- `K2Node.h` — advanced-display flag, advanced-pin display rules
- `EdGraphNode.h` — `ENodeEnabledState` tri-state
- `SGraphNodeComment.h` — resizable container with drag-along children
- `SGraphNodeKnot.h` — pinless passthrough for wire routing
- `SGraphNode.h` — per-node comment bubble
- `K2Node_CallFunction.h` — tooltip / menu category / corner icon
- `SKismetInspector.cpp` — selection-driven Details panel
- `FindInBlueprints.h` — cross-graph string search

## Picks (in priority order)

### 1. Right-pane Node Details inspector — small

**What:** Wire `GraphEdit.NodeSelected` → side panel that shows the
selected node's payloads as a labeled form (mirrors UE's
`SKismetInspector.ShowDetailsForSingleObject`), instead of cramming
everything into the on-canvas body.

**Why it fits PS1Graph:** Dialogue's Lua Snippet and FSM's
`on_enter` / `on_update` / `on_exit` are explicitly:
> "Snippets are single-line. Chain statements with `;` for multiple
> statements. Multi-line authoring is a polish slice — text-area
> inputs in the State node body instead of LineEdits"
> — *docs/ps1graph-fsm-authoring.md*, Limits

A side `TextEdit` solves multi-line for every kind at once without
per-kind body churn.

**Why it might not:** Adds a docking surface; users on narrow monitors
lose canvas width.

### 2. Per-node `Enabled / Disabled / DevelopmentOnly` tri-state — small

**What:** Mirror UE's `ENodeEnabledState` (`EdGraphNode.h:167`).
Right-click → Enabled / Disabled / Development-only. Disabled nodes
pass exec through; Dev-only emit guarded by a compile-time switch.

**Why it fits:** Authors today only have delete-and-undo to mute a
Play-Sound or Cutscene step while bug-hunting. Dev-only directly
serves the debug-console smoke-test path documented in dialogue
troubleshooting.

**Why it might not:** Adds a payload slot per node and a code-gen
branch in `PS1GraphCompiler` — small per kind but touches every kind.

### 3. Reroute / Knot nodes — small

**What:** Port `SGraphNodeKnot` — a pinless 1-in / 1-out passthrough
drawn as a small dot, used purely to bend wires.

**Why it fits:** Quest graphs have AND-merges:
> "Multiple incoming edges into one objective = AND"
> — *docs/ps1graph-quest-authoring.md*

which produce wire crossings the moment you have >3 objectives.
Wires currently route through the visual body of every Choice/Line
node.

**Why it might not:** Requires teaching the compiler walker to
skip-through knot nodes (cheap) and the GraphEdit DragConnection to
spawn-on-empty-canvas (less cheap in Godot).

### 4. Find-in-graphs panel — medium

**What:** Port `FindInBlueprints` shape: one search box, scans every
`.tres` under `res://graphs/` for matching node payloads (flag names,
snippet text, line text), click-to-jump.

**Why it fits:** The dialogue troubleshooting doc:
> "the flag isn't set yet. `Persist.Set("name", true)` somewhere
> upstream, or check the flag name matches between Set Flag and
> Condition nodes"
> — *docs/ps1graph-dialogue-authoring.md*, Troubleshooting

String-keyed flags + string-keyed FSM events ("Transition events
are strings. No type system — typos compile silently") are the
silent-failure modes; a global find is the cheapest mitigation
before D3-4's event-vocabulary validator lands.

**Why it might not:** Cross-`.tres` indexing duplicates what the
future D3-4 event-vocab validator + Lua API IDL host-mode tests will
produce more accurately.

### 5. Compact node title with corner icon + category-tinted title bar — small

**What:** UE's `K2Node_CallFunction::GetCornerIcon` /
`GetMenuCategory` / `GetNodeTitleColor` — each node carries a
one-glyph corner icon and a category-tinted title strip.

**Why it fits:** Dialogue mixes Line / Choice / Set-Flag / Condition
/ Cutscene / Sound / Lua nodes in one graph and they all have the
same gray header. Set-Flag (state-mutating) and Lua-Snippet (escape
hatch) deserve a visual flag for review.

**Why it might not:** Pure cosmetics on a 4-graph-kind system; could
just bold the title.

### 6. Tooltips driven by per-kind metadata table — small

**What:** UE `GetTooltipText()` / `GetMenuCategory()` populate the
action-menu hover. Single
`static IReadOnlyDictionary<string, (string Title, string Tooltip, string Category)> KindMeta`
in the dock; the palette popup and `GraphNode.TooltipText` both read
it.

**Why it fits:** New authors hit:
> "no GiveItem node. Blocked on a missing Inventory Lua API"
> — *docs/ps1graph-dialogue-authoring.md*, Limits

The palette gives them no hint what each kind does until they drop
one. Tooltips also document what's deprecated vs. shipped.

**Why it might not:** Trivial dictionary-of-strings; risks staleness
vs. the authoring docs.

## Rejects (UE has it; doesn't fit PS1Graph)

- **Variables / Functions / Macros / Local Variables panel ("My
  Blueprint" tree).** D4 reject list (`ROADMAP.md`):
  > "wildcard pin types ... runtime VM (compile to Lua, zero runtime
  > cost)"

  Lua's `_G` and per-script env already are the variable system; a UI
  panel adds a second store to keep in sync.

- **Visual breakpoints / Watch Points.** Compile-to-Lua means runtime
  debugging is the PCSX-Redux Lua console; no VM to halt.

- **Wildcard pins / type promotion / autocast.** Fixed pin-type set
  is a deliberate constraint per D4 reject list.

- **Per-node resizable comment box that auto-moves contained nodes**
  (`SGraphNodeComment::HandleSelection bUpdateNodesUnderComment`).
  We have a `comment` *node*; a containing *box* with drag-along
  semantics is a Godot-GraphEdit framework gap, not a small port.

## Suggested ordering when work resumes

1. **#1 Node Details inspector** — single biggest UX win, unblocks
   the multi-line snippet ask across dialogue / FSM / quest in one
   pass.
2. **#6 Tooltips + kind metadata table** — trivial to ship, makes
   the palette self-documenting; also creates the data table #5
   needs.
3. **#5 Corner icon + tinted title** — reads from the same metadata
   table; one-day polish that lifts every existing kind.
4. **#2 Enabled tri-state** — small per kind but touches every kind;
   ship as a single sweep across the compiler + dock.
5. **#3 Reroute knots** — wait until a quest or FSM graph in the
   field hits readability problems; visual cleanup only.
6. **#4 Find-in-graphs** — defer until D3-4 ships the vocab
   validator and we see whether typos remain a real pain.
