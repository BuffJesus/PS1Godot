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
        UI.SetText(sysVoiceText, "Please ignore the checkered cube.")  -- ~33 chars
        UI.SetCanvasVisible(sysVoiceCanvas, true)
        Audio.Play("system_ignore_checkered", 100, 64)
        enterTick = Timer.GetFrameCount()
        -- Clear any pending auto-hide left over from a previous exit.
        -- Without this, re-entering the zone while the previous "It is
        -- not part of the test" fade is still scheduled would cause
        -- test_logger's onUpdate to yank the canvas mid-message.
        sysVoiceHideAtFrame = nil
    end
end

function onTriggerExit(idx)
    if sysVoiceCanvas >= 0 then
        -- After spending a few seconds inside, show the follow-up
        -- before hiding. Otherwise just hide immediately.
        local stayed = Timer.GetFrameCount() - enterTick
        if stayed > 90 and sysVoiceText >= 0 then
            UI.SetText(sysVoiceText, "It is not part of the test.")
            Audio.Play("system_not_part_of_test", 100, 64)
            -- Ask test_logger's onUpdate to hide the canvas after ~3 s.
            -- Trigger scripts don't get onUpdate callbacks, so we need
            -- a collaborator that does. Shared global sentinel carries
            -- the target frame number.
            sysVoiceHideAtFrame = Timer.GetFrameCount() + 180
        else
            UI.SetCanvasVisible(sysVoiceCanvas, false)
            sysVoiceHideAtFrame = nil
        end
    end
end
