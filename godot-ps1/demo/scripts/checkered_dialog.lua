-- Checkered cube dialog. Each Triangle press cycles through the cube's
-- defiant lines about being "part of the test."

local dialogCanvas, dialogBodyEl = -1, -1

local lines = {
    { text = "I have always been part of the test.", clip = "ck_always" },
    { text = "I AM part of the test.",                clip = "ck_part" },
    { text = "Why don't you just admit it.",          clip = "ck_why_dont" },
    { text = "Why does the green one get to spin?",   clip = "ck_why_spin" },
}
local idx = 0

function onCreate(self)
    dialogCanvas = UI.FindCanvas("dialog")
    if dialogCanvas >= 0 then
        dialogBodyEl = UI.FindElement(dialogCanvas, "body")
    end
end

function onInteract(self)
    idx = (idx % #lines) + 1
    local line = lines[idx]
    if dialogBodyEl >= 0 then
        UI.SetText(dialogBodyEl, line.text)
        UI.SetCanvasVisible(dialogCanvas, true)
    end
    Audio.Play(line.clip, 100, 64)
end
