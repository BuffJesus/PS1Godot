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

Six patterns where current authoring is awkward (or impossible):

1. **Boss HP / stats** — ✅ landed 2026-05-19. PS1Stats Resource
   carries HP / Stamina / Mana on PS1MeshInstance; Lua's Stats.*
   API queries + mutates.
2. **Hitbox / hurtbox distinction** — currently every `OverlapBox`
   tag is the same.
3. **Damage events** — currently each caller (projectile, melee
   swing, environmental hazard) handles damage application inline.
4. **Lock-on camera** — currently a tag-based marker; no auto-yaw
   or strafe-relative movement.
5. **Twin-stick camera tuning** — twin-stick analog input is
   already wired by default in `Controls::HandleControls`
   (controls.cpp:217–227), but sensitivity, deadzone, and pitch
   clamp are global runtime constants; per-scene tunables and an
   opt-out flag for fixed-camera scenes would close the gap.
6. **Dodge / roll with i-frames** — most of dodge is Lua-side
   recipe (movement override, stamina cost, cooldown) but i-frames
   need runtime collision support — a per-entity invulnerability
   counter that the collision queries skip.

This RFC sketches the surface for each. Implementation is gated
on prioritization — none of this is necessary to ship the first
boss; all of it makes the second boss faster.

## Primitive 1 — `PS1Stats` component  ✅ LANDED (2026-05-19)

**Status:** shipped. Splashpack v33. `PS1Stats.cs` (Resource),
`PS1MeshInstance.Stats` slot, exporter writes 16 B per record,
runtime expands to a dense per-entity array, Lua bindings are 9
methods under `Stats.*` (GetHP / SetHP / GetMaxHP and parallel
Stamina + Mana getters/setters). HP + Stamina + Mana all ship in
v1 — HUD authoring (health/stamina/mana bars) is one coherent
setup. Mana for non-magic games and Stamina for non-action games
default to 0 (every query returns 0; HUD authoring can branch on
MaxStamina/MaxMana to skip those bars).

Live: [Lua API → Stats](https://buffjesus.github.io/PS1Godot/lua-api/stats/).

Original surface below for the record.

### Original surface

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

## Primitive 5 — twin-stick camera tuning on `PS1Player`

!!! warning "Earlier draft was wrong"
    The first draft of this section claimed "right stick is
    exposed but unwired" and proposed a ~2.5-day implementation.
    Re-reading `psxsplash-main/src/controls.cpp:217–227` proved
    that wrong — the right stick **already drives** player yaw
    (X) and pitch (Y, clamped to `[-0.5π, +0.5π]`) every frame in
    `HandleControls`, and the existing `playerRotationY` →
    camera-rig formula in `scenemanager.cpp:947` makes the camera
    follow visibly. So twin-stick is the **default** for analog
    pads, not a missing feature. This primitive is what actually
    *is* missing — per-scene tuning, opt-out for fixed-camera
    scenes, and an authoring-level toggle.

### Context

Twin-stick (left = move, right = camera) already ships for analog
pads via the default `HandleControls` tick:

```cpp
// psxsplash-main/src/controls.cpp:217-227 (paraphrased)
if (abs(rightStickX) > m_stickDeadzone) {
    playerRotationY += (rightStickX * rotSpeed) >> 7 * dt12;
}
if (abs(rightStickY) > m_stickDeadzone) {
    playerRotationX -= (rightStickY * rotSpeed) >> 7 * dt12;
    playerRotationX = clamp(playerRotationX, -0.5_pi, +0.5_pi);
}
```

`m_stickDeadzone`, `rotSpeed`, and the pitch clamp range are
**global runtime constants** today. Fine for the demo. Insufficient
for a project with multiple distinct scenes (slow boss arena
needs a slower turn rate; high-speed scene wants snappy control;
fixed-camera scenes shouldn't move the camera at all).

### Proposed surface

```cs
public enum CameraControlMode
{
    AnalogStick = 0,  // current default — right stick → yaw + pitch
    ButtonsOnly = 1,  // ignore the right stick (fixed-camera scenes,
                      // tank-controls homages, etc.). L1/R1 still rotate.
    LockedOn = 2,     // reserved for primitive 4 (Camera.LockOn).
                      // Camera tracks target; left-stick strafes.
}

[Export] public CameraControlMode CameraControl = CameraControlMode.AnalogStick;
[Export] public int YawSensitivity = 100;    // % of default (100 = current)
[Export] public int PitchSensitivity = 100;  // % of default
[Export] public int StickDeadzone = 32;      // raw fp7 stick units
```

The exporter stamps these fields into the splashpack player
record (one new struct, ~8 bytes). The runtime reads them at
scene init and passes them into `Controls` so `HandleControls`
uses per-scene values instead of globals.

`ButtonsOnly` short-circuits the right-stick block in
`HandleControls` — `playerRotationY` only moves on L1/R1
presses. Cleaner than asking authors to set sensitivity to 0
(which would still consume input).

### Lua surface

```cpp
// Camera.GetControlMode() -> string ("analog" | "buttons" | "lockedon")
// Camera.SetControlMode(name) -> nil
```

For an options menu that lets the player toggle between
analog-stick and button-only at runtime.

### Why engine-side

- **Tuning across scenes** — authors set values in the
  inspector, no per-scene Lua hacks.
- **Fixed-camera opt-out** — a clean `ButtonsOnly` mode for
  Resident Evil / FFVII-style scenes is more readable than
  "set sensitivity to 0."
- **Hook for `LockedOn`** — once primitive 4 lands, the same
  enum lets `Camera.LockOn` swap behavior without a separate
  flag.

### What this is **not**

- Not adding twin-stick (already there).
- Not decoupling camera yaw from player yaw (that's the lock-on
  primitive 4).
- Not adding lock-on (that's primitive 4).

### Implementation cost

- C# enum + inspector fields on `PS1Player`: ~half day.
- Splashpack format bump (one new player record): ~half day.
- Exporter wiring (`SplashpackWriter` emits the new bytes): ~half
  day.
- Runtime reads the new fields, threads them into `Controls`:
  ~half day.
- Optional Lua bindings (`Camera.GetControlMode/SetControlMode`):
  ~half day.

**Total: ~2.5 days. Smallest material primitive after the runtime
default already does most of the work.** Pure additive — no
behavior change for scenes that don't touch the new fields.

## Primitive 6 — dodge / roll with i-frames

### Context

Souls-likes hinge on the dodge mechanic — directional roll with a
brief invulnerability window (i-frames), stamina cost, recovery
frames. The other primitives (PS1Stats for stamina, onDamage for
the damage flow that i-frames suppress) feed this one; landing
those first means dodge slots in cleanly.

Most of the dodge is buildable in Lua today, but **i-frames need
runtime collision support** — the collision system has no concept
of "this entity is currently invulnerable, skip my hit." That's
the engine-side gap this primitive fills.

### What's already buildable in Lua

- **Movement override** — read left-stick at dodge press, lock
  in the direction vector, override `Entity.SetPosition` each
  frame for N frames.
- **Stamina cost** — `Stats.GetStamina(self) - DODGE_COST`,
  via the just-landed Stats API.
- **Cooldown** — a frame counter local in `onUpdate`.
- **Animation trigger** — `SkinnedAnim.Play(self, "dodge_fwd")`
  picked by left-stick quadrant at press time.

### The actual engine gap — i-frame collision

The collision query path (`Physics.Raycast`, `Physics.OverlapBox`,
the runtime's player↔enemy contact resolution) returns hits
without checking any per-entity invulnerability state. The fix is
small but invasive:

```cpp
// In gameobject.hh — add a per-entity i-frame counter
struct GameObject {
    // ... existing fields ...
    uint16_t iframesRemaining = 0;  // decrements each tick; 0 = vulnerable
};
```

Plus collision-side filtering:

```cpp
// In Physics_Raycast / Physics_OverlapBox / CollisionSystem
if (target->iframesRemaining > 0) continue;  // skip — invulnerable
```

Plus a per-tick decrement somewhere in `SceneManager::GameTick`.

### Proposed Lua surface

```cpp
// Controls.StartIFrames(object, frames) -> nil
// Set the entity's invulnerability window to `frames`. Counts down
// at 60 Hz. While > 0, all damage-dispatching collision queries
// skip this entity.
static int Controls_StartIFrames(lua_State* L);

// Controls.IsInvulnerable(object) -> boolean
static int Controls_IsInvulnerable(lua_State* L);
```

Lives under `Controls` not `Stats` because i-frames are a control-
flow state, not a stat value. Could equally land under `Combat.*`
if we add a new namespace.

### Why engine-side

- **Performance** — checking iframesRemaining inside the existing
  collision loops adds one branch per hit candidate. Lua-side
  equivalents would require post-filtering every Physics.Overlap
  result, with the Lua tick cost that implies.
- **Correctness** — the runtime's collision resolution
  (player↔world, enemy↔player damage) doesn't surface through Lua
  at all. Lua-side i-frames can only filter Lua-initiated queries;
  they can't block the runtime's own contact response.

### What dodge looks like end-to-end (with this primitive)

```lua
local DODGE_FRAMES = 18
local IFRAME_WINDOW = 12   -- shorter than dodge duration — late-frame
                           -- dodges are punished
local STAMINA_COST = 25
local DODGE_COOLDOWN = 30

local dodgeFrames = 0
local cooldown = 0

function onUpdate(self, dt)
    if cooldown > 0 then cooldown = cooldown - 1 end

    -- Press to dodge — gated on stamina + cooldown
    if cooldown == 0
       and Input.IsPressed(Input.CIRCLE)
       and Stats.GetStamina(self) >= STAMINA_COST then
        local lx, _ = Input.GetAnalog(Input.LEFT_STICK)
        -- ... compute direction ...
        dodgeFrames = DODGE_FRAMES
        cooldown = DODGE_COOLDOWN
        Stats.SetStamina(self, Stats.GetStamina(self) - STAMINA_COST)
        Controls.StartIFrames(self, IFRAME_WINDOW)
        SkinnedAnim.Play(self, "dodge")
    end

    -- During dodge — manual movement override
    if dodgeFrames > 0 then
        -- ... advance position along stored dir ...
        dodgeFrames = dodgeFrames - 1
    end
end
```

### Implementation cost

- `GameObject.iframesRemaining` field + scene-init init to 0: trivial.
- Per-tick decrement in `SceneManager::GameTick`: ~10 lines.
- Filter in `Physics.Raycast` + `Physics.OverlapBox` + runtime
  collision contact response: ~half day of careful edits across
  collision.cpp + scenemanager.cpp.
- `Controls.StartIFrames` + `Controls.IsInvulnerable` Lua bindings:
  ~half day.
- Documentation update — combat-patterns "Dodge / roll" section
  showing the end-to-end pattern: half day.

**Total: ~2 days.** Smaller than I expected when first writing
this RFC — the rest of dodge is Lua-side recipe that doesn't
need engine support.

## Primitive 7 (deferred) — fog-gate trigger

Not strictly necessary; the combat-patterns recipe shows how to
compose one. But if a soul-like is a primary use case, a
single-node `PS1FogGate` that bundles the trigger + fade-canvas +
input-freeze + persist-flag would save 30 lines of Lua per gate.

Defer until at least 3 encounters have been authored with the
composable pattern — landing it too early picks the wrong shape.

## Ordering

Implementation order if we land more:

1. **`PS1Stats`** ✅ landed 2026-05-19 (v33).
2. **`onDamage` callback** — small follow-up now that Stats is
   live. Consolidates damage flow + i-frame check.
3. **Dodge / roll with i-frames** — needs the per-entity i-frame
   counter the onDamage path would also use, so they batch.
   ~2 days together with onDamage.
4. **`Camera.LockOn` / `Camera.LockOff`** — independent track,
   marquee souls-like feel. ~4 days.
5. **Twin-stick camera tuning** — small win, can land any time.
6. **`PS1HurtBox`** — incremental refinement; works without it
   for first encounters.
7. **`PS1FogGate`** — defer until composable pattern's pain is felt.

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
