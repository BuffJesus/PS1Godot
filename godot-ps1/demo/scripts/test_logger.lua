-- Demo "main" script attached to the green Cube. Drives:
--   - HUD tick counter
--   - Intro cutscene narration (auto-advancing system voice text in
--     sync with the PS1AudioEvents on the cutscene)
--   - Multi-line dialog sequences on Triangle interaction matching
--     the Portal-style script
--   - Idle meta line (gated to fire only after the cutscene ends)

local tick = 0

-- UI handles, resolved once in onCreate. -1 = element absent.
local hudCanvas, tickCounterEl = -1, -1
local dialogCanvas, dialogBodyEl = -1, -1
local sysVoiceCanvas, sysVoiceText = -1, -1

-- Cutscene-frame-based timing for narration. Increments only while
-- Cutscene.IsPlaying() returns true so the text reveals stay in sync
-- with the audio events on the cutscene regardless of game framerate.
local cutsceneFrame = 0
local narrationIdx = 1
local cutsceneRanThisSession = false
local narrationHidden = false

-- Five system-voice text reveals matched to the audio event frames on
-- IntroCutscene (frames 5/60/100/150/200). Text shows just before
-- audio fires so the subtitle leads the voice slightly.
local narration = {
    { 5,   "Welcome to the Interactive Demonstration Environment(TM)." },
    { 60,  "Please do not be alarmed by the cube." },
    { 100, "It is perfectly stable." },
    { 150, "The other cube is also intentional." },
    { 200, "We think." },
}

-- Dialog sequences, one per interaction. Each line stays for `dur`
-- frames before auto-advancing. Sequence ends → dialog hides.
--   Press 1: 3-line first conversation.
--   Press 2: "Okay, good talk." (then dialog disappears).
--   Press 3+: cycle three extras one per press.
local INTERACTIONS = {
    {
        { text = "Hey.",                                              clip = "sc_hey",        dur = 60  },
        { text = "...You're not supposed to be here yet.",            clip = "sc_not_yet",    dur = 110 },
        { text = "Did the camera finish moving? It never tells me.", clip = "sc_camera",     dur = 130 },
    },
    {
        { text = "Okay, good talk.",                                  clip = "sc_good_talk",  dur = 90  },
    },
}
local EXTRAS = {
    { text = "I spin because it gives me purpose.",                   clip = "sc_purpose",        dur = 110 },
    { text = "The checkered one thinks it's better than me.",         clip = "sc_thinks_better",  dur = 120 },
    { text = "Don't trust anything that bobs.",                       clip = "sc_bobbing",        dur = 100 },
}

-- Active dialog playback state.
local activeSeq = nil
local activeIdx = 0
local activeFrame = 0
local interactionCount = 0
local extraIdx = 0

-- Idle detection. Watches Player.GetPosition once gameplay begins
-- (cutscene fully done). Resets when the player moves. Threshold ≈ 5s
-- of stillness.
local lastX, lastZ = 0, 0
local idleFrames = 0
local idleShown = false
local IDLE_THRESHOLD = 150

local function showLine(line)
    if dialogBodyEl >= 0 then
        UI.SetText(dialogBodyEl, line.text)
        UI.SetCanvasVisible(dialogCanvas, true)
    end
    Audio.Play(line.clip, 100, 64)
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

    -- ── Cutscene narration text reveals ──
    -- Drive cutsceneFrame from IsPlaying so text aligns with audio
    -- events regardless of framerate. When the cutscene ends, hide
    -- the system voice canvas once.
    local playing = Cutscene.IsPlaying()
    if playing then
        cutsceneRanThisSession = true
        cutsceneFrame = cutsceneFrame + 1
        if sysVoiceCanvas >= 0 and narrationIdx <= #narration
                and cutsceneFrame >= narration[narrationIdx][1] then
            UI.SetText(sysVoiceText, narration[narrationIdx][2])
            UI.SetCanvasVisible(sysVoiceCanvas, true)
            narrationIdx = narrationIdx + 1
        end
    elseif cutsceneRanThisSession and not narrationHidden then
        if sysVoiceCanvas >= 0 then
            UI.SetCanvasVisible(sysVoiceCanvas, false)
        end
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

    -- ── Idle detection ──
    -- Never fires while the cutscene is playing or while a dialog
    -- sequence is mid-flight (player would be reading not idle).
    if not playing and activeSeq == nil and sysVoiceCanvas >= 0 then
        local p = Player.GetPosition()
        local moved = (p.x ~= lastX) or (p.z ~= lastZ)
        if moved then
            idleFrames = 0
            if idleShown then
                idleShown = false
                UI.SetCanvasVisible(sysVoiceCanvas, false)
            end
        elseif cutsceneRanThisSession then
            idleFrames = idleFrames + 1
            if not idleShown and idleFrames > IDLE_THRESHOLD then
                UI.SetText(sysVoiceText, "You appear to be standing still. This is either intentional... or deeply concerning.")
                UI.SetCanvasVisible(sysVoiceCanvas, true)
                Audio.Play("system_idle_detected", 100, 64)
                idleShown = true
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
