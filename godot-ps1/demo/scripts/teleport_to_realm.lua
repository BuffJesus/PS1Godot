-- Trigger script: when the player walks into the back of RoomB they
-- jump to the "checkered realm" sub-scene. One-shot guard prevents
-- repeated loads if the player lingers in the trigger volume.
--
-- Scene layout (intro_splash.tscn is scene 0, SubScenes = [demo, realm]):
--   0 = intro_splash.tscn
--   1 = demo.tscn
--   2 = checkered_realm.tscn  <- target

local fired = false

function onTriggerEnter(idx)
    if fired then return end
    fired = true
    Debug.Log("teleport_to_realm: portal trigger hit, loading scene 2 (realm)")
    Scene.Load(2)
end
