-- Trigger script for the checkered-cube ambient zone. Shows the
-- "system voice" denying the cube's existence as the player approaches,
-- then trails off when they leave.

local sysVoiceCanvas = -1
local sysVoiceText = -1
local enterTick = -1

function onTriggerEnter(idx)
    sysVoiceCanvas = UI.FindCanvas("system_voice")
    if sysVoiceCanvas >= 0 then
        sysVoiceText = UI.FindElement(sysVoiceCanvas, "vtxt")
        UI.SetText(sysVoiceText, "Please ignore the checkered cube.")
        UI.SetCanvasVisible(sysVoiceCanvas, true)
        Audio.Play("system_ignore_checkered", 100, 64)
        enterTick = Timer.GetFrameCount()
    end
end

function onTriggerExit(idx)
    if sysVoiceCanvas >= 0 then
        -- After spending a few seconds inside, show the follow-up
        -- before hiding. Otherwise just hide.
        local stayed = Timer.GetFrameCount() - enterTick
        if stayed > 90 and sysVoiceText >= 0 then
            UI.SetText(sysVoiceText, "It is not part of the test.")
            Audio.Play("system_not_part_of_test", 100, 64)
        else
            UI.SetCanvasVisible(sysVoiceCanvas, false)
        end
    end
end
