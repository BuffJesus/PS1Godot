-- Fog gate for the boss-smoke scene. Migrated to Encounter.new
-- (Combat framework Phase 3, RFC docs/internal/rfc/combat-framework.md
-- §L2). The hand-rolled version was ~70 lines covering four
-- separate bug fixes (#1, #6, #9, #9-redux); now ~25 with all
-- four baked into the library.
--
-- `id = "smoke_boss"` produces Persist keys "smoke_boss_aggro" and
-- "smoke_boss_dead" — same names the brain's `persist_dead_key`
-- already writes, so the brain's death path closes the encounter
-- automatically without an explicit `markCleared` call.
--
-- The visible fog wall is still a sibling PS1MeshInstance with
-- UVScrollSpeed > 0; this trigger handles the gating only.
--
-- TRIGGER_Z_RAW reference: FogGateTrigger transform Godot (0, 1, 2),
-- runtime maps Godot z * -1024 → fp12 PSX z (empirically verified;
-- player spawn at Godot z=8 reports as fp12 z=-8192), so trigger
-- center fp12 z = 2 * -1024 = -2048.

local encounter = Encounter.new{
    id                 = "smoke_boss",
    hp_canvas          = "boss_hp",
    music              = "boss_theme",
    music_volume       = 100,
    sfx_on_enter       = "fog_gate_whoosh",
    block_retreat      = true,
    trigger_z_raw      = -2048,
    arena_anchor_z_raw = 0,
}

function onTriggerEnter(self, index)
    encounter:onEnter()
end

function onTriggerExit(self, index)
    encounter:onExit()
end
