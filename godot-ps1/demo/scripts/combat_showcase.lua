-- Combat-kit showcase. Exercises every runtime API that shipped during
-- the 2026-04-22 session, in the shape most jam games need:
--   - Entity.Spawn / Destroy / Tag / FindNearest  (pooled dynamic objects)
--   - Physics.Raycast / Physics.OverlapBox        (hit detection)
--   - Camera.ShakeRaw / Scene.PauseFor            (game-feel juice)
--
-- How to wire this up in the demo scene:
--   1. Drop N copies of a small "projectile" PS1MeshInstance somewhere
--      offscreen. Set each: Tag=1 (TAG_BULLET below), StartsInactive=true.
--   2. Drop M copies of an "enemy" PS1MeshInstance where you want them
--      visible. Set each: Tag=2 (TAG_ENEMY below). Add a PS1Collision =
--      Solid so OverlapBox + Raycast can hit them.
--   3. Select any object you want to host this script and set ScriptFile.
--   4. At runtime:
--        L2 — spawn a bullet, Raycast-check every frame for hits
--        R2 — melee swing: OverlapBox in front of player, crunch
--        R3 — lock onto nearest enemy (marker via SetTag change)
--
--      L2/R2/R3 are the only buttons free on both digital and analog
--      pads: Square/L3 = sprint, L1/R1 = rotate, Cross = jump, Triangle
--      = interact. L2/R2/R3 survive both modes without conflict.
--
-- PS1 conventions baked in:
--   - Y is INVERTED: +Y is down, -Y is up (see scenemanager.cpp:555).
--     So "below head" is camera.y + N, not camera.y - N.
--   - Player.GetPosition() returns the CAMERA head in third-person
--     (scenemanager.cpp:551-556), NOT the player's body. The rig sits
--     behind the player; we step forward by MUZZLE_AHEAD along camera
--     forward so projectiles emerge from the player's chest, not the
--     camera. MUZZLE_AHEAD = rig back-offset + a little extra.
--   - psxlua's tokenizer rejects decimal literals. All tuning constants
--     are integers or raw fp12 ints passed to *Raw variants.

local TAG_BULLET = 1
local TAG_ENEMY  = 2
local TAG_LOCKED = 9   -- arbitrary — marker on the currently-locked enemy

-- Shake intensities in raw fp12 (4096 = 1.0 world unit).
local SHAKE_SHOT   = 246    -- ~0.06 world units — punchy shot
local SHAKE_HIT    = 614    -- ~0.15 — solid enemy hit
local SHAKE_MELEE  = 491    -- ~0.12 — melee base (scaled up per hit)
local SHAKE_MELEE_PER = 164 -- ~0.04 — additional shake per melee hit
local SHAKE_WHIFF  = 123    -- ~0.03 — whiff feedback
local SHAKE_LOCK   = 82     -- ~0.02 — subtle lock-on confirm

-- Camera→muzzle offset. The 3rd-person rig sits ~3 units behind the
-- player (see demo.tscn rig offset (0,1,3)); +1 more puts the bullet
-- in front of the player body instead of at the camera.
local MUZZLE_AHEAD = 4
-- Vertical drop from camera (head) to chest. +Y is down on PS1.
local MUZZLE_DROP  = 1

-- Bullet lifetime state. { handle, dir, ttl } per entry.
local bullets = {}

-- Cooldowns — IsPressed is edge-triggered so one-tap-one-action holds,
-- but these damp accidental double-taps a little. Decremented each tick.
local meleeCooldown = 0
local MELEE_COOLDOWN = 20

local BULLET_SPEED_UNITS  = 1
local BULLET_RAYCAST_DIST = 2

local lockedEnemy = nil

-- ── helpers ───────────────────────────────────────────────────────

-- Horizontal camera forward. Returns nil if the camera is pointing
-- (near-)straight up or down, in which case there's no well-defined
-- horizontal heading. Callers should early-out.
local function flatForward()
	local f = Camera.GetForward()
	local fx = f.x
	local fz = f.z
	-- FixedPoint supports ==; exact zero is fine because the common
	-- degenerate case is the camera snapped straight up/down.
	if fx == 0 and fz == 0 then return nil end
	return Vec3.normalize(Vec3.new(fx, 0, fz))
end

-- Clear lockedEnemy if the handle matches victim. Called whenever we
-- destroy an enemy so toggleLock doesn't SetTag a dead entity later.
local function clearLockIf(victim)
	if lockedEnemy ~= nil and victim == lockedEnemy then
		lockedEnemy = nil
	end
end

-- ── L2: spawn + fly a bullet ──────────────────────────────────────
local function spawnBullet()
	local camPos = Player.GetPosition()
	local fwd = flatForward()
	if fwd == nil then
		Debug.Log("combat: L2 ignored — camera has no horizontal forward")
		return
	end

	-- MUZZLE_AHEAD steps past the 3rd-person rig to the player's front.
	local spawnPos = Vec3.new(
		camPos.x + fwd.x * MUZZLE_AHEAD,
		camPos.y + MUZZLE_DROP,
		camPos.z + fwd.z * MUZZLE_AHEAD)

	local bullet = Entity.Spawn(TAG_BULLET, spawnPos)
	if bullet == nil then
		Debug.Log("combat: L2 bullet pool exhausted")
		return
	end

	bullets[#bullets + 1] = { handle = bullet, dir = fwd, ttl = 90 }
	Camera.ShakeRaw(SHAKE_SHOT, 6)
	Audio.Play("sc_hey", 90, 64)
	Debug.Log("combat: L2 shot fired")
end

-- Per-frame bullet tick: fly forward, raycast one step ahead, destroy
-- on hit. Physics.Raycast returns { object, distance, point } or nil,
-- where `object` is an array index that FindByIndex resolves.
local function tickBullets()
	for i = #bullets, 1, -1 do
		local b = bullets[i]
		b.ttl = b.ttl - 1

		local cur = Entity.GetPosition(b.handle)
		local origin = Vec3.new(cur.x, cur.y, cur.z)
		local dirV   = Vec3.new(b.dir.x, b.dir.y, b.dir.z)
		local hit = Physics.Raycast(origin, dirV, BULLET_RAYCAST_DIST)
		if hit ~= nil then
			local victim = Entity.FindByIndex(hit.object)
			if victim ~= nil and Entity.GetTag(victim) == TAG_ENEMY then
				clearLockIf(victim)
				Entity.Destroy(victim)
				Camera.ShakeRaw(SHAKE_HIT, 14)
				Scene.PauseFor(4)
				Debug.Log("combat: bullet killed enemy")
			end
			Entity.Destroy(b.handle)
			table.remove(bullets, i)
		elseif b.ttl <= 0 then
			Entity.Destroy(b.handle)
			table.remove(bullets, i)
		else
			local nextX = cur.x + b.dir.x * BULLET_SPEED_UNITS
			local nextZ = cur.z + b.dir.z * BULLET_SPEED_UNITS
			Entity.SetPosition(b.handle, Vec3.new(nextX, cur.y, nextZ))
		end
	end
end

-- ── R2: melee swing ───────────────────────────────────────────────
-- 2×2 footprint centered MUZZLE_AHEAD in front of camera (which is
-- roughly 1 unit in front of the player body). Vertical range spans
-- head-to-feet so the swing catches enemies at body height.
local function meleeSwing()
	if meleeCooldown > 0 then return end
	meleeCooldown = MELEE_COOLDOWN

	local camPos = Player.GetPosition()
	local fwd = flatForward()
	if fwd == nil then
		Debug.Log("combat: R2 ignored — camera has no horizontal forward")
		return
	end
	local cx = camPos.x + fwd.x * MUZZLE_AHEAD
	local cz = camPos.z + fwd.z * MUZZLE_AHEAD
	-- Y is inverted: head at camPos.y, feet at camPos.y + ~2 (below).
	local minV = Vec3.new(cx - 1, camPos.y,     cz - 1)
	local maxV = Vec3.new(cx + 1, camPos.y + 2, cz + 1)

	local hits = Physics.OverlapBox(minV, maxV, TAG_ENEMY)
	local hitCount = #hits
	if hitCount > 0 then
		for i = 1, hitCount do
			clearLockIf(hits[i])
			Entity.Destroy(hits[i])
		end
		Camera.ShakeRaw(SHAKE_MELEE + SHAKE_MELEE_PER * hitCount,
						14 + 2 * hitCount)
		Scene.PauseFor(5 + hitCount)
		Debug.Log("combat: R2 melee connected")
	else
		-- No Debug.Log here — R2 whiffs fire on every missed press, and
		-- PSX stdout (TTY UART write) is slow enough that rapid R2 spam
		-- was causing perceptible frame dips. Shake alone is the feedback.
		Camera.ShakeRaw(SHAKE_WHIFF, 5)
	end
end

-- ── R3: lock onto nearest enemy ───────────────────────────────────
local function toggleLock()
	if lockedEnemy ~= nil then
		-- Guard: the locked enemy may have been destroyed by an
		-- earlier melee/bullet hit. clearLockIf is called from every
		-- kill path, but a race between destroy and the next R3
		-- is still conceivable — Entity.GetTag on a destroyed handle
		-- should return 0 or error; belt-and-braces, just null out.
		Entity.SetTag(lockedEnemy, TAG_ENEMY)
		lockedEnemy = nil
		Camera.ShakeRaw(SHAKE_LOCK, 4)
		Debug.Log("combat: R3 lock cleared")
		return
	end

	local p = Player.GetPosition()
	local nearest = Entity.FindNearest(Vec3.new(p.x, p.y, p.z), TAG_ENEMY)
	if nearest == nil then
		Debug.Log("combat: R3 no enemy in range")
		return
	end
	lockedEnemy = nearest
	Entity.SetTag(nearest, TAG_LOCKED)
	Camera.ShakeRaw(SHAKE_LOCK, 4)
	Debug.Log("combat: R3 locked onto enemy")
end

-- ── Lifecycle ─────────────────────────────────────────────────────

function onCreate(self)
	Debug.Log("combat_showcase: ready — L2 shoot, R2 melee, R3 lock")
end

function onUpdate(self, dt)
	if meleeCooldown > 0 then meleeCooldown = meleeCooldown - 1 end
	tickBullets()

	if Input.IsPressed(Input.L2) then spawnBullet() end
	if Input.IsPressed(Input.R2) then meleeSwing()  end
	if Input.IsPressed(Input.R3) then toggleLock()  end
end
