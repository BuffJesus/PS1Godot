# psxsplash improvement tracker

Living list of improvements we'd like to see in psxsplash. This doc exists so
that when we eventually file upstream issues or PRs, they come with **concrete
evidence** accumulated during Phase 2 porting — not speculation.

## Ground rules

- **We do not modify psxsplash as our default.** See `CLAUDE.md` for the
  consume-unchanged decision.
- **Every entry below should accumulate evidence** as we hit it during Phase 2
  exporter work. "Evidence" = a specific symptom, commit, or struct layout
  change that cost us time or forced a workaround.
- **Delivery channels**, in preference order:
  1. **Upstream PR** — for general-purpose wins (schema format, test harness,
     IDL, hot-reload).
  2. **Upstream issue** — for design conversations too big for a drive-by PR.
  3. **Local patch** in `patches/psxsplash/` — only if it blocks Phase 2 and
     upstream won't merge in a reasonable window. Document why.
  4. **Fork** — still no. Merge cost would eat the project.

## Entry template

When you hit a pain point, add a dated entry to the relevant item's
**Evidence** section. Keep it short: what broke, where, what it forced.

```markdown
- `YYYY-MM-DD` — <one-line symptom>. (<file:line or PR link>)
  Workaround: <if any>.
```

---

## High leverage (aligned with our goals)

### 1. Schema-driven splashpack format

**Problem.** `psxsplash-main/src/splashpack.hh` is hand-written structs with
`static_assert(sizeof(...) == N)` guards, and `splashpack.cpp` loads them with
`reinterpret_cast` cursor walks. Every format bump (v10→v11→…→v20) requires
byte-perfect matching changes on both sides. Backward compat is binary — old
splashpacks fail the `version >= 20` hard assert in
`splashpack.cpp:LoadSplashpack()`.

**Proposed direction.** Schema-driven format. Candidates:
- **FlatBuffers** — zero-copy reads fit PSYQo's pointer-fixup style, proven on
  embedded. Grumpycoders precedent in related projects.
- **Cap'n Proto** — similar zero-copy, slightly heavier runtime.
- **Custom TLV** — most work, most control, probably overkill.

Benefits: auto-generated readers on both sides, versioned fields with sane
defaults instead of all-or-nothing asserts, less cross-repo porting drag when
SplashEdit bumps the format.

**Status.** Unfiled.

**Evidence.**
- _(empty — fill during Phase 2 step 1 when we port the writer)_

---

### 2. Hot-reload of scene data

**Problem.** `scenemanager.cpp` loads splashpacks wholesale — a new load
replaces whole-scene state. There is no mechanism to diff an incoming
splashpack against in-memory state and swap only changed blocks.

**Why we care.** Phase 3 is pitched as "F5-to-play" with sub-second feedback.
A full-scene reload through PCdrv is still slower than "nudge texture,
see result immediately" would be. Without runtime support, the ceiling on
our iteration loop is "fast full rebuild", not true hot-swap.

**Proposed direction.** Small, incremental:
- Split splashpack sections into independently-reloadable blocks (textures,
  meshes, Lua, audio are the obvious candidates).
- Add `SceneManager::ReloadBlock(BlockType, uint8_t*, size_t)` entry.
- PCdrv-side: write a sentinel file (`.reload`) that the runtime watches.

**Status.** Unfiled. Needs a design conversation before a PR — this is a
public API addition.

**Evidence.**
- _(empty)_

---

### 3. Host-mode / test build

**Problem.** No way to unit-test psxsplash logic without an emulator or real
hardware. The bar for "did I break something" is "launch PCSX-Redux, boot,
observe" — which is slow and bad for CI.

**Proposed direction.** A `HOST_BUILD=1` make target that:
- Stubs `psyqo::GPU`, `psyqo::SPU`, `psyqo::GTE` with host-side mocks that
  record calls rather than executing them.
- Compiles with the host's C++ compiler (MSVC/Clang/GCC), not `mipsel-none-elf`.
- Produces a binary that can boot, load a splashpack from disk, step one
  frame, and assert invariants.

Precedent: Grumpycoders' `psyqo-testing` shows this is feasible within the
PSYQo model.

**Status.** Unfiled.

**Evidence.**
- _(empty — most likely trigger is "splashpack v21 breaks silently and we
  didn't notice for a week")_

---

### 4. Lua API IDL

**Problem.** `luaapi.hh` + `luaapi.cpp` declare ~18 modules (Entity, Vec3,
Input, Camera, Audio, Timer, Cutscene, Animation, SkinnedAnim, Persist,
Scene, Math, Random, Convert, Debug, Controls, Interact, UI, Player) through
hand-written C++ registration. Function signatures exist only as code. No
single source of truth for:
- C++ binding registration
- Lua-side type stubs / autocomplete
- End-user docs
- Our Godot plugin's EmmyLua stub generator (Phase 3)

Drift is inevitable as the API grows.

**Proposed direction.** Declarative IDL (YAML/JSON/TOML) describing every
module, function, parameter, return type, and doc string. Code generators
produce:
- C++ registration tables (replace current `RegisterAll`).
- EmmyLua `.lua` stubs for editor autocomplete.
- Markdown reference docs.

Our plugin becomes a first-party consumer of the IDL — no need to parse the
C++ header ourselves.

**Status.** Unfiled. This is a larger refactor; better proposed after we've
seen Phase 2 step 5 (Lua scripting path).

**Evidence.**
- _(empty)_

---

## Medium (nice but not critical for our Phase 1–3)

### 5. Lua observability / per-frame profiler

**Problem.** `profiler.cpp` / `profiler.hh` exist but expose limited info.
No per-Lua-function timing, no per-script frame budget overlay. Optimization
is guesswork.

**Proposed direction.** Hook Lua VM to sample `onUpdate`/`onInteract`/etc.
call times, aggregate per frame, expose via debug overlay or PCdrv-exported
CSV.

**Status.** Unfiled.

**Evidence.** _(empty)_

---

### 6. Audio depth — 3D spatialization, reverb, music layering

**Problem.** `audiomanager.{hh,cpp}` + `musicmanager.{hh,cpp}` mix ADPCM
voices with volume/pan. No 3D attenuation, no per-voice reverb routing, no
dynamic music layering. PSYQo's SPU support is rich enough to do all three;
real PS1 titles (MGS, Silent Hill, FFVII) did.

**Proposed direction.** `Audio.Play3D(clip, position, params)` Lua API that
computes attenuation + pan from `Camera.GetPosition()`. Reverb bus routing
flag per voice. Parallel music tracks with crossfade API.

**Status.** Unfiled. Scope-expanding; probably a Phase 4-ish ask upstream.

**Evidence.** _(empty)_

---

### 7. Animation blending

**Problem.** `animation.{hh,cpp}` does keyframe playback. `skinmesh.{hh,cpp}`
recently added skinned meshes (v18). No blend trees, no A→B crossfade, no
additive layers, no IK. Character animation is stuck at "play clip N".

**Proposed direction.** Minimum viable: `Animation.PlayBlended(name, weight)`
that lerps against whatever's currently playing. Transitions become
data-driven from Godot side.

**Status.** Unfiled.

**Evidence.** _(empty)_

---

### 8. Save system abstraction

**Problem.** No `Save.Get(key)` / `Save.Set(key)` Lua API backed by memory-card
blocks. Each game rolls its own memcard handling. `Persist.*` in luaapi only
survives within a session.

**Proposed direction.** `Save.Commit()` / `Save.Load(slot)` / `Save.Get(key)`
Lua API, with a documented block-layout convention.

**Status.** Unfiled.

**Evidence.** _(empty)_

---

### N+6. Portal-culling safety fallback hides the entire feature when spawn is in catch-all

**Problem.** `renderer.cpp` `RenderWithRooms` has a `else { render all rooms }`
fallback for `cameraRoom < 0`:

```cpp
} else {
    // Camera room unknown - render ALL rooms as safety fallback.
    for (int r = 0; r < roomCount; r++) if (r != catchAllIdx) renderRoom(r, full);
}
```

When the player is anywhere outside the authored rooms (valid in
partial-interior scenes — a courtyard with two buildings, a spawn in
open space, the catch-all chunk during streaming) every room renders
unconditionally. Portal culling becomes invisible to the user, which we
diagnosed only after pixel-peeping screenshots and dumping the splashpack.

**Evidence.**
- `2026-04-20` — Phase 2 bullet 12 test scene (two rooms off to the side of
  the demo) appeared to have zero culling regardless of position. Took a hex
  dump of the splashpack to rule out a writer bug before spotting the
  fallback.
  Workaround: local patch (`psxsplash-main/src/renderer.cpp`) that deletes
  the `else` branch. With it removed, stepping out of a room immediately
  hides both rooms — matching every other portal renderer's behaviour.

**Upstream direction.** Convert the fallback into an opt-in via compile flag
(`PSXSPLASH_ROOM_SAFETY_FALLBACK`) or header field on `SplashpackSceneSetup`.
Default to OFF. Games that need the all-rooms-render safety net can opt
back in; portal-correct games (the common case) get the expected behaviour
without a local patch.

---

## Low priority / deferred

- **Streaming scene chunks by proximity.** BVH is the right structure; the
  disc layout planner to keep related data adjacent on CD is the hard part.
  Scope: big. Filed as mental note only.
- **i18n / font translation management.** `font.hh` in PSYQo handles
  rendering; no translation catalog.
- **Multiplayer / link-cable support.** Not a common ask.
- **Multiple Lua VMs.** Isolation win vs. 2 MB RAM — probably not worth it.

---

### N. Honor playerStartPosition / playerStartRotation when no nav regions

**Problem.** `SceneManager::InitializeScene()` only updates the camera from
the player position when nav regions are loaded
(`m_cameraFollowsPlayer = m_navRegions.isLoaded()`,
`scenemanager.cpp:76`). On scenes without nav regions (e.g., a "hello cube"
test scene), the splashpack header's `playerStartPosition` is read into
`m_playerPosition` but never propagated to `m_currentCamera` — so the camera
stays at world origin even when the author placed it elsewhere. Result:
black screen if the scene's geometry isn't around origin.

Compounding issue: `playerStartRotation` is stored as fp12 short
(`PackedVec3` = 3 × `FixedPoint<12, int16>`), but the consumer
`playerRotationX/Y/Z` is `FixedPoint<10>`. There's no conversion — the raw
int16 is reinterpreted, giving ~4× wrong angles.

**Proposed direction.**
- At end of `InitializeScene()`, unconditionally call
  `m_currentCamera.SetPosition(...)` from `m_playerPosition` so author
  camera placement applies on first frame regardless of nav config.
- Either change `playerStartRotation` to `FixedPoint<10>` short on the wire
  (format bump), or add an explicit conversion in the loader.

**Status.** Local patch applied in `psxsplash-main/src/scenemanager.cpp`
2026-04-19 — sets position from header, leaves rotation unfixed (TODO).
Good upstream PR candidate: small change, broadly useful.

**Evidence.**
- `2026-04-19` — Phase 2 bullet 2E: untextured cube splashpack loaded but
  rendered black. Trace: camera stayed at origin while geometry was placed
  for a (6,4,6) view. Workaround: local SetPosition call after
  `m_playerPosition` assignment.

---

### N+5. UI text has no word-wrap; overflows right edge

**Problem.** `UISystem::renderProportionalText()` and the system-font
chainprintf path both walk the string linearly and advance cursorX
off the right edge of the element when the text is longer than the
author-declared width. Newlines in the text buffer aren't handled
either.

**Why we care.** Authors write natural dialog lines that blow past the
~28 char budget of a typical dialog box and have to manually break
them into multiple UI elements or truncate at the source. Makes
localization actively painful.

**Proposed direction.** Word-break the string at glyph boundaries when
cursorX + glyph.advance > x + w: insert an implicit newline, reset
cursorX, advance cursorY by font line height. Honor explicit `\n` in
the text buffer the same way. Optional authoring field: overflow
mode (Truncate / Wrap / Scroll).

**Status.** Unfiled. Purely a renderer change; no schema impact.

**Evidence.**
- `2026-04-20` — Demo dialog lines ("Did the camera finish moving?
  It never tells me.") overflowed off the right edge of a 224 px
  body element. Workaround: authors shorten on-screen text and let
  the voice clip carry the full phrasing.

---

### N+4. Background / sky color tied to fog color

**Problem.** `Renderer::SetFog()` does:

```cpp
m_clearcolor = fog.color;
```

So the framebuffer clear color is always the fog color. Every pixel
of "sky" (anywhere there's no geometry, including the entire upper
half of an exterior scene) renders as the saturated fog color. A
soft distance haze becomes a wall of opaque colored void above the
horizon.

**Why we care.** Authors immediately try saturated fog colors (deep
green forest, blue dusk, red Mars surface) and get an unusable
flat-color sky. Workaround is to pick fog colors that double as
acceptable skies — but a real "fog stays in the distance only" look
is impossible with one color.

**Proposed direction.** Two fields in `SPLASHPACKFileHeader`:
`backgroundR/G/B` for the framebuffer clear and the existing
`fogR/G/B` for the distance tint. Renderer takes both:

```cpp
m_clearcolor = backgroundColor;  // not fog.color
```

Authors get separate control: blue sky + grey fog, or whatever the
scene needs. PS1 still has no actual skybox, but at least the empty
clear isn't pretending to be fog.

**Status.** Unfiled. One header field addition + one renderer line.
Backwards compat: if backgroundColor is unset (all zero), fall back
to the current behavior (clear = fog color).

**Evidence.**
- `2026-04-19` — Phase 2 fog testing. Author set FogColor to a
  saturated green for forest atmosphere, got an opaque green sky
  filling the upper half of the screen instead of fog confined to
  distant geometry. Screenshot in session log.

---

### N+3. Fog near/far distance not independently authorable

**Problem.** `Renderer::SetFog()` derives the fog *near* distance from
the *far* distance with a hardcoded ratio:

```cpp
m_fog.fogFarSZ = 20000 / fog.density;
int32_t fogNear = fogFarSZ >> 3;   // far / 8
```

So authors can only choose *how far away the fog wall is* (via the
density byte). They can't say "no fog within 5m, full fog at 30m" —
the start is always 1/8 of the end. Result: thick close fog when
authors want a soft distance haze, or vice-versa.

**Why we care.** Fog is the cheapest way to hide PS1 draw distance
limits (see `docs/ps1_large_rpg_optimization_reference.md`'s "hide
distance aggressively"). Authors hit this immediately when trying to
match a specific look — a town with fog only at the edges, a cave
with fog filling the room, etc. Currently they can't get either
without modifying the runtime.

**Proposed direction.** Replace the single `density` byte in
`SPLASHPACKFileHeader` with a pair of `fogNearSZ` + `fogFarSZ`
values (or keep density and add an explicit `fogStartRatio` byte for
backward compat). Renderer's interpolation code already takes both —
just stop computing one from the other.

**Status.** Unfiled. Needs a header field addition + version bump,
which is straightforward but a public schema change.

**Evidence.**
- `2026-04-19` — Phase 2 bullet 8 / 10 / fog-fix testing. Author set
  density=5 expecting "fog past mid-distance"; got dense fog wall
  starting at ~500 sz units. Workaround: bumped density range from
  1–10 to 1–100 in PS1Scene so density=1 (fogFar≈20000) gives the
  faintest possible fog. Independent near/far would skip the
  workaround.

---

### N+1. Cutscene camera rotation track ignores the player's facing convention

**Problem.** The cutscene's camera-rotation track sets the camera to raw
PSX angles (the runtime's `Camera::SetRotation` consumes
`psyqo::Angle` triples and builds a Y·X·Z rotation matrix from them).
Authors writing keyframes naturally think "rotation 0 = camera at rest /
looking at the scene the same way the player does" — but the player rig
runs at `playerRotationY = 1.0_pi` (180°), and a keyframe of (0, 0, 0)
makes the camera face the OPPOSITE direction (PSX +Z, away from a
Godot-authored scene whose forward is -Z).

The result: every cutscene authored with naïve "0 yaw" keyframes looks
the wrong way for the entire shot, and the handoff at the end snaps
180° because the player rig immediately re-applies playerRotationY.
Symptom: the first thing on screen at cutscene start is the back wall
of the scene; the moment control hands back, the camera spins around.

**Why we care.** Phase 2 bullet 10 ships cutscenes; this convention
mismatch is the first thing every author hits. Workaround on our side
is to write `Vector3(pitch, 180, 0)` everywhere, but that's exactly the
kind of "you have to know the secret" papercut we want the editor to
hide.

**Proposed direction.**
- Quick: have the cutscene's camera-rotation track interpret keyframes
  as *deltas from playerRotationY* (so 0 yaw = "match the player's
  facing"). Keeps existing splashpacks working if we gate on a header
  flag or version bump.
- Better: an explicit `LookAt` track type (`TrackType::CameraLookAt`)
  that takes a world-space target and orients the camera at it each
  frame. Authors author intent, runtime computes angles. Dovetails
  with future PS1Cutscene UX in the editor (drag a target, draw a
  line in the viewport).

**Why we care.** Cutscenes are Phase 2 bullet 10 on our side. If the runtime
is already known to mis-interpolate camera tracks, we either (a) wait for
an upstream fix before investing in the editor-side cutscene UX, or
(b) reproduce it with a minimal Godot-authored cutscene and send evidence
upstream so the fix lands sooner.

**Proposed direction.**
- During Phase 2 bullet 10 implementation, author a minimal cutscene (one
  camera position keyframe, one rotation keyframe, linear interp) and
  observe whether the reported wobble reproduces.
- If it does, bisect: which track type (`TrackCameraPos`, `TrackCameraRot`,
  `TrackCameraLookAt`)? Euler/quaternion conversion? fp10 vs fp12 unit
  mismatch (same family of bug as entry N)?
- File upstream with a minimal `.splashpack` that reproduces.

**Status.** Reproduced + diagnosed 2026-04-20. Root cause was **on our
side, not psxsplash's** — `SceneCollector.EncodeKeyframeValue` used
`DegToFp10 = 4096f / 360f` for rotation tracks, but `psyqo::Angle` is
`FixedPoint<10>` measured in fractions of Pi (per
`nugget/psyqo/trigonometry.hh:45-48`): 1.0 pi-unit = 180° = 1024 raw
fp10. So our encoder was doubling every angle. Authored 180° became
360° (wraps to 0°), authored 45° became 90° (camera pitched straight
up). Fixed by switching the constant to `1024f / 180f`. Entry kept as
a postmortem so the specific failure pattern ("any cutscene with
non-zero yaw produces camera staring at sky") is searchable.

**Reading the psyqo + psxsplash sources gave the actual rules:**
- `psyqo::Matrix33::vs[i]` is **column i** (verified against
  `soft-math.cpp::multiplyMatrix33` indexing at line 132).
- `Camera::SetRotation` builds `M = rotY * rotX * rotZ`
  (`camera.cpp:33-36`).
- `worldToCamera` (`renderer.cpp:447-458`) computes
  `outZ = vs[2] · (world − cam)`, so `vs[2]` (column 2 of M) is the
  **world direction the camera looks**.
- `SceneCollector.EncodeKeyframeValue` (line 836-839) negates Godot X
  and Y angles before writing them as PSX angles.

**The papercut.** With yaw=0, positive Godot pitch (encoded as PSX
negative pitch) makes the camera look down. With yaw=180° (which is
what the player rig uses, so the natural choice for handoff), the
matrix multiplication `rotY(180°) * rotX(θ_x)` flips the sign of the
Y-component of column 2, so the **same Godot pitch sign now means UP
instead of down**. Authors writing `Vector3(-45, 180, 0)` ("look down
45°, facing the player") get a camera staring at the sky.

**Workaround in our demo.** Every CamRotKf is `Vector3(POSITIVE pitch,
180, 0)` — verified by deriving the matrix from psyqo's source.

**Evidence.**
- _(verbal, no paste yet)_ — psxsplash maintainer reported cutscene
  camera movement sometimes incorrect.
- `2026-04-20` (1) — naïve `Vector3(-50, 0, 0)` keyframes pointed the
  camera at the back wall the entire shot, then snapped 180° at
  handoff (yaw mismatch with player rig).
- `2026-04-20` (2) — adding yaw=180° while keeping pitch sign convention
  (`(-45, 180, 0)`) made the camera stare at the sky (pitch sign
  flipped through `rotY(180°) * rotX`). Final fix: `(+45, 180, 0)` with
  the convention derived from source.

---

### N+2. Audio clips wiped by redundant `m_audio.init()` in InitializeScene

**Problem.** `SceneManager::loadScene()` does the correct sequence:
1. `m_audio.init()` (resets manager state)
2. `uploadSpuData()` → `m_audio.loadClip()` for each clip (DMA ADPCM to
   SPU RAM, sets `m_clips[i].loaded = true`)
3. `InitializeScene(newData, …)` (parses splashpack, populates
   `m_audioClipNames`)

But `InitializeScene()` (scenemanager.cpp:48 on HEAD) **also** calls
`m_audio.init()` right at its top. That second init flips every
`m_clips[i].loaded` back to `false` without actually freeing SPU RAM.
Result: `AudioManager::play(clipIndex, …)` always hits the `!loaded`
early-return and returns -1, even though the ADPCM data is sitting in
SPU RAM correctly.

Source of truth comment already exists in the same file
(scenemanager.cpp:106): *"Audio clip names are stored in the splashpack.
ADPCM data is loaded separately via uploadSpuData() before
InitializeScene() is called."* So the author knew the ordering — the
second init looks like a leftover from an earlier design where
`InitializeScene` was standalone.

**Fix.** Delete the redundant `m_audio.init()` at the top of
`InitializeScene`. `InitializeScene` is called from exactly one place
(`loadScene:967`), which already inits before upload.

**Status.** **Upstreamed** — fixed in psxsplash `70ada6e` (2026-04-20,
"Fixed normal SPU audio breakage due to CDDA"). Local patch removed
during vendor refresh on 2026-04-20; we now run the upstream behavior
verbatim.

**Evidence.**
- `2026-04-19` — Phase 2 bullet 6 testing. `uploadSpuData` prints
  `loadClip -> 1` (success) but Lua `Audio.Play(0)` returns -1.
  Bisected to the second `m_audio.init()` in `InitializeScene`. Removing
  it made audio play.

---

### N+7. Sequenced music sequencer (custom format, MIDI-driven)

**Problem.** psxsplash plays single-clip ADPCM via `AudioManager` and
streams CD-DA via `MusicManager`, but has no sequenced-music story —
no SEQ/VAB player, no MIDI parser, no per-channel pitch shifting on
short instrument samples. Discord 2026-04-20 (spicyjpeg) confirmed
sequenced music is on the roadmap with no concrete ETA. Games that
want chiptune/tracker-style BGM either ship as multiple long
single-track ADPCM loops (huge SPU footprint) or wait.

**What we built.** A self-contained sequencer on top of `AudioManager`,
shipped as an additive patch — no breaking changes to existing
psxsplash APIs.

- `src/musicsequencer.{hh,cpp}` — new class, ~300 LOC. Mono per
  channel; each music channel owns a fixed SPU voice for the song's
  lifetime. Tick model advances by the existing `m_dt12` so playback
  scales naturally with frame timing.
- 84-entry pitch table (exact 12th root of 2 in fp12, capped at the
  SPU's 0x3FFF), precomputed as static rodata. We tried iterating
  `rate = (rate * NUM) / DEN` with reasonable-sized fixed-point
  ratios first; rounding error compounds enough over 36 semitones to
  produce wildly wrong pitches.
- Splashpack format bumped v21 → v22. Header tail grew 8 bytes
  (`musicSequenceCount` + pad + `musicTableOffset`); section appended
  after audio is an array of 24-byte `MusicTableEntry { dataOffset,
  dataSize, char[16] name }` rows pointing at PS1M blobs.
- PS1M binary blob: 16 B header (`"PS1M"` magic, BPM, ticksPerBeat,
  channelCount, eventCount, loopStartTick) + 8 B `MusicChannelEntry`
  × N + 8 B `MusicEvent` × M.
- `AudioManager` extensions:
  - `reserveVoices(int n)` — first N voices off-limits to `play()`.
    Sequencer claims its pool on `playByIndex`, releases on `stop()`.
  - `playOnVoice(int voice, int clipIdx, int vol, int pan)` — direct
    placement bypassing the free-voice search. Sequencer drives notes
    through this so dialog can never steal a held music note.
  - `play()` rewritten to scan voices `[m_reservedForMusic,
    MAX_VOICES)` itself (psyqo's `getNextFreeChannel` always starts
    at 0 and doesn't know about the reservation).
  - `getClipDurationFrames(int)` — derived from ADPCM size + sample
    rate; small but useful for Lua-side dialog timing.
- `SceneManager` owns the sequencer + a parallel name table
  (`findMusicSequenceByName`); registers sequences from
  `SplashpackSceneSetup::musicSequences[]` in `InitializeScene`,
  ticks each frame next to the animation player, calls `stop()`
  before `m_audio.reset()` on scene teardown.
- Lua API: `Music.Play(nameOrIndex, vol)`, `Music.Stop()`,
  `Music.IsPlaying()`, `Music.SetVolume(v)`, `Music.GetBeat()`,
  `Music.Find(name)`. `Audio.GetClipDuration(nameOrIndex)` also added.

**A couple of small gotchas worth flagging if you upstream a similar
design:**
- `m_dt12` convention is 4096 = one **30** fps frame (per the
  `(elapsed_us * 4096) / 33333` calc), not 60. Cost an hour debugging
  music-plays-at-half-speed.
- Sort note-off before note-on at the same tick. Otherwise repeated
  same-pitch notes (every snare hit in a typical drum loop) get
  silenced by their own trailing note-off through a mono-per-channel
  cut.

**Status.** Local patch in `psxsplash-main/src/musicsequencer.{hh,cpp}`
+ touches in `audiomanager`, `scenemanager`, `splashpack`, `luaapi`,
`Makefile`. Format documented in `docs/sequenced-music-format.md`,
authoring side documented in the plugin's README. Self-contained
enough that it's a viable upstream PR / starting point for the
official sequencer when that work begins.

**Evidence.**
- `2026-04-20` — Demo scene needs background music; only existing
  options are CD-DA streaming (heavy authoring chain, no sample-level
  pitch control) or one-shot ADPCM (no sequencing). Built the
  sequencer end-to-end in one session; runs the Reaper-authored test
  song at correct tempo + pitch with bass/pad/lead/kick/snare/hat.

---

## Changelog

- `2026-04-18` — Document created. All entries speculative, pending Phase 2
  evidence.
- `2026-04-19` — Added entry N (camera-init-from-header). First piece of
  real Phase 2 evidence.
- `2026-04-19` — Added entry N+1 (cutscene camera movement). Placeholder
  from verbal report by psxsplash maintainer; reproduce during bullet 10.
- `2026-04-19` — Added entry N+2 (audio clips wiped by redundant init).
  Real Phase 2 bullet 6 bisect; local one-liner patch applied.
- `2026-04-20` — Entry N+2 upstreamed in psxsplash `70ada6e`. Vendor
  refresh pulled upstream to latest main; local patch dropped.
- `2026-04-20` (evening) — Added entry N+7 (sequenced music sequencer).
  Self-contained additive patch; viable upstream PR or design reference.
