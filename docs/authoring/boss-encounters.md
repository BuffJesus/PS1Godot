# Boss encounters

A souls-style boss encounter — fog wall, music swell, HP bar
reveal, committed-attack AI, no retreat until victory — is
buildable today on the existing combat primitives. This page is
the end-to-end recipe.

The reference implementation is
[`godot-ps1/demo/boss_smoke/`](https://github.com/BuffJesus/PS1Godot/tree/main/godot-ps1/demo/boss_smoke){ target="_blank" } —
the scene, the brain, the player, the fog gate, the HUD. Read
this page, then read the demo; everything maps 1:1.

Prerequisites:

- [Combat patterns](combat-patterns.md) — the underlying APIs
  (`Physics.OverlapBoxDetailed`, `Camera.ShakeRaw`, `Stats.DealDamage`,
  hurtboxes, lock-on).
- [PS1TriggerBox](nodes/ps1-trigger-box.md) — the fog-gate
  trigger.
- [PS1UICanvas](nodes/ps1-ui-canvas.md) — the boss HP bar.

The pattern below was hardened by debugging eleven distinct
foot-guns in a single session (see
[combat-patterns.md → "Foot-guns observed in production"](combat-patterns.md#foot-guns-observed-in-production)
for the gotcha list). Follow this recipe and you skip them.

## The cast

Six nodes in your `.tscn`:

| Node | Purpose |
|---|---|
| `Floor` (PS1MeshInstance, `Collision=Static`) | Arena ground |
| `Player` (Node3D + PS1Player + Avatar child + HurtBox child) | Who you control |
| `Boss` (PS1MeshInstance + PS1Stats + ScriptFile + HurtBox children) | Who you fight |
| `FogWall` (PS1MeshInstance, `Collision=None`) | Visible barrier — passable |
| `FogGateTrigger` (PS1TriggerBox + ScriptFile) | Encounter trigger — fires Lua callbacks |
| `BossHPCanvas` (PS1UICanvas, `VisibleOnLoad=false`) | Top-of-screen HP bar |

Optional but recommended:

| Node | Purpose |
|---|---|
| `ControlsHelpCanvas` (PS1UICanvas, `VisibleOnLoad=true`) | First-run controls overlay |
| `PlayerHPCanvas` (PS1UICanvas) | Bottom-left HP / stamina HUD |
| `Sky` (PS1Sky) | Atmospheric tint |

## The four scripts

| Script | Hosted on | Job |
|---|---|---|
| `boss_smoke_fog_gate.lua` | FogGateTrigger | Encounter lifecycle (music, HP bar, AI wake-up, retreat block) |
| `boss_smoke_brain.lua` | Boss | State machine — IDLE / AGGRO / TELL / HIT / RECOVER / PHASE2 / DEAD |
| `boss_smoke_player.lua` | Player/Avatar | Input, HUD bar updates, dodge, melee |
| `boss_smoke_player_input.lua` *(if controls overlay)* | Player | First-run X-dismiss of controls panel |

## Authoring sequence

### 1. Build the arena floor + boss spawn position

A `PlaneMesh` with `subdivide_width=8` `subdivide_depth=8`
(162 tris, fits in GTE budget). Static collision. Center at
world origin.

Spawn the boss at a clear-line-of-sight position from where
the player will enter. The demo uses `Boss.transform.origin =
(0, 1, -6)` Godot (player spawn at `(0, 1, 8)` so the boss is
~14 Godot meters away on first glimpse).

### 2. Author the fog wall + trigger

The fog wall is **visual only** (`Collision = None`). The
trigger is a separate `PS1TriggerBox` co-located with the wall
that fires Lua callbacks when the player crosses.

```ini
[node name="FogWall" type="MeshInstance3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 2)
mesh = SubResource("fog_plane")
material_override = ExtResource("preview_mat")
script = ExtResource("ps1_mesh")
Collision = 0    ; passable — the trigger handles entry
Tag = 0
UVScrollSpeed = Vector2(12, 8)   ; animates the wall

[node name="FogGateTrigger" type="Node3D" parent="."]
script = ExtResource("ps1_trigger_box")
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 2)
AABB = AABB(-1, 0, 1, 2, 2, 2)
ScriptFile = "res://demo/scripts/boss_smoke_fog_gate.lua"
```

The trigger AABB extends through the wall mesh by 1 unit each
side — players can't walk past the wall without crossing the
trigger.

### 3. Boss with hurtboxes and stats

The boss needs three things to be combat-ready:

1. A `PS1Stats` resource with `MaxHP > 0` (e.g. 200).
2. At least one `PS1HurtBox` child so `Physics.OverlapBoxDetailed`
   can find it.
3. A `ScriptFile` pointing at the brain.

```ini
[node name="Boss" type="MeshInstance3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, -6)
mesh = SubResource("cube_2")
script = ExtResource("ps1_mesh")
Collision = 1
Tag = 7        ; TAG_BOSS — used by Camera.LockOn + retreat block
Stats = ExtResource("boss_stats")        ; the .tres
ScriptFile = "res://demo/scripts/boss_smoke_brain.lua"

[node name="HeadHurtBox" type="Node3D" parent="Boss"]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, -0.8, 0)
script = ExtResource("ps1_hurtbox")
Size = Vector3(0.5, 0.3, 0.5)
DamageMultiplier = 200   ; 2× damage on head crits

[node name="BodyHurtBox" type="Node3D" parent="Boss"]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0)
script = ExtResource("ps1_hurtbox")
Size = Vector3(0.8, 0.6, 0.5)
DamageMultiplier = 100   ; baseline

[node name="LegsHurtBox" type="Node3D" parent="Boss"]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0.8, 0)
script = ExtResource("ps1_hurtbox")
Size = Vector3(0.6, 0.4, 0.5)
DamageMultiplier = 50    ; legs are 0.5× — soft target
```

The multipliers are `(applied_damage = base_damage × multiplier / 100)`. Three hurtboxes is plenty; one body box would also work for
a non-critable boss.

### 4. Player with hurtbox

**Easy to forget** — the player avatar also needs a `PS1HurtBox`
or it can't be damaged. A single body-sized box is fine for v1:

```ini
[node name="BodyHurtBox" type="Node3D" parent="Player/Avatar"]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0)
script = ExtResource("ps1_hurtbox")
Size = Vector3(0.8, 1.6, 0.8)
DamageMultiplier = 100
```

Without this, every boss swing whiffs silently — combat looks
broken but neither HP bar moves and the screen still shakes
(because the boss's tell shake fires regardless of whether the
swing connects).

### 5. The fog gate script

The fog gate owns the **encounter lifecycle**: music, HP bar
reveal, boss wake-up, retreat block, post-death cleanup.

```lua
-- boss_smoke_fog_gate.lua
--
-- IMPORTANT: the runtime's OnTriggerEnterScript / OnTriggerExitScript
-- pass only the trigger INDEX as the first arg, not a GameObject
-- handle. So `self` inside these callbacks is a number, not an entity
-- table — Entity.GetPosition(self) would return nil. The trigger's
-- world position is hardcoded below from the .tscn authoring.

-- FogGateTrigger transform: Godot (0, 1, 2). The runtime maps Godot
-- z * -1024 → fp12 PSX z. So trigger center fp12 z = 2 * -1024 = -2048.
local TRIGGER_Z_RAW = -2048

function onTriggerEnter(self, index)
    -- Post-kill: gate stays open, no re-trigger.
    if Persist.Get("smoke_boss_dead") == 1 then return end
    -- Re-entry during active fight (after retreat snap-back) — don't
    -- re-fire music + HP bar reveal.
    if Persist.Get("smoke_boss_aggro") == 1 then return end

    Audio.PlaySfx("fog_gate_whoosh")
    Music.Play("boss_theme", 100)

    -- Reveal the boss HP bar; the brain only resizes the fill.
    local hpCanvas = UI.FindCanvas("boss_hp")
    if hpCanvas >= 0 then
        UI.SetCanvasVisible(hpCanvas, true)
    end

    -- Wake the boss. brain.onUpdate gates on this Persist flag and
    -- stays dormant until it flips to 1 — keeps the boss in IDLE
    -- (no chase, no tell shake) while the player is still outside
    -- the arena.
    Persist.Set("smoke_boss_aggro", 1)
end

function onTriggerExit(self, index)
    -- One-way encounter wall. During an active fight (post-cross,
    -- pre-kill), the fog wall is solid from the inside — retreating
    -- through it snaps the player back into the arena.
    if Persist.Get("smoke_boss_aggro") ~= 1 then return end
    if Persist.Get("smoke_boss_dead") == 1 then return end

    local p = Player.GetPosition()

    -- Demo layout: boss sits at higher fp12 z than the trigger;
    -- spawn at lower. If the player exited with z below the trigger
    -- center, they retreated toward spawn. Snap them back to fp12
    -- z=0 (~1024 raw past the trigger AABB top edge) preserving x,
    -- and shake the camera for thud feedback.
    if p.z._raw < TRIGGER_Z_RAW then
        Player.SetPosition(Vec3.new(p.x, p.y, 0))
        Camera.ShakeRaw(82, 4)
    end
end
```

Key things this script does and one common mistake to avoid:

- **Ownership of the HP bar is here**, not in the brain. Putting
  it in the brain causes the bar to appear on frame 1 (the brain
  runs from frame 1 regardless of the player's position).
- **`Persist.Set("smoke_boss_aggro", 1)`** is the encounter
  start signal. The brain's `onUpdate` returns early when this is
  ≠ 1, keeping the boss truly dormant pre-cross.
- **`onTriggerExit` enforces the one-way wall.** Note the
  hardcoded `TRIGGER_Z_RAW` — the trigger callbacks don't pass
  the trigger entity to Lua, so you can't query its position at
  runtime. Read the value off the .tscn transform once and bake it.
- **`Persist.Get` returns nil for keys that have never been
  set.** This is fine for the `== 1` and `~= 1` checks (`nil ~= 1`
  is just true). It's only a problem if you try to concatenate or
  do arithmetic — default with `or 0` then.

### 6. The boss brain

The brain runs the state machine. Six states, each with a clear
purpose:

| State | Purpose | Exit |
|---|---|---|
| `IDLE` | Boss dormant (pre-fog-gate or out-of-aggro) | `distSqRaw < AGGRO_RADIUS_SQ` → AGGRO |
| `AGGRO` | Chase player; transition to TELL when in attack range AND not recovering | `distSqRaw < ATTACK_RADIUS_SQ` AND `stateTimer ≤ 0` → ATTACK_TELL |
| `ATTACK_TELL` | Windup. Tell shake fires on entry. | `stateTimer ≤ 0` → ATTACK_HIT |
| `ATTACK_HIT` | Swing active. `fireAttack` runs once on entry. | `stateTimer ≤ 0` → AGGRO (with RECOVER_FRAMES preloaded) |
| `PHASE2` | HP < threshold; same machine, faster cadence | HP ≤ 0 → DEAD |
| `DEAD` | Frozen, hide HP bar, mark Persist | (terminal) |

```lua
-- boss_smoke_brain.lua

local STATE_IDLE        = 0
local STATE_AGGRO       = 1
local STATE_ATTACK_TELL = 2
local STATE_ATTACK_HIT  = 3
local STATE_PHASE2      = 4
local STATE_DEAD        = 5

-- Radii in raw fp12² to match distSqRaw below.
-- 8 world units → 8 * 4096 = 32768 fp12 → squared = 1,073,741,824.
-- 2 world units → 2 * 4096 = 8192 fp12 → squared = 67,108,864.
-- NOTE: FixedPoint.__mul rescales (a.raw * b.raw) / 4096, leaving
-- the product in fp12 not fp12². bossToPlayer below sidesteps that
-- by multiplying the .raw ints directly, so the comparison units
-- line up.
local AGGRO_RADIUS_SQ  = 1073741824
local ATTACK_RADIUS_SQ = 67108864
local TELL_FRAMES    = 30   -- windup
local HIT_FRAMES     = 12   -- swing-active
local RECOVER_FRAMES = 30   -- post-swing chase window
local SWING_DAMAGE   = 18

local state = STATE_IDLE
local stateTimer = 0
local phase2Triggered = false

local function bossToPlayer(self)
    local b = Entity.GetPosition(self)
    local p = Player.GetPosition()
    local dx = p.x - b.x
    local dz = p.z - b.z
    -- Raw-int squared distance to match *_RADIUS_SQ units. Don't
    -- write `dx*dx + dz*dz` here — FP.__mul scales the result
    -- by /4096 and you end up 4096× under-range.
    local distSqRaw = dx._raw * dx._raw + dz._raw * dz._raw
    return dx, dz, distSqRaw
end

local function faceToward(self, dx, dz)
    Entity.SetRotationY(self, Math.Atan2(dx, dz))
end

local function updateHPBar(self)
    local canvas = UI.FindCanvas("boss_hp")
    if canvas < 0 then return end
    local bar = UI.FindElement(canvas, "fill")
    local maxHP = Stats.GetMaxHP(self)
    if maxHP > 0 then
        UI.SetSize(bar, (Stats.GetHP(self) * 280) / maxHP, 4)
    end
end

-- Swing volume anchored on the BOSS — not the player. Anchoring on
-- the player gives the swing infinite reach (the AABB always wraps
-- the target). Anchoring on the attacker means players who step
-- out during the 30-frame tell genuinely dodge.
local function fireAttack(self)
    local b = Entity.GetPosition(self)
    local minV = Vec3.new(b.x - 2, b.y - 1, b.z - 2)
    local maxV = Vec3.new(b.x + 2, b.y + 2, b.z + 2)
    local hits = Physics.OverlapBoxDetailed(minV, maxV)
    for i = 1, #hits do
        -- Skip self: boss's own hurtboxes are inside this AABB,
        -- so without the filter every swing rolls SWING_DAMAGE
        -- into the boss's own HP — boss kills itself in ~11 cycles.
        if hits[i].object ~= self then
            local applied = Stats.DealDamage(hits[i].object, SWING_DAMAGE, self)
            if applied > 0 then
                Camera.ShakeRaw(614, 14)
                Scene.PauseFor(4)
            end
        end
    end
end

function onCreate(self)
    -- Reset on scene (re)load so the encounter starts dormant.
    Persist.Set("smoke_boss_aggro", 0)
    Debug.Log("boss brain ready — HP " .. Stats.GetMaxHP(self))
end

function onUpdate(self, dt)
    if state == STATE_DEAD then return end
    -- Encounter gate: stay frozen until the fog wall has been
    -- crossed. Without this the boss aggros from frame 1.
    if Persist.Get("smoke_boss_aggro") ~= 1 then return end

    updateHPBar(self)

    local dx, dz, distSqRaw = bossToPlayer(self)

    -- Face the player whenever not IDLE.
    if state ~= STATE_IDLE then
        faceToward(self, dx, dz)
    end

    if state == STATE_IDLE then
        if distSqRaw < AGGRO_RADIUS_SQ then
            state = STATE_AGGRO
            stateTimer = 0
        end

    elseif state == STATE_AGGRO then
        -- Chase if recovering OR out of range. The recovery window
        -- (set on HIT→AGGRO transition) keeps the boss stepping
        -- toward the player between attacks instead of camping at
        -- the exact attack-range edge.
        local doStep = stateTimer > 0 or distSqRaw > ATTACK_RADIUS_SQ
        if doStep then
            if stateTimer > 0 then stateTimer = stateTimer - 1 end
            local b = Entity.GetPosition(self)
            local step = 4096 / 32   -- ~0.03 units/frame
            Entity.SetPosition(self, Vec3.new(
                b.x + (dx * step) / 4096,
                b.y,
                b.z + (dz * step) / 4096))
        else
            state = STATE_ATTACK_TELL
            stateTimer = TELL_FRAMES
        end

    elseif state == STATE_ATTACK_TELL then
        if stateTimer == TELL_FRAMES then
            Camera.ShakeRaw(82, 4)   -- subtle tell shake
        end
        stateTimer = stateTimer - 1
        if stateTimer <= 0 then
            state = STATE_ATTACK_HIT
            stateTimer = HIT_FRAMES
            fireAttack(self)
        end

    elseif state == STATE_ATTACK_HIT then
        stateTimer = stateTimer - 1
        if stateTimer <= 0 then
            state = STATE_AGGRO
            stateTimer = RECOVER_FRAMES   -- the recovery chase window
        end

    elseif state == STATE_PHASE2 then
        -- Faster cadence; rest of the machine is the same.
        local doStep = stateTimer > 0 or distSqRaw > ATTACK_RADIUS_SQ
        if doStep then
            if stateTimer > 0 then stateTimer = stateTimer - 1 end
            local b = Entity.GetPosition(self)
            local step = 4096 / 20   -- faster than phase 1
            Entity.SetPosition(self, Vec3.new(
                b.x + (dx * step) / 4096,
                b.y,
                b.z + (dz * step) / 4096))
        else
            state = STATE_ATTACK_TELL
            stateTimer = TELL_FRAMES / 2
        end
    end
end

-- Runtime fires onDamage after the HP debit lands.
function onDamage(self, applied, source)
    -- Brief invuln so a flurry of hits can't insta-trigger phase 2.
    Controls.StartIFrames(self, 6)

    local hp = Stats.GetHP(self)
    local maxHP = Stats.GetMaxHP(self)

    if hp <= 0 then
        state = STATE_DEAD
        Camera.ShakeRaw(1228, 30)
        Scene.PauseFor(12)
        Camera.LockOff()
        UI.SetCanvasVisible(UI.FindCanvas("boss_hp"), false)
        Persist.Set("smoke_boss_dead", 1)
        Entity.SetActive(self, false)
        return
    end

    if hp < maxHP / 2 and not phase2Triggered then
        phase2Triggered = true
        state = STATE_PHASE2
        Controls.StartIFrames(self, 60)
        Camera.ShakeRaw(900, 30)
    end
end
```

**Six rules baked into this brain** that you can't easily see
unless you've debugged them:

1. **`bossToPlayer` uses `._raw` int multiplication**, not the
   `*` operator. `FixedPoint.__mul` rescales the result by /4096;
   if you don't bypass it, distSq lands in fp12 (not fp12²) and
   the `*_RADIUS_SQ` thresholds are 4096× too high — the boss
   thinks every distance is "in attack range" and never chases.
2. **AGGRO chases while `stateTimer > 0 OR out_of_range`.** The
   `stateTimer > 0` part is the recovery-window chase that makes
   the boss visibly track between attacks. Drop it and the boss
   only chases when actively out of range — which is rare in a
   small arena, so the boss looks stationary.
3. **`fireAttack` anchors the AABB on the boss, not the player.**
   Otherwise the swing has infinite reach: the AABB always wraps
   the target. Anchored on the attacker, the swing has finite
   physical extent and players can dodge by stepping out.
4. **`fireAttack` skips `hits[i].object == self`.** The boss's
   own hurtboxes sit inside its swing AABB; without the filter,
   every swing damages the boss itself (~11 cycles to suicide).
5. **The encounter-gate check (`if Persist.Get(...) ~= 1`)** is
   the FIRST thing in `onUpdate` after STATE_DEAD. Without it,
   the brain runs from frame 1 and aggros on the player at
   scene-load.
6. **`onCreate` resets the persist flag.** Otherwise a respawn
   carries the prior aggro state into the new scene, and the
   boss is hostile from frame 1 again.

### 7. The player script

Player input + HUD updates + dodge + melee. The HUD-update
piece is the relevant part for this recipe:

```lua
-- boss_smoke_player.lua
local function updateBars(self)
    local hpCanvas = UI.FindCanvas("player_hp")
    if hpCanvas < 0 then return end
    local hpBar = UI.FindElement(hpCanvas, "hp_fill")
    local stBar = UI.FindElement(hpCanvas, "stamina_fill")

    local maxHP = Stats.GetMaxHP(self)
    if maxHP > 0 then
        -- 100 = authored hp_fill width, 4 = authored hp_fill height
        UI.SetSize(hpBar, (Stats.GetHP(self) * 100) / maxHP, 4)
    end

    local maxSta = Stats.GetMaxStamina(self)
    if maxSta > 0 then
        UI.SetSize(stBar, (Stats.GetStamina(self) * 100) / maxSta, 4)
    end
end

function onUpdate(self, dt)
    -- ... input handling ...
    updateBars(self)
end
```

`UI.SetSize` operates on the element handle; the second arg is
the **fill width**, not the percentage. Compute
`current * authored_width / max` and pass that.

### 8. HUD canvas authoring

Three elements per bar (BG + fill + label). Order matters —
the first child is drawn FIRST (at the back), so put the
background first:

```ini
[node name="PlayerHPCanvas" type="Node" parent="."]
script = ExtResource("ps1_uicanvas")
CanvasName = "player_hp"
VisibleOnLoad = true
Residency = 0

[node name="hp_bg" type="Node" parent="PlayerHPCanvas"]
script = ExtResource("ps1_uielement")
ElementName = "hp_bg"
Type = 1                   ; Box
X = 16
Y = 200
Width = 104
Height = 8
Color = Color(0.08, 0.08, 0.08, 1)

[node name="hp_fill" type="Node" parent="PlayerHPCanvas"]
script = ExtResource("ps1_uielement")
ElementName = "hp_fill"
Type = 1                   ; Box
X = 18                     ; 2px inset from BG.X
Y = 202                    ; 2px inset from BG.Y
Width = 100                ; 4px narrower than BG
Height = 4
Color = Color(0.24, 0.78, 0.24, 1)
```

Convention:

- **BG** first child of canvas, dark, full-rect.
- **fill** second child, bright accent color, 2-4px inset from
  BG on each side.
- Optional **label** third child, `Type = 2` (Text), positioned
  above or below.

This is the order that lets the fill draw on top of the BG
(`uisystem.cpp:484+` iterates element children in reverse so
the first-authored child ends up at the OT tail = drawn first =
at the back).

## Testing the encounter end-to-end

A complete encounter should pass these eight runtime checks:

1. **Boot to first frame**: boss visible at spawn, **not moving**.
2. **Walk toward fog wall**: boss still not moving, no screen
   shake, no HP bar visible.
3. **Cross the fog wall**: `fog_gate_whoosh` SFX, `boss_theme`
   music starts, boss HP bar fades in at top, boss wakes up and
   starts chasing.
4. **Boss reaches attack range** (~2 world units from player):
   chase pauses, tell shake fires, boss winds up for 30 frames,
   swings.
5. **Standing still through the swing**: player takes 18 damage
   (1× body multiplier), hit shake + 4-frame hitstop fires.
6. **Stepping out during the 30-frame tell**: swing whiffs,
   no damage, no hit shake (the tell shake still plays — that's
   the windup, telegraphed regardless of dodge).
7. **Walking back across the fog wall during the fight**:
   player gets snapped to fp12 z=0 inside the arena, small
   thud shake fires. Cannot retreat.
8. **Killing the boss**: death shake (1228, 30), 12-frame
   hitstop, lock-on releases, boss HP bar fades out, boss
   entity deactivates. Subsequent fog-wall crossings: no music,
   no aggro, free passage.

If any check fails, cross-reference against the
[foot-guns section](combat-patterns.md#foot-guns-observed-in-production).

## What's coming

This recipe is being extracted into a framework — see
[`docs/internal/rfc/combat-framework.md`](https://github.com/BuffJesus/PS1Godot/blob/main/docs/internal/rfc/combat-framework.md){ target="_blank" }
for the plan. When it lands:

- `boss_smoke_brain.lua` will shrink from ~200 lines to ~25 via
  a `Combat.MeleeBoss{...}` declaration that bakes in all six
  rules listed in §6.
- `boss_smoke_fog_gate.lua` will collapse to ~10 lines via an
  `Encounter.new{...}` declaration.
- Player HUD update will collapse to a one-time
  `UI.BindStatBars{...}` registration in `onCreate`.
- A `PS1Encounter` composite Godot node will let designers wire
  the whole thing in the inspector without writing Lua at all.

Until then: this page is the recipe. The
[boss_smoke demo](https://github.com/BuffJesus/PS1Godot/tree/main/godot-ps1/demo/boss_smoke){ target="_blank" }
is the reference implementation; copy from it, don't write from
scratch.

## Related

- [Combat patterns](combat-patterns.md) — the underlying combat
  primitives and the foot-gun reference.
- [PS1TriggerBox](nodes/ps1-trigger-box.md) — encounter trigger.
- [PS1UICanvas](nodes/ps1-ui-canvas.md) — boss HP bar host.
- [FSM graphs](graphs/fsm.md) — alternative to writing the
  state machine in Lua by hand.
- [Lua API → Stats](../lua-api/stats.md) ·
  [Camera](../lua-api/camera.md) · [Physics](../lua-api/physics.md) ·
  [UI](../lua-api/ui.md) · [Persist](../lua-api/persist.md).
