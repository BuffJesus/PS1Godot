-- Scene-level script for the checkered realm. Runs when scene_1 loads.
-- Re-establishes per-scene state that the runtime doesn't auto-reset
-- between scene loads:
--   * controls (in case the previous scene left them disabled)
--   * camera mode (in case the player toggled to first-person before
--     teleporting — third-person is the realm's intended default)
--   * the cube's bounce animation

function onCreate(self)
    Debug.Log("realm_init: scene_1 loaded — checkered realm")
    Controls.SetEnabled(true)
    Camera.SetMode("third")
    Animation.Play("realm_bounce", { loop = true })
end
