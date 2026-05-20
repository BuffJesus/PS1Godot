-- Player combat for the boss-smoke scene. Hosted on a dummy
-- PS1MeshInstance with Stats authored so the player has trackable
-- HP / Stamina. Exercises:
--   - Input.IsPressed / IsHeld + the analog stick (twin-stick default)
--   - Camera.LockOn / LockOff / IsLocked — R3 toggles
--   - Controls.StartIFrames + Stats.SetStamina — dodge (Circle)
--   - Physics.OverlapBoxDetailed + Stats.DealDamage — melee (R2)
--   - Player HP / stamina bars via UI.SetElementW
--   - onDamage callback — hit feedback when the boss tags us

local TAG_BOSS = 7

local DODGE_FRAMES   = 18
local IFRAME_WINDOW  = 12
local DODGE_COOLDOWN = 30
local DODGE_STAMINA  = 25
local DODGE_SPEED    = 800  -- raw fp12 per frame (~0.2 world units)
local STAMINA_REGEN  = 4096 // 30  -- 1 unit / 30 frames

local MELEE_DAMAGE   = 12
local MELEE_COOLDOWN = 20

local dodgeFrames = 0
local dodgeDirX = 0
local dodgeDirZ = 0
local cooldown = 0
local meleeCD = 0
local staminaRegenAcc = 0

local function updateBars(self)
    local hpCanvas = UI.FindCanvas("player_hp")
    if hpCanvas < 0 then return end
    local hpBar = UI.FindElement(hpCanvas, "hp_fill")
    local stBar = UI.FindElement(hpCanvas, "stamina_fill")

    local hpFill = (Stats.GetHP(self) * 100) // Stats.GetMaxHP(self)
    UI.SetElementW(hpBar, hpFill)

    local maxStamina = Stats.GetMaxStamina(self)
    if maxStamina > 0 then
        local stFill = (Stats.GetStamina(self) * 100) // maxStamina
        UI.SetElementW(stBar, stFill)
    end
end

local function tryLockOn()
    if Camera.IsLocked() then
        Camera.LockOff()
        Camera.ShakeRaw(82, 4)
        return
    end
    local p = Player.GetPosition()
    local boss = Entity.FindNearest(p, TAG_BOSS)
    if boss then
        Camera.LockOn(boss)
        Camera.ShakeRaw(82, 4)
    end
end

local function tryDodge(self)
    if cooldown > 0 then return end
    if Stats.GetStamina(self) < DODGE_STAMINA then return end

    -- Direction: left stick if held; otherwise backstep from facing.
    local lx, ly = Input.GetAnalog(Input.LEFT_STICK)
    if Math.Abs(lx) < 200 and Math.Abs(ly) < 200 then
        -- No stick → back-step. Use the camera forward as a proxy
        -- since the player facing matches camera in 3p mode.
        local f = Camera.GetForward()
        dodgeDirX = -f.x
        dodgeDirZ = -f.z
    else
        dodgeDirX = lx
        -- ly Y points down on stick (+y = stick pulled toward player)
        -- but world Z = forward, so flip.
        dodgeDirZ = -ly
    end

    dodgeFrames = DODGE_FRAMES
    cooldown = DODGE_COOLDOWN
    Stats.SetStamina(self, Stats.GetStamina(self) - DODGE_STAMINA)
    Controls.StartIFrames(self, IFRAME_WINDOW)
    Camera.ShakeRaw(123, 4)
end

local function tryMelee(self)
    if meleeCD > 0 then return end
    meleeCD = MELEE_COOLDOWN

    local p = Player.GetPosition()
    local f = Camera.GetForward()
    -- AABB 2 units in front of the player, 1.5 wide
    local cx = p.x + f.x * 8192 // 4096  -- 2 units forward
    local cz = p.z + f.z * 8192 // 4096
    local minV = Vec3.new(cx - 1, p.y - 1, cz - 1)
    local maxV = Vec3.new(cx + 1, p.y + 2, cz + 1)
    local hits = Physics.OverlapBoxDetailed(minV, maxV)
    if #hits > 0 then
        for i = 1, #hits do
            local hit = hits[i]
            -- hurtbox multiplier: 100 = 1x baseline; head 2x, body 1x, legs 0.5x
            local dmg = (MELEE_DAMAGE * hit.multiplier) // 100
            Stats.DealDamage(hit.object, dmg, self)
        end
        Camera.ShakeRaw(491 + 164 * #hits, 8 + #hits)
        Scene.PauseFor(5 + #hits)
    else
        Camera.ShakeRaw(123, 5)
    end
end

function onCreate(self)
    Debug.Log("player ready — HP " .. Stats.GetMaxHP(self)
              .. " stamina " .. Stats.GetMaxStamina(self))
    updateBars(self)
end

function onUpdate(self, dt)
    if cooldown > 0 then cooldown = cooldown - 1 end
    if meleeCD > 0 then meleeCD = meleeCD - 1 end

    -- Stamina regen — slow, gated on not currently dodging
    if dodgeFrames == 0 and Stats.GetStamina(self) < Stats.GetMaxStamina(self) then
        staminaRegenAcc = staminaRegenAcc + dt
        if staminaRegenAcc >= 4096 then
            Stats.SetStamina(self, Stats.GetStamina(self) + 1)
            staminaRegenAcc = staminaRegenAcc - 4096
        end
    end

    -- Inputs
    if Input.IsPressed(Input.R3) then tryLockOn() end
    if Input.IsPressed(Input.CIRCLE) then tryDodge(self) end
    if Input.IsPressed(Input.R2) then tryMelee(self) end

    -- Active dodge — manual position override during the window
    if dodgeFrames > 0 then
        local p = Player.GetPosition()
        Player.SetRotation({ x = 0, y = 0, z = 0 })  -- can't rotate during dodge
        -- Position write via Entity.SetPosition on the player handle —
        -- runtime uses player position from Player.GetPosition, but
        -- since the player is also a GameObject we can move it that way.
        -- (For dodging the actual player rig, Player.SetPosition is the
        -- right API — Player.SetPosition doesn't exist yet, so this is
        -- the limitation surfaced.)
        dodgeFrames = dodgeFrames - 1
    end

    updateBars(self)
end

-- Boss hit the player.
function onDamage(self, applied, source)
    -- Brief recovery i-frames so the player can't be combo'd to death
    Controls.StartIFrames(self, 18)
    Camera.ShakeRaw(491, 12)
    Scene.PauseFor(3)

    if Stats.GetHP(self) <= 0 then
        -- Smoke scene: just print + freeze. Real game would Cutscene.Play
        -- a "you died" sequence here.
        Debug.Log("player died")
        Camera.ShakeRaw(900, 60)
        Controls.SetEnabled(false)
    end
end
