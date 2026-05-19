# PS1 Graph Find

Under **PS1 Tools → Graph Find**. Cross-graph substring search —
"which dialogue / quest / FSM references this string?"

<!-- SCREENSHOT: docks/graph-find.png — search results for "breathing_low" across the graphs -->

## The problem it solves

Quest, dialogue, FSM, and behavior tree authoring leans on string
keys: Persist flag names, FSM event names, audio clip names,
outcome ids. **Typos compile silently.** A
`Persist.Set("met_bob")` followed by a Condition node checking
`met_bbo` runs forever without the branch ever taking the true
arm — no compile error, no runtime warning.

Graph Find scans every `.tres` under `res://` for
`PS1GraphResource`s, then for each node checks the Payload +
Payloads array + EnabledState against your search query. Match
results list the graph path + node id + the matching text;
clicking a hit opens the `.tres` in Godot's FileSystem.

## When to use it

- **Rename safely** — about to rename a Persist flag from
  `met_bob` to `met_bob_at_inn`? Search first to see every node
  that references the old name.
- **Diagnose dead branches** — quest objective never fires?
  Search the outcome name across the dialogue + FSM graphs. If
  zero matches, no one's setting the condition that would fire it.
- **Audit string literals** — quick sanity check after a session
  of authoring that you didn't fat-finger an event name.

## What gets scanned

- Every `.tres` whose embedded script extends `PS1GraphResource`.
- Per node: the `Payload` field (single-field nodes like Line),
  the `Payloads` array (multi-field nodes like Choice), and the
  `EnabledState` condition string.
- Case-insensitive by default; toggle for case-sensitive in the
  options.

## What doesn't (yet)

- **Lua source files** — strings inside `.lua` aren't included
  in the search. The
  [References viewer](references.md) does asset-reference
  search across `.lua` for path strings, but not arbitrary
  literal matching. A v2 slice could add Lua-side regex search.
- **Jump-to-node** — clicking a hit opens the `.tres` but
  doesn't auto-select the matched node in the Graph editor.
  Slice 2.

## Related

- [PS1 Graph editor](graph.md) — where you fix matches you find.
- [References viewer](references.md) — for asset-path references
  rather than string-content references.
