# PS1 Doctor

The unified validator dock — `PS1DoctorDock` — is the "show me
everything" view for scene problems the exporter caught. The
right-side panel's [Top offenders](ps1godot-panel.md#top-offenders)
list is the compact summary; Doctor is the full one.

<!-- SCREENSHOT: docks/doctor.png — Doctor loaded with the demo scene's warnings, categories expanded -->

## What it shows

Doctor reads the **last export summary** that the
`SceneCollector` + exporter produce. Every offender carries:

- The offending **node path** (clickable — focuses the node in the
  SceneTree).
- A **category** (VRAM, SPU, Mesh, UI, Collision, Lua, etc.).
- A **severity** (Info / Warning / Blocker).
- A **reason** — what the validator caught, often with the
  specific number that exceeded the budget.

The list is grouped by category by default, severity-filterable,
and ordered by severity within each group (Blockers first).

## Severity taxonomy

| Severity | Meaning | Effect on Run-on-PSX |
|---|---|---|
| **Info** | Useful context, not a problem. (e.g. "atlas 3 is 38% full") | None — informational only. |
| **Warning** | Will probably work but is fragile. (e.g. "texture is single-use in atlas 5 — wastes 56% of the TPage") | None today; Run-on-PSX proceeds. |
| **Blocker** | Will not run, or will brick. (e.g. "VRAM allocation overflows", "duplicate `PS1UICanvas.CanvasName`") | Run-on-PSX refuses to launch PCSX-Redux. |

The split lets you ship a "I know what I'm doing" tune of the
scene that has Warnings, without losing the safety net that
catches Blockers.

## Severity-filter checkboxes

Three checkboxes at the top filter the visible rows in real time.
Default state: all three on. Common patterns:

- **Blockers only** — pre-flight before a release export.
- **Warnings + Info off** — keeps the list short when you're
  iterating and don't want to scroll past dozens of Info rows.
- **Info only** — read-the-numbers mode, e.g. understanding which
  atlases have headroom.

## Categories

The current category set, in display order:

| Category | Source check | Example |
|---|---|---|
| **VRAM** | `TexturePacker.Validate` | "VRAM full at 1024×512 — needs 1088×512 (overflow 64 px)" |
| **SPU** | `AudioPacker.Validate` | "SPU 96% full — under-budget at 16 KB, 0 free for streaming" |
| **Mesh** | `MeshConverter.Validate` | "Mesh has 12,400 triangles — PS1 single-frame budget is ~1,500" |
| **UI** | `UICollector.Validate` | "Two `PS1UICanvas` named `dialog` — names must be unique" |
| **Collision** | `BVHBuilder.Validate` | "BVH would have 14,000 leaves — runtime build will be slow" |
| **Lua** | `LuaCollector.Validate` | "`cube.lua` references `Audio.PlayLoop` — not in the runtime API" |
| **Scene** | `SceneCollector.Validate` | "No `PS1Scene` root — exporter won't run" |

Adding a new category requires the validator to push an offender
with that category string; Doctor groups automatically.

## Click-to-focus

Single-click a node path → SceneTree highlights the node + scrolls
to it. Double-click → opens the Inspector pinned to that node.

Both work even when the offending node is deep in a sub-scene or
inside a `PackedScene` reference — the path is resolved through
the loaded scene graph at click time.

## Refresh behavior

The dock updates whenever an export runs (Run-on-PSX, "Export
only" Quick Action, or any other path that produces an
`LastExportSummary`). There's no separate Refresh button yet — the
slice-1 design keeps Doctor and the exporter tightly coupled so
warnings never go stale.

A "validate without exporting" Refresh button is on the slice-2
list — handy when you want to clear warnings as you author without
the full export cost.

## Compared to other surfaces

- The **PS1Godot panel's Top offenders** list is capped at 8 rows
  and uses one-line summaries. Use it during normal authoring;
  jump to Doctor when you're triaging a longer list.
- The **Godot Output panel** shows raw `PushWarning` /
  `PushError` output. Doctor parses the same data into the
  structured table — same information, presented for triage
  instead of for diagnostic logs.
