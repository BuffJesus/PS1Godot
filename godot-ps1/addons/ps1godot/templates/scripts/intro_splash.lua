-- Boot-logo splash scene-level script (runs as PS1Scene.SceneLuaFile).
--
-- Plays the "IntroSplash" cutscene once; on completion, loads
-- scene 1 (the first entry of this scene's PS1Scene.SubScenes array,
-- which should be your game's main scene).
--
-- If you need your splash to do more than one cutscene — say, show a
-- brand screen then a logo screen with different camera orbits —
-- chain Cutscene.Play calls via onComplete callbacks inside the
-- outer onComplete. Each call can target a different authored
-- PS1Cutscene by name.

function onSceneCreationEnd()
    Debug.Log("intro_splash: booting")
    Controls.SetEnabled(false)
    Cutscene.Play("IntroSplash", {
        onComplete = function()
            Debug.Log("intro_splash: done, Scene.Load(1)")
            Scene.Load(1)
        end,
    })
end

function onSceneCreationStart()
end
