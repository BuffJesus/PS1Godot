-- Checkered cube dialog. Each Triangle press cycles through the cube's
-- defiant lines about being "part of the test." Auto-hides the dialog
-- canvas after ~3 s so the player isn't stuck staring at the last line
-- if they walk away mid-conversation.

local dialogCanvas, dialogBodyEl = -1, -1

local lines = {
    { text = "Always been part of the test.",  clip = "ck_always" },
    { text = "I AM part of the test.",          clip = "ck_part" },
    { text = "Why don't you just admit it.",   clip = "ck_why_dont" },
    { text = "Why does green get to spin?",    clip = "ck_why_spin" },
}
local idx = 0

-- Frames remaining until the dialog auto-hides (set on each interact,
-- counted down in onUpdate). 0 = no pending hide.
local hideCountdown = 0
local HIDE_FRAMES = 180   -- ~3 s at 60 fps onUpdate

function onCreate(self)
    dialogCanvas = UI.FindCanvas("dialog")
    if dialogCanvas >= 0 then
        dialogBodyEl = UI.FindElement(dialogCanvas, "body")
    end
end

-- Owner ID for the shared `currentDialogOwner` global. See test_logger.lua
-- for the coordination protocol — each script claims ownership when it
-- opens the dialog so the previous owner stops auto-advancing.
local MY_DIALOG_OWNER = 2

function onInteract(self)
    currentDialogOwner = MY_DIALOG_OWNER
    idx = (idx % #lines) + 1
    local line = lines[idx]
    if dialogBodyEl >= 0 then
        UI.SetText(dialogBodyEl, line.text)
        UI.SetCanvasVisible(dialogCanvas, true)
    end
    Audio.Play(line.clip, 100, 64)
    hideCountdown = HIDE_FRAMES
end

function onUpdate(self, dt)
    if hideCountdown > 0 then
        hideCountdown = hideCountdown - 1
        if hideCountdown == 0 and currentDialogOwner == MY_DIALOG_OWNER then
            if dialogCanvas >= 0 then
                UI.SetCanvasVisible(dialogCanvas, false)
            end
        end
    end
end
