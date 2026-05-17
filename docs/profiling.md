# Profiling environment — design + patch

Closes the design gap behind:

> ### 5. Lua observability / per-frame profiler
> **Problem.** `profiler.cpp` / `profiler.hh` exist but expose
> limited info. No per-Lua-function timing, no per-script frame
> budget overlay. Optimization is guesswork.
> — `docs/psxsplash-improvements.md`

And the Phase 2.5 bullet:

> - [ ] `Debug.Profile("chunk-rebuild", fn)` — per-frame timing
>       ring buffer surfaced in the dev overlay. Essential once
>       Lua starts doing real work.
> — `ROADMAP.md`

The optimization reference's whole thesis is "PS1 budgets don't
scale; you have to know what's spending them." Without
profiling, authors guess. This doc designs the measurement
plumbing — what data the runtime collects, how it gets to the
editor, what the dock does with it.

Drop this file at `docs/profiling.md`.

## Goal

Open the dock's Profile tab. See a per-frame breakdown:

```
Frame 1234 (29.8 fps, 33.6 ms)
  Scene update            12.1 ms
    Lua callbacks          8.4 ms  (66 objects)
      onUpdate             7.9 ms  ← top sample: enemy_ai.lua (1.2 ms × 12 calls)
      onTrigger            0.5 ms
    Physics                1.8 ms
    Animation              1.9 ms
  Render                  16.2 ms
    GTE transforms         5.4 ms  (3812 verts)
    OT build               2.1 ms  (3094 entries / 4096 limit)
    GPU submit             4.7 ms
    VBlank wait            4.0 ms
  Audio update             1.1 ms
  Misc                     4.2 ms
```

Drill down: click `onUpdate` to see which scripts contributed.
Click `enemy_ai.lua` to see per-function timing. The optimizer
becomes "looking at numbers" instead of "looking at the screen
hoping to see lag."

Non-goal: a Tracy-quality flame graph. PS1's RAM is the
constraint; a ring buffer of last 60 frames is the budget. For
deep dives, authors run the host-mode tests
(`host-mode-testing.md`) or export captures from the runtime.

## What's in place

- **`profiler.cpp` / `profiler.hh`** in psxsplash. Provides a
  basic `Profiler::Section` RAII timer that accumulates
  microseconds per named section. Used inside the runtime for
  a small set of phases but not exposed to scripts and not
  surfaced to the editor.
- **`PSXSPLASH_FPSOVERLAY`** compile flag enables an on-screen
  FPS counter. Useful at-a-glance but no detail.
- **`PSXSPLASH_MEMOVERLAY`** compile flag enables a runtime
  heap usage overlay. Same shape — useful but coarse.
- **PCdrv** is the data-pipe for non-real-time reporting
  (already used by the debugging pipeline).
- **`Profiler::startSection / endSection`** counts cycles via
  the PS1's hardware cycle counter — accurate to a few
  cycles. The accuracy primitive is already correct.

So the timing primitive exists and works. The work is in
expanding it to cover more of the runtime, binding it to Lua,
and getting the data into the dock.

## Design

Four data streams: per-frame phase timings, per-callback Lua
timings, ring buffer of recent frames, and on-demand deep
captures. All flow through the same PCdrv pipeline introduced in
`debugging.md`.

### Stream 1 — Per-frame phase timings

The runtime maintains a fixed-shape struct of phase microsecond
counts, updated each frame. The phases reflect what the
existing per-frame loop already does:

```cpp
struct FrameProfile {
    uint16_t frameIndex;
    uint16_t totalMicros;
    
    // Scene update
    uint16_t sceneUpdateMicros;
    uint16_t luaCallbacksMicros;
    uint16_t physicsMicros;
    uint16_t animationMicros;
    uint16_t tierEvalMicros;
    
    // Render
    uint16_t bvhCullMicros;
    uint16_t gteTransformMicros;
    uint16_t otBuildMicros;
    uint16_t gpuSubmitMicros;
    uint16_t vblankWaitMicros;
    
    // Audio / system
    uint16_t audioMicros;
    uint16_t miscMicros;
    
    // Counts (not microseconds — discrete metrics)
    uint16_t visibleObjectCount;
    uint16_t visibleTriCount;
    uint16_t otEntryCount;
    uint16_t activeLuaCallbacks;
};
static_assert(sizeof(FrameProfile) == 36, "FrameProfile must fit");
```

36 bytes per frame × 60 frames of ring buffer = 2.2 KB total.
Negligible.

Each phase uses the existing `Profiler::Section` RAII pattern:

```cpp
// In SceneManager::Update:
{
    Profiler::Section s(m_profile.sceneUpdateMicros);
    {
        Profiler::Section s2(m_profile.luaCallbacksMicros);
        DispatchAllLuaCallbacks();
    }
    {
        Profiler::Section s2(m_profile.physicsMicros);
        UpdatePhysics();
    }
    // ... etc
}
```

The profiler is always-on in dev builds (PCdrv-enabled). Cost
per section: one `mfc0` instruction (cycle counter read) at
entry, one at exit, one subtract. ~10 cycles per section. Even
20 sections per frame is ~200 cycles — negligible against a
~1 million cycle frame budget.

Release builds (CD-ROM target) compile the profiler out via
`PSXSPLASH_PROFILE=0`.

### Stream 2 — Per-callback Lua timings

Each Lua callback invocation gets timed. Aggregate by callback
type + source script:

```cpp
struct LuaCallbackProfile {
    uint8_t  scriptIndex;       // index into m_luaFiles
    uint8_t  callbackType;      // onUpdate=0, onCollision=1, ...
    uint16_t callCount;
    uint32_t totalMicros;       // sum across all invocations this frame
};
```

The dispatcher wraps each call:

```cpp
void DispatchOnUpdate(GameObject* obj) {
    if (obj->isErrored()) return;
    if (!ShouldDispatch(obj, frameCount)) return;
    
    Profiler::ScopedLuaTime t(obj->luaFileIndex, CB_ON_UPDATE);
    InvokeLuaCallback(obj, "onUpdate");
}
```

`ScopedLuaTime` accumulates into a per-(script, callback) entry.
At end of frame, the runtime sorts by total micros and reports
the top 8 to the ring buffer. The "top 8" cap keeps the per-frame
report bounded; full distribution lives in deep-capture mode
(Stream 4).

The mapping script-index → script-name lives in the splashpack's
name table — the dock resolves it on display.

### Stream 3 — Frame ring buffer

The last 60 frames of FrameProfile + top-8 LuaCallbackProfiles
sit in a runtime ring buffer. On each frame, the runtime writes
the latest frame's record. The dock pulls the ring buffer
periodically (every 250 ms is plenty) via PCdrv.

Why 60 frames specifically: that's two seconds at 30 fps. Long
enough to see the spike when something hitches, short enough to
hold in 2 KB.

The dock displays the last frame's breakdown as the headline,
with a sparkline of the past 60 frames per metric for trend
context. Click a sparkline → freeze and inspect that frame's
breakdown.

### Stream 4 — Deep-capture mode

For when "the last 60 frames" isn't enough — replicating a bug
that happens over 5 seconds, profiling a chunk-load that takes
30 frames, etc.

Author triggers capture from Lua:

```lua
-- Start recording. Continues until Profile.StopCapture or N
-- frames pass (whichever first).
Profile.StartCapture("dungeon-entry", 300)  -- 10 seconds at 30 fps

-- ... gameplay continues ...

Profile.StopCapture()  -- ends early if desired
```

While capturing, the runtime writes a full per-frame record
(every callback invocation timed individually, not aggregated)
to a separate PCdrv file: `build/.debug/captures/<name>.bin`.

Captures use a compact binary format to fit large recordings:

```
[CaptureHeader 16 B]
[FrameRecord 36 B] × N
[CallbackRecord 12 B] × M  (variable per frame)
```

A 300-frame capture with ~50 callbacks per frame = ~200 KB.
PCdrv handles it; the dock loads on demand.

The dock's "Captures" tab lists saved captures with a click-to-
load preview. Loaded captures show:

- Full frame-by-frame timeline.
- Per-callback summary across the capture.
- Top spikes (frames where total > N standard deviations from
  mean).
- Export as CSV for spreadsheet analysis.

### Stream 5 — On-screen overlay (existing extension)

The current `PSXSPLASH_FPSOVERLAY` flag puts FPS in the top-left.
Extend to optionally show key metrics from FrameProfile:

```
fps: 28.4   tri: 3812/5000   ot: 3094/4096
   lua: 8.4ms   render: 16.2ms
```

The "key metrics" set is small but the highest-leverage view:
authors looking at PSX see at a glance whether they're over
budget. Configurable per-line via a runtime flag (don't show
all when scene is normal; ramp up when something's hot).

Toggled via a new `Debug.SetOverlay(level)` Lua API: 0 = off,
1 = FPS only, 2 = FPS + counts, 3 = full breakdown.

### Dock display

A new tab: **Profile**. Three sub-views:

**A. Latest frame.** The headline. Shows the most-recent frame
record as the breakdown above. Auto-updates at 4 Hz. When
paused (a "Pause" button), stays on the displayed frame.

**B. History sparklines.** A grid of 60-frame sparkline charts,
one per metric (total ms, Lua ms, render ms, vblank ms,
visible tri count, OT entries). Click a sparkline → expand to
full chart with axes.

**C. Top callbacks.** A table: callback type, source script,
total time this frame, call count, avg per call. Sortable.

**D. Captures.** List of saved captures (deep-mode). Click to
load and analyze. "Start capture" button initiates the
runtime's capture mode for N seconds.

### `Debug.Profile` Lua wrapper

The roadmap's `Debug.Profile("name", fn)` is a script-side
convenience that creates a one-off named section:

```lua
function onUpdate(self, dt)
    Debug.Profile("ai_path", function()
        ComputePathToPlayer(self)
    end)
    
    Debug.Profile("ai_attack", function()
        TryAttack(self)
    end)
end
```

Named sections show up in the per-callback summary alongside
the implicit `onUpdate` total. Authors profile specific Lua
hotspots without rewriting their script.

Implementation: `Debug.Profile` is just a Lua-side helper that
calls `Profile.Push(name)` + `Profile.Pop()` around `fn()`. The
runtime maintains a stack of active sections; pop accumulates
into a per-section bucket.

## Implementation stages

Six stages. Stage 1 ships the foundation; later stages add
sophistication and richer surfaces.

### Stage 1 — Frame phase timings

The smallest standalone win.

- Extend `profiler.cpp` with the FrameProfile struct.
- Instrument each phase in the per-frame loop with
  `Profiler::Section`.
- Maintain the 60-frame ring buffer in runtime RAM.
- Expose the latest frame via PCdrv read (single file
  rewritten each frame).
- Dock display: minimal "Profile" tab showing latest frame.

Verifiable: the dock shows live frame timings updating at 4 Hz;
expected values reasonable (~30 fps demo shows ~33 ms).

### Stage 2 — Per-callback Lua timings

- `ScopedLuaTime` wrapper in the dispatcher.
- Per-(script, callback) aggregation per frame.
- Top-8 sort at frame end.
- Dock display: "Top callbacks" table.

### Stage 3 — Sparklines + history

- Dock pulls the full ring buffer (not just latest frame).
- 60-frame sparkline charts per metric.
- Click-to-expand-full-chart.

### Stage 4 — Deep-capture mode

- `Profile.StartCapture` / `StopCapture` Lua API.
- Binary capture format + writer.
- Dock "Captures" tab with load + analyze.
- CSV export.

### Stage 5 — `Debug.Profile` wrapper + named sections

- `Profile.Push` / `Profile.Pop` runtime API.
- `Debug.Profile(name, fn)` Lua helper.
- Named sections in the per-callback summary.

### Stage 6 — On-screen overlay enhancement

- Extend FPSOVERLAY to show configurable lines.
- `Debug.SetOverlay(level)` Lua API.
- Per-level layout.

## What to instrument

The defaults already-relevant for instrumentation are the
hottest paths. From the renderer:

- **BVH cull pass.** `cullFrustum` traversal cost. Scales with
  scene complexity.
- **GTE transforms.** `setupObjectTransform` + per-triangle
  `rtps`. Scales with visible triangles.
- **OT build.** Inserting primitives into ordering table
  buckets. Scales with visible tri count.
- **GPU submit.** DMA the OT chain to the GPU. Mostly fixed
  cost per frame.
- **VBlank wait.** If positive, you're under budget. If zero,
  you're at frame ceiling.

From the scene update:

- **Lua callbacks** (per type: update / tier-change / trigger
  / interact / cutscene).
- **Physics** (collision resolution, dynamic-mover updates).
- **Animation** (skinned mesh bone updates, anim track
  evaluation).
- **Tier evaluation** (per-frame distance checks from
  `tiered-simulation.md`).

From the system:

- **Audio update** (music sequencer tick, voice management).
- **Memory card I/O** (when active — rare).
- **PCdrv overhead** (profiler reporting itself; small but
  visible).

Together these account for >95% of frame time. The "Misc"
bucket catches the rest.

## Open questions / tradeoffs

**Profiler self-overhead.** The Profile pipeline costs cycles
to run. Hot estimate: ~5% of frame time when fully on (60+
sections, callback wrapping, PCdrv write). Mitigation:

- Section overhead is ~10 cycles each — fine for top-level
  phases (~20 of them), expensive for per-callback wrapping at
  scale (100+ callbacks).
- Callback wrapping is opt-in per dispatcher pass — flag bit on
  Profile state. `Profile.SetCallbackTiming(false)` from Lua
  turns it off when not needed.
- PCdrv write happens once per dock-pull (every 250 ms), not
  every frame.

**RAM cost.** 2 KB for the ring buffer is the steady state.
Deep captures live on PCdrv (host filesystem) so don't consume
PS1 RAM. Tolerable.

**Production builds.** `PSXSPLASH_PROFILE=0` in the Makefile
strips all profiling instrumentation. Same pattern as the
debug pipeline. CD-ROM targets default to off.

**Real-hardware profiling.** PCdrv doesn't exist on hardware.
On-screen overlay (Stream 5) is the only profiling surface
there. The full dock-based view is emulator-only. Document
this.

**Frame-time variance.** The PS1 cycle counter is precise but
not consistent with real-world ms (depends on CPU clock,
emulator timing accuracy, etc.). Convert to ms in the dock
display using a calibration value (assumed 33.86 MHz CPU
clock for NTSC). Authors targeting PAL adjust.

**Capture file size for long sessions.** A 5-minute capture
could be ~10 MB. PCdrv handles it but the dock load gets
slow. Mitigation: chunked capture format with index, dock
streams from the file rather than loading whole. Phase 4
work; not blocking.

**What about GPU profiling?** PS1 GPU doesn't expose
register-level timing. We can measure "DMA submit time" but
not "actual GPU draw time" — those are coupled by VBlank.
"VBlank wait" is the indirect signal: low VBlank wait = GPU
busy. Document this.

**Aggregation across scripts.** If 20 enemies all run the
same `enemy_ai.lua` script, do we report 20 separate entries
or aggregate? Aggregate by `(scriptIndex, callbackType)` —
the "this script's onUpdate cost 8 ms across 20 calls" is
the useful answer. Per-object breakdown exists in deep
captures for the rare case.

**Profile overhead consistency.** With profile on, the runtime
runs slower. Authors see "Profile shows 28 fps" and worry it's
the real speed. Mitigation: a "subtract profile overhead"
estimate, computed by running a calibration frame at startup
with all sections enabled but no work. Resulting overhead-ms
is displayed in the dock and subtracted from totals.

**Comparison across runs.** "Did my latest change make
things faster?" needs run-to-run comparison. The capture
export to CSV addresses this — authors run a captured
scenario before and after, diff the CSVs. Dock could show
this directly with two loaded captures side-by-side (Phase
4).

**Histogram view.** Some workloads have bimodal distributions
("most frames 30 fps, occasional spikes to 15 fps") that
average values hide. A histogram of frame times across a
capture surfaces this. Stage 4+ addition.

## Suggested entries

### For `docs/psxsplash-improvements.md`

Update the existing #5 entry with the design pointer; add:

> ### N+M. Per-callback Lua timing in dispatcher
>
> **Problem.** Lua-side optimization is guesswork. Authors
> can't tell which callback dominates a frame budget, or which
> script's `onUpdate` is the hotspot.
>
> **Proposed direction.** Wrap each Lua callback invocation
> with a profile section; aggregate per-(script, callback)
> per frame; surface top-N to a runtime ring buffer that the
> dock pulls. Cost: ~5% frame time with full instrumentation
> on; opt-out via runtime flag for steady-state runs. Full
> design: `docs/profiling.md`.
>
> **Status.** Filed.

### For `ROADMAP.md`

> - [ ] **Profiling environment — frame phase + Lua timing.**
>       Extend `profiler.cpp` with 36-byte FrameProfile struct
>       covering all main phases. Wrap Lua callbacks for
>       per-script/per-type timing. 60-frame ring buffer +
>       deep-capture mode for offline analysis. Dock surface
>       (Profile tab) with live latest-frame breakdown, history
>       sparklines, top-callbacks table, and capture management.
>       `Debug.Profile(name, fn)` Lua helper for inline
>       sections. Full design: `docs/profiling.md`.

## Changelog

- `2026-05-11` — Document created. Eleventh patch doc in the
  series. Closes the design gap behind psxsplash-improvements
  #5 (Lua observability) and the roadmap's `Debug.Profile`
  bullet. Pairs with `debugging.md` (shared PCdrv reporting),
  `host-mode-testing.md` (offline analysis runs there).
