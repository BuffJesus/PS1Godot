-- Combat-kit showcase. Exercises every runtime API that shipped during
-- the 2026-04-22 session, in the shape most jam games need:
--   - Entity.Spawn / Destroy / Tag / FindNearest  (pooled dynamic objects)
--   - Physics.Raycast / Physics.OverlapBox        (hit detection)
--   - Camera.Shake / Scene.PauseFor               (game-feel juice)
--   - SkinnedAnim.BindPose                        (idle posing)
--
-- How to wire this up in the demo scene:
--   1. Drop N copies of a small "projectile" PS1MeshInstance somewhere
--      offscreen. Set each: Tag=1 (TAG_BULLET below), StartsInactive=true.
--   2. Drop M copies of an "enemy" PS1MeshInstance where you want them
--      visible. Set each: Tag=2 (TAG_ENEMY below). Add a PS1Collision =
--      Solid so OverlapBox + Raycast can hit them.
--   3. Select any object you want to host this script (e.g., the green
--      Cube) and set its ScriptFile to this file.
--   4. At runtime:
--        L2 — spawn a bullet, Raycast-check every frame for hits
--        R2 — melee swing: OverlapBox in front of player, crunch
--        R3 — lock onto nearest enemy (marker via SetTag change)
--
--      L2/R2/R3 are the only buttons free on both digital and analog
--      pads: Square is sprint-on-digital, L3 is sprint-on-analog,
--      L1/R1 are rotation-on-digital, Cross is jump, Triangle is
--      interact (used by test_logger dialog). L2/R2/R3 survive both
--      modes and don't conflict with any built-in control.
--
-- The script is deliberately long-form and commented — the goal is to
-- be a copy-paste reference for the melee/shooter authoring pattern.

local TAG_BULLET = 1
local TAG_ENEMY  = 2
local TAG_LOCKED = 9   -- arbitrary — used as a flag on the currently-locked enemy

-- Bullet lifetime state, indexed by bullet GameObject index. Entries
-- created on Spawn, pruned on Destroy or when the bullet raycasts into
-- a solid. Each: { ttl = frames, dir = {x,y,z} }.
local bullets = {}

-- Melee swing cooldown (frames) so holding Circle doesn't machine-gun
-- the hit. Set when a swing fires; decremented in onUpdate.
local meleeCooldown = 0
local MELEE_COOLDOWN = 20

-- ── Square: spawn + fly a bullet ──────────────────────────────────
-- Pattern: Entity.Spawn returns the handle of a newly-activated pool
-- instance. Store per-instance state in the Lua array `bullets`;
-- Entity.GetPosition / SetPosition accept / return the handle.
local function spawnBullet()
    local playerPos = Player.GetPosition()
    -- Direction: camera forward. Works for both 1st- and 3rd-person
    -- camera modes — you shoot where you're looking. Flatten Y and
    -- re-normalize via Vec3.normalize so bullets fly level regardless
    -- of camera pitch.
    local fwd = Vec3.normalize(Vec3.new(Camera.GetForward().x, 0, Camera.GetForward().z))

    local spawnPos = Vec3.new(playerPos.x + fwd.x, playerPos.y - 1, playerPos.z + fwd.z)

    local bullet = Entity.Spawn(TAG_BULLET, spawnPos)
    if bullet == nil then
        -- Pool exhausted. Real games would either grow the pool (author
        -- more template instances) or recycle the oldest bullet first.
        Debug.Log("combat: bullet pool exhausted")
        return
    end

    -- Fire-and-forget per-bullet state. 90-frame TTL ≈ 1.5 s at 60 fps.
    bullets[#bullets + 1] = {
        handle = bullet,
        dir = fwd,
        ttl = 90,
    }
    Camera.Shake(0.06, 6)
    Audio.Play("sc_hey", 90, 64)   -- whatever short sfx your scene has
end

-- Per-frame bullet tick: fly forward, raycast ahead one step, destroy
-- on hit. Physics.Raycast returns { object, distance, point } or nil.
local BULLET_SPEED = 0.3   -- world units per frame

local function tickBullets()
    for i = #bullets, 1, -1 do
        local b = bullets[i]
        b.ttl = b.ttl - 1

        local cur = Entity.GetPosition(b.handle)
        local nextX = cur.x + b.dir.x * BULLET_SPEED
        local nextZ = cur.z + b.dir.z * BULLET_SPEED

        -- Raycast one step ahead. If we hit an enemy's collider, kill
        -- the bullet AND the enemy, with hit-stop + shake for feedback.
        local origin = Vec3.new(cur.x, cur.y, cur.z)
        local dirV   = Vec3.new(b.dir.x, b.dir.y, b.dir.z)
        local hit = Physics.Raycast(origin, dirV, BULLET_SPEED + 0.1)
        if hit ~= nil then
            local victim = Entity.FindByIndex(hit.object)
            if victim ~= nil and Entity.GetTag(victim) == TAG_ENEMY then
                Entity.Destroy(victim)
                Camera.Shake(0.15, 14)
                Scene.PauseFor(4)
            end
            Entity.Destroy(b.handle)
            table.remove(bullets, i)
        elseif b.ttl <= 0 then
            Entity.Destroy(b.handle)
            table.remove(bullets, i)
        else
            Entity.SetPosition(b.handle, Vec3.new(nextX, cur.y, nextZ))
        end
    end
end

-- ── Circle: melee swing ───────────────────────────────────────────
-- Pattern: OverlapBox against a hitbox just in front of the player.
-- Tag filter narrows to enemies only (skips walls / pickups / etc.).
-- On hit: destroy the enemy, juice the impact, brief hit-stop.
local function meleeSwing()
    if meleeCooldown > 0 then return end
    meleeCooldown = MELEE_COOLDOWN

    local p = Player.GetPosition()
    local fwd = Vec3.normalize(Vec3.new(Camera.GetForward().x, 0, Camera.GetForward().z))
    -- 1.5 × 1.5 × 1.5 m hitbox centered 1 m in front of the player.
    local cx = p.x + fwd.x
    local cz = p.z + fwd.z
    local minV = Vec3.new(cx - 0.75, p.y - 1.5, cz - 0.75)
    local maxV = Vec3.new(cx + 0.75, p.y + 0.5, cz + 0.75)

    local hits = Physics.OverlapBox(minV, maxV, TAG_ENEMY)
    local hitCount = #hits
    if hitCount > 0 then
        for i = 1, hitCount do
            Entity.Destroy(hits[i])
        end
        -- Bigger shake + longer pause when multiple enemies die at once.
        Camera.Shake(0.12 + 0.04 * hitCount, 14 + 2 * hitCount)
        Scene.PauseFor(5 + hitCount)
    else
        -- Whiff — still a small shake so the swing feels real.
        Camera.Shake(0.03, 5)
    end
end

-- ── Triangle: lock onto nearest enemy ─────────────────────────────
-- Pattern: FindNearest(pos, tag) → handle or nil. Marks the locked
-- enemy by changing its tag so a future script could style / outline
-- it. Clears any previous lock first.
local lockedEnemy = nil

local function toggleLock()
    -- Clear previous lock — restore the enemy's TAG_ENEMY so the
    -- Square+Circle attacks can find it again.
    if lockedEnemy ~= nil then
        Entity.SetTag(lockedEnemy, TAG_ENEMY)
        lockedEnemy = nil
        return
    end

    local p = Player.GetPosition()
    local nearest = Entity.FindNearest(Vec3.new(p.x, p.y, p.z), TAG_ENEMY)
    if nearest == nil then
        return
    end
    lockedEnemy = nearest
    Entity.SetTag(nearest, TAG_LOCKED)
    Camera.Shake(0.02, 4)
end

-- ── Lifecycle ──────────────────────────────────────────────────────

function onCreate(self)
    -- Nothing yet; the per-bullet state is lazily built.
end

function onUpdate(self, dt)
    if meleeCooldown > 0 then meleeCooldown = meleeCooldown - 1 end
    tickBullets()

    -- Single-frame press edges via Input.IsPressed (misnamed: it's
    -- actually the JustPressed / wasButtonPressed variant — see
    -- Input.IsHeld for the continuous flag).
    if Input.IsPressed(Input.L2) then spawnBullet() end
    if Input.IsPressed(Input.R2) then meleeSwing()  end
    if Input.IsPressed(Input.R3) then toggleLock()  end
end
