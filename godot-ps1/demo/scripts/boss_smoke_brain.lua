-- Boss brain for the boss-smoke scene. Exercises:
--   - Stats.GetHP / GetMaxHP — phase transitions on HP thresholds
--   - onDamage(self, applied, source) — hitstun + phase entry cutscene
--   - Controls.StartIFrames(self, frames) — brief invuln on phase enter
--   - Camera.ShakeRaw / Scene.PauseFor — game-feel on death
--
-- AI is intentionally minimal — patrol-then-aggro state machine
-- with one attack tell. Real bosses author this through PS1Graph FSM /
-- BT; this is the "smallest thing that exercises every primitive."

local STATE_IDLE        = 0
local STATE_AGGRO       = 1
local STATE_ATTACK_TELL = 2
local STATE_ATTACK_HIT  = 3
local STATE_PHASE2      = 4
local STATE_DEAD        = 5

-- Radii expressed as squared FP12 to skip a sqrt per frame. The
-- runtime doesn't ship Math.Sqrt, and distance-squared is fine for
-- "in range?" checks since we never need the actual distance value.
-- 8 world units → 8 * 4096 = 32768 fp12 → squared = 1073741824.
-- 2 world units → 2 * 4096 = 8192 fp12 → squared = 67108864.
-- distSqRaw below is computed from raw int multiplication of fp12
-- values, so its units are fp12² to match the constants. NOTE:
-- FixedPoint.__mul rescales (a.raw * b.raw) / 4096, which would
-- silently leave distSq 4096× too small vs these thresholds and
-- collapse aggro/attack ranges to ~0. bossToPlayer routes around
-- that by multiplying the .raw ints directly.
local AGGRO_RADIUS_SQ  = 1073741824
local ATTACK_RADIUS_SQ = 67108864
local TELL_FRAMES   = 30      -- pre-attack windup (0.5 s)
local HIT_FRAMES    = 12      -- swing duration where hurtbox can hit player
local RECOVER_FRAMES = 30     -- post-swing recovery before next decision
local SWING_DAMAGE  = 18

local state = STATE_IDLE
local stateTimer = 0
local phase2Triggered = false

-- Returns (dx, dz, distSqRaw) — caller compares distSqRaw (plain
-- int, units = fp12²) against the *_RADIUS_SQ constants and uses
-- (dx, dz) for yaw + chase-step calculations.
--
-- distSqRaw computed from raw int multiplication so units match
-- the thresholds. The seemingly more natural `dx*dx + dz*dz` goes
-- through FixedPoint.__mul which divides by 4096 — that leaves
-- distSq in fp12 units instead of fp12², 4096× too small vs the
-- constants, and the boss thinks every range is attack range.
-- Range stays in int32: max sensible distance is ~46k fp12 (~11
-- PSX units, well outside this scene), squared = ~2.1G, fits.
local function bossToPlayer(self)
    local b = Entity.GetPosition(self)
    local p = Player.GetPosition()
    local dx = p.x - b.x
    local dz = p.z - b.z
    local dxRaw = dx._raw
    local dzRaw = dz._raw
    local distSqRaw = dxRaw * dxRaw + dzRaw * dzRaw
    return dx, dz, distSqRaw
end

-- Snap the boss yaw so its +Z faces the player. Math.Atan2 returns
-- an FP12 pi-fraction (1.0 = π), the same convention Entity.SetRotationY
-- consumes — no manual scaling needed.
local function faceToward(self, dx, dz)
    local heading = Math.Atan2(dx, dz)
    Entity.SetRotationY(self, heading)
end

local function updateHPBar(self)
    local canvas = UI.FindCanvas("boss_hp")
    if canvas < 0 then return end
    local bar = UI.FindElement(canvas, "fill")
    local maxHP = Stats.GetMaxHP(self)
    if maxHP > 0 then
        UI.SetSize(bar, (Stats.GetHP(self) * 280) / maxHP, 4)  -- 280 = authored bar width, 4 = bar H
    end
end

-- Boss attacks: melee swing anchored at the boss's own position, not
-- the player's. Previous version built the swing box around the
-- player, which meant the swing had infinite reach — boss could sit
-- at z=4589 and still hit a player at the floor edge ±15 PSX away,
-- because the box always teleported to wherever the player stood.
-- Now the box is a 4×4 PSX cube around the boss (matching the
-- ATTACK_RADIUS of 2 units), so players outside that physical
-- volume don't get touched — including players who back away
-- during the 30-frame TELL windup.
local function fireAttack(self)
    local b = Entity.GetPosition(self)
    local minV = Vec3.new(b.x - 2, b.y - 1, b.z - 2)
    local maxV = Vec3.new(b.x + 2, b.y + 2, b.z + 2)
    local hits = Physics.OverlapBoxDetailed(minV, maxV)
    for i = 1, #hits do
        -- Skip self: the boss's own hurtboxes are inside this AABB.
        -- Without this guard, every swing rolls SWING_DAMAGE into
        -- the boss's own HP — boss kills itself in ~11 cycles.
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
    -- Boss is dormant until the player crosses the fog wall. The
    -- gate script flips this to 1 on entry; clearing it here makes
    -- the flag scene-load-local rather than save-game persistent,
    -- so a respawn after death (or a fresh scene boot) drops the
    -- boss back to IDLE instead of leaving it pre-aggroed.
    Persist.Set("smoke_boss_aggro", 0)
    Debug.Log("boss brain ready — HP " .. Stats.GetMaxHP(self))
end

function onUpdate(self, dt)
    if state == STATE_DEAD then return end
    -- Encounter gate: stay frozen until the fog wall has been crossed.
    -- Without this the boss aggros on the player from frame 1 (player
    -- spawn is inside the 8-unit aggro radius), tell-shake spamming
    -- before the player has even reached the arena.
    if Persist.Get("smoke_boss_aggro") ~= 1 then return end

    updateHPBar(self)

    local dx, dz, distSqRaw = bossToPlayer(self)

    -- Always face the player (except in IDLE — boss hasn't noticed
    -- the player yet, so it stays at its authored facing).
    if state ~= STATE_IDLE then
        faceToward(self, dx, dz)
    end

    if state == STATE_IDLE then
        if distSqRaw < AGGRO_RADIUS_SQ then
            state = STATE_AGGRO
            stateTimer = 0
        end

    elseif state == STATE_AGGRO then
        -- AGGRO has two responsibilities:
        --   1. Out-of-range chase: step toward the player until
        --      distSqRaw drops back under attack range. State stays
        --      AGGRO for as many frames as it takes.
        --   2. Post-swing recovery window: when arriving from HIT,
        --      stateTimer is preloaded to RECOVER_FRAMES. Chase
        --      during this window even if already in range, so the
        --      boss visibly tracks the player around the arena
        --      between attacks instead of camping at exactly the
        --      attack-range edge.
        -- Both branches step toward the player; only the transition
        -- to TELL fires when recovery is done and the player is in
        -- range.
        local doStep =
            stateTimer > 0
            or distSqRaw > ATTACK_RADIUS_SQ
        if doStep then
            if stateTimer > 0 then stateTimer = stateTimer - 1 end
            -- Step toward player. Normalize the (dx, dz) heading by
            -- the FP12 magnitude so the step is constant speed
            -- regardless of distance. Math.Atan2 + sin/cos would be
            -- cleaner but we already have dx/dz handy.
            local b = Entity.GetPosition(self)
            local step = 4096 / 32  -- ~0.03 units/frame, slow boss
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
            Camera.ShakeRaw(82, 4)
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
            stateTimer = RECOVER_FRAMES
        end

    elseif state == STATE_PHASE2 then
        -- Faster + harder phase 2. Same chase shape, shorter tell.
        if distSqRaw > ATTACK_RADIUS_SQ then
            local b = Entity.GetPosition(self)
            local step = 4096 / 20  -- faster than phase 1
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

-- Runtime fires onDamage after the HP debit lands; check the new HP
-- here for phase transition + death dispatch.
function onDamage(self, applied, source)
    -- Brief invuln so a flurry of hits can't insta-trigger phase 2
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
        Controls.StartIFrames(self, 60)  -- invuln during the transition shake
        Camera.ShakeRaw(900, 30)
    end
end
