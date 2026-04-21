-- Demo "main" script attached to the green Cube. Drives:
--   - HUD tick counter
--   - Intro cutscene narration: text + audio co-fired from Lua so they
--     are perfectly synced regardless of game framerate
--   - Multi-line auto-advancing dialog sequences on Triangle press
--   - Two-beat idle meta line, gated to fire only after the cutscene
--     ends and only when the player is genuinely standing still

local tick = 0

-- Camera mode toggle state. Select cycles between "third" and "first"
-- via Camera.SetMode. ThirdPerson is the default; the runtime auto-hides
-- the avatar mesh in first-person so the camera can live at player eye
-- height without rendering the back of its own head.
local cameraMode = "third"

-- UI handles, resolved once in onCreate. -1 = element absent.
local dialogCanvas, dialogBodyEl = -1, -1
local sysVoiceCanvas, sysVoiceText = -1, -1

-- (Player avatar tracking moved to the runtime's v21 auto-avatar path.
-- Kept here as a sentinel comment so anyone searching for playerMesh
-- finds the explanation.)

-- ── Narration (system voice during intro cutscene) ──
-- Drives a Lua-side cutscene-frame counter (incremented per onUpdate
-- while Cutscene.IsPlaying() returns true) so reveals only happen
-- during the cutscene. Each entry fires text + audio together.
local cutsceneFrame = 0
local narrationIdx = 1
local cutsceneRanThisSession = false
local narrationHidden = false
-- SystemVoice text element is 288 px wide (~36 chars/line at 8 px/char).
-- Runtime now supports '\n' as an explicit line break (advances Y by
-- glyph height, resets X), so we can author two-line text where the
-- intended line doesn't fit single-line. Voice clip plays the full
-- long-form phrasing.
--
-- Frames are in Lua-tick units (~60 fps onUpdate). Pacing: each line
-- starts shortly after the previous audio finishes, with ~0.5–1 s
-- of breathing room. Audio clip durations (approx): welcome 3.2 s,
-- not_alarmed 2.1 s, stable 1.5 s, other_cube 2.2 s, we_think 1.0 s.
local narration = {
    { 5,   "Welcome to the Interactive\nDemonstration Environment.", "system_welcome"          },
    { 210, "Please do not be alarmed\nby the cube.",                 "system_not_alarmed"      },
    { 345, "It is perfectly stable.",                                "system_perfectly_stable" },
    { 455, "The other cube is also\nintentional.",                   "system_other_cube"       },
    { 600, "We think.",                                              "system_we_think"         },
}

-- ── Green cube dialog sequences ──
-- Press 1: full first conversation (3 auto-advancing lines)
-- Press 2: "Okay, good talk." (then dialog hides)
-- Press 3+: cycles three extras one per press
-- Durations are in onUpdate ticks (~60 fps), longer than the audio so
-- there's a beat of "text held" silence after each line plays.
-- Dialog body element is 224 px wide (~28 chars/line at 8 px/char).
-- Use '\n' to split long lines across two rows (runtime-supported).
local INTERACTIONS = {
    {
        { text = "Hey.",                                          clip = "sc_hey",       dur = 110 },
        { text = "...You're not supposed\nto be here yet.",       clip = "sc_not_yet",   dur = 200 },
        { text = "Did the camera finish\nmoving? It never tells me.", clip = "sc_camera", dur = 240 },
    },
    {
        { text = "Okay, good talk.",                              clip = "sc_good_talk", dur = 160 },
    },
}
local EXTRAS = {
    { text = "I spin because it\ngives me purpose.",              clip = "sc_purpose",       dur = 200 },
    { text = "The checkered one thinks\nit's better than me.",    clip = "sc_thinks_better", dur = 220 },
    { text = "Don't trust anything\nthat bobs.",                  clip = "sc_bobbing",       dur = 180 },
}

local activeSeq, activeIdx, activeFrame = nil, 0, 0
local interactionCount, extraIdx = 0, 0

-- Watchdog: if the auto-advance state-machine wedges and a line has
-- been on screen for longer than `current.dur + DIALOG_IDLE_GRACE`
-- frames without progressing, force-hide. Resets on every onInteract
-- and every line advance so a normal sequence never trips it — only
-- a stuck dialog does, and it clears within ~3 s instead of staying
-- on screen forever.
local lastAdvanceFrame = 0
local DIALOG_IDLE_GRACE = 180   -- 3 s at 60 fps onUpdate

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
    -- Start the bullet-11 test rig's wave animation if present. findSkinAnim
    -- returns silently if there's no mesh called "SkinnedMesh" in the scene,
    -- so this no-ops for scenes without the test asset.
    SkinnedAnim.Play("SkinnedMesh", "wave", { loop = true })
    -- Lock the player in place for the intro cutscene; otherwise pad input
    -- moves the character around while the camera is on its track and the
    -- player ends up wherever they wandered (instead of at PS1Player's
    -- authored spawn) when control returns. Re-enabled in onUpdate when
    -- Cutscene.IsPlaying() drops to false.
    Controls.SetEnabled(false)
    Cutscene.Play("intro")
end

function onUpdate(self, dt)
    tick = tick + 1

    -- Auto-hide delayed system_voice messages. Trigger scripts (e.g.,
    -- checkered_ambient) that want to show a follow-up message then
    -- fade it can't poll time themselves — they set the global
    -- `sysVoiceHideAtFrame` and we handle the hide on their behalf.
    if sysVoiceHideAtFrame ~= nil and Timer.GetFrameCount() >= sysVoiceHideAtFrame then
        hideSysVoice()
        sysVoiceHideAtFrame = nil
    end

    -- Press Select to toggle first/third person. Demo only — a real game
    -- would put this behind an Options menu + persisted preference.
    if Input.IsPressed(Input.SELECT) then
        if cameraMode == "third" then
            Camera.SetMode("first")
            cameraMode = "first"
        else
            Camera.SetMode("third")
            cameraMode = "third"
        end
        Debug.Log("camera mode -> " .. cameraMode)
    end

    -- Player avatar tracking now happens in the runtime (v21 auto-avatar
    -- via PS1Player's child MeshInstance3D + playerAvatarOffset header
    -- field). The Lua loop that used to Entity.SetPosition the Player
    -- mesh each frame was removed — it was duplicating work and risked
    -- fighting the runtime's own transform update. See scenemanager.cpp
    -- "auto-track the player avatar mesh" block.

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
        -- Cutscene just finished — give the player back their input. Pairs
        -- with the SetEnabled(false) at the bottom of onCreate.
        Controls.SetEnabled(true)
    end

    -- ── Active dialog sequence advancement ──
    -- Yield if some other script took over the dialog canvas — otherwise
    -- our next scheduled line would overwrite their text. We don't hide
    -- the canvas here; the new owner is managing it.
    if activeSeq ~= nil then
        if currentDialogOwner ~= MY_DIALOG_OWNER then
            activeSeq = nil
        else
            activeFrame = activeFrame + 1
            local current = activeSeq[activeIdx]
            if activeFrame >= current.dur then
                activeIdx = activeIdx + 1
                activeFrame = 0
                lastAdvanceFrame = tick
                if activeIdx <= #activeSeq then
                    showLine(activeSeq[activeIdx])
                else
                    if dialogCanvas >= 0 then UI.SetCanvasVisible(dialogCanvas, false) end
                    activeSeq = nil
                end
            elseif (tick - lastAdvanceFrame) > current.dur + DIALOG_IDLE_GRACE then
                -- Watchdog tripped — line has been on screen for longer
                -- than its authored duration plus a 3 s grace and still
                -- hasn't advanced. Force-hide rather than leaving it stuck.
                if dialogCanvas >= 0 then UI.SetCanvasVisible(dialogCanvas, false) end
                activeSeq = nil
                Debug.Log("test_logger: dialog watchdog fired, forced hide")
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
                showSysVoice("You appear to be\nstanding still.", "system_idle_detected")
                idleStep = 1
                idleStepFrame = 0
            end
        elseif idleStep == 1 then
            idleStepFrame = idleStepFrame + 1
            if idleStepFrame > IDLE_BEAT_PAUSE then
                showSysVoice("This is either intentional...\nor deeply concerning.", "system_deeply_concerning")
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

-- Owner ID for the shared `currentDialogOwner` global. Each script that
-- shows the dialog canvas claims ownership on Triangle press; other scripts
-- see the change and stop advancing their own queued sequences. Without
-- this, interacting with Cube2 mid-conversation would be overwritten by
-- the next auto-advancing line from Cube on the shared dialog canvas.
local MY_DIALOG_OWNER = 1

function onInteract(self)
    currentDialogOwner = MY_DIALOG_OWNER
    interactionCount = interactionCount + 1
    if interactionCount <= #INTERACTIONS then
        activeSeq = INTERACTIONS[interactionCount]
    else
        extraIdx = (extraIdx % #EXTRAS) + 1
        activeSeq = { EXTRAS[extraIdx] }
    end
    activeIdx = 1
    activeFrame = 0
    lastAdvanceFrame = tick
    showLine(activeSeq[activeIdx])
    Debug.Log("test_logger: dialog sequence " .. interactionCount .. " started")
end
