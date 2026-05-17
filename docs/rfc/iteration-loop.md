# Iteration loop — design + patch

Closes several roadmap items that all point at the same workflow
gap:

> - [ ] F5-to-play → PCSX-Redux debugger attach (Phase 3)
> — `ROADMAP.md`

> - [ ] Lua hot-swap: re-exporting a single `.lua` while the
>       emulator is running re-uploads only that bytecode via
>       PCdrv.
> — `ROADMAP.md`

> ### 2. Hot-reload of scene data
> **Why we care.** Phase 3 is pitched as "F5-to-play" with
> sub-second feedback. A full-scene reload through PCdrv is
> still slower than "nudge texture, see result immediately"
> would be.
> — `docs/psxsplash-improvements.md`

These three are facets of the same thing: the edit-test cycle.
Currently authors save, hit Run-on-PSX, wait ~15 seconds for the
emulator to relaunch, observe. Target: save in Godot, see the
change on PSX in 1–2 seconds.

Drop this file at `docs/iteration-loop.md`.

## Goal

Three reload tiers, picked automatically based on what changed:

| Tier | Trigger | Time | What reloads |
| --- | --- | --- | --- |
| 1 — Lua hot-swap | `.lua` file saved | ~200 ms | Just that script's bytecode |
| 2 — Scene hot-reload | Any non-Lua scene asset changed | ~1–2 s | Splashpack + sidecars, no re-launch |
| 3 — Full restart | Code/runtime/format change | ~15 s | Emulator relaunch (current path) |

Author hits save. The editor figures out which tier applies and
pushes the change. The runtime applies it without losing the
player's position, the current chunk, or any non-serializable
runtime state.

Non-goal: live shader recompile. The runtime doesn't compile
shaders — the PS1 GPU isn't programmable. The "shader" is the
vertex-jitter+affine-UV behavior baked into the rendering loop;
nothing to reload.

## Why this matters

Iteration speed dominates productivity. Authors making 20
small tweaks per session save 15 minutes per tier upgrade:

- Lua-only tier: 200 ms × 20 = 4 s of waiting vs current 15 s ×
  20 = 5 minutes.
- Scene-asset tier: 1.5 s × 20 = 30 s vs the same 5 minutes.

That's the difference between flow and frustration. The
optimization reference doesn't say it explicitly, but every
PS1-era dev who's written a postmortem mentions iteration speed
as the thing that mattered most.

## What's in place

- **Run-on-PSX button** in the dock (`PS1GodotDock.cs`). Runs
  the cmd-script pipeline: export → build (if needed) → launch.
- **`scripts/launch-emulator.cmd`** spins up PCSX-Redux with
  `-pcdrv` flag, which exposes the host filesystem to the
  emulated PS1. The exporter writes to `godot-ps1/build/`;
  the runtime reads from there.
- **PCdrv passthrough** means file changes on host show up
  instantly to the emulated PS1 — there's no ISO bake step
  during iteration. Just relaunch the runtime to pick up new
  files.
- **`SceneManager::RequestSceneLoad`** can swap scenes at
  runtime — used today for explicit `Scene.Load(N)` calls.
  Could be repurposed for hot-reload.
- **Lua VM is rebuilt on scene load.** Scripts re-run their
  `onCreate` from scratch — there's no live state to preserve
  for script changes.

So the building blocks exist. The work is in the editor side
(detecting changes, triggering reloads, displaying status) and
in two small additions to the runtime (Lua hot-swap, scene
hot-reload signal).

## Design

### Tier 1 — Lua hot-swap

Cheapest tier. Only Lua source changed; runtime everything else
stays.

**Detection.** Godot fires a signal when a `.lua` file is saved
inside the project (`EditorFileSystem.ResourcesReimported`).
The dock subscribes; on signal, it kicks off the hot-swap path
instead of full export.

**Compilation.** The exporter has a Lua compilation step today
(`luac_psx` via `PS1LuaScriptLanguage`). Tier 1 runs just that
step for the affected file, producing fresh bytecode.

**Push path.** PCdrv lets the runtime read from host. The editor
writes the new bytecode to a sentinel file:
`godot-ps1/build/.hotswap/<script_name>.luac`. A new runtime
hook polls this directory once per frame (cheap — single
directory listing through PCdrv every 30 frames):

```cpp
// In SceneManager::Update or a sibling per-frame hook:
void CheckLuaHotswap() {
    if (m_frameCount % 30 != 0) return;
    
    // Walk .hotswap/ directory. For each .luac:
    char path[64];
    while (PCDRV::NextHotswapFile(path)) {
        const char* scriptName = ExtractScriptName(path);
        int luaIdx = FindLuaFileIndex(scriptName);
        if (luaIdx < 0) {
            DEBUG_LOG("Hotswap: unknown script %s", scriptName);
            PCDRV::DeleteHotswapFile(path);
            continue;
        }
        // Replace the bytecode in m_luaFiles[luaIdx].
        m_luaVm->ReloadScript(luaIdx, ReadFile(path));
        DEBUG_LOG("Hotswap: reloaded %s", scriptName);
        PCDRV::DeleteHotswapFile(path);
    }
}
```

**Script-level reload.** The Lua VM reloads only the specific
script. Each GameObject's per-instance state (the `self` table)
survives — the script's `onUpdate` next frame uses the new code
with the same state. Authors get hot-reload of behavior while
preserving "the boss is at half-health, mid-fight."

**Limits.** Hot-swap doesn't run a new `onCreate`. Variables
declared at chunk-level in the script (above the function
definitions) keep their old values from the original load. The
author who needs to re-initialize calls `Scene.Reload()`
explicitly.

### Tier 2 — Scene hot-reload

Mid-tier. Non-Lua scene content changed: a mesh, a texture, an
audio clip, a UI canvas.

**Detection.** Same `ResourcesReimported` signal but for
non-`.lua` paths. The dock distinguishes: any non-Lua change
triggers Tier 2 unless a runtime/format-affecting file changed
(then Tier 3).

**Export.** Run the full exporter — produces fresh
`scene_N.splashpack + .vram + .spu` triplets in `build/`.

**Hot-reload signal.** Same sentinel-file pattern as Tier 1.
Write `build/.hotswap/.reload-scene-<index>`. Runtime checks
the directory on the same poll cadence; when it finds a
reload sentinel:

1. **Pause the active scene.** Stop physics, freeze AI,
   cancel any in-flight animations.
2. **Walk the on-disk splashpack and diff against the loaded
   scene.** What sections changed?
3. **Selective re-upload.** Textures and CLUTs that changed
   re-upload to VRAM. Audio clips that changed re-DMA into
   SPU RAM. GameObjects with changed mesh data have their
   `polygons` pointer + `polyCount` updated in place.
   Unchanged objects keep their existing state.
4. **Re-register changed Lua scripts** through the Tier 1
   path.
5. **Resume.** Player position, camera, current chunk, save
   state — all preserved. Visual changes apply immediately.

**Critical: state preservation rules.**

Objects identified by name across the reload keep their state.
A `PS1MeshInstance` named "RedCube" that had `self.health = 50`
before reload still has `self.health = 50` after, even if its
mesh data changed.

New objects get standard `onCreate`. Deleted objects fire
`onDelete` and free up. Renamed objects look like delete + new
to the diffing system — this is a documented gotcha; authors
keep names stable across reloads.

**Diff scope.** First implementation diffs at the
section level — "did the textures change as a block." Finer
diffing (per-texture, per-object) is Phase 4+ work. Section-level
catches the common cases:

- Edit one texture → re-upload all textures (fast).
- Edit one mesh → re-upload all meshes (still fast — meshes
  are KB-scale).
- Edit Lua → use Tier 1 path.

### Tier 3 — Full restart

The fallback. Anything that changes the runtime contract goes
through full restart:

- C++ runtime code changed.
- Splashpack format version bumped.
- GDExtension binary changed.
- `psxsplash.ps-exe` itself changed.

The dock detects these by comparing file mtimes against the
last successful launch. If a runtime file is newer, it
schedules a full relaunch instead of attempting hot-reload.

This is the current default path. Stage 0 is essentially "don't
break what works."

### File watcher

Godot's `EditorFileSystem` already watches the project's
filesystem. Hook into its existing signals:

```csharp
// In PS1GodotPlugin._EnterTree:
var fs = EditorInterface.Singleton.GetResourceFilesystem();
fs.ResourcesReimported += OnResourcesReimported;

private void OnResourcesReimported(string[] paths)
{
    var tier = ClassifyChange(paths);
    switch (tier)
    {
        case ReloadTier.LuaHotSwap:
            DoLuaHotswap(paths);  // ~200 ms
            break;
        case ReloadTier.SceneHotReload:
            DoSceneHotReload();   // ~1-2 s
            break;
        case ReloadTier.FullRestart:
            DoFullRestart();      // ~15 s
            break;
    }
}

private ReloadTier ClassifyChange(string[] paths)
{
    bool needsFullRestart = paths.Any(p =>
        p.Contains("/psxsplash") ||
        p.Contains("/scripting/") ||
        p.EndsWith(".gdextension"));
    if (needsFullRestart) return ReloadTier.FullRestart;
    
    bool allLua = paths.All(p => p.EndsWith(".lua"));
    return allLua ? ReloadTier.LuaHotSwap : ReloadTier.SceneHotReload;
}
```

Optional gating: a dock toggle "Auto-reload on save" (default
off until Stage 2 is stable). When off, the file watcher
notices changes but doesn't act — authors hit a "Push changes"
button instead.

### Dock UX

Three indicators in the dock:

1. **Reload status line.** "Auto-reload: ON" / "Auto-reload:
   OFF". Click to toggle.
2. **Last reload result.** "Last reload: 312 ms (Lua hot-swap,
   2 scripts)" or "Last reload: 1.8 s (scene hot-reload)" or
   "Last reload failed — see console."
3. **Pending reload indicator.** When a save triggered a reload
   that's in progress: a small spinner + "Reloading…" text.
   Disappears on success.

Error display: if a hot-reload fails (typo in Lua, splashpack
write error, runtime refused), the dock shows the error message
inline with a "Retry" button. Errors don't auto-fallback to
full restart — that would silently mask author mistakes.

### Hardware vs emulator

PCdrv only exists in the emulator. Real-hardware testing skips
all of this and uses the ISO build path (`disc-layout.md`).
That's correct: iteration happens in the emulator, real-hardware
testing is the "final check" that happens occasionally.

For developers who only have hardware (rare — most have an
emulator handy), the iteration loop falls back to Tier 3 always.
Document this as the SetUp.md "no PCdrv available" caveat.

## Implementation stages

Five stages — each shippable, each measurable.

### Stage 1 — Auto-export on save (existing path) ✅ shipped 2026-05-16

Wired in PS1GodotPlugin + PS1GodotDock. Default off.

- `EditorFileSystem.ResourcesReimported` subscription in
  `PS1GodotPlugin._EnterTree`.
- "Auto-run on save" CheckBox in the dock; signal flips the
  plugin's `_autoRunOnSave` field.
- Re-entrancy guard (`_pipelineInProgress`) prevents a second
  save firing while an export is in flight.
- Filter skips `.uid`, `.import`, and anything under `build/`
  so the pipeline doesn't feed back into itself.
- Toggle state lives in-memory only for now (no EditorSettings
  persistence — defer until the toggle proves stable).

Stages 2 (Lua hot-swap), 3 (scene hot-reload), 4 (UX polish:
last-reload timing, retry button), and 5 (selective asset
diff) are deferred — each touches the runtime and is its own
session.

### Stage 2 — Lua hot-swap

The big quality-of-life jump for script work.

- Hotswap sentinel directory and PCdrv polling hook (runtime).
- Editor writes recompiled `.luac` to the sentinel directory on
  Lua save.
- Runtime applies the swap, preserves per-instance state.

Time savings: ~14 s per iteration on Lua changes (~200 ms vs
~15 s).

### Stage 3 — Scene hot-reload (section-level diff)

The medium-lift one. Touches both editor and runtime.

- Editor produces a "scene reload" sentinel file after
  successful export.
- Runtime applies the new splashpack on top of existing scene:
  pause, diff sections, re-upload changed sections, resume.
- Name-based state preservation for unchanged GameObjects.

Time savings: ~13 s per iteration on scene-asset changes.

### Stage 4 — Editor UX polish

- Reload status line.
- "Last reload" timing indicator.
- Pending-reload spinner.
- Failure display with retry.

No new functionality, but turns a "what just happened" experience
into a "I can see what just happened" one.

### Stage 5 — Selective asset hot-reload

Finer diffing for faster reloads when only a small change
happened.

- Per-texture, per-mesh, per-audio diff at byte level.
- Re-upload only the changed assets.
- Most reloads become 200–500 ms instead of 1–2 s.

This is the "edit one texture, see it in 200 ms" experience
the optimization reference's iteration-speed bullet points at.

## Open questions / tradeoffs

**What state survives a hot-reload?** Lua per-instance state
(the `self` table for each GameObject) survives. Player
position and current scene/chunk survive. Audio state mid-clip
gets cut (clip replays from the start when its source changed
— the alternative, in-place editing of an ADPCM stream that's
mid-playback, has too many edge cases). Cutscenes in-flight
get cancelled and the scene resumes without them.

**Hotswap directory cleanup.** Sentinel files build up if the
runtime polls poorly or the emulator crashes. Mitigation: at
each emulator launch, the launcher script wipes the
`.hotswap/` directory. Stale files don't accumulate across
sessions.

**Lua hot-swap and state-machine corruption.** A script edits
its FSM state in mid-run; hot-swap replaces the script while
the state machine is in the middle of a transition. Edge case.
Mitigation: state machine transitions become atomic — the FSM
finishes the current transition before applying the swap, OR
the swap waits for an idle frame. Document, then test the
behavior with the standard demo's combat showcase.

**Polling cadence.** Polling the PCdrv directory once every 30
frames (1 Hz) means hot-swap latency is up to 1 second.
Faster polling = more PCdrv calls = more emulator overhead.
Tunable per-project; 30 frames is the sane default. Authors
who want sub-second feedback can drop to 15 (twice per second).

**Hot-reload during cutscene playback.** Cutscenes lock the
camera and player. Reloading mid-cutscene cancels the cutscene
and snaps the camera back to player-following. Reasonable
behavior, but worth a one-line documentation note.

**File-system race condition.** Editor writes the new splashpack
while the runtime is mid-read. Mitigation: editor writes to
`scene_N.splashpack.new`, then atomic-renames to
`scene_N.splashpack`. Sentinel file is written after the
rename completes, so the runtime never sees a half-written
file.

**Audio mid-stream behavior.** Background music sequencer is
running. Texture change triggers reload. What happens to the
music? Two options: continue uninterrupted (more pleasant),
or restart from current beat (simpler). Default to continue —
the music sequencer's state is preserved across non-audio
hot-reloads.

**State preservation gotchas in author code.** A common
mistake: declaring per-frame state as a chunk-level local in
the Lua script. After hot-reload that local resets, surprising
the author. Document this; the right pattern is `self.foo`
not `local foo` at chunk scope.

**Hot-reload of the player's mesh.** Reloading the player's
mesh while the player is moving = visible pop. Authors expect
this; rare enough not to optimize. The alternative would be
"freeze player input during reload" which feels worse than the
pop.

**Failure recovery.** Hot-reload fails (the new splashpack is
invalid). What's the runtime in? Options: keep the previous
scene running (no change visible) or crash to error screen.
Default to "keep previous running" — the dock shows the error,
author fixes and re-saves, next save triggers another reload.

**Reload across format-version mismatch.** Author changes the
splashpack format mid-session (rare but possible during
exporter development). New splashpack has version N+1, runtime
expects N. Behavior: hot-reload refuses with an error
"format mismatch — restart needed." The full-restart path
picks up the new runtime that supports the new version.

## Suggested entries

### For `docs/psxsplash-improvements.md`

Replace the existing "Hot-reload of scene data" entry (#2)
with the expanded design pointing at this doc. Add new entries:

> ### N+M. Lua bytecode hot-swap via PCdrv
>
> **Problem.** Lua source changes require a full scene reload
> today — the runtime has no mechanism to replace a single
> script's bytecode while preserving other state.
>
> **Proposed direction.** Per-frame polling of a
> `.hotswap/` sentinel directory; on detection, atomically
> replace the matching `LuaFile`'s bytecode and continue.
> Per-GameObject state (the `self` table) survives the swap.
> See `docs/iteration-loop.md` Tier 1.
>
> **Status.** Filed.

> ### N+M+1. Scene hot-reload via splashpack section diff
>
> **Problem.** Replacing scene contents today requires a full
> emulator relaunch. Authors iterating on textures or meshes
> wait 15 seconds per change.
>
> **Proposed direction.** Sentinel-file trigger to reload a
> running scene's splashpack on top of existing state. Section-
> level diff (textures / audio / meshes / scripts); selective
> re-upload of changed sections. Name-based GameObject identity
> preserves per-instance state across reload. See
> `docs/iteration-loop.md` Tier 2 / Stage 3.
>
> **Status.** Filed.

### For `ROADMAP.md`

Replace the "F5-to-play" + "Lua hot-swap" bullets with:

> - [ ] **Iteration loop — three reload tiers.** Auto-detect
>       what changed and apply the cheapest reload that
>       preserves runtime state. Lua hot-swap (~200 ms), scene
>       hot-reload (~1–2 s), full restart (~15 s, current
>       behavior). Composed pipeline: file watcher in editor →
>       PCdrv sentinel files → runtime polling → in-place
>       update. Full design: `docs/iteration-loop.md`.

## Changelog

- `2026-05-11` — Document created. Ninth patch doc in the
  series. Consolidates three roadmap items (F5-to-play, Lua
  hot-swap, scene hot-reload). Pairs with `debugging.md` for
  the "what happens when a reload breaks" story.
