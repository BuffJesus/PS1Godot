-- Fog gate for the boss-smoke scene. Composes the encounter trigger
-- from existing primitives — no PS1FogGate node needed. The visible
-- fog wall is a sibling PS1MeshInstance with UVScrollSpeed > 0; this
-- trigger handles the gating.
--
-- IMPORTANT: the runtime's OnTriggerEnterScript / OnTriggerExitScript
-- pass only the trigger INDEX as the first arg, not a GameObject
-- handle. So `self` inside these callbacks is a number, not an entity
-- table — Entity.GetPosition(self) would return nil. The trigger's
-- world position is hardcoded below from the .tscn authoring.
--
-- FogGateTrigger transform: Godot (0, 1, 2). The runtime maps Godot
-- z * -1024 → fp12 PSX z (verified empirically: player spawn at
-- Godot z=8 reports as fp12 z=-8192). So trigger center fp12 z =
-- 2 * -1024 = -2048. AABB extent ±1 unit in z = ±1024 fp12, so
-- AABB spans fp12 z=-3072 to -1024.
local TRIGGER_Z_RAW = -2048

function onTriggerEnter(self, index)
    -- One-shot per scene-instance (across deaths, the player crosses
    -- the gate again and re-fires the music — that's the souls feel.)
    -- Persist this only if you want "fight cleared, gate stays open"
    -- behavior across save/load.
    if Persist.Get("smoke_boss_dead") == 1 then return end
    -- Don't re-fire if a retreat snap-back below has put the player
    -- back into the trigger AABB during an active encounter.
    if Persist.Get("smoke_boss_aggro") == 1 then return end

    Audio.PlaySfx("fog_gate_whoosh")
    Music.Play("boss_theme", 100)

    -- Reveal the boss HP bar now that the encounter has actually
    -- started. The brain script only resizes the fill — visibility
    -- is owned here so the bar can't appear before the fog gate.
    local hpCanvas = UI.FindCanvas("boss_hp")
    if hpCanvas >= 0 then
        UI.SetCanvasVisible(hpCanvas, true)
    end

    -- Wake the boss. brain.onUpdate gates on this Persist flag and
    -- stays dormant until it flips to 1 — keeps the boss in IDLE
    -- (no chase, no tell shake) while the player is still outside
    -- the arena.
    Persist.Set("smoke_boss_aggro", 1)

    -- Lock-on is opt-in via R3 (see boss_smoke_player.lua:tryLockOn).
    -- Auto-engaging here removed because there's no way to break it
    -- without a pad, and the souls "press to lock" idiom is what the
    -- demo's actually trying to teach.
end

function onTriggerExit(self, index)
    -- One-way encounter wall. During an active fight (post-cross,
    -- pre-kill), the fog wall is solid from the inside — retreating
    -- through it snaps the player back into the arena. Souls fights
    -- commit you to the room until the boss is dead.
    if Persist.Get("smoke_boss_aggro") ~= 1 then return end
    if Persist.Get("smoke_boss_dead") == 1 then return end

    local p = Player.GetPosition()

    -- Demo layout: boss sits at higher fp12 z than the trigger; spawn
    -- at lower. If the player exited with z below the trigger center,
    -- they retreated toward spawn. Snap them to fp12 z=0 (well inside
    -- the arena, ~1024 raw past the trigger AABB top edge), preserving
    -- their x. Shake camera for thud feedback.
    if p.z._raw < TRIGGER_Z_RAW then
        Player.SetPosition(Vec3.new(p.x, p.y, 0))
        Camera.ShakeRaw(82, 4)
    end
end
