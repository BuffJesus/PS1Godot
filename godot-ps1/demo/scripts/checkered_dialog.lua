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
local MIN_HIDE_FRAMES = 90    -- ~1.5 s floor when the clip is missing/short
local HIDE_TAIL       = 30    -- ~0.5 s after audio ends

-- Music ducking — see test_logger.lua for the convention. We read
-- the global `bgmMasterVol` so the restore target stays in sync with
-- whatever the BGM owner picked, even if it changes at runtime.
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
    local hold = MIN_HIDE_FRAMES
    if line.clip ~= nil then
        duckMusic()
        Audio.Play(line.clip, 100, 64)
        local dur = Audio.GetClipDuration(line.clip)
        if dur > 0 then
            local needed = dur + HIDE_TAIL
            if needed > hold then hold = needed end
        end
    end
    hideCountdown = hold
end

function onUpdate(self, dt)
    if hideCountdown > 0 then
        hideCountdown = hideCountdown - 1
        if hideCountdown == 0 then
            -- Hide unconditionally on our own deadline. The previous
            -- ownership-gated hide left the canvas stuck when ownership
            -- got handed off mid-conversation; simpler to always hide
            -- and let whoever takes the canvas next re-show it.
            if dialogCanvas >= 0 then
                UI.SetCanvasVisible(dialogCanvas, false)
            end
            restoreMusic()
        end
    end
end
