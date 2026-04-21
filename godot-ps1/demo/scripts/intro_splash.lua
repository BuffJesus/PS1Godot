-- Scene-level script for the intro splash (scene 0).
--
-- Boot-logo sequence mimicking the stock PS1 boot aesthetic:
--   - 5 seconds of camera orbit around HeadSpider
--   - "HeadSpider Studios Presents" text at top
--   - "Licensed by HeadSpider Studios" style text at bottom
--   - Chime plays at cutscene frame 0 (sample-accurate, via PS1AudioEvent)
--   - Cutscene onComplete -> Scene.Load(1) transitions into demo.tscn
--
-- Runs via PS1Scene.SceneLuaFile, which only exposes onSceneCreation*
-- hooks (scene-global Lua, not per-GameObject). That's all we need —
-- Cutscene.Play drives the visuals, onComplete handles the transition.

function onSceneCreationEnd()
    Debug.Log("intro_splash: scene 0 boot")

    -- Controls off during the splash — we don't want pad input to move
    -- a player that doesn't exist visually in this scene.
    Controls.SetEnabled(false)

    -- Orbit cutscene. Chime fires via a PS1AudioEvent at frame 0 inside
    -- the cutscene, so we don't need a separate Audio.Play here.
    Cutscene.Play("intro_splash", {
        onComplete = function()
            Debug.Log("intro_splash: cutscene complete, loading scene 1")
            Scene.Load(1)
        end,
    })
end

function onSceneCreationStart()
end
