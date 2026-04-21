-- Scene-level script for the checkered realm. Scene scripts use
-- `onSceneCreationStart` / `onSceneCreationEnd` hooks — NOT the
-- per-object `onCreate(self)` hook that GameObject scripts use
-- (psxsplash-main/src/lua.cpp:378-380 only resolves the two scene
-- globals). Previous version had onCreate() here and it silently
-- never fired.
--
-- Re-establishes per-scene state that the runtime doesn't auto-reset
-- between scene loads:
--   * controls (the previous scene may have disabled them for its
--     intro cutscene)
--   * camera mode (previous scene may have toggled to first-person)
--   * the cube's bounce animation

function onSceneCreationEnd()
    Debug.Log("realm_init: scene_1 loaded — checkered realm")
    Controls.SetEnabled(true)
    Camera.SetMode("third")
    Animation.Play("realm_bounce", { loop = true })
end
