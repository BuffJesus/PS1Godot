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
-- COORDS: PSX-runtime, not Godot. With the demo's GteScaling=4:
--   x_psx =  x_godot / 4
--   y_psx = -y_godot / 4
--   z_psx = -z_godot / 4
-- And PSX pitch is sign-inverted vs intuition (negative pitch = look
-- DOWN — memory `project_camera_pitch_sign`).
--
-- TRIGGER INDICES: assigned in scene-walk order. With this scene's
-- two triggers, TriggerA=0 and TriggerB=1. If you add more triggers
-- elsewhere in the scene, renumber the dispatch below.

-- Camera A frames Room A. Editor placement: (0, 6, 10) → PSX (0, -1.5, -2.5).
local function poseRoomA()
    Camera.SetPosition(Vec3.new(0, -1.5, -2.5))
    Camera.SetRotation(Vec3.new(-0.16, 0.0, 0.0))
end

-- Camera B frames Room B. Editor placement: (0, 6, -2) → PSX (0, -1.5, 0.5).
-- Same downward pitch as A; just a different XZ anchor.
local function poseRoomB()
    Camera.SetPosition(Vec3.new(0, -1.5, 0.5))
    Camera.SetRotation(Vec3.new(-0.16, 0.0, 0.0))
end

function onSceneCreationStart()
    Debug.Log("prerendered_demo: scene boot — multi-room fixed cameras")
end

function onSceneCreationEnd()
    Controls.SetEnabled(true)

    -- Player spawns in Room A; show Room A's backdrop, hide B's, lock
    -- the camera to Room A's pose. The runtime stays on this pose
    -- until a trigger handler below changes it.
    poseRoomA()
    Camera.SetMode("fixed")

    UI.SetVisible("background_a", true)
    UI.SetVisible("background_b", false)
end

-- Fires when the player's AABB enters either trigger box.
-- Indices are assigned in scene-walk order:
--   0 = TriggerA (re-entering Room A from the corridor)
--   1 = TriggerB (entering Room B from the corridor)
function onTriggerEnter(triggerIndex)
    if triggerIndex == 0 then
        Debug.Log("prerendered_demo: enter Room A")
        poseRoomA()
        UI.SetVisible("background_a", true)
        UI.SetVisible("background_b", false)
    elseif triggerIndex == 1 then
        Debug.Log("prerendered_demo: enter Room B")
        poseRoomB()
        UI.SetVisible("background_a", false)
        UI.SetVisible("background_b", true)
    end
end
