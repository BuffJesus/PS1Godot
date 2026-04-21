-- Trigger in the checkered realm: walks the player back to the main
-- demo. One-shot guard so we don't bounce in/out while the player
-- stands in the trigger.
--
-- Scene layout (intro_splash.tscn is scene 0, SubScenes = [demo, realm]):
--   0 = intro_splash.tscn
--   1 = demo.tscn      <- target
--   2 = checkered_realm.tscn

local fired = false

function onTriggerEnter(idx)
    if fired then return end
    fired = true
    Debug.Log("teleport_back: returning to main demo (scene 1)")
    Scene.Load(1)
end
