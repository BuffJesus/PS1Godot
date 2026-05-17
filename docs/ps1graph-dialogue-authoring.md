# PS1Graph Dialogue Authoring

How to author a PS1Graph **dialogue** graph and get it running on PSX.
Covers the full path from clicking *New* in the dock to seeing
on-screen dialogue when you F5. Companion to `ROADMAP.md` *Graph
authoring framework (PS1Graph) / D1* — this doc is the practical
walkthrough.

## TL;DR — the five steps

1. **Author the graph.** PS1 Graph dock → set `Kind: Dialogue` →
   *New* → right-click → drop Line / Choice / Set Flag / Condition /
   Play Sound / Start Cutscene / Comment nodes. Wire exec pins.
2. **Save it.** *Save As…* → e.g. `res://graphs/intro.tres`. A
   sibling `intro.lua` is written automatically.
3. **Ship the .lua.** Select your `PS1Scene` → Inspector → Scripting →
   add `res://graphs/intro.lua` to **UserScripts**.
4. **Author the dialogue_box canvas.** Add a `PS1UICanvas` named
   exactly `dialogue_box` to your scene with the elements listed
   below. (Skip this on first pass — without a canvas the walker
   prints to the debug console, which is fine for verifying the
   graph runs.)
5. **Trigger the dialogue.** In your scene Lua, call
   `Dialog.RunGraph(_G.dialogue_<basename>)`.

## How the pieces wire up

```
┌──────────────────┐   sibling .lua   ┌─────────────────────┐
│  PS1Graph dock   │ ────────────────▶│  PS1Scene           │
│  Save → .tres    │                  │   .UserScripts      │
│      + .lua      │                  │   ↑ ships at export │
└──────────────────┘                  └─────────┬───────────┘
                                                │
                                                ▼
┌──────────────────────────────────┐    LoadLuaFile runs the chunk
│  PSX runtime (psxsplash)         │    once at scene init, which
│   _G.dialogue_<basename> = {...} │    installs the dialogue table
│                                  │    on _G but does nothing else.
│   Author Lua somewhere:          │
│     Dialog.RunGraph(             │
│       _G.dialogue_<basename>)    │
│   → walker starts                │
└──────────────────────────────────┘
```

The compiled `.lua` is just a global-assignment of the dialogue table.
**Nothing happens until `Dialog.RunGraph` is called** — the table sits
in `_G` waiting for a trigger. That's why step 5 above is necessary.

## The basename → global name mapping

The compiler derives a Lua-safe identifier from the .tres filename:

| Saved path | Global name |
|---|---|
| `res://graphs/intro.tres` | `_G.dialogue_intro` |
| `res://dlg/mom-yells.tres` | `_G.dialogue_mom_yells` |
| `(unsaved)` | `_G.dialogue_unnamed` |

Algorithm: filename without extension, lowercased, non-alphanumeric
characters replaced with `_`, leading digit prefixed with `_`.
Visible in the first line of the compiled .lua.

## The `dialogue_box` canvas

The walker `findCanvas("dialogue_box")` on `Dialog.RunGraph`. If the
canvas exists, the walker drives it. If it doesn't, the walker
prints to the PCSX-Redux debug console instead — useful for
verifying the graph logic before you've built any UI.

**Required canvas name:** exactly `dialogue_box`.

**Expected element names** (all optional — missing elements are
skipped silently, so you can ship a minimal canvas first and grow it):

| Element name | Type | Purpose |
|---|---|---|
| `speaker` | `PS1UIText` | Current line's speaker name |
| `text` | `PS1UIText` | Current line body / choice prompt |
| `option_1` | `PS1UIText` | Choice option 1 text |
| `option_2` | `PS1UIText` | Choice option 2 text |
| `option_3` | `PS1UIText` | Choice option 3 text |
| `cursor_1` | any | Visibility-toggled cursor next to option 1 |
| `cursor_2` | any | Visibility-toggled cursor next to option 2 |
| `cursor_3` | any | Visibility-toggled cursor next to option 3 |

The cursor elements can be `PS1UIImage`, `PS1UIBox`, or even another
`PS1UIText` displaying `>`. The walker toggles their `visible`
property to match the currently-selected option; only one is visible
at a time.

A reasonable starting layout:

- `dialogue_box` canvas, `sortOrder` high enough to draw over gameplay.
- A `PS1UIBox` background spanning the lower third of the 320×240 screen.
- `speaker` (left-aligned, top of the box).
- `text` (left-aligned, below speaker).
- `option_1`, `option_2`, `option_3` (stacked, indented slightly).
- `cursor_1..3` as small arrow images aligned with each option row.

You can start with just `speaker` + `text` to get line dialogue
working, then add `option_*` and `cursor_*` when you add a Choice
node.

## Triggering the dialogue from Lua

Once the .lua is in `UserScripts` and (optionally) the canvas exists,
trigger the dialogue from any Lua context:

```lua
-- From a scene script's onSceneCreationEnd:
function onSceneCreationEnd()
    Dialog.RunGraph(_G.dialogue_intro)
end

-- From a trigger volume's onTriggerEnter:
function onTriggerEnter(self, other)
    if not Persist.Get("met_bob") then
        Dialog.RunGraph(_G.dialogue_first_meeting)
    end
end

-- From an interactable's onInteract (NPC dialogue):
function onInteract(self)
    Dialog.RunGraph(_G.dialogue_shopkeeper)
end
```

`Dialog.RunGraph` returns immediately — the walker continues across
frames. While it's running, `Dialog.IsActive()` returns `true`.
`Dialog.Stop()` aborts in-progress dialogue (useful for cutscene
interrupts).

## Node-kind cheat sheet

| Editor node | Compiles to | What it does |
|---|---|---|
| **Line** | `kind="line"` + speaker, text, next | Display one line, wait for X |
| **Choice** | `kind="choice"` + 3 options | D-pad to select, X to confirm |
| **Set Flag** | `kind="action"` + `Persist.Set(name, bool)` | Auto-advance |
| **Condition (flag)** | `kind="condition"` + `Persist.Get(name) == true` | Branch on flag, two exec outs |
| **Play Sound** | `kind="action"` + `Audio.PlaySfx(clip)` | Auto-advance |
| **Start Cutscene** | `kind="action"` + `Cutscene.Play(id)` | Auto-advance |
| **Comment** | — | Decoration; compiles to nothing |

Action and Condition nodes chain at one node per frame (~16ms each)
which is imperceptible — a Set-Flag → Play-Sound → Line sequence
looks instant.

## Player controls during dialogue

- **X (Cross):** Advance line / confirm choice
- **D-pad Up:** Move cursor up in a choice
- **D-pad Down:** Move cursor down in a choice

One-frame input deferral on the entry frame of each node — the X
press that advanced *into* a node won't immediately advance back out.
This means choices are stable from the moment they appear.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Nothing happens after `Dialog.RunGraph` call | Wrong global name. Compare your `_G.dialogue_<basename>` against the first line of the compiled `.lua`. |
| Walker logs in debug console but no on-screen text | No `dialogue_box` canvas in the scene — the walker falls back to printf. |
| Some lines appear but speaker is blank | The `speaker` element isn't named exactly `speaker`, or doesn't exist on the canvas. Walker silently skips missing elements. |
| Choices don't navigate | Either no `cursor_*` elements (cursor invisible) or fewer than 2 active options. |
| Branch always takes the false arm | The flag isn't set yet. `Persist.Set("name", true)` somewhere upstream, or check the flag name matches between Set Flag and Condition nodes. |
| Compiled .lua exists but isn't running on PSX | Not in `PS1Scene.UserScripts`, or the export didn't include it. Look for `[PS1Godot] Lua on 'PS1Scene.UserScripts[N]': res://…` in the editor Output after export. |
| Editor crashes on graph operations | Restart Godot — C# DLLs don't hot-reload, stale plugin code persists across project builds. |

## Limits to know about

- **3 choice options max.** Variable-pin GraphNode support isn't in
  yet; chain Choice nodes for wider fanout.
- **`Condition` reads flags only.** Arbitrary Lua expressions need a
  future "Lua Snippet" node kind (not shipped).
- **No `GiveItem` node.** Blocked on a missing `Inventory` Lua API in
  the runtime; lands when inventory ships.
- **D-pad Up/Down is edge-triggered**, no auto-repeat. Long option
  lists need one press per move.
- **Canvas isn't authored for you.** First-run experience is the
  printf fallback until you build the `dialogue_box`.

## Where the code lives

| Concern | File |
|---|---|
| Editor dock | `godot-ps1/addons/ps1godot/ui/PS1GraphEditorDock.cs` |
| Resource types | `godot-ps1/addons/ps1godot/graph/PS1Graph{Resource,Node,Connection}.cs` |
| Compiler | `godot-ps1/addons/ps1godot/graph/PS1GraphCompiler.cs` |
| Runtime walker | `psxsplash-main/src/dialogue.{hh,cpp}` |
| Lua API binding | `psxsplash-main/src/luaapi.cpp` (search `Dialog_`) |
| Sibling .lua write | `PS1GraphEditorDock.WriteCompiledLuaSibling` |
| `UserScripts` plumbing | `godot-ps1/addons/ps1godot/exporter/SceneCollector.cs` (search `UserScripts`) |

`ROADMAP.md` "Graph authoring framework (PS1Graph)" tracks the
slice-by-slice changelog.
