# Combat patterns

A boss-encounter / souls-like / action-game recipe page. The PS1Godot
runtime ships with the primitives for combat — projectile spawn +
raycast hit detection, melee overlap boxes, screen shake, hitstop,
tag-based lock-on, FSM/BT enemy brains — but no single page tied them
together until now. This is that page.

The reference implementation is
[`godot-ps1/demo/scripts/combat_showcase.lua`](https://github.com/BuffJesus/PS1Godot/blob/main/godot-ps1/demo/scripts/combat_showcase.lua){ target="_blank" }
— 229 lines exercising every combat-relevant API the runtime
ships. Read it; this page references its patterns.

## What you've got

| Surface | API | Use for |
|---|---|---|
| Spawn / despawn | [`Entity.Spawn`](../lua-api/entity.md#entity-spawn) · [`Entity.Destroy`](../lua-api/entity.md#entity-destroy) | Pooled projectiles, enemies, pickups |
| Tags | [`Entity.SetTag`](../lua-api/entity.md#entity-settag) · [`FindByTag`](../lua-api/entity.md#entity-findbytag) · [`FindNearest`](../lua-api/entity.md#entity-findnearest) | "Is this thing an enemy / pickup / projectile?" |
| Hit detection | [`Physics.Raycast`](../lua-api/physics.md#physics-raycast) · [`Physics.OverlapBox`](../lua-api/physics.md#physics-overlapbox) | Bullet vs target / melee swing |
| Game-feel | [`Camera.ShakeRaw`](../lua-api/camera.md#camera-shakeraw) · [`Scene.PauseFor`](../lua-api/scene.md#scene-pausefor) | Hit feedback, hitstop |
| Camera | [`Camera.GetForward`](../lua-api/camera.md#camera-getforward) · [`Camera.SetMode`](../lua-api/camera.md#camera-setmode) · [`Camera.SetH`](../lua-api/camera.md#camera-seth) | 1st/3rd-person, manual yaw |
| Input freeze | [`Controls.SetEnabled`](../lua-api/controls.md#controls-setenabled) | Cutscenes, dialog pauses |
| AI brains | PS1Graph FSM + Behavior Tree | Boss state machines |
| Atmosphere | [`PS1Sky`](nodes/ps1-sky.md) · `PS1Scene` fog · [`Music.SetVolume`](../lua-api/music.md) | Arena vibe, ducking ambient |
| Persistence | [`Persist.Get`](../lua-api/persist.md#persist-get) · [`Persist.Set`](../lua-api/persist.md#persist-set) | "Have I killed this boss?" |

## Projectile combat

The showcase's L2 path. Spawn a tagged bullet, give it a direction
+ TTL, raycast every frame to detect hits.

```lua
local TAG_BULLET = 1
local TAG_ENEMY  = 2

local bullets = {}  -- { handle, dir, ttl } per entry
local BULLET_SPEED  = 1
local BULLET_RAYCAST_DIST = 2

local function spawnBullet()
    local dir = Camera.GetForward()
    -- Spawn point: a bit ahead of camera in the look direction
    local cam = Camera.GetPosition()
    local spawnPos = Vec3.new(
        cam.x + dir.x * 4,
        cam.y + 1,  -- chest height (Y is inverted)
        cam.z + dir.z * 4)
    local b = Entity.Spawn(TAG_BULLET, spawnPos)
    if b then
        bullets[#bullets + 1] = { handle = b, dir = dir, ttl = 60 }
        Camera.ShakeRaw(246, 6)  -- ~0.06 world units of shake
    end
end

function onUpdate(self, dt)
    if Input.IsPressed(Input.L2) then spawnBullet() end
    for i = #bullets, 1, -1 do
        local b = bullets[i]
        local hit = Physics.Raycast(
            Entity.GetPosition(b.handle), b.dir, BULLET_RAYCAST_DIST)
        if hit and Entity.GetTag(hit.object) == TAG_ENEMY then
            Entity.Destroy(hit.object)       -- the enemy
            Entity.Destroy(b.handle)         -- the bullet
            Camera.ShakeRaw(614, 14)         -- ~0.15 — solid hit
            Scene.PauseFor(4)                -- 4-frame hitstop
            table.remove(bullets, i)
        else
            -- advance + decay
            -- ...
        end
    end
end
```

The hitstop (`Scene.PauseFor(4)`) is the single biggest game-feel
unlock — every gameplay system freezes for 4 frames, then resumes.
Cheaper than animation work, conveys impact instantly.

## Melee combat

The showcase's R2 path. Overlap a box in front of the player; every
overlapping tagged entity takes a hit.

```lua
local MELEE_COOLDOWN = 20  -- frames between swings
local meleeCooldown = 0

local function meleeSwing()
    if meleeCooldown > 0 then return end
    meleeCooldown = MELEE_COOLDOWN

    local cam = Camera.GetPosition()
    local fwd = Camera.GetForward()
    -- AABB in front of the player, 2 units forward, 1.5×1.5×2 size
    local minV = Vec3.new(cam.x + fwd.x * 1 - 1, cam.y, cam.z + fwd.z * 1 - 1)
    local maxV = Vec3.new(cam.x + fwd.x * 3 + 1, cam.y + 2, cam.z + fwd.z * 3 + 1)
    local hits = Physics.OverlapBox(minV, maxV, TAG_ENEMY)
    if #hits > 0 then
        for i = 1, #hits do Entity.Destroy(hits[i]) end
        Camera.ShakeRaw(491 + 164 * #hits, 8 + #hits)
        Scene.PauseFor(5 + #hits)
    else
        Camera.ShakeRaw(123, 5)  -- whiff feedback
    end
end
```

Two notes:

- **Scale shake + hitstop by hit count.** Hitting 3 enemies in one
  swing should feel weightier than hitting 1.
- **Whiff feedback matters.** Even a missed swing benefits from a
  tiny camera shake — confirms input was received.

## Twin-stick camera

Modern action games (Devil May Cry, Bayonetta, Elden Ring with
lock-off mode) use the right stick for free camera control. The
runtime ships the API but doesn't wire the default rig — author
it in your scene's `onUpdate`:

```lua
local YAW_SENSITIVITY   = 8     -- larger = slower camera
local PITCH_SENSITIVITY = 12    -- pitch typically slower than yaw

function onUpdate(self, dt)
    -- Read the right stick (returns FixedPoint<12> in [-1.0, 1.0])
    local rx, ry = Input.GetAnalog(Input.RIGHT_STICK)

    -- Apply yaw (horizontal)
    if rx ~= 0 then
        Camera.SetH(Camera.GetH() + rx / YAW_SENSITIVITY)
    end

    -- Pitch (vertical) — third-person pitches the camera relative
    -- to the player; use Camera.SetRotation for full 3-axis control.
    if ry ~= 0 then
        local r = Camera.GetRotation()
        Camera.SetRotation(r.x + ry / PITCH_SENSITIVITY, r.y, r.z)
    end
end
```

**Two named constants** make this read clean: `Input.LEFT_STICK`
(`0`) and `Input.RIGHT_STICK` (`1`). Both ship as standard
Lua-registered constants alongside `Input.CROSS`, `Input.L1`,
etc.

### Deadzone

Stick values near zero from sloppy pad calibration cause the
camera to drift. Cheap fix:

```lua
local function deadzone(v, threshold)
    if v > threshold or v < -threshold then return v end
    return 0
end

-- In onUpdate
local rx, ry = Input.GetAnalog(Input.RIGHT_STICK)
rx = deadzone(rx, 256)  -- ~0.06 of range; tune per pad
ry = deadzone(ry, 256)
-- ... apply as before
```

Most analog pads with worn pots want at least a 200-bit deadzone
on the right stick. A virtual reference pad doesn't need any.

### When lock-on is active

The twin-stick pattern only applies when **not** locked on. When
the player has locked a target (see the lock-on section below),
yaw is driven by the player→target vector, not by right-stick
input. The standard wrap:

```lua
function onUpdate(self, dt)
    if lockedEnemy then
        -- yaw camera toward locked target
        local targetPos = Entity.GetPosition(lockedEnemy)
        local playerPos = Player.GetPosition()
        Camera.SetH(yawFromVector(targetPos.x - playerPos.x,
                                  targetPos.z - playerPos.z))
    else
        -- free twin-stick control
        local rx, ry = Input.GetAnalog(Input.RIGHT_STICK)
        if rx ~= 0 then
            Camera.SetH(Camera.GetH() + rx / YAW_SENSITIVITY)
        end
    end
end
```

`yawFromVector(dx, dz)` is a helper — `math.atan2(dx, dz)`
converted to the runtime's pi-fraction convention (pi-fraction =
radians / π, so 90° = 0.5).

### Runtime gap

There's no runtime-side "right-stick drives camera" mode on
`PS1Player` today — the rig only knows L1 / R1 button-driven yaw
and the camera-forward player movement. The twin-stick recipe
above runs in Lua at scene scope, so every scene that wants this
feel copy-pastes the snippet.

A `CameraControl = ButtonsLR | RightStick | LockedOn` setting on
`PS1Player` would consolidate this — see
[`internal/rfc/boss-encounter-primitives.md`](https://github.com/BuffJesus/PS1Godot/blob/main/docs/internal/rfc/boss-encounter-primitives.md){ target="_blank" }
primitive 5.

## Lock-on (current pattern)

The showcase's R3 path. Tag-based, not engine-supported. Toggles
the locked enemy's tag to a sentinel value (visible to the
exporter for a marker overlay).

```lua
local TAG_LOCKED = 9
local lockedEnemy = nil

local function toggleLock()
    if lockedEnemy then
        Entity.SetTag(lockedEnemy, TAG_ENEMY)  -- restore
        lockedEnemy = nil
        return
    end
    local player = Camera.GetPosition()
    lockedEnemy = Entity.FindNearest(player, TAG_ENEMY)
    if lockedEnemy then
        Entity.SetTag(lockedEnemy, TAG_LOCKED)
        Camera.ShakeRaw(82, 4)  -- subtle confirm
    end
end
```

What this **doesn't** do that you'd want for Elden Ring–style
encounters:

- Auto-yaw the camera to keep the locked target on screen.
- Strafe-style player movement (left-stick goes orthogonal to
  player→target vector instead of camera-forward-relative).
- Visual reticle on the locked target.

The auto-yaw piece is achievable today by recomputing
`Camera.SetH(yawToTarget)` each frame in `onUpdate` — see the
[Camera Lua API](../lua-api/camera.md). Strafe movement requires
custom player movement (the runtime's default rig uses
camera-forward); the runtime would need a "lock-on movement mode."

A first-class `Camera.LockOn(target)` / `Camera.LockOff()` is
proposed in
[`internal/rfc/boss-encounter-primitives.md`](https://github.com/BuffJesus/PS1Godot/blob/main/docs/internal/rfc/boss-encounter-primitives.md){ target="_blank" }.

## Boss HP

No `PS1Stats` component exists yet. Author HP as a Lua local on the
boss's script, surface via UI:

```lua
local maxHP = 100
local hp = maxHP

local function takeDamage(amount)
    hp = hp - amount
    Camera.ShakeRaw(491, 8)
    if hp <= 0 then onDeath() end
    updateHPBar()
end

local function updateHPBar()
    local canvas = UI.FindCanvas("boss_hp")
    if canvas >= 0 then
        local bar = UI.FindElement(canvas, "fill")
        -- Width proportional to remaining HP. Original bar W = 200.
        local fillW = (hp * 200) // maxHP
        UI.SetElementW(bar, fillW)
    end
end
```

The HP bar canvas is a normal `PS1UICanvas`:

```
Scene (PS1Scene)
└── BossHPCanvas (PS1UICanvas, Name = "boss_hp", VisibleOnLoad = false)
    ├── BG    (PS1UIElement, Type = Box, color = dark)
    ├── Fill  (PS1UIElement, Type = Box, color = blood red, X/Y/W/H authored)
    └── Label (PS1UIElement, Type = Text, "Margit, the Fell Omen")
```

Show on encounter start: `UI.SetCanvasVisible(canvas, true)`. Hide
on boss death.

## Boss phases

FSM graphs handle this cleanly. Each phase is a state; the
condition for entering the next phase is "current HP below a
threshold."

```
[idle] →on_aggro→ [phase1_attack] →on_low_hp(50)→ [phase1_exit_cutscene]
                                                        ↓
                                                    [phase2_intro]
                                                        ↓
                                                  [phase2_attack] →on_low_hp(0)→ [death]
```

Conditions on transitions test against Persist-backed values:

```lua
-- in the boss's onUpdate
Persist.Set("boss_hp", hp)
-- FSM's transition guard checks Persist.Get("boss_hp") < 50
```

The phase exit cutscene (boss screams + transformation + name
reveal) is a [`PS1Cutscene`](nodes/ps1-cutscene.md) — multi-track
camera + audio + object animation, fired by the FSM transition
into `phase1_exit_cutscene`.

## Fog gates

Elden Ring's locked-arena pattern. A trigger box at the doorway
that:

1. Freezes player input (or restricts to "can't leave").
2. Shows a fog-wall UI canvas blocking the doorway visually.
3. Cues the boss's appearance.
4. Disables itself once the boss is dead.

```lua
function onTriggerEnter(self, index)
    if Persist.Get("godrick_dead") == 1 then return end  -- already cleared
    Controls.SetEnabled(false)
    UI.SetCanvasVisible(UI.FindCanvas("fog_wall"), true)
    Music.Play("godrick_theme", 100)
    Cutscene.Play("godrick_intro")
    -- Cutscene's OnFinishLua re-enables controls after the intro
end
```

The fog-wall canvas is a translucent `PS1UIElement.Type = Box`
sized to cover the doorway in screen space — author against the
camera angle at the trigger point.

## Death / respawn

No formal "respawn point" system. Wire it yourself:

```lua
function onPlayerDeath()
    Cutscene.Play("you_died")  -- 2s fade-to-black with title
    -- Cutscene.OnFinishLua respawns:
    Scene.Load(Persist.Get("respawn_scene") or 0)
    Player.SetPosition(
        Persist.Get("respawn_x") or 0,
        Persist.Get("respawn_y") or 0,
        Persist.Get("respawn_z") or 0)
end
```

Respawn-point checkpoints set the Persist keys on activation
(`PS1TriggerBox` with a "save_point" script that writes the
player's current position).

## Atmosphere

The arena fight's *vibe* is half the encounter:

- **Music** — author a 2-stage `PS1MusicSequence` (low intensity
  before aggro, high intensity during fight). Fire `Music.Play` on
  the fog-gate trigger; ducking + crossfade via `Music.SetVolume`
  during phase transitions.
- **Audio cues** — boss roar on appearance, footstep on every
  attack (give the player audible patterns to read). All via
  [`PS1AudioClip`](nodes/ps1-audio-clip.md) + `Audio.PlaySfx`.
- **Fog and color** — a darker `PS1Scene.Fog` color tinted toward
  the boss's theme (red for fire bosses, sickly green for poison
  ones). Pair with a darker `PS1Sky` tint via `TintColor`.
- **Sub-scene arena** — use [`PS1Scene.SubScenes`](nodes/ps1-scene.md)
  to load a dedicated "boss arena" scene when the player enters
  the fog gate. Keeps the asset budget tight + lets the arena have
  its own VRAM packing.

## Putting it together — minimum viable Elden Ring–style boss

A first encounter you can ship today, with zero engine changes:

1. **Arena** — a sub-scene with low fog, dark sky tint, custom music.
2. **Fog gate** — `PS1TriggerBox` at the doorway, OneShot until
   defeated (Persist-tracked).
3. **Boss intro cutscene** — `PS1Cutscene` zooming the camera to
   the boss + showing a `PS1UICanvas` with the boss's name + a roar
   audio clip.
4. **Boss FSM** — 3 states: idle, attack, low_hp. Transitions on
   HP thresholds.
5. **Player attacks** — copy the showcase's L2 (ranged) + R2 (melee).
6. **Boss HP bar** — bottom-of-screen `PS1UICanvas`, scales `fill`
   element width with boss HP.
7. **Death sequence** — `Cutscene.Play("you_died")` on player HP 0;
   `Scene.Load` back to the last checkpoint. Boss-death sequence
   sets Persist `godrick_dead = 1`.

Total scripting: ~150 lines of Lua + the FSM graph + 2 cutscenes.

## What's *not* easy today

If your encounter design needs any of these, the patterns above
get awkward and a first-class primitive would help. Listed in the
[boss encounter primitives RFC](https://github.com/BuffJesus/PS1Godot/blob/main/docs/internal/rfc/boss-encounter-primitives.md){ target="_blank" }:

- **Lock-on with camera tracking** — today's Lua loop is correct
  but jittery on low frame counts; engine-side `Camera.LockOn`
  would fix that.
- **Hurtboxes vs hitboxes** — distinct weak-points (boss head as
  crit zone) need first-class authoring.
- **Stagger / poise** — running a poise counter in Lua works but
  the dispatch (`onStagger(self)`) wants to be a runtime callback,
  not a Lua poll.
- **Damage events** — `onDamage(self, amount, source)` would
  consolidate the "every caller handles damage" pattern.

## Related

- [`combat_showcase.lua`](https://github.com/BuffJesus/PS1Godot/blob/main/godot-ps1/demo/scripts/combat_showcase.lua){ target="_blank" }
  — the reference 229-line implementation.
- [Lua API → Entity](../lua-api/entity.md) · [Physics](../lua-api/physics.md) · [Camera](../lua-api/camera.md) · [Scene](../lua-api/scene.md) — the called surface.
- [FSM graphs](graphs/fsm.md) · [Behavior tree graphs](graphs/behavior-tree.md) — boss brains.
- [PS1Cutscene](nodes/ps1-cutscene.md) — intro / phase-transition / death cinematics.
