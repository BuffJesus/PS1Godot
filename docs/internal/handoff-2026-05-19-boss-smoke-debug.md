# Handoff — boss_smoke debug arc (2026-05-19)

Multi-session debugging of `godot-ps1/demo/boss_smoke/`. Started with
build/runtime crashes; chipped through a long list of "works on
emulator now but feels wrong" / "doesn't render" / "no input"
bugs. **Nothing in this arc is committed yet** — all changes still
in the working tree. Run `git status` to see the full list.

User pattern: one issue at a time, F5-verify each, then move on.

---

## What is actually fixed and confirmed working

- **Build**: `psxsplash-cdrom.ps-exe` compiles, ISO builds, PCSX-Redux
  launches. `HurtBoxTableEntry` static_assert mismatch (20 vs 18 bytes)
  corrected in `psxsplash-main/src/splashpack.hh`.
- **Lua syntax**: psxlua is Lua 5.2 integer-only (`LUA_NUMBER=long`);
  16 spots that used `//` for integer division swapped to `/` in
  both `boss_smoke_player.lua` and `boss_smoke_brain.lua`. `/`
  already does integer division in this VM.
- **Wrong stat/script-to-entity mapping**: `StaticBatchOptimizer`
  was reordering `data.Objects` (pinned first, then eligibles) even
  when no actual batching occurred — shifting indices and breaking
  cross-references (stats, colliders, hurtboxes). Fixed in
  `addons/ps1godot/exporter/StaticBatchOptimizer.cs` with an
  early-exit when no bucket has ≥2 members.
- **`UI.SetElementW` is not an API**: replaced 2× with
  `UI.SetSize(elem, w, h)` in both demo Lua files.
- **`s_sceneManager` scope**: `StatsResolveIndex` couldn't see
  `s_sceneManager` as a free function. Promoted to private static
  method on `LuaAPI` in `luaapi.hh`/`luaapi.cpp` so it has access
  via `s_sceneManager->findGameObjectIndex(go)`. Added
  `SceneManager::findGameObjectIndex` in `scenemanager.hh`.
- **Floor pushing the player off the edge**: `Collision::testAABB`
  in `collision.cpp` now detects "floor" colliders (Y-range × 8 < XZ
  max) and switches to a Y-only push-out instead of XZ pushback.
  `scenemanager.cpp` now applies `pushBack.y` too so floor lift
  actually lands.
- **Fog**: `FogDensity = 2` in `boss_smoke.tscn` (legacy density
  mode). Units in `FogNear`/`FogFar` are **GTE-Z fp12** not metres —
  the earlier 6/30 values nuked the screen.
- **Skybox**: `PS1Sky` node with `sky_stars.png` placeholder added
  to `boss_smoke.tscn` (BitDepth=0, Tint=(0.7, 0.55, 0.7) — purple
  per user preference).
- **Floor visibility**: `PlaneMesh.subdivide_width = subdivide_depth = 8`
  added — was 2 tris (way too big for GTE), now 162 tris.
- **`distSq:raw()` Lua flood**: psyqo-lua's FixedPoint metatable
  defines `:raw()` / `:toNumber()` but **never sets `__index`** —
  so method-call sugar `obj:raw()` returns nil. Userdata is just
  `{_raw = value}` with a metatable. Boss brain now reads
  `distSq._raw` directly (`boss_smoke_brain.lua:110`). **This is a
  recurring footgun** — saved as memory
  `project_psxlua_fixedpoint_no_methods.md`. Apply the same field-
  access pattern in any future Lua that touches FixedPoint internals.
- **HUD .tscn field names**: the old canvas used the pre-refactor
  names (`ElementType`, `W`, `H`, `ColorR/G/B`). Rewrote both
  `BossHPCanvas` and `PlayerHPCanvas` with current names: `Type`,
  `Width`, `Height`, `Color = Color(r, g, b, 1)`. With the old
  names Godot silently fell through to defaults — `Type=Text` with
  text `"Text"`, which is why the user previously saw text labels
  where bars should be.
- **Over-the-shoulder camera**: `boss_smoke.tscn` Camera3D moved
  from `(0, 1, 3)` (centered-behind) to `(0.5, 1.4, 3)`
  (right-shoulder, head-height). Saved this as feedback memory
  `feedback_camera_over_shoulder.md` — apply to any future
  third-person rig.
- **Pad analog mode**: PCSX-Redux's emulated pad was reporting as
  `DigitalPad` (`0x41`) with ADC stuck at `0xff`. Edited
  `C:\Users\Cornelio\AppData\Roaming\pcsx-redux\pcsx.json` —
  `"DeviceType": 0 → 1` on both pads (Digital → Analog/DualShock).
  JSON naming is reversed: `DeviceType` selects the controller
  model the emulated console sees, `PadType` selects the host
  input source. Right stick now produces input.

---

## Open bugs (highest signal at the top)

### 1. Boss HP bar visible from frame 1 (Lua bug, easy fix)

`boss_smoke_brain.lua:60-72`:

```lua
local function updateHPBar(self)
    local canvas = UI.FindCanvas("boss_hp")
    if canvas < 0 then return end
    if not hpBarShown then
        UI.SetCanvasVisible(canvas, true)   -- <-- fires on first onUpdate
        hpBarShown = true
    end
    ...
end
```

`updateHPBar` runs every `onUpdate`, so frame 1 flips `boss_hp`
visible regardless of `VisibleOnLoad = false` on the canvas in
`.tscn`. Player hasn't even reached the fog gate yet, but the HP
bar is up.

**Fix:** delete the auto-show in `updateHPBar`. Show the bar from
the fog-gate trigger (`boss_smoke_fog_gate.lua` is the natural
home — it already runs `Music.Play("boss_theme")` and
`Camera.LockOn(boss)` on entry; add a `UI.SetCanvasVisible(UI.FindCanvas("boss_hp"), true)`
right after). Boss brain should only *resize the fill*, never
toggle visibility. The death path in `onDamage` already hides it
correctly.

### 2. HUD bars have no colour (still rendering empty)

After the `.tscn` field-name fix and the Lua flood fix, bars are
visible as outlined rectangles but no fill colour. Need runtime
investigation; this is **not** an exporter problem.

**What is verified clean:**
- `SceneCollector.cs:957-959` reads `el.Color`, converts via
  `Mathf.Clamp((int)(color.R * 255f), 0, 255)` — produces correct
  byte values (e.g. `0.78 → 199`). Verified by reading the field
  values in the live .tscn (boss fill = `Color(0.78, 0.16, 0.16, 1)`,
  player HP fill = `Color(0.24, 0.78, 0.24, 1)`).
- `SplashpackWriter.cs:1346` writes `ColorR/G/B` as three bytes in
  the element record's 48-byte block. Type-specific block at offset
  16 writes nothing meaningful for `Box` (type=1), just 16 zero
  bytes.
- All 7 elements export with correct counts:
  `[PS1Godot] UICanvas 'boss_hp': residency=Gameplay, 3 elements, 0 3D models`
  `[PS1Godot] UICanvas 'player_hp': residency=Gameplay, 4 elements, 0 3D models`.

**Suspects in priority order:**
1. **`UI.FindElement` returns -1**, then `UI.SetSize(-1, w, h)`
   no-ops on a -1 handle. Width stays at the authored value (which
   is fine), but maybe something else in the pipeline silently
   trashes the colour. Instrument `UI.FindElement` in
   `psxsplash-main/src/luaapi.cpp` to print the resolved index
   for the next 4 calls — confirm it returns sane non-negative
   indices for both `hp_fill` and `boss_hp/fill`.
2. **Runtime renders Box but with `Color`-source confusion** —
   the `Box` path in `uisystem.cpp` may be reading `Color` from a
   different field of the element struct than where the writer puts
   it. Diff the writer offsets against the runtime parse offsets.
   Element record is 48 B; `ColorR/G/B` at byte 24-26 per
   `SplashpackWriter.cs:1339-1347`. Verify the runtime reads them
   from the same offset.
3. **PSX RGB modulate** — runtime might expect mid-grey (byte 128)
   as "neutral" with `modulate_scale=2.0`. If so, byte `199` doesn't
   reach full brightness; should look pinkish-dim red, not totally
   colorless. Check whether `Box` uses the same modulate path as
   meshes.

Start with #1 (cheapest), promote to #2/#3 if `UI.FindElement` is
clean.

### 3. Camera pitch (look up/down) "doesn't feel right"

User confirmed right stick now works after the PCSX config fix,
but said pitch "works, but it doesn't feel correct".

**Diagnosis:** at `scenemanager.cpp:1094-1112` the camera-follow
rotates the offset by `playerRotationY` (yaw) only:

```cpp
auto sinY = m_trig.sin(playerRotationY);
auto cosY = m_trig.cos(playerRotationY);
auto camX = ... + cosY * activeOffset.x + sinY * activeOffset.z;
auto camY = ... + activeOffset.y;   // <-- constant 1.4, no pitch
auto camZ = ... - sinY * activeOffset.x + cosY * activeOffset.z;
m_currentCamera.SetPosition(camX, camY, camZ);
m_currentCamera.SetRotation(playerRotationX, playerRotationY, playerRotationZ);
```

The camera's *rotation* tracks pitch via `playerRotationX`, but the
camera's *position* doesn't — it sits at a fixed height behind the
player. So when the user pitches up/down, the camera rotates in
place, swinging the world up/down instead of arcing the camera
over/under the player (Souls/Elden Ring orbital feel).

**Fix:** apply pitch rotation to (Y, Z) of the offset before the
yaw rotation. For pitch θ:

```
Z_pitched = Z_authored * cos(θ) + Y_authored * sin(θ)
Y_pitched = Y_authored * cos(θ) - Z_authored * sin(θ)
```

Then the existing yaw rotation operates on `(X_authored,
Z_pitched)`. Camera ends up orbiting the player's head, which is
what the user means by "feels correct".

Clamp pitch to a sane range (e.g. ±π/3 ≈ 60°) somewhere upstream
so the camera doesn't go inverted-vertical. Likely lives in
`controls.cpp` where pitch is integrated from right-stick Y.

### 4. Framerate dips

User reports performance dipping during gameplay. Total geometry
budget is well within range (188 triangles across 4 meshes per the
build log), so this is probably not raw fillrate.

**Hypotheses to test in order:**
1. **The Lua flood was the cost.** Previous runs were printing
   `attempt to call method 'raw' (a nil value)` once per `onUpdate`
   call — i.e., per frame, with a full traceback. `ramsyscall_printf`
   is not free. Now that the flood is fixed, re-measure before
   chasing anything else.
2. **UV-scroll on the fog wall.** `FogWall.UVScrollSpeed = (12, 8)`
   updates UVs every frame. Probably cheap on 2 tris but worth
   confirming.
3. **Boss script's per-frame `Physics.OverlapBoxDetailed`** call
   inside `fireAttack` only fires on attack-hit frames, so it
   shouldn't be the constant cost.
4. **Stamina regen accumulator** runs every frame in
   `boss_smoke_player.lua:125-131`. Cheap, but check.
5. The 162-tri subdivided floor adds GTE load. If the scene runs
   fine without it, the floor mesh may need re-tessellation (the
   subdivision was added to make a 2-tri mesh fit in GTE — there
   should be a happy medium).

Use the perfoverlay (`PSXSPLASH_PERFOVERLAY` is already defined in
the build flags) to see per-system frame time. If
`perfoverlay.cpp` doesn't already break out GTE vs CPU vs Lua,
add the split first.

---

## Lower-priority dev-loop polish

- **Diagnostic noise**: the run log is flooded with `[StatsDiag]`,
  `[ColliderDiag]`, `[CollideDiag]`, `[FRC]`, `[PadDiag]` lines.
  Those were added incrementally for the bugs above. Once #1–#4 are
  closed, gate them all behind a single `#ifdef PSXSPLASH_VERBOSE`
  or similar, and remove the per-frame ones entirely. `[PadDiag]`
  in `controls.cpp:156-175` is keyed to `& 63 == 0` (~1/s at
  60 fps) so its output rate is fine; the rest are once-on-load or
  on-event and can stay during dev but should be flag-gated for
  ship.
- **`cameraAimOffset` task** is **deferred**. Earlier in the
  session the user asked for "push the engine-side
  cameraAimOffset" (so over-the-shoulder camera can converge
  inward when the offset shifts right). I'd started the splashpack-
  format plumbing but had not touched the binary format yet when
  the pad / Lua-flood blockers surfaced. Resume by adding
  `CameraAimYawOffset` + `CameraAimPitchOffset` exports to
  `PS1Player.cs`, plumbing through
  `SceneCollector.cs` → `SceneData.cs` → `SplashpackWriter.cs`
  (bump splashpack to v36) → `splashpack.cpp/hh` →
  `scenemanager.cpp:1112` where the runtime `SetRotation` call
  adds the offsets to player yaw/pitch before rotating the camera.
  Plenty of room left in the 256-byte header — could also reuse
  one of the 2-byte pad fields rather than bumping the format,
  reviewer's call.

---

## Uncommitted state

`main` currently at `7221785 fix(demo): boss_smoke crash — bad UIDs in .tres + .tscn`.

Working-tree changes (none staged, none pushed):

```
M godot-ps1/addons/ps1godot/exporter/SplashpackWriter.cs   (likely just whitespace from the cameraAimOffset poke — verify)
M godot-ps1/addons/ps1godot/exporter/StaticBatchOptimizer.cs   (early-exit fix)
M godot-ps1/addons/ps1godot/nodes/PS1Player.cs   (likely whitespace — verify)
M godot-ps1/demo/boss_smoke/boss_smoke.tscn   (skybox + HUD rewrite + camera offset + fog density)
M godot-ps1/demo/scripts/boss_smoke_brain.lua   (// → /, _raw access, UI.SetSize, maxHP>0 guards)
M godot-ps1/demo/scripts/boss_smoke_player.lua   (// → /, UI.SetSize, maxStamina>0 guards)
M psxsplash-main/src/collision.cpp   (floor-mode push-out)
M psxsplash-main/src/controls.cpp   (lifted [PadDiag] above digital/analog branch)
M psxsplash-main/src/luaapi.cpp   (StatsResolveIndex rewrite + xprintf include)
M psxsplash-main/src/luaapi.hh   (StatsResolveIndex now private static member)
M psxsplash-main/src/scenemanager.cpp   (apply pushBack.y for floor)
M psxsplash-main/src/scenemanager.hh   (findGameObjectIndex helper)
M psxsplash-main/src/splashpack.cpp   (HurtBoxTableEntry size fix follow-on — verify)
M psxsplash-main/src/splashpack.hh   (HurtBoxTableEntry size_assert 20 → 18)
```

Before commit:
1. `git diff godot-ps1/addons/ps1godot/exporter/SplashpackWriter.cs`
   and `git diff godot-ps1/addons/ps1godot/nodes/PS1Player.cs` —
   these were touched during the deferred cameraAimOffset poke and
   may contain stub fields that should be reverted (or finished —
   reviewer's call).
2. `/caveman-review` the rest of the diff for shape.
3. `/caveman-commit` per fix area — these are independent and
   should land as separate commits (build fix, collision floor,
   Lua flood, StaticBatch reorder, HUD .tscn, etc.). Keep
   `boss_smoke.tscn` as one commit — it's a single demo asset edit.
4. The two `.cs.uid` and `.lua.uid` files under `??` are Godot-
   generated UIDs that should be committed (project tracks them);
   the `.tmp.*` file and `.editor.log` should not.

PCSX-Redux config edit (`pcsx.json`) is outside the repo and is
not under version control. Document the change in the commit
message for any "right stick works now" follow-on so future readers
know the emulator-config dependency.

---

## Memory updates landed this session

- `feedback_camera_over_shoulder.md` — third-person rigs offset
  to right shoulder, never centered-behind.
- `project_psxlua_fixedpoint_no_methods.md` — use `fp._raw`, not
  `fp:raw()`, in any PS1Godot Lua. Metatable has no `__index`.

Both indexed in `MEMORY.md`. Future sessions opening either
PS1Godot or any Lua-on-psyqo project will see these in context.
