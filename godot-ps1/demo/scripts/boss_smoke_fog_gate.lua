-- Fog gate for the boss-smoke scene. Composes the encounter trigger
-- from existing primitives — no PS1FogGate node needed. The visible
-- fog wall is a sibling PS1MeshInstance with UVScrollSpeed > 0; this
-- trigger handles the gating.

function onTriggerEnter(self, index)
    -- One-shot per scene-instance (across deaths, the player crosses
    -- the gate again and re-fires the music — that's the souls feel.)
    -- Persist this only if you want "fight cleared, gate stays open"
    -- behavior across save/load.
    if Persist.Get("smoke_boss_dead") == 1 then return end

    Audio.PlaySfx("fog_gate_whoosh")
    Music.Play("boss_theme", 100)

    -- Reveal the boss HP bar now that the encounter has actually
    -- started. The brain script only resizes the fill — visibility
    -- is owned here so the bar can't appear before the fog gate.
    local hpCanvas = UI.FindCanvas("boss_hp")
    if hpCanvas >= 0 then
        UI.SetCanvasVisible(hpCanvas, true)
    end

    -- Lock-on is opt-in via R3 (see boss_smoke_player.lua:tryLockOn).
    -- Auto-engaging here removed because there's no way to break it
    -- without a pad, and the souls "press to lock" idiom is what the
    -- demo's actually trying to teach.
end
