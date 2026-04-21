-- Realm-specific cube dialog. Realm ships with no audio clips, so the
-- canvas just holds for LINE_MIN_HOLD frames after each press. If we
-- later add voice-overs, LINES entries can sprout a `clip` field and
-- the showLine path below will honour clip length like the main demo.

local dialogCanvas, dialogBodyEl = -1, -1
local tick = 0
local hideAtTick = 0
local LINE_MIN_HOLD = 90   -- ~1.5 s at 60 fps onUpdate
local LINE_TAIL     = 30   -- ~0.5 s after audio ends

-- Music ducking — same convention as the main demo. The realm ships
-- with no music sequence today (bgmMasterVol stays nil), so these
-- helpers are no-ops here. Added pre-emptively so future realm BGM
-- "just works" without revisiting this file.
local DUCK_VOL = 18
local function duckMusic()
    if Music ~= nil and bgmMasterVol ~= nil then
        Music.SetVolume(DUCK_VOL)
    end
end
local function restoreMusic()
    if Music ~= nil and bgmMasterVol ~= nil then
        Music.SetVolume(bgmMasterVol)
    end
end

local LINES = {
    { text = "You found me." },
    { text = "Did green send you?" },
    { text = "...he always does." },
    { text = "He has... issues." },
    { text = "(Triangle again to skip.)" },
    { text = "Fine. Go back." },
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
    local line = LINES[idx]
    if dialogBodyEl >= 0 then
        UI.SetText(dialogBodyEl, line.text)
        UI.SetCanvasVisible(dialogCanvas, true)
    end
    local hold = LINE_MIN_HOLD
    if line.clip ~= nil then
        duckMusic()
        Audio.Play(line.clip, 100, 64)
        local dur = Audio.GetClipDuration(line.clip)
        if dur > 0 then
            local needed = dur + LINE_TAIL
            if needed > hold then hold = needed end
        end
    end
    hideAtTick = tick + hold
end

function onUpdate(self, dt)
    tick = tick + 1
    if hideAtTick > 0 and tick >= hideAtTick then
        if dialogCanvas >= 0 then
            UI.SetCanvasVisible(dialogCanvas, false)
        end
        restoreMusic()
        hideAtTick = 0
    end
end
