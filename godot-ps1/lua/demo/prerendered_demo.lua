-- Pre-rendered background sample scene script.
-- ROADMAP Phase 4 stretch — Resident Evil / FFVII style fixed camera.
--
-- The PS1UICanvas with `bg` image (sortOrder=9999) shows the baked
-- backdrop. The PS1Camera in the scene tree was used to bake the
-- backdrop; this script re-applies that camera's transform at runtime
-- so the player + dynamic props render with the same projection the
-- baked image used. SetMode("fixed") tells the runtime to leave the
-- camera alone after that — player movement won't drag it around.
--
-- IMPORTANT: PS1Godot's Lua camera coords are PSX-runtime coords (not
-- Godot coords). Conversion: x_psx = x_godot / GteScaling, y_psx =
-- -y_godot / GteScaling, z_psx = -z_godot / GteScaling. With the
-- demo's GteScaling=4 and the PS1Camera placed at Godot world
-- (0, 6, 10) looking at (0, 0.5, 0):
--   pos_psx = (0, -1.5, -2.5)
--   yaw     = 0   (camera faces along -Z which is "+Z PSX")
--   pitch   = atan2(5.5, 10) ≈ 28.8° ≈ 0.5 rad — but PSX pitch is
--             inverted (memory `project_camera_pitch_sign`), so use a
--             SMALL OR NEGATIVE pitch to look DOWN at the floor.
-- Tune these by eye until the live render frames the backdrop.

function onSceneCreationStart()
    Debug.Log("prerendered_demo: scene boot — fixed camera + baked BG")
end

function onSceneCreationEnd()
    -- Lock player input off if the scene is observation-only; leave on
    -- so Cross / D-pad / left stick can wiggle the player around the
    -- floor for movement testing against the invisible collision.
    Controls.SetEnabled(true)

    -- Camera placement (PSX coords). Match these to where the PS1Camera
    -- node was placed when the BG was baked.
    Camera.SetPosition(Vec3.new(0, -1.5, -2.5))

    -- Pitch / yaw / roll in pi-units (FixedPoint<12>). Negative pitch
    -- = look DOWN (memory: pitch sign is inverted in psyqo).
    -- 0.16 ≈ 28° down for a high-angle isometric framing.
    Camera.SetRotation(Vec3.new(-0.16, 0.0, 0.0))

    -- Disable the per-frame "follow player" rig. From this point on the
    -- camera holds at the position/rotation we just set; only Lua can
    -- move it again.
    Camera.SetMode("fixed")
end
