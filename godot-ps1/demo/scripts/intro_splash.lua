-- Scene-level script for the intro splash (scene 0).
--
-- Boot-logo sequence: HeadSpider spinning on its own axis while the
-- authored PS1-startup MIDI plays end-to-end.
--   - 14-second static shot, HeadSpider turning a full 360°.
--   - "HeadSpider Studios" text top, "Licensed by HeadSpider Studios"
--     text bottom (PS1-BIOS homage without copying wording).
--   - ps1_startup music sequence (`demo/audio/music/ps1_startup.mid`,
--     5 MIDI channels bound to our single inst_lead sample) plays as
--     the audio bed, ~13.17s + 0.5s decay.
--   - Cutscene onComplete -> Scene.Load(1) transitions into demo.tscn.
--     Cutscene length (420 frames / 14s) deliberately outlasts the
--     music so the handoff never clips the final chime.
--
-- Runs via PS1Scene.SceneLuaFile, which only exposes onSceneCreation*
-- hooks (scene-global Lua, not per-GameObject). Music.Play kicks off
-- the audio; Cutscene.Play drives the visuals + transition.

function onSceneCreationEnd()
    Debug.Log("intro_splash: scene 0 boot")

    -- Controls off during the splash — we don't want pad input to move
    -- a player that doesn't exist visually in this scene.
    Controls.SetEnabled(false)

    -- One-shot sequenced music. Volume 110 / 127 leaves headroom for
    -- Dialog-ducking conventions elsewhere in the demo. The sequence
    -- resource has LoopStartBeat=-1 so it self-terminates after the
    -- last note instead of looping.
    Music.Play("ps1_startup", 110)

    Cutscene.Play("intro_splash", {
        onComplete = function()
            Debug.Log("intro_splash: cutscene complete, loading scene 1")
            Scene.Load(1)
        end,
    })
end

function onSceneCreationStart()
end
