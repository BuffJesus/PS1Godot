-- Trigger script: when the player walks into the back of RoomB they
-- jump to the "checkered realm" sub-scene (Scene.Load(1) loads
-- scene_1.splashpack). One-shot guard prevents repeated loads if the
-- player lingers in the trigger volume.

local fired = false

function onTriggerEnter(idx)
    if fired then return end
    fired = true
    Debug.Log("teleport_to_realm: portal trigger hit, loading scene 1")
    Scene.Load(1)
end
