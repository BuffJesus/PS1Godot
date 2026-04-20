-- Test script to verify the PS1Godot Lua scripting pipeline.
-- Attach to a PS1MeshInstance's ScriptFile property and run on PSX.
-- Debug.Log output lands in the in-game Log window.

local tick = 0

-- UI handles, resolved once in onCreate. -1 means the canvas/element
-- wasn't exported — harmless (UI.SetText short-circuits on bad handles).
local hudCanvas = -1
local tickCounterEl = -1
local dialogCanvas = -1

function onCreate(self)
    Debug.Log("test_logger: onCreate fired")
    local byName = Audio.Play("test", 100, 64)
    Debug.Log("test_logger: Audio.Play('test') -> " .. byName)

    hudCanvas = UI.FindCanvas("hud")
    if hudCanvas >= 0 then
        tickCounterEl = UI.FindElement(hudCanvas, "tick_counter")
    end
    dialogCanvas = UI.FindCanvas("dialog")
    Debug.Log("test_logger: hud=" .. hudCanvas .. " tickEl=" .. tickCounterEl .. " dialog=" .. dialogCanvas)
end

function onUpdate(self, dt)
    tick = tick + 1
    if tick % 30 == 0 then
        Debug.Log("test_logger: onUpdate tick=" .. tick)
        if tickCounterEl >= 0 then
            UI.SetText(tickCounterEl, "tick=" .. tick)
        end
    end
end

function onInteract(self)
    Debug.Log("test_logger: onInteract (pressed X near cube)")
    -- Toggle the MenuOnly dialog canvas so the interactable demos both
    -- the onInteract event and the MenuOnly residency path.
    if dialogCanvas >= 0 then
        local vis = UI.IsCanvasVisible(dialogCanvas)
        UI.SetCanvasVisible(dialogCanvas, not vis)
    end
end
