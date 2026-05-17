# Object scale animation — design + patch

**Status (2026-05-17):** No implementation yet. GameObject has no
`scaleFp12` field, `setupObjectTransform` has no scale branch,
no `Entity.SetScale`/`GetScale`/`AnimateScale` Lua bindings,
`PS1MeshInstance` has no `InitialScale`/`StaticScale` exports,
and no `TrackType::ObjectScale` exists. Sized as ~3–4 commits
across psxsplash (eventMask split into 16+16 with scaleFp12,
renderer branch, Lua bindings) + PS1Godot (inspector exports,
writer optional scale, Entity.AnimateScale helper) + Stage 3
ObjectScale track type. Deferred to its own session — naturally
pairs with the prerendered-meshes session that unblocks
pulse-scale / spawn-pop usage.

Closes a small but specific gap referenced in
`prerendered-meshes.md` and called out as a Phase 2.5
community ask:

> **Out of scope.** Pulse scale (rhythmic grow / shrink to draw
> attention) and **spawn pop** (scale up on appear) both need
> per-object scale support, which is tracked separately under
> Phase 2.5's "Object scale animation" community ask. Once that
> lands, both become tiny Lua loops driving `Entity.SetScale`.
> — `docs/prerendered-meshes.md`

> **Object scale animation** — Discord feature request,
> psxsplash channel, 2026-04. Authoring-side half inlined into
> ROADMAP's Phase 2.5 rendering options.

This doc spells out what "per-object scale" actually means on PS1
(non-obvious — the GameObject struct is locked at 92 bytes), where
the scale fits, and what authoring + Lua surface ships.

Drop this file at `docs/scale-animation.md`.

## Goal

Pulse a collectible to draw attention. Pop a coin from 0 → 1.2 ×
→ 1.0 × when it spawns. Squash and stretch a character on jump
takeoff and landing. Scale a UI 3D model preview from inventory
when the player hovers it. All cheap, all driven from Lua, all
authorable on the existing PS1Animation track type.

Non-goal: per-axis non-uniform scale on skinned meshes (would
require bone-by-bone scale matrices — expensive and rarely wanted).
Default to uniform scale; per-axis is a Stage 3 follow-up that
only applies to static meshes if a real use case appears.

## Why this isn't trivial

PS1 uses GTE rotation matrices for object transforms, not 4×4
matrices with embedded scale. The current `setupObjectTransform`
in `renderer.cpp` does:

```cpp
psyqo::Matrix33 finalMatrix;
MatrixMultiplyGTE(m_currentCamera->GetRotation(), obj->rotation,
                  &finalMatrix);
writeSafe<PseudoRegister::Translation>(objectPosition);
writeSafe<PseudoRegister::Rotation>(finalMatrix);
```

The 3×3 rotation matrix carries orientation. Position is a
separate `Vec3`. There's no scale anywhere — the renderer
assumes meshes render at their authored size.

To add scale, the rotation matrix needs to be pre-scaled before
upload. That's a `Matrix33` multiplication by a diagonal scale
matrix:

```cpp
psyqo::Matrix33 scaled = obj->rotation;
scaled.vs[0].x = scaled.vs[0].x * obj->scale;  // assume uniform scale
scaled.vs[0].y = scaled.vs[0].y * obj->scale;
scaled.vs[0].z = scaled.vs[0].z * obj->scale;
scaled.vs[1].x = scaled.vs[1].x * obj->scale;
// ... etc, 9 multiplies for the matrix
```

Cheap (9 fp12 mults per object per frame) but non-zero. Worth
gating behind a flag so non-scaled objects pay nothing.

GameObject is 92 bytes with the static assert. Adding `scale`
needs to find space.

## Design

### GameObject placement

GameObject has 4 bytes of `eventMask` declared as "Runtime-only:
Lua event bitmask (set during RegisterGameObject). In the
splashpack binary these 4 bytes are _reserved1 + _reserved2
(zeros)." The eventMask is a runtime computed value — not
something the splashpack carries.

Two practical paths:

**Option A: Cut into eventMask.** Currently 32 bits; uses fewer
than 16 (one bit per callback type). Drop to 16 bits, use the
other 16 for scale (fp12, range 0.0–16.0). The header preserves
the 4 reserved bytes; runtime initializes scale at the same time
it computes eventMask from the loaded script's callback set.

**Option B: Side table.** A separate `ScaleRuntimeState[]` array
parallel to `m_gameObjects`, indexed by GameObject index. 2
bytes per object (fp12 uniform scale). Slightly more cache miss
risk but no GameObject struct surgery.

Pick A. It's the same trick as the LOD design used and it
preserves cache locality during render. eventMask uses ~6 of
its 32 bits today; 16 is plenty for any plausible expansion.

```cpp
// In gameobject.hh:
union {
    uint32_t eventMaskAndScale;
    struct {
        uint16_t eventMask;
        uint16_t scaleFp12;     // 4096 = 1.0× scale; 0 = unscaled (no scale path)
    };
};
```

`scaleFp12 == 0` is the sentinel for "no scale — skip the matrix
multiply in setupObjectTransform." `scaleFp12 == 4096` is the
identity (1.0 ×), which is a no-op visually but exercises the
scale path. Most objects ship with `scaleFp12 == 0` for free.

### Renderer integration

In `setupObjectTransform`:

```cpp
psyqo::Matrix33 finalMatrix;
MatrixMultiplyGTE(m_currentCamera->GetRotation(), obj->rotation,
                  &finalMatrix);
if (obj->scaleFp12 != 0) {
    // Apply uniform scale by multiplying each matrix entry.
    int32_t s = obj->scaleFp12;
    finalMatrix.vs[0].x.value = (finalMatrix.vs[0].x.value * s) >> 12;
    finalMatrix.vs[0].y.value = (finalMatrix.vs[0].y.value * s) >> 12;
    finalMatrix.vs[0].z.value = (finalMatrix.vs[0].z.value * s) >> 12;
    finalMatrix.vs[1].x.value = (finalMatrix.vs[1].x.value * s) >> 12;
    // ... 9 entries total
}
writeSafe<PseudoRegister::Rotation>(finalMatrix);
```

9 mults + 9 shifts per scaled object per frame. Inlined,
~100 cycles. For 20 scaled objects on screen, that's ~2000
cycles — fraction of a percent of a frame at 30 fps.

### Authoring surface

`PS1MeshInstance` (and friends — `PS1MeshGroup`, `PS1SkinnedMesh`,
`PS1Sprite`) gets an export:

```csharp
[ExportGroup("PS1 / Scale")]
// Initial uniform scale. 1.0 = author size; values outside
// [0.1, 10.0] are unusual but legal. Runtime cost is per-frame
// matrix multiplies — turn off (set to 1.0) for unchanging-scale
// objects to skip the scale path entirely.
[Export(PropertyHint.Range, "0.1,10.0,0.01")]
public float InitialScale { get; set; } = 1.0f;

// When true, the runtime treats this object as never scaled —
// scaleFp12 stays 0, renderer takes the fast path. Default true
// for objects whose InitialScale is exactly 1.0 (most of them);
// flip to false explicitly when authoring "this will be scaled
// at runtime even though it starts at 1×."
[Export] public bool StaticScale { get; set; } = true;
```

Authors who want a runtime-animated scale set `StaticScale =
false`. The default of `true` means the renderer's scale path
adds zero cost to existing scenes.

### Lua API

```lua
-- Get / set uniform scale. Setting auto-clears StaticScale.
Entity.GetScale(obj)         -- returns number (fp12 / 4096 as Lua float)
Entity.SetScale(obj, s)      -- s is a Lua number; 1.0 = author size

-- Convenience: spawn pop animation. Lerps from `from` to 1.0 over
-- `frames` frames, then to `to` over another `frames` frames.
-- Returns a handle that can be cancelled mid-flight.
local h = Entity.AnimateScale(obj, 0.0, 1.2, 6)  -- pop
-- Equivalent to a 12-frame two-segment lerp with the overshoot.

-- Cancel an in-flight animation.
Entity.CancelScaleAnimation(h)
```

Implementation note: `AnimateScale` is a thin convenience over
a per-object animation state that updates inside SceneManager.
Authors who want more control write their own lerp in Lua.

### Animation track

The existing `PS1Animation` system supports position and rotation
tracks. Add `TrackType::ObjectScale`:

```cpp
enum class TrackType : uint8_t {
    Position = 0,
    Rotation = 1,
    UVScroll = 2,        // (roadmapped texture animation)
    FrameSwap = 3,       // (roadmapped texture animation)
    Scale = 4,           // NEW — uniform scale lerp
};
```

Keyframes carry a single fp12 scale value. Track playback lerps
between keyframes the same way Position does. Authors get
authored scale animations for cutscenes / scripted moments
without writing Lua.

### Where scale composes well

- **Pulse-scale collectibles** (`prerendered-meshes.md`'s
  out-of-scope item): Lua loop, ~8 lines.
- **Spawn pop** for any Entity.Spawn: `AnimateScale(obj, 0, 1.0, 6)`
  on the `onEnable` hook.
- **Hit reaction**: `AnimateScale(self, 1.0, 0.85, 2)` on hurt,
  then back to 1.0. Squash without skinning.
- **UI model previews** (`PS1UIModel`): grow on hover, shrink on
  unhover. The UI 3D widget already orbits; adding scale is one
  more property.
- **Cutscene flourish**: tween a banner's logo from 0 → 1 over
  30 frames at scene-start.
- **Pickup "loot beam"**: scale-pulse a star sprite at the
  drop location for 2 seconds to draw the eye.

All of these are ~8–15 line Lua snippets once the primitive
exists. None of them justify a new system; they all justify the
one primitive.

## Implementation stages

Tiny scope, three stages.

### Stage 1 — Runtime scale field + renderer path

psxsplash side. `[runtime]` ask.

- Repurpose 2 of the 4 reserved GameObject bytes for `scaleFp12`.
- `setupObjectTransform` gains the scale branch (gated on
  `scaleFp12 != 0`).
- Lua API: `Entity.GetScale(obj)` / `Entity.SetScale(obj, s)`.
- Update the splashpack writer to optionally write a non-zero
  initial scale.

Verifiable: Lua snippet that pulses a cube — `Entity.SetScale(self,
1.0 + 0.2 * math.sin(Scene.Time() * 4))` — produces a visibly
pulsing cube on PSX.

### Stage 2 — Authoring + convenience helpers

PS1Godot side.

- `PS1MeshInstance.InitialScale` + `StaticScale` properties.
- Exporter writes scale value when `StaticScale = false` or
  `InitialScale != 1.0`.
- Lua helper: `Entity.AnimateScale(obj, from, to, frames)` plus
  cancel.
- Demo: a coin prefab with a spawn-pop and idle pulse, ~10 lines
  of Lua.

### Stage 3 — Scale animation track

- `TrackType::ObjectScale` in the runtime.
- `PS1AnimationTrack.TargetProperty = Scale` option in the
  exporter.
- Cutscene support: scale tracks compose with position / rotation
  tracks in the same `PS1Cutscene`.

## Open questions / tradeoffs

**Uniform vs per-axis.** Uniform is simpler, cheaper, and covers
99 % of use cases (pulse, pop, squash). Per-axis would require
either 3 separate scale values (12 bytes per object — doesn't fit
in the reserved struct space) or a different storage scheme
(side table — see Option B above). Defer per-axis until a real
use case appears; document the limitation.

**Scale and BVH cull.** The BVH AABB is built against authored
mesh size. A 2× scaled object spills outside its AABB. Two
fixes:

1. *Conservative AABB.* Inflate the object's AABB by the maximum
   anticipated scale. Authors set `MaxRuntimeScale = 1.5` so
   the AABB inflates 1.5×. Cheap and good enough.
2. *Per-frame AABB update.* When scale changes, recompute the
   AABB. Cheap (8 corners scaled by the same factor) but more
   per-frame work.

Default to option 1. Authors who animate scale beyond their
authored `MaxRuntimeScale` get a small render artifact (early
cull at the AABB edge) — never a crash.

**Skinned meshes.** Skinning happens per-vertex via `rtps`; the
object-level scale on a skinned mesh would only affect the
root transform, which is the SkinAnimSet's bone-0 frame. The
end visual is "the whole character scales uniformly" — fine for
hit reactions, problematic for "scale just the head." Per-bone
scale is its own feature; not v1.

**Scale and collision.** AABB colliders don't track runtime
scale by default. A 2× scaled hit-pickup has the same collision
radius as a 1× one. Two fixes:

1. *Author the collider for max scale.* Static-size collider,
   pickup feels generous at small scales.
2. *Sync collider on scale change.* Runtime updates the collider
   AABB when scale changes. Extra work but predictable.

Default to option 1; most "pulsing collectible" cases want a
generous static hitbox anyway. Lua API for the rare cases:
`Entity.UpdateCollider(obj)` after a scale change.

**Negative scale.** -1.0 scale flips the mesh (winding inverts).
Useful for left/right hand variants without authoring two
meshes. Not v1 — needs a winding-flip pass in the renderer.
Document as a "later" feature.

**Animation curve.** Linear lerp between keyframes is the v1
behavior. Ease-in / ease-out / spring would be nicer for pop
animations. Two paths: add ease modes to the animation track
(structural) or write Lua ease helpers (cheap). Lua helpers
are the right v1; spring physics belongs in a follow-up.

**Cost ceiling.** 9 mults + shifts per scaled object per frame
is fine for ~50 scaled objects. Beyond that, consider:

- *Sleep-when-static.* When scale isn't changing this frame,
  skip the matrix multiply — use a cached pre-scaled matrix.
- *Tier integration.* `Tiered` objects in Tier 2/3 skip scale
  updates entirely (they probably aren't visibly pulsing
  anyway).

Optimize when measured. The naive path is fine for any plausible
PS1 scene.

## Suggested entries

### For `docs/psxsplash-improvements.md`

> ### N+M. Per-object uniform scale
>
> **Problem.** GameObject transforms support position + rotation
> but not scale. Pulse-scale collectibles, spawn-pop animations,
> squash-and-stretch hit reactions, and other "scale a thing
> over time" effects can't be expressed without per-vertex
> remesh hacks.
>
> **Why we care.** PS1-era games used scale effects liberally
> (Crash Bandicoot's stretch animations, Spyro's flame-breath
> scale-up). The optimization reference's "memorable effect
> timing" advice over "raw effect density" points at exactly
> this — one well-timed scale pulse beats ten new mesh assets.
>
> **Proposed direction.** Repurpose 2 of the 4 reserved
> GameObject bytes (currently `_reserved1` / `_reserved2`) as
> `scaleFp12` (fp12 uniform scale; 0 = sentinel for "no scale,
> fast path"). `setupObjectTransform` pre-scales the rotation
> matrix when `scaleFp12 != 0` — 9 mults + 9 shifts per scaled
> object per frame. Lua API: `Entity.GetScale` /
> `Entity.SetScale` / `Entity.AnimateScale`. New animation
> track type `ObjectScale` for authored scale curves. Full
> design: `docs/scale-animation.md`.
>
> **Status.** Filed. No splashpack format bump required for the
> runtime — uses existing reserved bytes. Authoring side does
> bump the writer to optionally populate the slot (v25 if
> rolled with chunk streaming, else its own bump).
>
> **Evidence.** _(empty until a real demo uses scale pulse)_

### For `ROADMAP.md`

> - [ ] **Object scale animation.** Uniform per-object scale
>       stored in reserved GameObject bytes, applied during
>       `setupObjectTransform`. `Entity.SetScale` / `GetScale`
>       Lua bindings + `AnimateScale` convenience for spawn-pop
>       / pulse / squash patterns. New `ObjectScale` track type
>       for authored animation. Composes with prerendered-mesh
>       collectibles (pulse scale + spawn pop) and PS1Sprite
>       (UI feedback). Full design: `docs/scale-animation.md`.

## Changelog

- `2026-05-11` — Document created. Eighth patch doc in the
  series. Unblocks pulse-scale + spawn-pop from
  `prerendered-meshes.md`'s deferred list. Smallest of the
  patch docs — focused on one primitive.
