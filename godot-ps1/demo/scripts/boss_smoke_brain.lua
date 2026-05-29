-- Boss brain for the boss-smoke scene. Migrated to Combat.MeleeBoss
-- with encounter binding (Combat framework Phase 2+3, RFC
-- docs/internal/rfc/combat-framework.md §L1+§L2). What used to be
-- ~200 lines + ~11 baked-in bug fixes is now this — the state
-- machine, encounter gate, and persist-key wiring all live in the
-- library where the next boss author inherits them by default.
--
-- `encounter_id = "smoke_boss"` pairs this brain with the
-- Encounter.new of the same id in boss_smoke_fog_gate.lua. The
-- library derives "smoke_boss_aggro" (gate flag — boss stays
-- dormant until Encounter:onEnter() flips it) and "smoke_boss_dead"
-- (death flag — the encounter reads this to skip re-entry).

local boss = Combat.MeleeBoss{
    encounter_id = "smoke_boss",

    aggro_radius  = 8,
    attack_radius = 2,

    tell_frames    = 30,
    hit_frames     = 12,
    recover_frames = 30,

    -- Asymmetric y matches the original 4×3×4 PSX swing cube
    -- (b.y - 1 .. b.y + 2) so an out-of-arena player can't be hit.
    swing_damage  = 18,
    swing_range   = 2,
    swing_y_below = 1,
    swing_y_above = 2,

    -- i-frames: brief on each hit, longer during the phase-2 cutscene
    -- shake so a flurry can't insta-trigger phase 2 again.
    iframes              = 6,
    iframes_phase_change = 60,

    hp_canvas  = "boss_hp",
    hp_element = "fill",

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
    Debug.Log("boss brain ready — HP " .. Stats.GetMaxHP(self))
end

function onUpdate(self, dt)
    boss:update(self, dt)
end

function onDamage(self, applied, source)
    boss:handleDamage(self, applied, source)
end
