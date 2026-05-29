-- Boss brain for the boss-smoke scene. Migrated to Combat.MeleeBoss
-- (Combat framework Phase 2, RFC docs/internal/rfc/combat-framework.md
-- §L1). The old hand-rolled state machine (~200 lines + ~11 bug
-- fixes baked in) is now ~30 lines — every range/swing/recovery
-- foot-gun lives inside the library where it can't be re-broken.
--
-- Encounter gate stays here for now: `Persist.Get("smoke_boss_aggro")`
-- check at the top of onUpdate is the fog-gate handoff. Phase 3 of
-- the framework (Encounter module) collapses that into Encounter.new
-- so the gate isn't per-boss boilerplate either.

local boss = Combat.MeleeBoss{
    -- Ranges (world units; squared internally to fp12²).
    aggro_radius  = 8,
    attack_radius = 2,

    -- Cadence — 60 fps at 30 frames/0.5 s for tell.
    tell_frames    = 30,
    hit_frames     = 12,
    recover_frames = 30,

    -- Damage + swing volume. Asymmetric y matches the original
    -- 4×3×4 PSX cube (b.y - 1 .. b.y + 2) so an out-of-arena
    -- player can't be reached.
    swing_damage  = 18,
    swing_range   = 2,
    swing_y_below = 1,
    swing_y_above = 2,

    -- i-frames: brief on each hit, longer during the phase-2 cutscene
    -- shake so a flurry can't insta-trigger phase 2 again.
    iframes              = 6,
    iframes_phase_change = 60,

    -- HUD + persistence.
    hp_canvas        = "boss_hp",
    hp_element       = "fill",
    persist_dead_key = "smoke_boss_dead",

    -- Game-feel (state machine itself is silent).
    on_tell = function() Camera.ShakeRaw(82, 4) end,
    on_hit_land = function(_, _, _, applied)
        if applied > 0 then
            Camera.ShakeRaw(614, 14)
            Scene.PauseFor(4)
        end
    end,
    on_death = function()
        Camera.ShakeRaw(1228, 30)
        Scene.PauseFor(12)
        Camera.LockOff()
    end,

    -- Phase 2 at 50% HP: faster tell, shorter recovery, big shake.
    phases = {
        {
            hp_ratio       = 0.5,
            tell_frames    = 15,
            recover_frames = 20,
            on_enter = function() Camera.ShakeRaw(900, 30) end,
        },
    },
}

function onCreate(self)
    -- Boss is dormant until the player crosses the fog wall (gate
    -- script flips this to 1). Clearing here makes the flag scene-
    -- load-local rather than save-game persistent.
    Persist.Set("smoke_boss_aggro", 0)
    Debug.Log("boss brain ready — HP " .. Stats.GetMaxHP(self))
end

function onUpdate(self, dt)
    -- Encounter gate — Phase 3 Encounter module replaces this.
    if Persist.Get("smoke_boss_aggro") ~= 1 then return end
    boss:update(self, dt)
end

function onDamage(self, applied, source)
    boss:handleDamage(self, applied, source)
end
