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

local AGGRO_RADIUS  = 8       -- world units; once player within, aggro
local ATTACK_RADIUS = 2       -- melee range
local TELL_FRAMES   = 30      -- pre-attack windup (0.5 s)
local HIT_FRAMES    = 12      -- swing duration where hurtbox can hit player
local RECOVER_FRAMES = 30     -- post-swing recovery before next decision
local SWING_DAMAGE  = 18

local state = STATE_IDLE
local stateTimer = 0
local phase2Triggered = false
local hpBarShown = false

local function distToPlayer(self)
    local b = Entity.GetPosition(self)
    local p = Player.GetPosition()
    local dx = b.x - p.x
    local dz = b.z - p.z
    return Math.Sqrt(dx * dx + dz * dz)
end

local function updateHPBar(self)
    local canvas = UI.FindCanvas("boss_hp")
    if canvas < 0 then return end
    if not hpBarShown then
        UI.SetCanvasVisible(canvas, true)
        hpBarShown = true
    end
    local bar = UI.FindElement(canvas, "fill")
    local hp = Stats.GetHP(self)
    local maxHP = Stats.GetMaxHP(self)
    local fillW = (hp * 280) // maxHP  -- 280 = authored bar width
    UI.SetElementW(bar, fillW)
end

-- Boss attacks: melee swing in front of facing direction.
local function fireAttack(self)
    local b = Entity.GetPosition(self)
    -- Box in front of the boss (boss faces +Z by default for this
    -- smoke scene; real authoring would use Entity.GetRotationY).
    local minV = Vec3.new(b.x - 1, b.y - 1, b.z - 3)
    local maxV = Vec3.new(b.x + 1, b.y + 1, b.z - 1)
    local hits = Physics.OverlapBoxDetailed(minV, maxV)
    for i = 1, #hits do
        local applied = Stats.DealDamage(hits[i].object, SWING_DAMAGE, self)
        if applied > 0 then
            Camera.ShakeRaw(614, 14)
            Scene.PauseFor(4)
        end
    end
end

function onCreate(self)
    Debug.Log("boss brain ready — HP " .. Stats.GetMaxHP(self))
end

function onUpdate(self, dt)
    if state == STATE_DEAD then return end

    updateHPBar(self)

    if state == STATE_IDLE then
        if distToPlayer(self) < AGGRO_RADIUS then
            state = STATE_AGGRO
            stateTimer = 0
        end

    elseif state == STATE_AGGRO then
        -- Simple chase — Entity.SetPosition stepping toward player.
        local b = Entity.GetPosition(self)
        local p = Player.GetPosition()
        local dx = p.x - b.x
        local dz = p.z - b.z
        local d = Math.Sqrt(dx * dx + dz * dz)
        if d > ATTACK_RADIUS then
            local step = 4096 // 32  -- ~0.03 units/frame, slow boss
            Entity.SetPosition(self, Vec3.new(
                b.x + (dx * step) // 4096,
                b.y,
                b.z + (dz * step) // 4096))
        else
            state = STATE_ATTACK_TELL
            stateTimer = TELL_FRAMES
        end

    elseif state == STATE_ATTACK_TELL then
        -- Telegraph: small camera shake to telegraph the wind-up.
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
        -- Identical AI but faster + harder. Real bosses would author
        -- distinct attack pattern; this just amps numbers.
        if distToPlayer(self) > ATTACK_RADIUS then
            local b = Entity.GetPosition(self)
            local p = Player.GetPosition()
            local dx = p.x - b.x
            local dz = p.z - b.z
            local step = 4096 // 20  -- faster than phase 1
            Entity.SetPosition(self, Vec3.new(
                b.x + (dx * step) // 4096,
                b.y,
                b.z + (dz * step) // 4096))
        else
            state = STATE_ATTACK_TELL
            stateTimer = TELL_FRAMES // 2  -- shorter tell — harder
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

    if hp < maxHP // 2 and not phase2Triggered then
        phase2Triggered = true
        state = STATE_PHASE2
        Controls.StartIFrames(self, 60)  -- invuln during the transition shake
        Camera.ShakeRaw(900, 30)
    end
end
