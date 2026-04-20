-- Demo "main" script attached to the green Cube. Drives:
--   - HUD tick counter
--   - Intro cutscene narration: text + audio co-fired from Lua so they
--     are perfectly synced regardless of game framerate
--   - Multi-line auto-advancing dialog sequences on Triangle press
--   - Two-beat idle meta line, gated to fire only after the cutscene
--     ends and only when the player is genuinely standing still

local tick = 0

-- UI handles, resolved once in onCreate. -1 = element absent.
local hudCanvas, tickCounterEl = -1, -1
local dialogCanvas, dialogBodyEl = -1, -1
local sysVoiceCanvas, sysVoiceText = -1, -1

-- ── Narration (system voice during intro cutscene) ──
-- Drives a Lua-side cutscene-frame counter (incremented per onUpdate
-- while Cutscene.IsPlaying() returns true) so reveals only happen
-- during the cutscene. Each entry fires text + audio together.
local cutsceneFrame = 0
local narrationIdx = 1
local cutsceneRanThisSession = false
local narrationHidden = false
-- Note: text length ~36 chars max for the SystemVoice text element
-- (288 px wide, ~8 px/char). Voice clip plays the full long-form line.
--
-- Frames are in Lua-tick units (~60 fps onUpdate). Each line waits
-- for the previous audio to finish + a real pause before firing.
-- Audio clip durations (approx): welcome 3.2 s, not_alarmed 2.1 s,
-- stable 1.5 s, other_cube 2.2 s, we_think 1.0 s. Gaps sized to
-- give each line ≥1 s of silence after audio finishes.
local narration = {
    { 5,   "Welcome to the Demo Environment.",  "system_welcome"          },
    { 260, "Do not be alarmed by the cube.",    "system_not_alarmed"      },
    { 500, "It is perfectly stable.",            "system_perfectly_stable" },
    { 720, "The other cube is also intentional.","system_other_cube"       },
    { 980, "We think.",                          "system_we_think"         },
}

-- ── Green cube dialog sequences ──
-- Press 1: full first conversation (3 auto-advancing lines)
-- Press 2: "Okay, good talk." (then dialog hides)
-- Press 3+: cycles three extras one per press
-- Durations are in onUpdate ticks (~60 fps), longer than the audio so
-- there's a beat of "text held" silence after each line plays.
-- Dialog body element is 224 px wide (~28 chars at 8 px/char). Lines
-- shortened to fit; voice clip carries the full original phrasing.
local INTERACTIONS = {
    {
        { text = "Hey.",                          clip = "sc_hey",       dur = 110 },
        { text = "Not supposed to be here yet.",  clip = "sc_not_yet",   dur = 200 },
        { text = "Did the camera stop moving?",   clip = "sc_camera",    dur = 240 },
    },
    {
        { text = "Okay, good talk.",              clip = "sc_good_talk", dur = 160 },
    },
}
local EXTRAS = {
    { text = "I spin. Gives me purpose.",         clip = "sc_purpose",       dur = 200 },
    { text = "Checkered thinks it's better.",     clip = "sc_thinks_better", dur = 220 },
    { text = "Don't trust things that bob.",      clip = "sc_bobbing",       dur = 180 },
}

local activeSeq, activeIdx, activeFrame = nil, 0, 0
local interactionCount, extraIdx = 0, 0

-- ── Idle detection ──
-- Two-beat sequence: "You appear to be standing still." → pause →
-- "This is either intentional... or deeply concerning." → hold → hide.
-- Gated behind IsPlaying so it can never fire during the cutscene.
local lastX, lastZ = 0, 0
local idleFrames = 0
local idleStep = 0       -- 0 = idle counting, 1 = beat-1 shown, 2 = beat-2 shown
local idleStepFrame = 0
local IDLE_THRESHOLD   = 300   -- ~5s at 60fps onUpdate
local IDLE_BEAT_PAUSE  = 180   -- ~3s gap between the two voice beats
local IDLE_HOLD        = 240   -- hold beat-2 visible ~4s before hiding

local function showLine(line)
    if dialogBodyEl >= 0 then
        UI.SetText(dialogBodyEl, line.text)
        UI.SetCanvasVisible(dialogCanvas, true)
    end
    Audio.Play(line.clip, 100, 64)
end

local function showSysVoice(text, clip)
    if sysVoiceCanvas >= 0 and sysVoiceText >= 0 then
        UI.SetText(sysVoiceText, text)
        UI.SetCanvasVisible(sysVoiceCanvas, true)
    end
    if clip ~= nil then Audio.Play(clip, 100, 64) end
end

local function hideSysVoice()
    if sysVoiceCanvas >= 0 then
        UI.SetCanvasVisible(sysVoiceCanvas, false)
    end
end

local function resetIdle()
    if idleStep > 0 then hideSysVoice() end
    idleFrames = 0
    idleStep = 0
    idleStepFrame = 0
end

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

    Animation.Play("bounce", { loop = true })
    Animation.Play("spin", { loop = true })
    Cutscene.Play("intro")
end

function onUpdate(self, dt)
    tick = tick + 1
    if tick % 30 == 0 and tickCounterEl >= 0 then
        UI.SetText(tickCounterEl, "tick=" .. tick)
    end

    -- ── Cutscene narration: text + audio co-fired ──
    local playing = Cutscene.IsPlaying()
    if playing then
        cutsceneRanThisSession = true
        cutsceneFrame = cutsceneFrame + 1
        if narrationIdx <= #narration and cutsceneFrame >= narration[narrationIdx][1] then
            local entry = narration[narrationIdx]
            showSysVoice(entry[2], entry[3])
            narrationIdx = narrationIdx + 1
        end
    elseif cutsceneRanThisSession and not narrationHidden then
        hideSysVoice()
        narrationHidden = true
    end

    -- ── Active dialog sequence advancement ──
    if activeSeq ~= nil then
        activeFrame = activeFrame + 1
        local current = activeSeq[activeIdx]
        if activeFrame >= current.dur then
            activeIdx = activeIdx + 1
            activeFrame = 0
            if activeIdx <= #activeSeq then
                showLine(activeSeq[activeIdx])
            else
                if dialogCanvas >= 0 then UI.SetCanvasVisible(dialogCanvas, false) end
                activeSeq = nil
            end
        end
    end

    -- ── Idle detection (only after cutscene + outside dialog) ──
    if not playing and activeSeq == nil and sysVoiceCanvas >= 0
            and cutsceneRanThisSession then
        local p = Player.GetPosition()
        local moved = (p.x ~= lastX) or (p.z ~= lastZ)
        if moved then
            resetIdle()
        elseif idleStep == 0 then
            idleFrames = idleFrames + 1
            if idleFrames > IDLE_THRESHOLD then
                showSysVoice("You're standing still.", "system_idle_detected")
                idleStep = 1
                idleStepFrame = 0
            end
        elseif idleStep == 1 then
            idleStepFrame = idleStepFrame + 1
            if idleStepFrame > IDLE_BEAT_PAUSE then
                showSysVoice("Intentional? Or concerning?", "system_deeply_concerning")
                idleStep = 2
                idleStepFrame = 0
            end
        elseif idleStep == 2 then
            idleStepFrame = idleStepFrame + 1
            if idleStepFrame > IDLE_HOLD then
                hideSysVoice()
                -- Stay in step 2 but with canvas hidden — won't re-fire
                -- until the player moves and resetIdle() runs.
                idleStep = 3
            end
        end
        lastX, lastZ = p.x, p.z
    end
end

function onInteract(self)
    interactionCount = interactionCount + 1
    if interactionCount <= #INTERACTIONS then
        activeSeq = INTERACTIONS[interactionCount]
    else
        extraIdx = (extraIdx % #EXTRAS) + 1
        activeSeq = { EXTRAS[extraIdx] }
    end
    activeIdx = 1
    activeFrame = 0
    showLine(activeSeq[activeIdx])
    Debug.Log("test_logger: dialog sequence " .. interactionCount .. " started")
end
