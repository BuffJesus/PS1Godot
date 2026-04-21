-- Realm-specific cube dialog. Doesn't rely on audio clips (the realm
-- ships with none) or on the main demo's sc_* / ck_* clip names.
-- Each Triangle press cycles to the next line; the canvas auto-hides
-- ~1.5 s after the last press.

local dialogCanvas, dialogBodyEl = -1, -1
local tick = 0
local hideAtTick = 0
local LINE_HOLD = 90   -- ~1.5 s at 60 fps onUpdate

local LINES = {
    "You found me.",
    "Did green send you?",
    "...he always does.",
    "He has... issues.",
    "(Triangle again to skip.)",
    "Fine. Go back.",
}
local idx = 0

function onCreate(self)
    dialogCanvas = UI.FindCanvas("dialog")
    if dialogCanvas >= 0 then
        dialogBodyEl = UI.FindElement(dialogCanvas, "body")
    end
end

function onInteract(self)
    idx = (idx % #LINES) + 1
    if dialogBodyEl >= 0 then
        UI.SetText(dialogBodyEl, LINES[idx])
        UI.SetCanvasVisible(dialogCanvas, true)
    end
    hideAtTick = tick + LINE_HOLD
end

function onUpdate(self, dt)
    tick = tick + 1
    if hideAtTick > 0 and tick >= hideAtTick then
        if dialogCanvas >= 0 then
            UI.SetCanvasVisible(dialogCanvas, false)
        end
        hideAtTick = 0
    end
end
