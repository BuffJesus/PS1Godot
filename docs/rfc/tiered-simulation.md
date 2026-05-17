# Tiered NPC simulation — design + patch

**Status (2026-05-17):** No implementation yet. GameObject has no
`tier`/`IsTiered`, SceneManager has no `UpdateTiers`, no
`Entity.GetTier`/`SetTier`/`onTierChanged` Lua bindings, no
`PS1Scene.Tier{0,1,2}DistanceMeters` exports. The "v25 splashpack"
format slot named in the doc is moot — we're at v32, real
implementation just appends to v33+. Sized as ~3 commits (runtime
tier evaluator + dispatcher gate + Lua bindings; exporter +
inspector; schedule tick + symbolic sim). Strict consumer of the
still-unshipped `StateMachine` primitive for full leverage, and
naturally pairs with chunk-streaming (Tier 3 = "in an unloaded
chunk") and lod-design (both keyed on player distance with
hysteresis). Deferred to its own session.

Closes the design gap behind the Phase 2.5 AI section in
`ROADMAP.md` and the optimization reference's explicit
recommendation:

> Tiered NPC simulation (Phase 2.5 AI section) directly matches
> the reference's "simulation should be tiered" — high-detail
> near player, cheap elsewhere. Align the `StateMachine`
> primitive so it can run at variable tick rates driven by
> distance-to-player.
> — `docs/ps1_large_rpg_optimization_reference.md`

> A PS1-scale RPG should feel alive through clever scripting and
> selective detail, not brute-force AI everywhere.

The "feel alive" half is what authors want. The "selective
detail" half is how PS1 silicon allows it. This doc spells out
the simulation tiers, the runtime mechanism, and the authoring
surface so all enemies don't run their full update loop every
frame just because they exist.

Drop this file at `docs/tiered-simulation.md`.

## Goal

A town with 20 NPCs feels populated. The 3 nearest run full AI;
the 5 medium-distance ones animate but skip pathing; the 12 far
ones tick once a second to advance a schedule. Frame cost stays
flat regardless of total population, scaling only with the
"active" near-set.

Non-goal: continuous-detail simulation (gradient falloff with
distance). Discrete tiers are easier to author, easier to
profile, and produce the same end result. PS1 budgets reward
discreteness.

## What's in place

- **`Entity.Spawn` / `Entity.Destroy` / pools.** Already
  shipped. The "many template instances activated as needed"
  pattern is the foundation — tiered sim layers on top of it.
- **`Entity.FindNearest(pos, tag)`** — handy primitive for
  "what enemy is closest to the player." Tiered sim leans on
  this for tier assignment.
- **The roadmapped `StateMachine.new({...})` primitive.** Not
  shipped yet, but the design exists in the Phase 2.5 AI
  bullets. This doc shapes its tick-rate dimension.
- **`Physics.Raycast` / `Physics.OverlapBox`.** Already shipped.
  Tier 0 (full simulation) uses these freely; lower tiers don't.
- **`Scene.LoadChunk` / `Scene.UnloadChunk`** (planned in
  `chunk-streaming.md`). Out-of-chunk NPCs become symbolic by
  definition — they don't have a script running because their
  chunk isn't loaded.

## The four tiers

Discrete labels, distance-driven by default but Lua-overridable:

| Tier | Distance | What runs each frame |
| --- | --- | --- |
| 0 — Active | < 8 m | `onUpdate` every frame, full AI, animation, physics. |
| 1 — Visible | 8–20 m | `onUpdate` every 4 frames, simplified pathing, animation runs. |
| 2 — Distant | 20–40 m | `onUpdate` every 30 frames (~ 1 Hz), schedule state only, no animation. |
| 3 — Symbolic | > 40 m, or out-of-chunk | No script callback. State updates via global schedule tick only. |

Distance is from the player to the NPC's position. Tier
thresholds are per-scene defaults (authored on `PS1Scene`),
overridable per-NPC.

The tier numbers are stable across all systems — animation
playback, audio attenuation, and chunk preloading can all
consult `Entity.GetTier(obj)` for consistent decisions.

## Design

Three pieces: a runtime tier evaluator, an authoring surface for
tier-aware scripts, and an integration with the existing
`StateMachine` primitive.

### Runtime tier evaluator

Lives in `SceneManager::Update`, runs once per frame *before*
GameObject scripts:

```cpp
// Pseudo-code in psxsplash::SceneManager::Update():
void UpdateTiers(const psyqo::Vec3& playerPos, int frameCount) {
    for (auto& obj : m_gameObjects) {
        if (!obj->isTiered()) continue;     // opt-in via flag bit
        if (!obj->isActive()) continue;
        
        int32_t dx = obj->position.x.raw() - playerPos.x.raw();
        int32_t dy = obj->position.y.raw() - playerPos.y.raw();
        int32_t dz = obj->position.z.raw() - playerPos.z.raw();
        int64_t dSq = (int64_t)dx*dx + (int64_t)dy*dy + (int64_t)dz*dz;
        
        uint8_t newTier =
            (dSq < tier0DistSq) ? 0 :
            (dSq < tier1DistSq) ? 1 :
            (dSq < tier2DistSq) ? 2 : 3;
        
        // Hysteresis to avoid boundary flicker
        if (newTier != obj->tier) {
            int32_t boundary = boundaryFor(newTier, obj->tier);
            int64_t margin = boundary / 20;  // 5%
            bool crossed = (newTier > obj->tier)
                ? (dSq > boundary + margin)
                : (dSq < boundary - margin);
            if (crossed) {
                obj->tier = newTier;
                obj->setTierChanged(true);  // fire onTierChanged next frame
            }
        }
    }
}
```

GameObject gains one new flag bit (`IsTiered`) and one byte for
the current tier. The squared-distance comparison is the same
trick used by the LOD doc — no isqrt, just integer math.

Tier 0/1/2/3 dist thresholds are scene-level fp24 squared
distances (4-byte each, 16 bytes total in the header).

### Script callback gating

The Lua dispatcher checks the object's tier before invoking
`onUpdate`:

```cpp
void DispatchOnUpdate(GameObject* obj, int frameCount) {
    if (obj->isTiered()) {
        uint8_t period = tierPeriod[obj->tier];   // 1, 4, 30, 0
        if (period == 0) return;                  // Tier 3 — no callback
        if ((frameCount + obj->tierPhase) % period != 0) return;
    }
    InvokeLuaCallback(obj, "onUpdate");
}
```

`tierPhase` is auto-seeded from `objIndex` so adjacent NPCs
don't all run their Tier 1 update on the same frame — same
spreading trick used everywhere else (LOD, bob phase). For a
4-frame Tier 1 with 4 enemies, each runs on a different frame.

When tier changes, a one-shot `onTierChanged(self, oldTier,
newTier)` callback fires the next frame. Authors hook this for
transition behavior — "fade animations out when entering Tier 2,
snap to home position when reaching Tier 3, etc."

### Authoring surface

`PS1MeshInstance` (and friends) get one new property:

```csharp
[ExportGroup("PS1 / Simulation")]
// Enable distance-based simulation tiering. NPCs / enemies want
// this; static props don't. Default off — the runtime cost is
// non-zero per tiered object per frame.
[Export] public bool Tiered { get; set; } = false;

// Maximum tier this object can reach. Important NPCs (named
// quest-givers, bosses) clamp at 1 or 2 — they always run their
// behavior, even at distance. Default 3 = full demotion.
[Export(PropertyHint.Range, "0,3,1")]
public int MaxTier { get; set; } = 3;
```

`PS1Scene` exposes the tier thresholds:

```csharp
[ExportGroup("PS1 / Simulation")]
[Export(PropertyHint.Range, "1,100,0.5,suffix:m")]
public float Tier0DistanceMeters { get; set; } = 8.0f;
[Export(PropertyHint.Range, "1,200,0.5,suffix:m")]
public float Tier1DistanceMeters { get; set; } = 20.0f;
[Export(PropertyHint.Range, "1,500,0.5,suffix:m")]
public float Tier2DistanceMeters { get; set; } = 40.0f;
// Anything past Tier 2 is Tier 3 (symbolic / no callback).
```

Per-scene tuning matters: a tight town wants Tier 0 = 4 m; an
open field wants Tier 0 = 15 m. Same author surface, different
values.

### Authored behavior per tier

Lua-side, scripts can opt in to tier-aware behavior:

```lua
function onUpdate(self, dt)
    local tier = Entity.GetTier(self)
    
    if tier == 0 then
        -- Full simulation
        FullPathfind(self)
        UpdateAnimation(self, dt)
        CheckPlayerThreat(self)
        ApplyPhysics(self, dt)
    elseif tier == 1 then
        -- Cheap pathing; animation still runs
        WalkTowardWaypoint(self)
        -- dt here is ~4× the usual frame dt since this runs every
        -- 4 frames. Scripts that want frame-rate-independent motion
        -- multiply movement speed by 4 when in Tier 1.
    elseif tier == 2 then
        -- Schedule advance only — what should this NPC be doing
        -- at this time of day?
        AdvanceSchedule(self)
    end
    -- Tier 3 never runs this callback at all.
end

function onTierChanged(self, oldTier, newTier)
    if newTier == 0 and oldTier > 0 then
        -- Snapped to detailed simulation — resync animation,
        -- pick fresh waypoint, etc.
        ResetToCurrentScheduleState(self)
    elseif newTier == 3 then
        -- About to stop running. Save state into self.lastKnown
        -- in case the player comes back later.
        self.lastKnown = { pos = self.position, state = self.state }
    end
end
```

For authors who don't write tier-aware code, the default behavior
is sensible: `onUpdate` just runs less often. A simple "wander
randomly" script does the right thing automatically — at Tier 2
the wandering happens every second instead of every frame, which
looks fine at distance.

### Symbolic simulation (Tier 3)

Tier 3 NPCs don't run any per-object callback. Instead, a single
scene-level `onScheduleTick` callback fires once per minute (or
authored interval) and updates *all* Tier 3 NPCs in one pass:

```lua
function onScheduleTick(time)
    -- Tier 3 NPCs: snap to their scheduled location based on
    -- world time of day. No pathing, just position teleports.
    for _, npcId in ipairs(npcsInTown) do
        local target = NpcSchedule.GetLocationFor(npcId, time)
        local obj = Entity.FindByName(npcId)
        if obj ~= nil and Entity.GetTier(obj) == 3 then
            Entity.SetPosition(obj, target)
        end
    end
end
```

This is the "townsfolk live their lives off-screen" pattern —
when the player comes back, NPCs are where they should be
without having simulated continuously.

### StateMachine integration

The Phase 2.5 AI bullet's `StateMachine.new(...)` primitive
needs to know about tiers. Specifically, state update functions
should fire based on the owning object's tier:

```lua
local guard = StateMachine.new(self, {
    patrol = {
        update = function(self, dt) PatrolStep(self) end,
        tickRate = "tier",   -- gated by Entity.GetTier(self)
    },
    chase = {
        update = function(self, dt) ChaseStep(self) end,
        tickRate = 1,        -- always every frame, regardless of tier
    },
})
```

The `tickRate = "tier"` default uses the tier-period table.
`tickRate = 1` overrides to always-on (useful for "chase" — once
the enemy noticed the player, you want responsive behavior
regardless of distance). Author opts in to overrides only when
the gameplay calls for it.

## Implementation stages

Three stages — small, sequential, each independently
testable.

### Stage 1 — Runtime tier evaluator

psxsplash side. `[runtime]` ask — file as a sibling improvement
entry alongside chunk streaming.

- `GameObject.tier` byte + `IsTiered` flag bit.
- `SceneManager::UpdateTiers()` per-frame.
- `tier0DistSq` / `tier1DistSq` / `tier2DistSq` header fields
  (12 bytes added — v25 format if rolled in with chunk
  streaming).
- Lua API: `Entity.GetTier(obj)`, `Entity.SetTier(obj, n)` for
  manual overrides.
- Dispatcher modification to gate `onUpdate` by tier period.
- `onTierChanged` callback dispatched on transitions.

Verifiable: a debug script logs the tier of a known NPC as the
player moves; tier changes match the configured distances with
5% hysteresis.

### Stage 2 — Authoring surface

PS1Godot side. Independent of Stage 1 — exporter just emits the
new fields; runtime ignores them until Stage 1 lands.

- `PS1MeshInstance.Tiered` + `MaxTier` properties.
- `PS1Scene.Tier{0,1,2}DistanceMeters` properties.
- Exporter writes tier fields into the header + per-object flags.
- VRAM viewer Tab 3 (`vram-viewer.md`) gains a column showing
  per-object tiered-ness, so authors see what's tiered.

### Stage 3 — Schedule + symbolic sim

The Tier 3 / `onScheduleTick` story:

- `PS1Scene.ScheduleTickIntervalSeconds` authored property.
- Runtime fires scene-level `onScheduleTick(time)` callback.
- Optional: `PS1NpcSchedule.tres` resource as a shipping
  template for "NPC at home at night, in market at noon"
  schedules. Plain Lua tables work too; the resource is just a
  convenience.

Verifiable: a demo scene with 4 NPCs and one moving player —
nearby NPCs animate, distant ones don't, far ones reposition on
the schedule tick.

## Open questions / tradeoffs

**Animation under tiering.** Tier 1 runs `onUpdate` every 4
frames but the rendering loop runs every frame. Skinned mesh
animations naturally degrade — they'd advance 1 frame of
animation per 4 rendered frames if `SkinnedAnim.Advance` runs
inside `onUpdate`. Two options:

1. Animation runs at full rate regardless of tier — only the
   AI logic ticks slower. Visual coherence preserved.
2. Animation slows to the tier's rate — cheaper but visible.

Default to option 1 (visual coherence). Lua scripts that want
the cheaper path explicitly skip animation calls when tier > 0.

**Tier 0 over-population.** What if 20 enemies cluster around
the player? All Tier 0, all running full AI every frame —
budget blown. Two safety nets:

1. **Tier 0 cap.** Scene-level `MaxTier0Actors` (default 8);
   beyond the cap, the runtime forces additional enemies into
   Tier 1 ranked by distance. Closest 8 stay Tier 0; the rest
   demote.
2. **Frame budgeting.** A `Task.RunOverFrames` style scheduler
   (roadmapped Phase 2.5) caps total per-frame Lua callback
   time and defers overflow to next frame. Independent of
   tiering but composes well.

Ship option 1 with the rest of Stage 1. Option 2 is its own
roadmap item.

**Tier sticking on dynamic objects.** A bullet fired from the
player is technically a GameObject. Should it be tiered? No —
bullets are short-lived and need consistent behavior; mark them
non-tiered (default). Same for projectiles, particles, anything
spawned by gameplay rather than authored as a world inhabitant.

**Per-tier audio attenuation.** A Tier 2 NPC's footstep sounds
shouldn't play — they're too far. Solution: the audio system's
3D positional play (`Audio.Play3D`, already roadmapped) handles
distance attenuation independently. Tiered sim and audio
attenuation stay separate concerns that happen to use the same
distance data.

**Tier 3 across chunks.** When a chunk unloads, all its NPCs
disappear from the runtime — by definition they're "more
symbolic than Tier 3." Their state lives in `Persist` save
data and reconstructs when the chunk reloads. Author writes
this by hook on `onChunkUnload` (chunk-streaming patch
exposes this). The chunk's `onScheduleTick` still runs while
the chunk is loaded.

**Manual tier override use cases.** `Entity.SetTier(obj, 0)`
forces an NPC into full simulation regardless of distance.
Useful for cutscenes, boss arenas, scripted sequences. The
manual override clears on a `Entity.SetTier(obj, -1)` ("auto")
call.

**Profiling tier distribution.** Authors need to see "of my 30
NPCs, 6 are Tier 0, 8 are Tier 1, 4 are Tier 2, 12 are Tier 3"
to know if their thresholds are tuned right. The VRAM viewer
gains a small "Simulation tier distribution" row in Tab 3
(OT pressure). Same data feed.

**Visual cues for distant NPCs.** Tier 2/3 NPCs don't animate
but still render their static mesh. Pairs naturally with LOD:
Tier 2 + LOD2 = a low-poly silhouette standing in place; Tier 3
+ LOD2 = the silhouette teleports to its schedule position
occasionally. The two systems compose; no integration code
needed.

## Suggested entries

### For `docs/psxsplash-improvements.md`

> ### N+M. Distance-based simulation tiering
>
> **Problem.** GameObject `onUpdate` callbacks fire every frame
> for every active object, regardless of distance to the player.
> A scene with 20 NPCs runs all 20 Lua callbacks per frame.
> Most could update at 1/4 or 1/30 the rate without visible
> degradation.
>
> **Why we care.** PS1 budgets don't scale; spending the same
> per-NPC cost on background extras as on combat actors burns
> frame budget. The optimization reference's "simulation should
> be tiered" recommendation needs a runtime hook to enforce.
>
> **Proposed direction.** Add `tier` byte + `IsTiered` flag to
> GameObject (still fits in the 92-byte struct via reserved
> slots). SceneManager evaluates per-frame distance from player,
> assigns Tier 0–3 with hysteresis. Lua dispatcher gates
> `onUpdate` invocation by tier period (1/4/30/never). Header
> grows 12 bytes for the three squared-distance thresholds.
> `Entity.GetTier` / `Entity.SetTier` / `onTierChanged` Lua
> bindings. Full design: `docs/tiered-simulation.md`.
>
> **Status.** Filed. Format bump rolls into the v25 chunk-streaming
> bump if shipped together.
>
> **Evidence.** _(empty until tiered NPCs land in the demo)_

### For `ROADMAP.md` AI building blocks section

> - [ ] **Distance-based simulation tiering.** GameObject opt-in
>       `Tiered` flag + per-scene tier thresholds. Runtime
>       evaluator demotes far NPCs to 1/4 or 1/30 update rates
>       (or symbolic, no callback). `Entity.GetTier(obj)` +
>       `onTierChanged(self, old, new)` Lua callbacks. Pairs
>       with `StateMachine` `tickRate = "tier"` for per-state
>       tick overrides. Full design: `docs/tiered-simulation.md`.

## Changelog

- `2026-05-11` — Document created. Seventh patch doc in the
  series. Closes the AI-tiering design gap referenced in the
  optimization reference. Composes naturally with
  `chunk-streaming.md` (Tier 3 = "in an unloaded chunk") and
  `lod-design.md` (tier + LOD level both driven by player
  distance with hysteresis).
