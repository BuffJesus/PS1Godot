# Handoff — UE port batch (sessions of 2026-05-17)

This session shipped a large batch of UE-inspired editor work across
the graph framework, dialogue, FSM, quest, and the wider editor
surface. **Most of it has not been F5-verified.** The user's pattern
is "one question/instruction at a time, I verify."

This doc is structured to be walked through in order — each numbered
step is a single concrete action + an expected observation. Next
session: take Phase A first, then ask the user to confirm each
result before moving to the next step.

After the verification phases there's a **"Continue development"**
section pointing at the next queued work.

---

## Session context summary

Pushed to `origin/main` between commits `9c82d54` (start of long
batch — dialogue UX polish) and `1a74cdc` (Lua REPL — end). Plus
the prior D1/D2/D3 graph framework slices. Roughly 30+ commits.

User F5-confirmed working before the UE port batch:
- D2-3 Quest per-objective callbacks (`[Quest] >> activate find_npc` etc.)
- D3-3 FSM per-state callbacks (`[FSM] enter patrol` etc.)
- D1g power-user nodes (only via code review, not user F5)
- D1h/D1i/D1j dialogue Line extensions (only via code review)

User-tested fixtures live at:
- `godot-ps1/test.tres` / `.lua` — dialogue smoke (5 lines, choice, audio, branches)
- `godot-ps1/bot_brain.tres` / `.lua` — FSM smoke (patrol↔chase)
- `godot-ps1/village_quest.tres` / `.lua` — quest smoke (3 objectives + outcome)

All three are referenced from the monitor scene's `PS1Scene.UserScripts`,
so a single F5 of `monitor.tscn` exercises all three runtimes.

---

## Phase A — Editor smoke test (plugin loaded, all tabs registered)

**A1.** Restart Godot. Open the project. Watch the Output panel.
*Expected:* `[PS1Godot] Plugin enabled. F5 = Run on PSX (export + build + launch).`
No red ERROR lines from `PS1ProjectSettings.Register` or any dock's
constructor.

**A2.** Look at the bottom panel tabs.
*Expected (in order):* the existing tabs (PS1 Godot, PS1 UI, PS1 VRAM,
PS1 Audio, PS1 Cheatsheet) plus the new ones from this session:
**PS1 Graph, PS1 Doctor, PS1 Quest Journal, PS1 Graph Find,
PS1 References, PS1 Lua REPL**.
*If a tab is missing:* check Output for a dock-constructor error;
its registration is in `PS1GodotPlugin._EnterTree`.

**A3.** Click each new tab once to confirm each one builds its UI
without throwing.
*Expected:* every tab opens; no exceptions in Output.

**A4.** Open Project → Project Settings → search "ps1godot". You
should see six keys under the `ps1godot/` prefix (budgets x2, audio,
launcher x2, iteration). Each has a default value.
*If absent:* `PS1ProjectSettings.Register` either didn't run or
silently failed.

---

## Phase B — Existing scene regression

The monitor scene exercises everything end-to-end. If it still works
unchanged, the dialogue / FSM / quest runtime extensions didn't
regress.

**B1.** F5 the monitor scene. Wait for PCSX-Redux to launch.
*Expected:* Game boots. Console shows `monitor: scene init`, then
all three smoke-test outputs:

```
[Dialog] start — entry=n0 (canvas=dialogue_box)
[Dialog] handles: speaker=21 text=22 option=[23,24,25] cursor=[-1,-1,-1]
[FSM] enter patrol
[FSM] initial = patrol
[FSM] exit patrol
[FSM] enter chase
[FSM] after see_player → chase
[FSM] exit chase
[FSM] enter patrol
[FSM] after lost_player → patrol
[FSM] Send('nope') fired = false
[Quest] >> activate find_npc
[Quest] start: active='find_npc'
[Quest] >> complete find_npc
[Quest] >> activate talk_to_npc
[Quest] complete find_npc -> unlocked='talk_to_npc'
[Quest] outcome (mid)=nil
[Quest] >> complete talk_to_npc
[Quest] >> activate defeat_orc
[Quest] >> complete defeat_orc
[Quest] >> trigger victory
[Quest] outcome (end)=victory
[Dialog] line emit: speaker='Your Mum' text='Hello' (canvas)
```

**B2.** Verify the toast (D1h+D1i+D1j+D2-4 + UE editor pick #3
combined effect): a colored notification panel appeared in the
bottom-right of the editor after F5 — green if no warnings, amber
if warnings, red on Halt.
*If absent:* `PS1ToastNotifier` either failed `_Ready` or never
got `Show()` called. Check `PS1GodotPlugin.OnRunOnPsx` for the
two `_toast?.Show(...)` calls.

**B3.** In the PSX, advance through the dialogue with X. Pick a
choice option. Confirm the audio clip plays on n0 and n3 (the lines
with `audio = "breathing_low"`).
*Expected:* audible "breathing_low" sample on those line entries.
*If silent:* `Audio.PlaySfx` from the walker may not be finding the
clip — check `[Dialog] line audio:` lines for errors.

---

## Phase C — Dialogue node kinds added this session

Test each new kind in isolation via the graph editor.

**C1.** Open `test.tres` in PS1 Graph dock. Right-click the canvas
to open the palette.
*Expected:* the palette lists the dialogue node kinds including the
new ones: **Lua Snippet, Lua Condition, Sub-Dialogue**, and all
existing kinds (Line, Choice, Set Flag, etc.). Hover any item.
*Expected:* a tooltip describing the kind appears (UE pick #6).

**C2.** Drop a **Lua Snippet** node next to an existing Line. Wire
its exec in from the previous node's exec out; wire its exec out to
the next node's exec in. Type `Debug.Log("snippet fired")` into its
body field.

**C3.** Drop a **Sub-Dialogue** node anywhere in the canvas. Type
something like `test` into its target field (refers to
`_G.dialogue_test`).

**C4.** Select any node. Look at the right pane.
*Expected (UE pick #1):* a side panel labeled with the kind +
`(id #N)`, showing per-payload `TextEdit` rows, plus an
**Enabled/Disabled/DevelopmentOnly** OptionButton at the top
(UE pick #2). Edits in the TextEdits should write back to
`PS1GraphNode.Payloads`.
*If side panel missing:* check `BuildUI`'s `HSplitContainer` got
constructed; default `SplitOffset = -300`.

**C5.** Flip a node to Disabled in the inspector.
*Expected:* the on-canvas node title gains a `[OFF]` prefix and
turns grey. Compile (toolbar button) — disabled nodes don't appear
in the output; predecessors' `next` chases past them.

**C6.** Right-click → drop a **Reroute** node. Wire it between two
existing line/action nodes (rewire the source exec out to Reroute's
in, Reroute's out to original target's in).
*Expected:* compile still works; reroute is transparent at runtime
(same chase logic as Disabled).

**C7.** Verify node title tints (UE pick #5): Dialogue kinds should
have a blue title strip, FSM teal, Quest amber. State-mutating /
side-effect kinds get a corner glyph (▶/✱/♪/⚡/↪/→/🏁).

---

## Phase D — Line node's new fields (D1h + D1i)

Already-working line nodes should gain audio + skippable + notifies
in their on-canvas body.

**D1.** Open `test.tres`. Click any Line node body.
*Expected:* below the speaker + text rows there's now an **audio
clip name** LineEdit, a **skippable** CheckBox, and a **notifies**
LineEdit with placeholder `12:Audio.PlaySfx("x") | 30:...`.

**D2.** On n0 ("Hello"), set audio = `breathing_low`, skippable =
true (already is). Add a notify `30:Debug.Log("notify-30 fired")`.
Save the graph.

**D3.** F5. On the PSX, n0's line entry should play the audio AND
print `notify-30 fired` 30 frames (~0.5s) into the line display.
*If notify doesn't fire:* check `[Dialog] notify[i] not a table` or
`load error` in console; the parser is strict on `frame:lua` format.

**D4.** Flip n0 to skippable = false. Re-F5. X-press during the
audio should be ignored until the clip finishes (`m_lineAdvanceLockFrames`
derived from audio duration).

---

## Phase E — Quest Journal dock

In-editor quest simulator (D2-4).

**E1.** Open the bottom panel → PS1 Quest Journal tab.
*Expected:* the dock renders with `(no quest loaded)` and a "Load
Quest…" button.

**E2.** Click Load Quest → pick `res://village_quest.tres`.
*Expected:* the dock populates with:
- Counters: "1 active • 0 complete • 3 total"
- One row per objective with state badge: ● amber for `find_npc`
  (active), · grey for the others (locked)
- "Outcome: —"

**E3.** Click **Complete** on the find_npc row.
*Expected:* find_npc gains ✓ green badge; talk_to_npc unlocks
(● amber, Complete button appears). Counters update.

**E4.** Click Complete on talk_to_npc, then defeat_orc.
*Expected:* "Outcome: victory" turns green at the bottom.

**E5.** Click **Reset**.
*Expected:* state resets — only find_npc is active again.

---

## Phase F — PS1 Doctor dock

Aggregated validator view (Doctor slice 1 + classifier fix).

**F1.** After F5 (Phase B), open PS1 Doctor tab.
*Expected:* the header shows scene count + error/warning count
("4 scene(s) • 0 error(s), 16 warning(s)" — count from the
monitor scene's known warnings).

**F2.** Verify category groups: VRAM warnings under "Texture / VRAM"
(NOT under Animation), audio warnings under "Audio", etc. The
classifier fix in `e6f6daf` made Animation match `anim/keyframe/
frames/fps` only (no bare `clip`).

**F3.** Click a row → confirms it highlights the node in SceneTree
or focuses the relevant offender.

**F4.** Toggle the Errors / Warnings checkboxes at the top —
the list filters in-place.

---

## Phase G — UE Blueprint port-plan picks (graph dock)

These touch authoring UX for existing graphs — should integrate
without breaking anything.

**G1.** Hover the right-click palette items in any graph — every
kind should show a hover tooltip describing what it does (pick #6).

**G2.** Hover a node's title bar on the canvas — same tooltip
should appear (pick #6).

**G3.** Verify category tints on existing nodes (pick #5):
- Dialogue Line/Choice/etc → blue title strip
- FSM State/Transition → teal
- Quest Objective/Outcome → amber

**G4.** Open the PS1 Graph Find tab. Type `breathing_low` → Search.
*Expected:* hits in `test.tres` (n0 + n3's audio field). Click a
hit → highlights the .tres in FileSystem.

**G5.** Verify the Disabled chase (pick #2): in `test.tres`, flip
n0 (Hello) to Disabled. Save. Compile (toolbar). Verify the
emitted `_G.dialogue_test.entry` skips to the next eligible node,
and the n1 (choice)'s entry won't reach n0.
*Note:* unflip n0 after testing so the rest of the dialogue smoke
still works.

---

## Phase H — Beyond-Blueprint picks

**H1.** In the FileSystem dock, navigate to a `PS1AudioClip.tres`
(e.g. anywhere a PS1AudioClip exists, or create one quickly).
*Expected:* the file's thumbnail shows a colored waveform with a
route stripe on top (amber for SPU, blue for XA, green for CDDA,
grey for Auto) instead of the generic Resource icon.
*If the icon hasn't refreshed:* Godot caches thumbnails in
`.godot/imported/` — delete that folder + restart, or just wait
~30s for the cache to update.

**H2.** Open PS1 References tab. Click **↩ From FileSystem selection**
after selecting a known asset (e.g., a texture used in the monitor
scene).
*Expected:* the dock lists every .tscn / .tres / .lua line that
references that asset (UID or path-string match). Click a hit →
FileSystem highlights it.

**H3.** Open PS1 Lua REPL tab. Type `print("hello from REPL")` →
Send.
*Expected:* the scrollback shows `> print(...)` + `→ sent`. In
the running PCSX-Redux (must be on PCdrv mode = no XA-routed
clips), within ~0.5s the debug console prints `hello from REPL`
followed by `[REPL] OK`. Note the monitor scene as configured
uses XA clips → forces ISO mode → REPL won't reach. To test:
temporarily blank `PS1Scene.AudioClips` of any XA-routed clips,
re-F5 to land in PCdrv mode.
*If `[REPL] version bumped but repl.lua missing` appears:* the
write order in `PS1LuaReplDock.Send()` was wrong; check `lua`
write happens before `ver` write.

**H4.** Author a tiny BT graph: PS1 Graph → New (Behavior Tree) →
drop a Selector + one Leaf. In the leaf, type `return "success"`.
Save. Verify the sibling `.lua` compiles to
`_G.bt_<basename> = { root = "n0", nodes = { n0 = { kind = "selector", children = {"n1"} }, n1 = { kind = "leaf", fn = ... } } }`.

**H5.** From any scene Lua, drive the BT:
```lua
local bot = BT.new(_G.bt_<basename>)
Debug.Log("BT tick = " .. bot:Tick(self))
```
*Expected:* PSX prints `BT tick = success`. (Easiest: temporarily
add to `scene_monitor_init.lua`'s onSceneCreationEnd.)

---

## Continue development after verification

Three queued directions, in increasing scope. Pick one per the
caveman-commit cadence ("one slice at a time, ask before commit").

### Option 1 — Polish the picks that landed (small)
- **PS1 Doctor slice 2:** explicit `Category` field on
  `LastExportSummary.Offender` (drops the inline string parse in
  `PS1DoctorDock.ClassifyOffender`), per-category tabs, "Re-run
  validators" button that runs the export validator pipeline
  without writing the splashpack.
- **Beyond-Blueprint pick #7 — tab badges:** re-attempt via
  walking `EditorInterface.Singleton.GetBaseControl()` for the
  bottom-panel button by name. If Godot 4.7-dev5 exposes a way
  via a more recent API, port it.
- **D3-4 FSM polish:** explicit "is initial" CheckBox on State
  node (override the lowest-Id rule), event-vocabulary
  validation (warn on transitions referencing events nowhere
  `Send`'d), multi-line snippet inputs already covered by the
  Node Details inspector.

### Option 2 — Resume the older queued initiatives (medium)
- **Music authoring Tier 1 (Import MIDI button)** — from
  `ROADMAP.md` "Music authoring experience" section. Pick a `.mid`
  → plugin parses → auto-creates a `PS1MusicChannel` skeleton per
  channel. Self-contained, big QoL win.
- **D4 PS1ScriptGraph** — last of the graph kinds. General-purpose
  Blueprint-style scripting for triggers + event reactions. Per
  ROADMAP D-section, this was deliberately last because the node
  palette is unbounded and use cases were vague until D1–D3 landed.
  Now that they have, scope is clearer: start with a small node
  set (event trigger, condition, action) and let it grow.
- **PS1 Doctor slice 2 + Music Tier 1** mentioned above.

### Option 3 — Third port plan (large)
Both UE port plans (`docs/ps1graph-ue-blueprint-port-plan.md` +
`docs/ps1godot-ue5-editor-port-plan.md`) covered Blueprint + the
wider editor. Untapped UE areas:

- **Animation system** (`Engine/Source/Editor/Persona`,
  `AnimGraph`) — UE has a sophisticated animation blueprint
  layer with state machines, blend spaces, montages. PS1Godot
  has `PS1Animation` (simple tracks) + skinned mesh + `Animation
  Notify` markers via the dialogue Line. A port plan would
  consider blend spaces (for run/walk speeds), state machines on
  animations (idle ↔ walk ↔ run via FSM), and montage-style
  one-shot overlays. Likely overlaps with the existing FSM
  framework — could share the runtime walker.
- **AI Perception / Pawn Sensing** — UE has a perception
  component that listens for sight/hearing stimuli. PS1Godot's
  BT can implement "see player" leaves manually but a
  perception layer would centralise it (line-of-sight via
  BVH raycasts, hearing radius events). Pairs with the
  recently-shipped BT.
- **Material editor / Niagara analogues** — UE has graph editors
  for materials and particles. PS1Godot's PS1MaterialMetadata
  is a flat property list; a tiny graph editor for material
  variants (different CLUTs per zone, palette swaps via
  selector) might pay off. Smaller scope than UE's Material
  Editor — most PS1 materials are simple.

### Recommended next-session opening
Walk through Phase A first (smoke test plugin loaded). If A passes,
do B (regression check) and ask the user what to verify next OR
which Option (1/2/3 above) to start.

---

## File index of this session's net additions

Created:
- `addons/ps1godot/graph/` — already had PS1Graph framework; this
  session added compile-time chase, Disabled, kind metadata
- `addons/ps1godot/ui/PS1DoctorDock.cs`
- `addons/ps1godot/ui/PS1QuestJournalDock.cs`
- `addons/ps1godot/ui/PS1GraphFindDock.cs`
- `addons/ps1godot/ui/PS1ReferenceViewerDock.cs`
- `addons/ps1godot/ui/PS1AudioClipPreviewGenerator.cs`
- `addons/ps1godot/ui/PS1ToastNotifier.cs`
- `addons/ps1godot/ui/PS1LuaReplDock.cs`
- `addons/ps1godot/PS1ProjectSettings.cs`
- `docs/ps1graph-dialogue-authoring.md` *(updated)*
- `docs/ps1graph-fsm-authoring.md` *(updated)*
- `docs/ps1graph-quest-authoring.md` *(updated)*
- `docs/ps1graph-bt-authoring.md`
- `docs/ps1graph-ue-blueprint-port-plan.md`
- `docs/ps1godot-ue5-editor-port-plan.md`

Modified runtime:
- `psxsplash-main/src/lua.{h,cpp}` — FSM.new, Quest.new, BT.new
  embedded helpers + TryRepl
- `psxsplash-main/src/main.cpp` — REPL poll alongside hot-swap
- `psxsplash-main/src/dialogue.{hh,cpp}` — D1h/D1i/D1j Line
  extensions (audio, notifies, sub-dialogue stack)

Smoke-test fixtures (local-only, gitignored):
- `godot-ps1/test.{tres,lua}` — dialogue
- `godot-ps1/bot_brain.{tres,lua}` — FSM
- `godot-ps1/village_quest.{tres,lua}` — quest
