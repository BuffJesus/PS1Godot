# RFC — Boss encounter primitives

**Status:** proposed
**Date:** 2026-05-19
**Driver:** souls-like boss encounter authoring surfaced four
distinct gaps where current patterns work but feel kludgey enough
to warrant first-class engine support.

## Context

PS1Godot ships solid combat scaffolding —
[`combat_showcase.lua`](https://github.com/BuffJesus/PS1Godot/blob/main/godot-ps1/demo/scripts/combat_showcase.lua){ target="_blank" }
demonstrates projectile + melee + lock-on + screen-shake + hitstop
using only Lua + the existing runtime API. A full Elden Ring–style
boss encounter is shippable today; the recipe is documented in
[`authoring/combat-patterns.md`](https://github.com/BuffJesus/PS1Godot/blob/main/docs/authoring/combat-patterns.md){ target="_blank" }.

But four patterns the recipe describes are awkward enough that
landing them as first-class primitives would meaningfully
streamline future encounter authoring:

1. **Boss HP / stats** — currently per-enemy Lua locals.
2. **Hitbox / hurtbox distinction** — currently every `OverlapBox`
   tag is the same.
3. **Damage events** — currently each caller (projectile, melee
   swing, environmental hazard) handles damage application inline.
4. **Lock-on camera** — currently a tag-based marker; no auto-yaw
   or strafe-relative movement.

This RFC sketches the surface for each. Implementation is gated
on prioritization — none of this is necessary to ship the first
boss; all of it makes the second boss faster.

## Primitive 1 — `PS1Stats` component

### Surface

A `Resource` (not a node) attached to a `PS1MeshInstance` via a
`Stats` slot:

```toml
# In a .tres file
[ext_resource path="res://stats/godrick_stats.tres" type="PS1Stats"]
```

```cs
// godot-ps1/addons/ps1godot/nodes/PS1Stats.cs
[GlobalClass]
public partial class PS1Stats : Resource
{
    [Export] public int MaxHP = 100;
    [Export] public int Poise = 50;
    [Export] public int Stamina = 100;
    [Export] public int Defense = 0;
    [Export] public string Element = "physical";  // physical|fire|frost|...
}
```

### Lua surface

```cpp
// In luaapi.hh
// Stats.GetHP(object) -> int
static int Stats_GetHP(lua_State* L);
// Stats.SetHP(object, hp) -> nil
static int Stats_SetHP(lua_State* L);
// Stats.GetMax(object, statName) -> int
//   statName: "hp" | "poise" | "stamina"
static int Stats_GetMax(lua_State* L);
// Stats.GetPoise(object) -> int
static int Stats_GetPoise(lua_State* L);
// Stats.SetPoise(object, value) -> nil
static int Stats_SetPoise(lua_State* L);
// ... etc.
```

### Splashpack changes

New per-entity table entry (8 B): `{ maxHP: i16, poise: i16, stamina: i16, flags: u16 }`.
Loader DMAs into a fixed-size array indexed by entity index. Lua
gets/sets read/write the array directly.

### Why this is the right shape

- **Pure data resource** — no behavior. Lua decides what to do on
  HP change; the component just stores.
- **Per-entity, not global** — multiple enemies can have different
  HP without script juggling.
- **Stamina / poise included** — same lifecycle as HP. Authors will
  want both anyway; cheaper to land together than retrofit.

### Implementation cost

- C# node + exporter: ~1 day.
- Splashpack v33 bump (append new section): ~half day.
- Runtime side (`stats.cpp`?) + Lua binding: ~1 day.
- Doctor warnings ("`PS1Stats` on a Static-collision mesh — did
  you mean Trigger?"): ~half day.

**Total: ~3 days.** Highest-leverage primitive.

## Primitive 2 — `PS1HurtBox`

### Surface

A child node of a `PS1MeshInstance` with its own local-space AABB.
Lets authors mark "this part of the enemy is a weak point":

```cs
[GlobalClass]
public partial class PS1HurtBox : Node3D
{
    [Export] public Vector3 Size = Vector3.One;
    [Export] public int DamageMultiplier = 100;  // 100 = 1×, 200 = 2× (crit)
    [Export] public string Tag = "default";
}
```

```
Godrick (PS1MeshInstance)
├── PS1HurtBox (Size = (0.5, 0.3, 0.5), Mult = 200, Tag = "head")
└── PS1HurtBox (Size = (1.0, 1.5, 0.5), Mult = 100, Tag = "body")
```

### Lua surface

`Physics.OverlapBox` already returns hit objects; extend to
optionally return hurtbox detail:

```cpp
// Physics.OverlapBoxDetailed({x,y,z}, {x,y,z}) ->
//   array of { object, hurtboxTag, multiplier }
static int Physics_OverlapBoxDetailed(lua_State* L);
```

### Implementation cost

- C# node + exporter (per-entity hurtbox array baked into the
  scene): ~1.5 days.
- Runtime collision query: ~1 day.
- Splashpack v34 bump (hurtbox section): ~half day.

**Total: ~3 days.** Pairs naturally with `PS1Stats` —
`Stats.DealDamage(target, base * multiplier // 100)`.

## Primitive 3 — `onDamage` callback

### Surface

A new runtime-dispatched Lua callback on entities, parallel to
existing `onCreate` / `onUpdate` / `onInteract`:

```lua
-- In a boss's Lua script
function onDamage(self, amount, source)
    -- self = this entity
    -- amount = damage to apply (already multiplied by hurtbox mult)
    -- source = the entity that caused damage, or nil for env damage

    -- Default: subtract from HP
    Stats.SetHP(self, Stats.GetHP(self) - amount)

    -- Phase 2 entry on threshold
    if Stats.GetHP(self) < 50 and not self.phase2_triggered then
        self.phase2_triggered = true
        Cutscene.Play("godrick_phase2")
    end

    -- Game-feel: hitstun
    Controls.SetEntityFrozen(self, 8)  -- 8 frames of stun
end
```

### How damage flows

Replace today's per-caller "destroy the entity" pattern:

```lua
-- OLD (combat_showcase.lua):
if hit and Entity.GetTag(hit.object) == TAG_ENEMY then
    Entity.Destroy(hit.object)
    Camera.ShakeRaw(614, 14)
end

-- NEW:
if hit then
    Stats.DealDamage(hit.object, 10, self.handle)
    -- Stats.DealDamage internally:
    --   - applies hurtbox multiplier from the hit
    --   - calls target's onDamage(self, amount, source)
    --   - if HP <= 0, default behavior fires onDeath then destroys
    Camera.ShakeRaw(614, 14)
end
```

### Implementation cost

- New `Stats.DealDamage` Lua API: ~half day (mostly wiring).
- Runtime callback dispatch via `onDamage`: ~1 day (mirror the
  existing `onInteract` pattern).

**Total: ~1.5 days.** Smallest primitive; gated on `PS1Stats`
landing first.

## Primitive 4 — `Camera.LockOn` / `Camera.LockOff`

### Surface

```cpp
// Camera.LockOn(targetEntity) -> nil
// Future frames: yaw the camera each tick so targetEntity sits
// centered horizontally. Pitch unchanged. Player movement
// becomes strafe-relative (left-stick = orthogonal to
// player→target, not camera-forward).
static int Camera_LockOn(lua_State* L);

// Camera.LockOff() -> nil
// Restore default camera-following behavior.
static int Camera_LockOff(lua_State* L);

// Camera.IsLocked() -> boolean
// Camera.GetLockTarget() -> object or nil
```

### Why engine-side

The Lua loop equivalent works but has issues:

- **Per-frame Lua dispatch cost** — `Camera.SetH(yawToTarget)` in
  `onUpdate` is fine on its own; combined with the per-frame
  player movement + animation + hit detection, the Lua tick gets
  fat fast on PSX clock budgets.
- **Strafe movement** — today's runtime player rig moves
  camera-forward-relative. Lock-on movement should be
  target-relative (the lock-on illusion breaks if pressing left
  doesn't strafe perpendicular to the boss). Requires runtime
  changes anyway; might as well do it once.
- **Visual reticle** — a target-marker overlay (small triangle
  above the locked entity) wants to live in the same render pass
  as the camera, not via Lua-driven UI repositioning.

### Implementation cost

- Runtime camera mode + per-frame yaw target: ~1 day.
- Runtime player movement strafe mode: ~1.5 days (testing
  + edge cases like "what if target is directly below player").
- Visual reticle (a sprite that follows the locked entity in
  screen space): ~1 day.
- Lua bindings: ~half day.

**Total: ~4 days.** Highest impact for souls-like feel but
biggest engine investment.

## Primitive 5 (bonus) — fog-gate trigger

Not strictly necessary; the combat-patterns recipe shows how to
compose one. But if a soul-like is a primary use case, a
single-node `PS1FogGate` that bundles the trigger + fade-canvas +
input-freeze + persist-flag would save 30 lines of Lua per gate.

Defer until at least 3 encounters have been authored with the
composable pattern — landing it too early picks the wrong shape.

## Ordering

Implementation order if we land any of this:

1. **`PS1Stats`** — unblocks every other primitive. Land first.
2. **`onDamage` callback** — small follow-up once Stats is live.
3. **`Camera.LockOn` / `Camera.LockOff`** — independent of Stats,
   land in parallel if appetite is there.
4. **`PS1HurtBox`** — incremental refinement; works without it
   for first encounters.
5. **`PS1FogGate`** — defer until composable pattern's pain is felt.

Stop at any step. The combat-patterns recipe page documents how
to do the things this RFC adds without these primitives — so
"none of this lands" is a valid resting state.

## What changes externally

If 1–3 land:

- **Splashpack v33 + v34** (two appends — Stats section + HurtBox
  section).
- **Lua API delta:** ~12 new bindings under `Stats.*` +
  `Physics.OverlapBoxDetailed` + `Camera.LockOn/LockOff/IsLocked/GetLockTarget`.
- **One new runtime callback:** `onDamage(self, amount, source)`.
- **Two new C# nodes:** `PS1Stats` (Resource), `PS1HurtBox` (Node3D).
- **Documentation:** the combat-patterns recipe page would update
  its "What's *not* easy today" section to reflect what landed.

Each is additive — no existing scenes break.

## Alternatives considered

- **Pure-Lua libraries** instead of engine primitives. Today's
  combat_showcase.lua is exactly that. The audit found this works
  but produces per-encounter scripting copy-paste at 100+ lines
  each. Primitives compress that to ~30 lines.
- **Component composition** like Godot's `Node3D` + `CollisionShape3D`
  + `RigidBody3D`. Already what we do for `PS1MeshInstance` +
  `PS1TriggerBox`. The Stats / HurtBox primitives would join that
  family.
- **A `PS1Enemy` mega-component** wrapping everything. Rejected as
  too prescriptive — projectile-spawners want Stats but not
  HurtBox; environmental hazards want onDamage but not Stats.
  Compose, don't bundle.
