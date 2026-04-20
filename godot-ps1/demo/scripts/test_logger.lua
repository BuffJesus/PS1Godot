-- Demo "main" script attached to the green Cube. Drives:
--   - HUD tick counter
--   - Intro cutscene narration (system voice text appearing on cue)
--   - Cycling green-cube dialog on Triangle press

local tick = 0

-- UI handles, resolved once in onCreate. -1 = element absent.
local hudCanvas, tickCounterEl = -1, -1
local dialogCanvas, dialogBodyEl = -1, -1
local sysVoiceCanvas, sysVoiceText = -1, -1

-- Narration table: { frame, text }. Drive system-voice reveals during
-- the intro cutscene (240 frames @ 30fps = 8 s). After the last entry
-- + a fade-out frame, the canvas hides and gameplay begins.
local narration = {
    { 5,   "Welcome to the Interactive Demonstration Environment(TM)." },
    { 60,  "Please do not be alarmed by the cube." },
    { 100, "It is perfectly stable." },
    { 150, "The other cube is also intentional." },
    { 200, "We think." },
}
local narrationIdx = 1
local narrationDoneFrame = 240  -- hide the canvas at the end of the cutscene

-- Green cube dialog. First four lines are the canonical first
-- conversation, then we loop through the extras.
local dialogLines = {
    "Hey.",
    "...You're not supposed to be here yet.",
    "Did the camera finish moving? It never tells me.",
    "Okay, good talk.",
    -- cycling extras start here (index 5+)
    "I spin because it gives me purpose.",
    "The checkered one thinks it's better than me.",
    "Don't trust anything that bobs.",
}
local dialogIdx = 0

function onCreate(self)
    Debug.Log("test_logger: onCreate fired")

    hudCanvas = UI.FindCanvas("hud")
    if hudCanvas >= 0 then
        tickCounterEl = UI.FindElement(hudCanvas, "tick_counter")
    end
    dialogCanvas = UI.FindCanvas("dialog")
    if dialogCanvas >= 0 then
        dialogBodyEl = UI.FindElement(dialogCanvas, "body")
    end
    sysVoiceCanvas = UI.FindCanvas("system_voice")
    if sysVoiceCanvas >= 0 then
        sysVoiceText = UI.FindElement(sysVoiceCanvas, "vtxt")
    end
    Debug.Log("test_logger: hud=" .. hudCanvas .. " dialog=" .. dialogCanvas .. " sv=" .. sysVoiceCanvas)

    -- Background animations + audio cue + intro cutscene.
    Animation.Play("bounce", { loop = true })
    Animation.Play("spin", { loop = true })
    Cutscene.Play("intro")
end

function onUpdate(self, dt)
    tick = tick + 1

    -- HUD tick counter, refreshed once a second.
    if tick % 30 == 0 and tickCounterEl >= 0 then
        UI.SetText(tickCounterEl, "tick=" .. tick)
    end

    -- Intro narration: walk through the table, swap to the next line
    -- as its frame is reached. Hide the canvas once the cutscene ends.
    if sysVoiceCanvas >= 0 then
        if narrationIdx <= #narration and tick >= narration[narrationIdx][1] then
            UI.SetText(sysVoiceText, narration[narrationIdx][2])
            UI.SetCanvasVisible(sysVoiceCanvas, true)
            narrationIdx = narrationIdx + 1
        elseif narrationIdx > #narration and tick >= narrationDoneFrame then
            UI.SetCanvasVisible(sysVoiceCanvas, false)
        end
    end
end

function onInteract(self)
    -- Each Triangle press advances one line. Past the canonical four,
    -- loop back into the cycling extras (indices 5..end).
    dialogIdx = dialogIdx + 1
    if dialogIdx > #dialogLines then dialogIdx = 5 end
    if dialogBodyEl >= 0 then
        UI.SetText(dialogBodyEl, dialogLines[dialogIdx])
        UI.SetCanvasVisible(dialogCanvas, true)
    end
    Debug.Log("test_logger: dialog[" .. dialogIdx .. "] = " .. dialogLines[dialogIdx])
end
