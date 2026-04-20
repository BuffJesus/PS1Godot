-- Test script to verify the PS1Godot Lua scripting pipeline.
-- Attach to a PS1MeshInstance's ScriptFile property and run on PSX.
-- Debug.Log output lands in the in-game Log window.

local tick = 0

function onCreate(self)
    Debug.Log("test_logger: onCreate fired")
    -- A/B test: try numeric index 0 (bypasses name lookup) and name "test"
    -- (goes through findAudioClipByName). If index 0 plays and name returns
    -- -1, the name table is borked; if both return -1, the clip isn't
    -- loaded at all (SPU upload failed).
    local byIndex = Audio.Play(0, 100, 64)
    Debug.Log("test_logger: Audio.Play(0) -> " .. byIndex)
    local byName = Audio.Play("test", 100, 64)
    Debug.Log("test_logger: Audio.Play('test') -> " .. byName)
end

function onUpdate(self, dt)
    tick = tick + 1
    -- Log once per second-ish so we can confirm onUpdate is ticking
    -- without flooding the log buffer.
    if tick % 30 == 0 then
        Debug.Log("test_logger: onUpdate tick=" .. tick)
    end
end
