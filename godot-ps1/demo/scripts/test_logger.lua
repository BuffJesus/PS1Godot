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

-- ── Green cube dialog (one line per Triangle press) ──
-- Each press cycles to the next line, plays the audio, and schedules
-- a hide ~1.5 s later. Pressing again before the hide-tick re-arms
-- the timer with the next line. Replaces the previous multi-line
-- auto-advance state machine which could wedge and leave a stuck
-- dialog box on screen — single-state code is harder to wedge.
local DIALOG_LINES = {
    { text = "Hey.",                                          clip = "sc_hey"          },
    { text = "...You're not supposed\nto be here yet.",       clip = "sc_not_yet"      },
    { text = "Did the camera finish\nmoving? It never tells me.", clip = "sc_camera"   },
    { text = "Okay, good talk.",                              clip = "sc_good_talk"    },
    { text = "I spin because it\ngives me purpose.",          clip = "sc_purpose"      },
    { text = "The checkered one thinks\nit's better than me.",clip = "sc_thinks_better"},
    { text = "Don't trust anything\nthat bobs.",              clip = "sc_bobbing"      },
}
local lineIdx = 0
local hideAtTick = 0
local LINE_MIN_HOLD = 90   -- ~1.5 s at 60 fps onUpdate (used when clip is short/missing)
local LINE_TAIL     = 30   -- ~0.5 s after audio ends before hiding

-- ── Walk / idle animation state ──
-- Drives the humanoid avatar's walk cycle. isWalking tracks the last
-- commanded state so we only call SkinnedAnim.Play on edges (starting
-- the clip every frame would reset it to frame 0 and the legs would
-- freeze mid-stride). walkLastX/Z are sampled independently of the
-- idle detector below because that block is gated on cutscene state.
local isWalking = false
local walkLastX, walkLastZ = 0, 0

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

-- Music ducking: drop the BGM master volume while a dialog is on
-- screen, restore on hide. Reads the global `bgmMasterVol` set by
-- onCreate so all scripts share the same restore target — and so a
-- script that boots later (without seeing the original volume) can
-- still restore correctly. DUCK_VOL is the level to drop to.
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

-- Show a dialog line. Returns the tick at which the dialog should
-- auto-hide — LINE_MIN_HOLD for silent/missing clips, or the clip's
-- actual playback length + LINE_TAIL when audio is present. This way
-- the player always hears the full voice clip before the box vanishes.
--
-- Scene.PauseFor gives a small impact cue on every reveal — this is
-- the "press X to continue" juice that makes dialog land instead of
-- just appearing. 3 frames ≈ 50 ms is enough to feel without
-- delaying the audio clip. (Camera.Shake would pair well but its
-- intensity arg is FP12 and psxlua can't parse decimal literals
-- like 0.04; expose a Camera.ShakeRaw(rawFp12, frames) helper or
-- pass a Convert.IntToFp-based expression when we need the shake.)
local function showLine(line)
    if dialogBodyEl >= 0 then
        UI.SetText(dialogBodyEl, line.text)
        UI.SetCanvasVisible(dialogCanvas, true)
    end
    Scene.PauseFor(3)
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
    return tick + hold
end

local function showSysVoice(text, clip)
    if sysVoiceCanvas >= 0 and sysVoiceText >= 0 then
        UI.SetText(sysVoiceText, text)
        UI.SetCanvasVisible(sysVoiceCanvas, true)
    end
    if clip ~= nil then
        duckMusic()
        Audio.Play(clip, 100, 64)
    end
end

local function hideSysVoice()
    if sysVoiceCanvas >= 0 then
        UI.SetCanvasVisible(sysVoiceCanvas, false)
    end
    restoreMusic()
end

local function resetIdle()
    if idleStep > 0 then hideSysVoice() end
    idleFrames = 0
    idleStep = 0
    idleStepFrame = 0
end

function onCreate(self)
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
    -- Kick off the demo BGM. Sequencer loops back to beat 0 when it
    -- runs out of events. The "master" volume gets stashed in a global
    -- so dialog scripts can duck and restore via Music.SetVolume(...)
    -- — see helpers below. 40/127 keeps the BGM well under voice
    -- dialog at full strength; tune higher per-scene if needed.
    bgmMasterVol = 40
    if Music ~= nil then
        Music.Play("retro_adventure", bgmMasterVol)
    end
    -- Seed walk-detector with the spawn position so the first-frame delta
    -- doesn't trip walk->idle detection against (0,0).
    do
        local p = Player.GetPosition()
        walkLastX, walkLastZ = p.x, p.z
    end
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

    -- Walk-cycle state machine for the humanoid avatar. Fires only on
    -- edges — calling SkinnedAnim.Play every frame would pin the clip
    -- to frame 0 and the legs would never move. Gated on Cutscene state
    -- so the avatar stays still during the intro camera track (controls
    -- are disabled then anyway, but being explicit avoids a blink of
    -- walk on the first post-cutscene frame).
    if not Cutscene.IsPlaying() then
        local pp = Player.GetPosition()
        local moving = (pp.x ~= walkLastX) or (pp.z ~= walkLastZ)
        walkLastX, walkLastZ = pp.x, pp.z
        -- Mixamo exports every take under the literal name "mixamo_com" —
        -- the exporter's auto-wire path passes the clip through verbatim.
        -- If/when we rename clips in the Godot AnimationPlayer editor,
        -- update the name here to match.
        if moving and not isWalking then
            SkinnedAnim.Play("Player", "mixamo_com", { loop = true })
            isWalking = true
        elseif not moving and isWalking then
            -- Rest in bind pose (T-pose) rather than freezing on the last
            -- walk-cycle frame, which would leave the character mid-stride.
            SkinnedAnim.BindPose("Player")
            isWalking = false
        end
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
        -- Cutscene just finished — give the player back their input. Pairs
        -- with the SetEnabled(false) at the bottom of onCreate.
        Controls.SetEnabled(true)
    end

    -- ── Single-line dialog auto-hide ──
    -- onInteract sets hideAtTick to (tick + LINE_HOLD). When tick passes
    -- that mark, hide the canvas unconditionally. Previous versions
    -- gated this on currentDialogOwner == MY_DIALOG_OWNER but that was
    -- left dialogs stuck when ownership got handed off — simpler to
    -- always hide on our own deadline, and let whoever took over
    -- re-show if they need to.
    if hideAtTick > 0 and tick >= hideAtTick then
        if dialogCanvas >= 0 then
            UI.SetCanvasVisible(dialogCanvas, false)
        end
        restoreMusic()
        hideAtTick = 0
    end

    -- ── Idle detection (only after cutscene + outside dialog) ──
    if not playing and hideAtTick == 0 and sysVoiceCanvas >= 0
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
    lineIdx = (lineIdx % #DIALOG_LINES) + 1
    hideAtTick = showLine(DIALOG_LINES[lineIdx])
end
