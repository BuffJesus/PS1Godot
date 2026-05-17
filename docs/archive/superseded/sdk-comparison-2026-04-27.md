# SDK Feature Comparison — PSYQo · PSn00bSDK · psxsplash · SplashEdit · PS1Godot

**Status (2026-05-17):** Archived snapshot. Feature surfaces of all
five codebases have moved on since the 2026-04-27 capture; treat as
historical evidence, not current state. Preserved for archaeology;
do not extend — regenerate a fresh comparison if needed.

**Generated 2026-04-27.** Compares the feature surfaces of the five
PS1-related codebases vendored at the project root.

> **Verification note:** the initial agent report (Explore subagent)
> flagged several features as "missing from psxsplash" that are
> actually present via the bundled psyqo library
> (`psxsplash-main/third_party/nugget/psyqo/`). Those claims have
> been corrected here. Surviving "missing" flags are spot-verified.

## A. Feature matrix

`Yes (psyqo)` means psxsplash links the feature in via the bundled
PSYQo library — usable from psxsplash code today, just not authored
inside the psxsplash source tree itself.

| Feature | PSYQo | PSn00bSDK | psxsplash | SplashEdit | PS1Godot |
|---|---|---|---|---|---|
| **Rendering** | | | | | |
| Primitive types (tri / quad / sprite / line) | ✓ | ✓ | ✓ (psyqo) | — | via psxsplash |
| Ordering table / z-sort | ✓ | ✓ | ✓ | — | via psxsplash |
| Vertex color (Gouraud) | ✓ | ✓ | ✓ | ✓ | ✓ (bake + writer) |
| GTE matrix transforms | ✓ | ✓ | ✓ | — | via psxsplash |
| Fixed-point math | ✓ (multi-precision) | ✓ (macros) | ✓ (psyqo) | — | via psxsplash |
| Bezier curves | ✓ (`bezier.hh`) | — | ✓ (psyqo) | — | not exposed to Lua |
| Triangle clipping | — | — | ✓ (`triclip.hh`) | — | via psxsplash |
| Fog | — | — | ✓ | partial | ✓ |
| UV scrolling / anim textures | — | — | ✓ | partial | partial |
| Sprite primitives | ✓ | ✓ | ✓ | — | via psxsplash |
| **Animation** | | | | | |
| Skeletal / skin mesh | indirect | — | ✓ (`skinmesh.hh`) | ✓ | ✓ (baked at v30) |
| Keyframe tracks (pos / rot / active) | — | — | ✓ | ✓ | ✓ |
| Looping / one-shot | — | — | ✓ | ✓ | ✓ |
| Animation events / callbacks | — | — | partial | ✓ | partial (Phase 7 pending) |
| Bezier easing on tracks | ✓ (math) | — | ✓ (math) | — | **not yet exposed in Godot anim editor** |
| **Audio** | | | | | |
| SPU channel control + ADSR | ✓ | ✓ | ✓ | ✓ | via psxsplash |
| DMA SPU upload | ✓ | ✓ | ✓ | ✓ | via psxsplash |
| Sequenced music (.mid → format) | — | — | ✓ (PS1M, v28+) | bespoke | ✓ (`MidiParser.cs`) |
| Sound macros / families | — | — | ✓ (v29+) | — | ✓ (`PS1SoundMacro/Family.cs`) |
| XA streaming | — | ✓ | ✓ (`xaaudio.hh`) | ✓ | ✓ |
| CDDA | — | — | ✓ | ✓ | ✓ |
| **MDEC video** | — | ✓ (examples) | — | — | — |
| **Collision / spatial** | | | | | |
| AABB / sphere collision | ✓ | — | ✓ | ✓ | via psxsplash |
| BVH acceleration | — | — | ✓ | ✓ | via psxsplash |
| Spatial raycasting | — | — | ✓ | ✓ | via psxsplash |
| Trigger volumes | — | — | ✓ (GameObject) | ✓ | ✓ (`PS1TriggerBox`) |
| Portals + room culling | — | — | ✓ (rooms/cells) | ✓ | ✓ (`PS1NavRegion`) |
| **Input** | | | | | |
| Pad state (digital + analog) | ✓ | ✓ | ✓ (`controls.hh`) | ✓ | via psxsplash + Lua |
| Multitap | ✓ | ✓ | partial | ✓ | partial |
| **Memory** | | | | | |
| Bump allocator | ✓ (`bump-allocator.hh`) | — | ✓ (psyqo) | — | via psxsplash |
| VRAM packing analyzer | — | — | implicit | ✓ | ✓ (`VRAMPacker.cs` + dock) |
| **File I/O** | | | | | |
| ISO9660 / CD-ROM read | ✓ | ✓ | ✓ | — | via psxsplash |
| PCdrv (host-fs in dev) | — | — | ✓ | — | ✓ (Run on PSX flow) |
| **Disc streaming pattern** | — | ✓ (cdstream example) | — | — | — |
| **Math / utility** | | | | | |
| Vec2/3/4, Mat3/4 | ✓ | — | ✓ (psyqo) | — | via psxsplash |
| Trig LUT | ✓ | — | ✓ (psyqo) | — | via psxsplash |
| Adler32 | ✓ | — | ✓ (psyqo) | — | via psxsplash |
| **Async / coroutine** | | | | | |
| C++20 coroutines | ✓ (`coroutine.hh`) | — | ✓ (psyqo, unused) | — | — |
| Task queue (Promise-like) | ✓ (`task.hh`) | — | ✓ (psyqo, unused) | — | — |
| **Lua-side `Timer.Sleep` style yield** | n/a | n/a | — | n/a | — |
| **Fonts** | | | | | |
| Kernel ROM font + render | ✓ | — | ✓ (psyqo) | — | via psxsplash |
| Custom font upload | ✓ | — | ✓ | — | ✓ (`PS1UIFontAsset`) |
| **Scripting** | | | | | |
| Lua VM | — | — | ✓ (psxlua) | ✓ (asset-side) | ✓ (GDExtension + IDE) |
| EmmyLua stubs / autocomplete | — | — | — | — | ✓ |
| Cross-entity state modules | — | — | ✓ (GameState) | — | ✓ |
| **UI** | | | | | |
| Box / text / image / progress | — | — | ✓ (`uisystem.hh`) | ✓ | ✓ |
| Anchor + flow layout | — | — | partial | ✓ | ✓ (`PS1UICanvas`) |
| **Authoring** | | | | | |
| Blender add-on | — | — | — | implicit | ✓ (today's work) |
| Godot editor plugin | — | — | — | — | ✓ |
| Cycles / scene-light vertex bake | — | — | — | partial (Lambert) | ✓ (Cycles + Lambert + AO) |
| Texture bit-depth analyzer + 4/8bpp preview | — | — | — | — | ✓ |
| MIDI → PS1M | — | — | — | — | ✓ |
| Cross-tool round-trip (`.glb` + JSON sidecars) | — | — | — | — | ✓ |
| Static-batch optimizer | — | — | — | — | ✓ (Slot D1) |
| **Tooling** | | | | | |
| Memory / SPU / VRAM overlays | — | example only | ✓ (`memoverlay.hh`) | console-only | ✓ (live dock + budget bars) |
| Validators (texture / audio / animation / UV / dedup) | — | — | partial | partial | ✓ (5-tier dock summary) |
| Click-to-focus from warning | — | — | — | — | ✓ |

## B. Gap list — leverage-ranked

### High leverage

1. **MDEC video playback** — present in PSn00bSDK (`examples/mdec/{mdecimage,strvideo}`); absent from psxsplash + PS1Godot. Cutscenes are the load-bearing feature for RPG / story-driven games. Integration: add `videocodec.hh` to psxsplash with MDEC DMA + frame-buffer management, expose `Video.Play(name)` to LuaAPI, add `PS1VideoClip` resource on the Godot side. **Effort: ~2 weeks.** PSn00bSDK reference at `PSn00bSDK-master/examples/mdec/strvideo/`.

2. **CD streaming pattern** — psxsplash has `fileloader` + ISO9660 (read-once). PSn00bSDK's `examples/sound/cdstream` shows the chunk-stream pattern (seek + buffered reads with double-buffering). Needed for large worlds, hidden-loading-screen disc seeks, multi-disc setups. **Effort: ~1 week** for the runtime pattern + a Godot-side disc layout validator. Already in ROADMAP Phase L3 territory.

3. **Lua `Timer.Sleep(frames)` / coroutine API** — Lua's native `coroutine.yield` is sufficient; psxsplash just needs a frame-driven scheduler that resumes yielded coroutines. Cutscene + dialogue + animation sync becomes order-of-magnitude simpler:
   ```lua
   play_animation("talk")
   Timer.Sleep(60)
   play_sound("sfx_swish")
   ```
   No C++ coroutine machinery needed (PSYQo's C++20 coroutines are over-engineered for what game code needs). **Effort: ~3-5 days.**

### Medium leverage

4. **Bezier easing exposed at the authoring layer** — psyqo's `bezier.hh` is already linked into psxsplash. We don't expose it to Lua or to the Godot animation track editor. Easing presets (cubic-in, ease-out, etc.) would land big on UX without runtime work. **Effort: ~1 day** for Lua bindings + Godot enum `PS1AnimationTrack.Easing`.

5. **Animation event markers (Phase 7)** — already in our ROADMAP; PSYQo / PSn00bSDK don't have it but SplashEdit has a basic version. Cross-cutting (Blender side ✓ design exists; needs Godot runtime hooks for `onAnimationEvent` Lua callback). **Effort: ~1 week** to ship end-to-end.

### Low leverage / niche

6. **Font system at runtime** — psxsplash has psyqo's font.hh available; we don't ship a font picker in the Godot editor. Most games pre-bake UI glyphs (which we do via `PS1UIFontAsset`). Skip until a debug-text use case forces it.

7. **GTE register profiling overlay** — useful for late-stage perf optimization, not for daily authoring. Defer to Phase 4.

## C. What our stack does that the others don't

1. **Cross-tool round-trip in both directions** — `.glb` + JSON sidecars; `Export to Godot` + `Send to Blender` + `Edit Mesh in Blender`; Slot C metadata enums match wire-for-wire. Nothing else has this.

2. **Cycles vertex-color bake (`COMBINED` / `DIFFUSE` / `AO`)** — Blender side ships full GI bake to vertex colors with the PSX 0.8 ceiling. SplashEdit's `PSXLightingBaker.cs` does Lambert + light-direction only.

3. **Slot D1 static-mesh batching with author-time hint** — auto-collapses meshes sharing draw-state into single GameObjects + Blender validator surfaces "would batch N → M" predictions before export.

4. **Validation pipeline → severity-coded dock** — five reporters (texture / audio / animation / UV / mesh-dedup) feed one summary line with click-to-focus per-mesh navigation. SplashEdit had per-category console output only.

5. **MIDI → PS1M sequenced music** — DAW-friendly authoring path. Unique.

6. **Lua as a first-class Godot script language** — attach `.lua` to nodes, EmmyLua autocomplete, decimal-rewriter (Lua 5.x integer-literal trap), syntax highlighting on the way.

7. **Texture compliance analyzer + 4bpp/8bpp preview in Blender** — author sees the PSX-quantized output without round-tripping through emulator.

8. **Splashpack v31 vertex pool** — ~50% storage savings over v29. Nothing else has the equivalent; SplashEdit ships the older expanded format.

## D. Top 5 recommended next implementations

### 1. Lua `Timer.Sleep` / coroutine scheduler (~3-5 days)

Cheapest dramatic UX win. Lua already supports `coroutine.yield`; we just need a per-frame scheduler in `psxsplash` that resumes yielded scripts. Cutscenes, dialogue, sequenced gameplay (boss-fight phases, escort missions, timed puzzles) all become order-of-magnitude simpler. No new file format, no Godot-side change required, only psxsplash runtime + a few Lua bindings. Authors get `Timer.Sleep(frames)` + can chain animation/audio/scene calls naturally.

### 2. MDEC video playback (~2 weeks)

Unblocks the entire RPG / story-driven game category. PSn00bSDK proves the hardware path works. Integration is self-contained: add a `VideoClip` type to psxsplash (parallel to `AudioClipRecord`), splashpack v32 carries an `MdecPayload` table, runtime decodes via MDEC DMA + GTE matrix bridge, LuaAPI exposes `Video.Play("intro")`. Godot side gets a `PS1VideoClip` resource referencing a source `.str`. Worth the work; first new "category-unlocking" feature since audio routing (XA/CDDA).

### 3. Bezier easing exposed at the authoring layer (~1 day)

Tiny commit, big perceived quality. psyqo's `bezier.hh` is already compiled into psxsplash; we just don't surface it. Add `PS1AnimationTrack.Easing` enum on the Godot side (Linear / EaseIn / EaseOut / EaseInOut + cubic Bezier custom), plumb the Bezier evaluation through the runtime when interpolating between keyframes, expose `Easing.Cubic(p1, p2, t)` to Lua. SplashEdit has zero easing; we'd be the first PS1 stack with it.

### 4. CD streaming runtime + disc layout validator (~1 week, phased)

Already on ROADMAP as Phase L3 territory. Don't ship the runtime first — start with a Godot-editor **disc layout visualizer** that reads our splashpack output + psxsplash's ISO layout, shows a timeline-style map of (chunk, seek, audio, video). Authors see "if I add 2 MB more audio, the player will hit a 200 ms stall on this seek." Then build the runtime streaming logic informed by the visualizer's findings.

### 5. Animation event markers (Phase 7) (~1 week)

Already designed in `docs/ps1godot_blender_addon_integration_plan.md`. Cross-cutting (Blender authoring → JSON sidecar → Godot collector → splashpack `AnimationEventRecord` → runtime `onAnimationEvent` Lua callback). Combat-game timing (hitbox open/close), footstep audio sync, projectile-spawn frames all need it. Worth doing now that we have the cross-tool round-trip stable enough to support a new metadata type without infrastructure friction.

---

## Honest closing

The user (Cornelio) ships PS1Godot with stronger authoring UX than
any other PS1 stack we surveyed — by a wide margin on Blender/Godot
integration. The places we lag (MDEC, CD streaming, Lua coroutines)
are well-defined, externally-referenced, and each independently
tractable. Top 1 + 2 + 5 from this list would close the most
meaningful gaps.

PSYQo's C++20 coroutines + task queue are *interesting* but
wrong-tool-for-the-job — game logic should yield in Lua, not in
C++. PSn00bSDK's MDEC + cdstream examples are direct ports we can
crib from.

Cel shading is already in ROADMAP from this morning's work; not
listed in this report's recommendations.
