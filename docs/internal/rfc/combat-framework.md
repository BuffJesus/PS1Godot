# RFC — Combat framework: encounter / boss-brain / stat-bar primitives

**Status:** proposed
**Date:** 2026-05-20
**Driver:** A real boss encounter (boss_smoke) shipped on the
existing primitives revealed eleven distinct authoring foot-guns
in one session of debugging. Every fix is now in the demo, but
the *next* boss author will hit the same wall unless we promote
the patterns from "documented in combat-patterns.md" to "the
default, with lints when you deviate."

This RFC is one tier above
[`boss-encounter-primitives.md`](boss-encounter-primitives.md):
that RFC shipped Stats / HurtBox / DealDamage / LockOn / IFrames
as engine primitives. This RFC composes them into framework
surfaces so the user doesn't re-derive the composition for every
boss.

---

## Context — what we just lived through

`docs/internal/handoff-2026-05-19-boss-smoke-debug.md` opened with
"boss attacks before fog gate," "boss doesn't move," and "no
damage gets through" as observable symptoms. Eleven separate
fixes later, every symptom is gone. Each fix taught one
authoring rule the framework needs to bake in:

| Bug | Authoring rule the framework must enforce |
|---|---|
| #1 HP bar premature reveal | UI lifecycle belongs to the encounter, not the boss brain |
| #2 HUD bars wrong z-order | Engine-fixed (reverse-iteration); no authoring impact |
| #5 Boss self-damaged from own attack box | Melee swings auto-skip the attacker |
| #6 Boss aggro'd from frame 1 | Brains need an encounter-active gate |
| #7 distSq via `dx*dx` was 4096× too small | FP math has hidden rescale; expose a `DistanceSqRaw` helper |
| #8 Player had no hurtbox | Doctor lint: PS1Stats without PS1HurtBox is a warning |
| #9 Player could retreat through fog wall | Encounters need a one-way mode |
| #10 `RECOVER_FRAMES` unused, boss never repositioned | State machine cadence isn't optional, it's the default |
| #11 Attack box anchored on player → infinite reach | Melee swings anchor on the attacker, not the target |
| Trigger `self` is a number, not an entity | Document or fix the API surface |
| `Persist.Get` returns nil for unset keys | `or 0` defaulting in helper APIs |

Eleven gotchas. Five of them I (Claude, in this session) tripped
on while writing first-pass fix code, not just diagnosing the
original demo. That's the bar: if I trip the wires, a designer
authoring their second boss absolutely will.

---

## Goals

1. **Painless second boss.** The boss_smoke author thought through
   every rule once. The second boss author should fill in five
   fields and ship.
2. **No new failure modes.** The framework wraps existing
   primitives. Authors who don't use it should be unaffected.
3. **Inspectable.** Designers using the Godot inspector should be
   able to author encounters / stat bars without writing Lua.
4. **Lints catch the silent bugs.** Doctor warnings replace "F5
   and stare at the screen for 10 minutes."
5. **No engine-level breaking changes.** Everything ships as
   additive Lua modules + composite Godot nodes + Doctor checks.
   The boss_smoke demo migrates from hand-written to framework
   in a single PR and the diff is the proof.

Non-goals: node-graph authoring (the existing PS1Graph machinery
gets a `BossBT` graph kind eventually, but it's a separate RFC
once the Lua/node layers have stabilized across 2-3 bosses).

---

## Design overview — five layers

| Layer | Surface | Audience |
|---|---|---|
| L1 | `Combat` Lua module | Brain authors who write Lua |
| L2 | `Encounter` Lua module | Trigger / fog-wall authors |
| L3 | `UI.Bar` Lua module | HUD authors |
| L4 | `PS1Encounter` + `PS1StatBar` composite nodes | Designers who don't write Lua |
| L5 | PS1Doctor lint checks | Everyone — runs at editor time |

L1–L3 ship as plain Lua under `godot-ps1/lua/lib/`. L4 ships as
composite Godot nodes that **lower into** L1–L3 calls at scene
export time — there's no second runtime path, the composite
nodes are pure authoring sugar. L5 ships as `PS1Doctor` checks
attached to existing entity classes.

This split means a designer can drop the composite nodes and the
output is identical to a hand-written script using the modules.
Programmers can drop down to the modules when they need custom
behavior. No two-tier runtime to maintain.

---

## L1 — `Combat` Lua module

`godot-ps1/lua/lib/combat.lua`. Autoloaded in every scene as a
global `Combat` table.

### Why it exists

Three bugs from this session (#5, #7, #11) were all "Claude or
the user wrote the same arithmetic by hand twice, and got it
wrong both times." `Combat` is the once-and-correct version.

### Surface

```lua
-- Distance helpers (fixes Bug #7)
local d = Combat.DistanceSqRaw(posA, posB)
    -- Returns plain int in fp12² units. Matches the *_RADIUS_SQ
    -- threshold convention (32768² = 1073741824 for 8 world units).
    -- Internally: dx._raw * dx._raw + dz._raw * dz._raw to skip
    -- the /4096 rescale FixedPoint.__mul applies.

local in_range = Combat.InRange(posA, posB, world_units)
    -- Squares world_units * 4096 internally. Sugar over
    -- DistanceSqRaw when authoring isn't already in fp12².

-- Melee swing (fixes Bug #5 + Bug #11)
local hits = Combat.MeleeSwing{
    attacker = self,            -- entity firing the swing
    range = 2,                  -- world units; AABB centered on attacker
    damage = 18,
    skip_self = true,           -- default true; opt-out for weird mechanics
    on_hit = function(hit, applied)  -- optional, called per non-self hit
        Camera.ShakeRaw(614, 14)
        Scene.PauseFor(4)
    end,
    on_whiff = function() end,  -- optional, called if hits is empty
}
    -- Returns the filtered hits list for caller inspection.
    -- AABB is `b - range` to `b + range` per axis (centered on
    -- attacker). The "boss reaching out to player" pattern is
    -- still expressible by passing a forward-offset Vec3 instead
    -- of `attacker`, but the default anchors on the swinger so
    -- the swing has finite reach.

-- Chase step
Combat.ChaseStep{
    self = self,
    dx = dx,                    -- already-computed deltas (FP)
    dz = dz,
    speed_fp12 = 128,           -- per-frame step; nominal souls boss = 128
}
    -- One call wraps the (d * step) / 4096 formula. Skips the
    -- y axis (chase is XZ-plane only).

-- Souls-style state machine
local boss = Combat.MeleeBoss{
    self = self,
    aggro_radius = 8,           -- world units; cubed internally to fp12²
    attack_radius = 2,
    tell_frames = 30,           -- windup before swing
    hit_frames = 12,            -- swing-active window
    recover_frames = 30,        -- post-swing chase window (fixes Bug #10)
    swing_damage = 18,
    swing_range = 2,            -- passed to MeleeSwing
    on_phase_change = function(self, from, to) end,
    phases = {
        { hp_ratio = 0.5,       -- below 50% HP, ramp aggression
          tell_frames = 20,
          recover_frames = 20,
          on_enter = function(self) Camera.ShakeRaw(900, 30) end },
    },
}

function onUpdate(self, dt)
    boss:update(self, dt)
end

function onDamage(self, applied, source)
    boss:handleDamage(self, applied, source)
end
```

### What the boss_smoke brain becomes

```lua
local TAG_BOSS = 7

local boss = Combat.MeleeBoss{
    self = "boss_smoke",
    aggro_radius = 8,
    attack_radius = 2,
    tell_frames = 30,
    hit_frames = 12,
    recover_frames = 30,
    swing_damage = 18,
    swing_range = 2,
    phases = {
        { hp_ratio = 0.5, tell_frames = 15, recover_frames = 20,
          on_enter = function(self) Camera.ShakeRaw(900, 30) end },
    },
}

function onCreate(self)
    Persist.Set("smoke_boss_aggro", 0)
end

function onUpdate(self, dt)
    if Persist.Get("smoke_boss_aggro") ~= 1 then return end
    boss:update(self, dt)
end

function onDamage(self, applied, source)
    boss:handleDamage(self, applied, source)
end
```

The original brain was ~200 lines. This is ~25, and the rules
that took eleven F5s to land are inside the library where they
can't be re-broken by the next author.

---

## L2 — `Encounter` Lua module

`godot-ps1/lua/lib/encounter.lua`. Autoloaded as `Encounter`.

### Why it exists

The fog gate had four jobs that all had to be done right:

1. Play music / reveal HP bar (Bug #1 owned this incorrectly).
2. Wake the boss (Bug #6 — Persist flag flip).
3. Block retreat while active (Bug #9 — onTriggerExit snap-back).
4. Handle re-entry edge cases (Bug #9 redux — onTriggerEnter
   guard against re-firing during active fight).

Each was a separate fix. Each is the same pattern for every
encounter. Encounter wraps the lot.

### Surface

```lua
local encounter = Encounter.new{
    id = "smoke_boss",              -- prefix for Persist keys
    boss_tag = TAG_BOSS,            -- optional; reset state on death
    hp_canvas = "boss_hp",          -- shown on enter, hidden on boss death
    music = "boss_theme",
    music_volume = 100,
    sfx_on_enter = "fog_gate_whoosh",
    block_retreat = true,
    trigger_z_raw = -2048,          -- snap-back reference; see "Trigger
                                    --   position gotcha" below
    arena_anchor_z_raw = 0,         -- snap target during retreat
    on_enter_extra = nil,           -- optional callback for custom behavior
}

function onTriggerEnter(self, index)
    encounter:onEnter()
end

function onTriggerExit(self, index)
    encounter:onExit()
end

-- In the boss's onDamage, when the boss dies:
encounter:markCleared()
```

`encounter:onEnter()` runs the Persist check, the music + canvas
+ aggro-flag side effects. `encounter:onExit()` runs the
retreat block. `encounter:markCleared()` flips the dead flag,
hides the HP bar, and (if `block_retreat=true`) reopens the gate.

### Trigger position gotcha

`OnTriggerEnterScript` / `OnTriggerExitScript` pass only the
trigger *index* to Lua, not a GameObject handle. So inside these
callbacks, `self` is a number; `Entity.GetPosition(self)` returns
nil. The Encounter module sidesteps this by requiring authors to
pass `trigger_z_raw` explicitly (derived from the .tscn
transform — see the boss_smoke fog gate's hardcoded `-2048`).

**Open question (see below):** should we extend the runtime so
trigger callbacks receive an entity handle? That removes the
hardcode but is a small breaking change to the existing API.

---

## L3 — `UI.Bar` Lua module

`godot-ps1/lua/lib/ui_bar.lua`. Extends the existing global `UI`
table with stat-bar helpers.

### Why it exists

Every entity with HP wants a bar. Every bar update is the same
3-line dance:

```lua
local canvas = UI.FindCanvas(canvas_name)
local element = UI.FindElement(canvas, element_name)
UI.SetSize(element, (Stats.GetHP(self) * authored_width) / Stats.GetMaxHP(self), height)
```

…times two for HP+stamina, times three for HP+stamina+mana. Plus
the FindCanvas / FindElement results never change at runtime
(canvas/element names are static post-load), so caching them on
first lookup is free.

### Surface

```lua
-- Imperative: call once per onUpdate per bar
UI.UpdateStatBar{
    entity = self,
    canvas = "player_hp",
    element = "hp_fill",
    stat = "hp",            -- one of "hp" | "stamina" | "mana"
    height = 4,             -- defaults to authored fill height
    low_threshold = 0.3,    -- optional
    low_color = {r=199, g=41, b=41},  -- optional flash color
}

-- Declarative: register once in onCreate, auto-updates each frame
UI.BindStatBars(self, {
    {canvas = "player_hp",   element = "hp_fill",      stat = "hp"},
    {canvas = "player_hp",   element = "stamina_fill", stat = "stamina"},
})
-- Bound bars are tracked in a module-private list; UI.TickBars()
-- runs in a scene-level pre-pass before user onUpdate fires, so
-- the per-update boilerplate disappears entirely. Bars
-- automatically unbind when the entity is destroyed (Entity.SetActive
-- false or scene unload).
```

The declarative form covers 90% of stat bars; the imperative form
exists for "bar tied to a non-stat value" (e.g. a charge meter).

### Interpolation (optional, v2)

A `slide_frames = 30` field on `UpdateStatBar` causes the
displayed fill to slide toward the target over N frames instead
of snapping. Implements the "yellow chip damage" / "delayed-fill"
feel from Sekiro / Dark Souls 2 without bespoke per-bar code.
Defer to v2 once the basic helpers prove out.

---

## L4 — Composite Godot nodes

### `PS1Encounter`

A `Node3D` (or simpler `Node`) that bundles a trigger + barrier +
boss reference into one inspector surface.

Exported properties:

| Property | Type | Meaning |
|---|---|---|
| `EncounterID` | `StringName` | Persist key prefix (e.g. "smoke_boss") |
| `BossEntity` | `NodePath` | The boss with PS1Stats |
| `BossHPCanvas` | `NodePath` | PS1UICanvas to reveal on entry |
| `MusicTrack` | `StringName` | Audio track id |
| `SfxOnEnter` | `StringName` | One-shot SFX on first cross |
| `BlockRetreat` | `bool` | Snap player back on outbound exit during active fight |
| `FogWall` | `NodePath` | Optional visual; hidden after boss death |
| `TriggerAABB` | `AABB` | The encounter trigger bounds |
| `ArenaAnchor` | `Vector3` | Snap target during retreat block (Godot world coords) |

At scene-export time, `PS1Encounter` lowers into:

- A `PS1TriggerBox` with the AABB
- An auto-generated Lua script invoking `Encounter.new{...}` with
  the user's properties, plus the two trigger callbacks
- Hidden state for the fog-wall visibility toggle

The runtime sees no new entity type — it's the same Encounter
module under the hood. A designer drops the node, fills the
seven fields, and ships.

### `PS1StatBar`

A `Node` (no transform) that authors a single bar as one
inspector surface, replacing the BG + fill + label tri-element
authoring.

Exported properties:

| Property | Type | Meaning |
|---|---|---|
| `CanvasName` | `StringName` | Parent canvas this bar belongs to |
| `ElementName` | `StringName` | Base name for generated children |
| `X` / `Y` | `int` | Top-left, screen px |
| `Width` / `Height` | `int` | Fill dimensions |
| `Padding` | `int` | BG extent beyond fill on each side (default 2) |
| `FillColor` | `Color` | Authored full-state color |
| `BGColor` | `Color` | Background panel color |
| `LowThreshold` | `float` | 0.0..1.0; below this ratio, use LowFillColor |
| `LowFillColor` | `Color` | Flash color |
| `Label` | `string` | Optional text overlay |
| `LabelColor` | `Color` | Text color |
| `TrackedEntity` | `NodePath` | Entity whose PS1Stats drives the fill |
| `TrackedStat` | `StringName` | "hp" / "stamina" / "mana" |
| `Interpolated` | `bool` | v2; smooth slide on change |

At export time, lowers into 2–3 `PS1UIElement` siblings inside
the named `PS1UICanvas`, and auto-emits the
`UI.BindStatBars(...)` call into the entity's Lua script (or a
sidecar `scene_init.lua`).

### Composability with hand-written scripts

Both composite nodes' Lua emission is opt-out: if the user's
script already calls `Encounter.new{}` or `UI.BindStatBars{}`,
the exporter skips its emission for that prefix. Hand-written and
composite-node usage can coexist within a single scene.

---

## L5 — PS1Doctor lint checks

Live in the editor; surface in the Doctor dock; never block
F5 export, only warn.

| Check | Severity | Trigger | Fix suggestion |
|---|---|---|---|
| `Stats without HurtBox` | Warning | Entity has `PS1Stats` with `MaxHP > 0` but no `PS1HurtBox` children | Quick action: "Add default body HurtBox" |
| `Stats without HUD` | Info | Entity has `PS1Stats.MaxHP > 0` but no `PS1StatBar` references it | "Add HUD bar" or "Mark as AutoHUD=none" |
| `Bar fill exceeds BG` | Warning | `PS1StatBar.Width > PS1StatBar.Width + 2*Padding` or fill at authored width > BG width | Auto-fix to clamp |
| `Encounter without boss` | Error | `PS1Encounter.BossEntity` is null or doesn't have Stats | None; surface to user |
| `Encounter ID collision` | Warning | Two `PS1Encounter` nodes share the same `EncounterID` | Suggest unique IDs |
| `Boss attack range > arena` | Info | Boss's `attack_radius` exceeds floor mesh extent | Designer hint: boss will never miss |
| `Boss missing PS1Lua` | Warning | Entity declared as `PS1Encounter.BossEntity` has no Lua script | Suggest a Combat.MeleeBoss template |
| `Trigger position not authored` | Info | `PS1Encounter` script template uses `Encounter.new{}` without `trigger_z_raw` set | Auto-compute from the trigger AABB at export |
| `Boxed colors near-black on BG` | Info | Fill RGB sum < 32 and BG RGB sum < 32 (readability) | Suggest brighter fill |

Checks 1, 2, 7 alone would have caught Bugs #8, #1, and the
brain-script-needs-to-exist authoring oversight. The rest are
defense-in-depth for the framework's own assumptions.

---

## Migration plan — boss_smoke as the proof

The existing boss_smoke demo is the validation case. Migration
strategy:

1. **Land L1 (Combat)** in a PR. Rewrite `boss_smoke_brain.lua`
   to use `Combat.MeleeBoss{}`. Diff shows ~200 → ~25 lines.
   F5 verifies identical runtime behavior to the post-Bug-#11
   state. Commit.
2. **Land L2 (Encounter)** in a follow-up PR. Rewrite
   `boss_smoke_fog_gate.lua`. Diff shows ~75 → ~10 lines. F5
   verifies identical behavior. Commit.
3. **Land L3 (UI.Bar)** in a third PR. Rewrite both `updateBars`
   functions in `boss_smoke_player.lua` and the `updateHPBar` in
   the brain (now removed because Combat.MeleeBoss handles it).
4. **Land L5 (Doctor)** alongside L1. Doctor produces warnings
   on the unmigrated demo if the migration PRs aren't merged,
   then goes silent once they are.
5. **Land L4 (composite nodes)** when there's a second boss to
   author. Composite nodes are extracted from the second boss's
   actual needs, not speculated.

This sequencing means we never have framework code without a
real caller, and the boss_smoke demo continues to ship working
software at every step.

---

## Implementation phases — what's needed when

### Phase 1 — Foundations (1–2 days)

- `godot-ps1/lua/lib/combat.lua` — DistanceSqRaw, InRange,
  MeleeSwing, ChaseStep helpers (no state machine yet).
- `godot-ps1/lua/lib/ui_bar.lua` — UpdateStatBar, BindStatBars.
- `PS1Doctor` checks 1, 3, 7, 9.
- Migrate `boss_smoke_player.lua` `updateBars` to UI.Bar.

Output: 4 bugs from the session can't recur. Demo is partially
migrated.

### Phase 2 — State machine (1 day)

- `Combat.MeleeBoss` class.
- Migrate `boss_smoke_brain.lua` to `Combat.MeleeBoss`.
- Phases support, on_phase_change callback.

Output: Bug #10 (recovery window) is baked in.

### Phase 3 — Encounter module (1 day)

- `godot-ps1/lua/lib/encounter.lua`.
- Migrate `boss_smoke_fog_gate.lua`.
- Doctor check 4, 5, 8.

Output: Bugs #1, #6, #9 baked in.

### Phase 4 — Composite nodes (2–3 days, gated on second boss)

- `PS1Encounter`, `PS1StatBar` Godot nodes + exporter lowering.
- Doctor check 2.

Output: Designers can author encounters without Lua.

### Phase 5 — Graph kind (deferred to its own RFC)

`BossBT` graph kind for visual state machine authoring. Out of
scope here; mentioned only for completeness. Compose, don't
bundle.

---

## Open design questions

1. **Trigger entity handle.** Should
   `OnTriggerEnterScript` / `OnTriggerExitScript` push a
   GameObject handle alongside the index? Yes, probably — but
   it's a small breaking change to anyone who's already written
   trigger callbacks. Backward-compat path: push the handle as
   the *first* arg and keep the index as the second; existing
   `function onTriggerEnter(self, index)` scripts that ignored
   `self` continue to work, scripts that treated `self` as a
   number break. Audit other demos before flipping.

2. **`Combat.MeleeBoss.self` parameter.** The state machine
   carries module-local state today (`state`, `stateTimer`,
   `phase2Triggered`). With multiple bosses in a scene, that's
   ambiguous. The MeleeBoss class above ducks the question
   ("self = self" in the call) but the implementation needs to
   key off the entity. Options: store state on the entity table
   via a module-private weak map, or push it into PS1Stats
   alongside HP. Lean toward weak map — keeps PS1Stats focused
   on, well, stats.

3. **AutoHUD on PS1Stats.** Tempting to declare "this stats
   resource auto-spawns its HUD bars" on the Resource itself.
   But Resource → UI dependency inverts the layering. Probably
   leave AutoHUD as a separate node (`PS1StatBar`) and let the
   Doctor's "Stats without HUD" check nudge authors. Revisit if
   the friction is high.

4. **Composite node lowering vs. runtime support.** Should
   `PS1Encounter` lower to Lua at export, or should the runtime
   gain a first-class `Encounter` system? Lowering is simpler
   and means the runtime never grows new responsibilities;
   first-class would let triggers receive the entity handle
   naturally. Lean toward lowering for v1, revisit if
   composability hits a wall.

5. **Interpolated stat bars.** Per Sekiro's "yellow chip" damage:
   actual HP drops instantly, displayed HP slides to target over
   N frames. Conceptually clean; needs a per-bar `displayed_hp`
   tracker that ticks each frame. Probably a v2 feature; the
   first wave of bars can be snap-fill.

6. **MeleeSwing return value.** Returns the filtered hits list?
   Doesn't return anything (fire-and-forget)? Returning the list
   matches today's authoring style and supports niche callers
   like "count how many enemies were caught in the swing." Lean
   toward returning, with hits = nil when no hits to keep the
   common case `if hits then ...` simple.

---

## Alternatives considered

- **Pure documentation.** Update `combat-patterns.md` with all
  eleven gotchas and call it a day. Rejected: the doc page is
  already long and the patterns it documents are subtly wrong
  (e.g. attack box at player position). Documenting bugs is
  worse than fixing them.

- **Engine-level boss class in C++.** A `PS1BossEntity` with HP
  state machine in the runtime. Rejected as too prescriptive
  per the existing `boss-encounter-primitives.md` "alternatives
  considered" — boss AI is the most game-specific layer; the
  framework should help, not dictate.

- **Skip L4 composite nodes.** Just ship L1–L3 and let
  designers learn Lua. Rejected because the repo's UI/UX
  philosophy (see `feedback_ui_ux_philosophy` memory) explicitly
  weights "non-intimidating" — Lua-required-to-author-a-boss is
  intimidating.

- **Replace PS1Graph FSM/BT with the Combat module.** Tempting
  because the boss is essentially a state machine. Rejected:
  PS1Graph and Combat occupy different rungs (visual authoring
  vs. Lua composition). The eventual `BossBT` graph kind would
  *compile to* Combat module calls, not replace them.

---

## What ships externally

If Phase 1–3 land:

- **Three new Lua modules** under `godot-ps1/lua/lib/`:
  `combat.lua`, `encounter.lua`, `ui_bar.lua`. Autoloaded as
  globals (`Combat`, `Encounter`, `UI.Bar`).
- **One new doctor pass** with 7-9 checks listed above.
- **Documentation:**
  - New: `docs/authoring/boss-encounters.md` — the
    end-to-end recipe page using only the framework.
  - Updated: `docs/authoring/combat-patterns.md` — gains a
    "Framework helpers" section that points readers at the
    framework before showing the from-scratch primitives.
  - Auto-generated: `docs/lua-api/combat.md`,
    `docs/lua-api/encounter.md` (the existing `gen_lua_api_docs.py`
    will pick them up from header comments).
- **Migrated demo:** `boss_smoke_brain.lua` shrinks from ~200 to
  ~25 lines; `boss_smoke_fog_gate.lua` from ~75 to ~10.

If Phase 4 lands:

- **Two new Godot nodes** under
  `godot-ps1/addons/ps1godot/nodes/`: `PS1Encounter.cs`,
  `PS1StatBar.cs`.
- **Exporter changes** in `SceneCollector.cs` and
  `SplashpackWriter.cs` to lower the composite nodes.
- **Documentation:** `docs/authoring/nodes/ps1-encounter.md`,
  `docs/authoring/nodes/ps1-statbar.md`.

Phase 5 (BossBT graph) is a separate RFC.

---

## How this RFC was written

A real boss encounter (boss_smoke) shipped on the existing
primitives at commit `61b4009` (handoff: 2026-05-19). Eleven
follow-up fixes between commits `b77c487` and the as-yet-
uncommitted Bug #10/#11 fixes brought the demo to a souls-
correct state. This RFC was written immediately after, while
the friction was fresh — that's deliberate. The eleven bugs
ARE the design input.
