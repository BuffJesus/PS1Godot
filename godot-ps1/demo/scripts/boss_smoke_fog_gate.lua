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

    -- Aim the camera at the boss. Lock-on engages target-relative
    -- input automatically.
    local p = Player.GetPosition()
    local boss = Entity.FindNearest(p, 7)  -- TAG_BOSS
    if boss then
        Camera.LockOn(boss)
    end

    -- Reveal the boss HP bar now that the encounter has actually
    -- started. The brain script only resizes the fill — visibility
    -- is owned here so the bar can't appear before the fog gate.
    local hpCanvas = UI.FindCanvas("boss_hp")
    if hpCanvas >= 0 then
        UI.SetCanvasVisible(hpCanvas, true)
    end
end

function onTriggerExit(self, index)
    -- Player retreating mid-fight breaks lock-on. Music stays — the
    -- fight isn't over until the boss dies.
    if Persist.Get("smoke_boss_dead") ~= 1 then
        -- Don't lock-off automatically; let the player retreat
        -- without losing lock. Real games vary on this.
    end
end
