-- Pre-rendered background sample scene script.
-- ROADMAP Phase 4 stretch (A + B + C) — Resident Evil / FFVII style
-- multi-room fixed-camera demo.
--
-- The scene has two rooms connected by a corridor. Each room has its
-- own PS1Camera + PS1UICanvas (with the baked BG Image at sortOrder
-- 9999) + PS1TriggerBox. Walking from one room to the other crosses
-- a trigger that swaps both the camera pose and the visible canvas —
-- the same authoring pattern Resident Evil used for tank-controls
-- camera cuts.
--
-- TRIGGER INDICES: assigned in scene-walk order. With this scene's
-- two triggers, TriggerA=0 and TriggerB=1. If you add more triggers
-- elsewhere in the scene, renumber the dispatch below.
--
-- Camera pose + FOV come from the Godot Camera3D inspector via the
-- exporter-injected `_ps1_cameras` table. Move FixedCameraA / B in
-- Godot, change FOV, F5 — the runtime updates without a Lua edit.

function onSceneCreationStart()
    Debug.Log("prerendered_demo: scene boot — multi-room fixed cameras")
end

function onSceneCreationEnd()
    Controls.SetEnabled(true)

    -- Player spawns in Room A; show Room A's backdrop, hide B's, lock
    -- the camera to Room A's pose. The runtime stays on this pose
    -- until a trigger handler below changes it.
    Camera.LoadFromExport("FixedCameraA")
    Camera.SetMode("fixed")

    UI.SetCanvasVisible("background_a", true)
    UI.SetCanvasVisible("background_b", false)
end

-- Fires when the player's AABB enters either trigger box.
-- Indices are assigned in scene-walk order:
--   0 = TriggerA (re-entering Room A from the corridor)
--   1 = TriggerB (entering Room B from the corridor)
function onTriggerEnter(triggerIndex)
    if triggerIndex == 0 then
        Debug.Log("prerendered_demo: enter Room A")
        Camera.LoadFromExport("FixedCameraA")
        UI.SetCanvasVisible("background_a", true)
        UI.SetCanvasVisible("background_b", false)
    elseif triggerIndex == 1 then
        Debug.Log("prerendered_demo: enter Room B")
        Camera.LoadFromExport("FixedCameraB")
        UI.SetCanvasVisible("background_a", false)
        UI.SetCanvasVisible("background_b", true)
    end
end
