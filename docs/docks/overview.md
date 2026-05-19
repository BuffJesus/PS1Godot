# Editor docks

PS1Godot adds a right-side dock and four bottom-panel tabs to
Godot. Each surface has a focused job; they share data (the
last-export summary feeds Doctor + the main panel's offender list
+ VRAM Viewer + Audio Routing) so authoring doesn't fragment
across windows.

<!-- SCREENSHOT: docks/strip.png — the 4-tab bottom-panel strip cropped tight -->

## At a glance

| Dock | Where | Job |
|---|---|---|
| [PS1Godot panel](ps1godot-panel.md) | Right-side | Scene budgets, Run-on-PSX, Quick Actions, Setup detection. |
| [PS1 Graph](graph.md) | Bottom — `PS1 Graph` tab | Authoring for Dialogue / FSM / Quest / Behavior Tree graphs. |
| [PS1 Doctor](doctor.md) | Bottom — `PS1 Doctor` tab | Unified validator — full offender list grouped by category. |
| **PS1 Authoring** (container) | Bottom — `PS1 Authoring` tab | Holds UI Canvas, VRAM Viewer, Audio Routing, Lua Cheatsheet, Quest Journal. |
| **PS1 Tools** (container) | Bottom — `PS1 Tools` tab | Holds Graph Find, References, Lua REPL. |

The two container tabs each hold a sub-tab strip; the contained
docks are documented under their own pages where they exist. A
2026-05 consolidation cut the bottom-panel strip from 10 tabs to
4 — older session diaries reference the pre-consolidation names.

## Reading order

If you're new and exploring:

1. [PS1Godot panel](ps1godot-panel.md) — the front door. Run-on-PSX
   lives here; everything else is supporting context.
2. [PS1 Doctor](doctor.md) — once you understand what the panel is
   showing, Doctor is the full validator view when you have a
   non-trivial scene with warnings.
3. [PS1 Graph](graph.md) — graph authoring (dialogue, FSM, quest,
   behavior tree). Skip if you're not yet using graphs.

The rest are reachable from `View → Bottom Panel` or by clicking the
tab strip when you enable the plugin.
