-- Trigger in the checkered realm: walks the player back to scene 0
-- (the main demo). One-shot guard so we don't bounce in/out while the
-- player stands in the trigger.

local fired = false

function onTriggerEnter(idx)
    if fired then return end
    fired = true
    Debug.Log("teleport_back: returning to main demo")
    Scene.Load(0)
end
