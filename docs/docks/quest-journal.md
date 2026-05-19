# PS1 Quest Journal

Under **PS1 Authoring → Quest Journal**. In-editor quest simulator
— click "Complete" on an objective to advance the quest state
without doing the round-trip through PCSX-Redux.

<!-- SCREENSHOT: docks/quest-journal.png — village_quest.tres loaded, find_npc complete, talk_to_npc active -->

## The loop it shortens

Quest authoring without this dock means: edit `.tres` → Save → F5
→ wait for splashpack export + PCSX-Redux boot → trigger
objective completions in-game → observe outcome → repeat. ~30
seconds per iteration, and you have to write Lua test scaffolding
in the demo scene to fire the completions because gameplay
doesn't expose a "complete this objective" affordance.

This dock collapses it: load `.tres` → click **Complete** on any
active objective → active set + outcome update live in the dock.

## What it shows

- **Quest progression panel** — the objectives that are currently
  Active, separated from Completed and Pending sets.
- **Per-objective actions** — Complete / Reset / Fail.
- **Dependency view** — visual indication of which objectives
  unlock which downstream ones.
- **Outcome traces** — when an outcome fires, the dock logs the
  outcome name + any side-effects (audio cues, scene transitions,
  Persist flag sets).

## Workflow

1. **File → Load** at the top of the dock, pick a quest `.tres`
   (e.g. `village_quest.tres`).
2. The Initial state populates — the first objectives that fire
   on quest start are listed as Active.
3. Click **Complete** on an objective to mark it done. Downstream
   objectives whose preconditions are now met become Active.
4. **Reset** at the top clears the simulator state back to the
   initial active set.

## When to use it

- **Debugging a "the wrong objective unlocked next" bug** —
  fastest way to see the dependency graph in action.
- **Testing a Fail path** — manually fail an objective without
  reproducing the in-game failure condition.
- **Validating a long quest** — step through a multi-stage quest
  in seconds rather than playing through it.

Doesn't replace in-game testing — gameplay-bound triggers
(`PS1TriggerBox.OnEnter` calling `Quest.Complete`) still need the
real runtime to verify. The dock validates the quest graph's
logic; the runtime validates that the right actors fire the right
completions.

## Related

- [Quest graphs](../authoring/graphs/quest.md) — authoring the
  `.tres` itself.
- [PS1 Graph editor](graph.md) — visual graph view of the same
  resource.
