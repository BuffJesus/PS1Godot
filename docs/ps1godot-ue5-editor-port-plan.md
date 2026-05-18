# PS1Godot ← UE 5.7 editor port plan (beyond Blueprint)

Companion to [`ps1graph-ue-blueprint-port-plan.md`](ps1graph-ue-blueprint-port-plan.md),
which covered Blueprint node UX. This doc mines the rest of the UE
editor for patterns worth porting — asset browser, validation
framework, sequencer, etc. — and keeps the "stays Godot-native"
constraint front-and-centre.

The bar: every pick uses Godot's existing extension points
(`EditorPlugin`, `EditorImportPlugin`, `EditorResourcePreviewGenerator`,
`AddCustomType`, project settings registration). Nothing that
requires re-implementing Godot infrastructure that already exists.

Sampled UE surfaces:
- `Editor/ContentBrowser` — asset thumbnails, type filters, right-
  click actions
- `Editor/AssetTools` — asset creation templates
- `Editor/DataValidation` — pre-cook validation framework
- `Editor/Sequencer` — cinematic timeline
- `Editor/UnrealEd` (Notification framework, Output Log filters)
- `DeveloperTools/CommandConsole`
- `Editor/AIGraph` (Behavior Tree)

## Picks (priority order)

### 1. Custom resource thumbnails — small

**What:** Implement `EditorResourcePreviewGenerator` for PS1Texture
(render the actual pixel data, indicate alpha-cutout via checker
overlay), PS1AudioClip (mini waveform + route tag), PS1MeshInstance
(silhouette + tri count overlay).

**Why fits:** UE Content Browser thumbnails let authors scan an asset
folder and spot the wrong-bpp texture / oversized audio at a glance.
Today PS1AudioClip / PS1MeshInstance are nondescript Resource icons in
the FileSystem dock — the user has to open each one to see what it is.

**Scope:** Three preview generators (~80 lines each); register in
PS1GodotPlugin via `EditorInterface.GetResourcePreviewer().AddPreviewGenerator()`.

**Why might not fit:** Godot's preview cache is sticky — if the
generator gets a bug, stale thumbnails persist across project loads
and the user has to nuke `.godot/imported/`. Worth a "Regenerate
PS1Godot Thumbnails" menu entry as part of the slice.

### 2. Asset Reference Viewer — medium

**What:** "Where is this used?" — pick a `PS1AudioClip` /
`PS1Texture` / mesh in the FileSystem and a dock lists every
`PS1Scene` / `PS1UICanvas` / `PS1MusicSequence` that references it,
click-to-jump. UE's Reference Viewer pattern.

**Why fits:** Audio clip names + texture paths are scattered across
scene `.tscn` files, `PS1Scene.AudioClips`, `PS1UIElement.Texture`,
material refs, etc. The Doctor's "single-use atlas candidate"
warning already hints at this; surfacing the inverse ("what uses
me?") makes it actionable. Extends the existing `PS1GraphFindDock`
substring pattern from graph payloads to typed asset references.

**Scope:** New dock, walks `res://` for `.tscn` + `.tres`, scans
for `ExtResource` / `SubResource` entries pointing at the selected
asset's UID. Click → highlight in FileSystem.

**Why might not fit:** UID-based scan misses runtime-resolved
references (Lua `Audio.PlaySfx("name")` calls). Slice 2 can layer
a string-grep pass over user `.lua` for those.

### 3. Toast notifications on F5 — small

**What:** Transient status pops in the editor viewport corner —
green "Export OK (290 KB splashpack / 49% SPU / VRAM over by 41 KB)"
for 3 s on success, red "Export FAILED: <reason>" stays until
dismissed. UE's `FNotificationInfo` / `FSlateNotificationManager`
pattern, implemented as a `PopupPanel` parented to the editor base.

**Why fits:** F5 today prints to the Output log which the user
often has collapsed. Doctor dock shows after-the-fact but the
author has to navigate to it. A toast surfaces the headline
without stealing focus.

**Scope:** Single `PopupPanel` with auto-fade Tween. Hook into the
existing `OnRunOnPsx` exit path + `LastExportSummary.Severity` for
the colour.

**Why might not fit:** Godot's editor doesn't expose the main
viewport's bounds cleanly from a plugin. Workaround: anchor to
the bottom-right of the screen via `Window.GetScreenPosition()`.

### 4. Lua REPL dock — medium

**What:** Bottom-panel dock with a `LineEdit` ("Lua >") + scrollback
`TextEdit`. Send a line → write it to a watched file on disk →
PCdrv-side psxsplash polls the file (already does this for Lua
hot-swap) → executes and writes result to a response file → dock
displays. UE's developer console for running game-side.

**Why fits:** Debugging today requires editing a `.lua` file, F5,
boot PSX, observe. A live REPL turns "is this flag set?" into a
one-line query against the running emulator. Reuses the
`LuaHotSwapWatcher` infrastructure already shipped.

**Scope:** Editor dock + a tiny psxsplash side patch (extend the
hot-swap watcher to also poll `repl_command.lua` + write
`repl_response.txt`). Existing PCdrv plumbing covers transport.

**Why might not fit:** Only works in PCdrv mode (not CD-ROM ISO).
That's already the F5-fast-iteration mode, so fine.

### 5. Behavior Tree graph kind (PS1BTGraph) — medium

**What:** Fifth PS1Graph kind. Nodes: Selector (try children
left-to-right until one succeeds), Sequence (run children
left-to-right until one fails), Decorator (wraps a child with a
condition), Leaf (Lua snippet returning success/fail/running).
UE's Behavior Tree pattern.

**Why fits:** FSM (D3) is good for "state with transitions" — patrol
↔ chase ↔ attack. BT is better for "decide what to do this tick" —
"check player visible → if yes try attack else try patrol." Common
AI authoring split. Reuses the entire PS1Graph framework + a small
runtime `BT.new` helper (~30 lines of Lua mirroring `FSM.new`).

**Scope:** Slice 1 = compiler + dock node kinds + auth-doc.
Slice 2 = `BT.new` runtime helper.

**Why might not fit:** AI authoring may not need both FSM and BT
for typical PS1 enemy density (3–6 enemy archetypes); BT shines
for 20+ archetypes with complex decision-making.

### 6. Project Settings → PS1Godot section — small

**What:** Register PS1-specific settings under Godot's
`Project → Project Settings → PS1Godot`. Consolidates: default VRAM
budget warning threshold, default audio sample rate, default
texture bpp, debug-overlay defaults, F5 launcher path overrides.
UE's nested Project Settings.

**Why fits:** These knobs are scattered across `PS1Scene` defaults,
env vars (`GODOT_EXE`, `PCSX_REDUX_EXE`), and hardcoded constants
in `PS1GodotPlugin`. One pane = one source of truth.

**Scope:** `ProjectSettings.AddPropertyInfo` for each knob; readers
fall back to current defaults when unset for backward compatibility.

**Why might not fit:** Settings UI is one-time read on project load
— changes need an editor restart to take effect for some plumbing.
Document this in the per-setting tooltip.

### 7. Tab badges (error count on dock title) — small

**What:** When Doctor has errors / warnings, append "(N)" to the
"PS1 Doctor" bottom-panel tab title. Likewise "PS1 Graph Find"
shows the last hit count "PS1 Graph Find (5)".

**Why fits:** Authors miss the Doctor tab between F5s because the
red glyph is buried inside the panel. Tab-title badge is always
visible.

**Scope:** Each dock exposes a `BadgeText` property; PS1GodotPlugin
updates the tab title on signal. Godot's `BottomPanel` tab title
is mutable via `SetBottomPanelTitle` (verify in 4.7).

**Why might not fit:** `SetBottomPanelTitle` may have been removed
in 4.7-dev; if so, fall back to a custom Label in the dock's first
row (visible when the dock IS open, not the tab — less useful).

### 8. Cinematic Sequencer (mini) — large

**What:** Timeline editor for `PS1Cutscene` resources. Drag
`PS1Animation` clips onto tracks; place dialogue triggers / camera
moves / audio cues at frame X. UE Sequencer's basic shape: a
horizontal timeline with vertical tracks, draggable keyframes,
scrub head.

**Why fits:** Cutscenes today are hand-authored Lua chained off
dialogue. A timeline view makes "play X at frame 60, fade to Y at
frame 120" visual instead of string-typed.

**Scope:** New dock with custom-drawn timeline (`Control._Draw`),
draggable Region items per clip, edit handles per keyframe. Several
hundred lines but self-contained.

**Why might not fit:** PS1 cutscenes are typically short (10–30 s)
— may not justify a dedicated editor over the existing Lua-chain
approach. Defer until cutscene volume hits a pain threshold.

## Rejected

- **UE Marketplace integration.** Godot has its own AssetLib; out of
  scope.
- **Source control integration in editor.** Godot has limited git
  support; PS1Godot would have to either build its own or accept the
  limits. Defer indefinitely — `git` CLI works fine.
- **Pawn / Controller / GameMode split.** This is an architectural
  decomposition, not a port. PS1Player covers the use cases for
  single-player PS1; adding pawn separation now is over-engineering.
- **Localization framework.** Real, useful, big — but a separate
  initiative (text extraction + locale switching at runtime + glyph
  packing into VRAM). Out of scope for this plan.
- **Live game-state inspector.** "Read Persist flags from running
  PCSX-Redux." Cool but requires an editor-side PCdrv reader and
  PSX-side serialization layer. Defer to a dedicated debug-tools
  pass.

## Suggested ordering when work resumes

1. **#1 Thumbnails** — biggest visual win per LOC; immediately
   improves every author's FileSystem dock experience.
2. **#3 Toast notifications** — small, makes F5 feel responsive.
3. **#7 Tab badges** — small, makes Doctor / Find dock state
   ambient.
4. **#6 Project Settings page** — consolidates knobs; one-time
   refactor that pays off long-term.
5. **#2 Reference Viewer** — biggest debugging value once asset
   counts grow; build after thumbnails so the row icons match.
6. **#5 Behavior Tree graph kind** — extends the proven PS1Graph
   framework; pure additive.
7. **#4 Lua REPL** — depends on a small psxsplash patch; ship after
   the editor-side picks are settled.
8. **#8 Cinematic Sequencer** — defer until cutscene authoring
   volume justifies it.
