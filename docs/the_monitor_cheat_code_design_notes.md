# The Monitor Cheat Code Design Notes

**Project:** The Monitor — PSXSplash Game Jam 2026  
**Scope:** jam-safe cheat-code support for a fixed-camera CCTV game  
**Important constraint:** there is **no player movement**.

The Monitor’s core loop is:

```text
sit at security station
switch CCTV feeds
watch timed events
press Cross to log what you saw
score at end of shift
```

The design explicitly keeps the player seated, disables movement controls, and uses D-pad Left/Right to switch between four fixed camera feeds. Cheats should therefore affect:

```text
CCTV feeds
CRT/static presentation
event visibility
event timing
score/logging behavior
event subjects
audio/ambience
```

They should **not** affect:

```text
player speed
jump height
gravity
collision
third-person movement
combat
inventory
quests
```

---

## 1. Existing Planned Cheats

The two planned codes are:

```text
BIGHEAD
HEREWEGO
```

These can still work for The Monitor, but they need Monitor-specific interpretations.

---

# 2. Cheat 1 — BIGHEAD

## Summary

```text
Code: BIGHEAD
Type: cosmetic
Risk: low
Jam-safe: yes
```

## Effect

Makes any humanoid/block-person event subjects have oversized heads.

This applies to:

```text
hallway figure walkby
hallway motionless figure
parking second figure
shadow/figure stand-ins if they use a visible mesh
```

## Why it fits The Monitor

The player does not move, but the game still has visible event subjects. Oversized heads are a classic cheat gag and will be funny when a spooky figure appears at the end of the hallway or beside a pillar.

## Implementation options

### Option A — segmented figure mesh

If the figure is built from separate head/body meshes:

```text
scale head mesh/object only
```

### Option B — simple block figure

If figure is a single mesh:

```text
use alternate big-head mesh variant
```

### Option C — skinned mesh

Probably not needed for The Monitor. If used:

```text
scale head bone if safe
```

## Rules

- Do not change hitboxes.
- Do not affect scoring.
- Apply when event subject is activated.
- Reapply on scene restart if cheat is still enabled.
- Cosmetic/session-only by default.

## Example behavior

```text
BIGHEAD enabled:
  hallway_figure appears with giant head
  parking_second_figure appears with giant head
  score rules unchanged
```

---

# 3. Cheat 2 — HEREWEGO

## Summary

```text
Code: HEREWEGO
Type: timing / replay convenience
Risk: medium
Jam-safe: yes, if clearly marked
```

## Effect

Compresses the shift timeline so events happen faster.

Instead of changing player movement, `HEREWEGO` turns the 8-minute shift into a faster “arcade/replay mode.”

Suggested behavior:

```text
ShiftSpeedMultiplier = 2.0
```

or:

```text
8-minute shift becomes 4-minute shift
```

## Why it fits The Monitor

The player is seated. There is no movement to boost. But speeding the shift makes sense as a cheat because the game is driven by a global event clock.

## Implementation approach

In `event_manager.lua`, shift time already advances each frame.

Normal:

```lua
shiftFrame = shiftFrame + 1
```

Cheat mode:

```lua
local speed = Cheats.IsEnabled("HEREWEGO") and 2 or 1
shiftFrame = shiftFrame + speed
```

If `dt / 4096` is used:

```lua
shiftFrame = shiftFrame + frameStep * speed
```

## Score behavior

Because this affects difficulty, mark it as progression-affecting.

Recommended score screen note:

```text
CHEATS USED
```

or:

```text
FAST SHIFT
```

## Rules

- Do not change controls.
- Do not skip events.
- Do not break event windows.
- Consider scaling log windows slightly if it becomes too punishing.
- Mark run as cheat-assisted.

---

# 4. Three Additional Monitor-Specific Cheats

Recommended first five:

```text
BIGHEAD
HEREWEGO
ALLSEE
STATIC
NIGHTOWL
```

The new three are:

```text
ALLSEE
STATIC
NIGHTOWL
```

They fit the no-movement CCTV design.

---

# 5. Cheat 3 — ALLSEE

## Summary

```text
Code: ALLSEE
Type: assist / information cheat
Risk: medium
Jam-safe: yes
```

## Effect

Gives subtle hints when an event is happening on another feed.

Possible presentation:

```text
feed label flickers
tiny red dot appears beside active feed number
CRT emits a short warning tick
inactive feed name briefly jitters
```

Example:

```text
CAM 03 - PARKING  *
```

or:

```text
CAM 03 text pulses for 30 frames when event fires
```

## Why it fits The Monitor

The core theme is attention: the only variable is what the player is watching. `ALLSEE` breaks that theme in a fun cheat-like way by letting the monitor “sense” activity elsewhere.

## Implementation idea

When an event fires:

```lua
if Cheats.IsEnabled("ALLSEE") then
    FeedManager.MarkFeedActivity(event.feed, 90)
end
```

`FeedManager` can then update HUD text or show a tiny indicator.

## Score behavior

This is an assist cheat.

Recommended:

```text
mark run as cheat-assisted
do not update best_score
or show "BEST not saved with ALLSEE"
```

## Rules

- Do not auto-log events.
- Do not switch feeds automatically.
- Do not reveal event names.
- Only hint that something is happening somewhere.
- Keep the normal game playable.

## Good feedback

```text
small indicator
short static tick
feed label flicker
```

Avoid:

```text
giant popup
event name spoiler
auto camera snap
```

---

# 6. Cheat 4 — STATIC

## Summary

```text
Code: STATIC
Type: cosmetic / atmosphere
Risk: low
Jam-safe: yes
```

## Effect

Turns up CRT/static presentation.

When enabled:

```text
feed switches show longer static flash
random micro-static flickers occur
CRT click gets slightly louder or more frequent
screen shake on feed switch is slightly stronger
static_noise overlay appears occasionally
```

This is basically “make the game look more haunted.”

## Why it fits The Monitor

The entire game is framed as CCTV/CRT observation. A static-heavy mode is thematic, cheap, and does not require new gameplay systems.

## Implementation ideas

Normal feed switch:

```text
static_flash visible ~20 frames
Camera.ShakeRaw(80, 4)
Audio.Play("crt_click")
```

STATIC mode:

```text
static_flash visible 35-45 frames
Camera.ShakeRaw(110, 5)
random 2-5 frame static flickers during shift
optional extra quiet static tick
```

## Score behavior

Cosmetic only.

Recommended:

```text
score still saves
```

## Rules

- Do not obscure events too much.
- Keep random static short.
- Avoid making subtle events unfair.
- Avoid high-frequency flashing.

## Good use

Add only small random flickers:

```text
every 6-20 seconds, 2-4 frames of static
```

Do not spam.

---

# 7. Cheat 5 — NIGHTOWL

## Summary

```text
Code: NIGHTOWL
Type: assist / accessibility
Risk: low/medium
Jam-safe: yes
```

## Effect

Makes logging more forgiving.

Possible effects:

```text
larger scoring windows
slower clock
longer event visibility for subtle events
```

Recommended jam-safe version:

```text
ScoreWindowMultiplier = 1.5
```

This means if an event normally has a 120-frame log window, it becomes 180 frames while NIGHTOWL is enabled.

## Why it fits The Monitor

The player is a security guard. “Night Owl” feels like an awareness/focus cheat without adding movement or combat.

## Implementation idea

During scoring:

```lua
local window = event.window
if Cheats.IsEnabled("NIGHTOWL") then
    window = window * 1.5
end
```

If fractional math is awkward, use integer-friendly values:

```lua
window = window + window / 2
```

## Score behavior

Assist cheat.

Recommended:

```text
do not update best_score
or mark score as assisted
```

## Rules

- Do not auto-log.
- Do not reveal events.
- Only affects scoring forgiveness.
- Keep end-screen honest.

## Score screen note

```text
CHEAT: NIGHTOWL
```

or:

```text
ASSISTED SHIFT
```

---

# 8. Recommended Cheat Set for The Monitor

| Code | Effect | Category | Saves best score? | Risk |
|---|---|---|---:|---:|
| BIGHEAD | Oversized figure heads | Cosmetic | yes | low |
| HEREWEGO | Faster shift timeline | Timing / challenge | no or assisted | medium |
| ALLSEE | Feed activity hints | Assist | no or assisted | medium |
| STATIC | Extra CRT/static chaos | Cosmetic | yes | low |
| NIGHTOWL | Wider scoring windows | Assist | no or assisted | low/medium |

---

# 9. Cheat State Model

Keep this tiny.

```lua
Cheats = Cheats or {
    enabled = {},
    assisted = false
}

function Cheats.IsEnabled(code)
    return Cheats.enabled[code] == true
end

function Cheats.Set(code, value)
    Cheats.enabled[code] = value == true

    if code == "HEREWEGO" or code == "ALLSEE" or code == "NIGHTOWL" then
        if value == true then
            Cheats.assisted = true
        end
    end
end

function Cheats.Toggle(code)
    Cheats.Set(code, not Cheats.IsEnabled(code))
end

function Cheats.IsAssisted()
    return Cheats.assisted == true
end
```

The score screen can check:

```lua
if Cheats.IsAssisted() then
    -- show assisted label
    -- optionally skip Persist.Set("best_score", caught)
end
```

---

# 10. Cheat Entry

The Monitor has limited controls:

```text
D-pad Left/Right = switch feed
Cross = log
Start = restart after shift
```

Cheat entry should not interfere with the main loop.

Recommended options:

## Option A — Title screen only

Cheat sequences only work while `title_card` is visible.

This is safest.

## Option B — Score screen only

Cheats are entered after a run, then apply on restart.

Also safe.

## Option C — Hidden debug Lua call

For jam testing:

```lua
Cheats.Toggle("BIGHEAD")
```

or a dev-only key sequence.

## Avoid

Do not allow normal D-pad feed switching to accidentally trigger cheats during the shift.

---

# 11. Suggested Button Sequences

Since the input set is small, keep sequences short but only active on title/score screen.

Examples:

```text
BIGHEAD:
  LEFT, RIGHT, LEFT, RIGHT, CROSS

HEREWEGO:
  RIGHT, RIGHT, RIGHT, CROSS

ALLSEE:
  LEFT, LEFT, RIGHT, RIGHT, CROSS

STATIC:
  LEFT, RIGHT, RIGHT, LEFT, CROSS

NIGHTOWL:
  LEFT, LEFT, LEFT, CROSS
```

These are placeholders. Pick whatever feels better.

---

# 12. Lua Integration Points

## 12.1 `scene_monitor_init.lua`

Initialize cheat state and title-screen cheat entry.

```lua
onSceneCreationEnd:
  Cheats.Init()
  Controls.SetEnabled(false)
  Camera.FollowPsxPlayer(false)
```

## 12.2 `feed_manager.lua`

Use cheats for:

```text
STATIC
ALLSEE
```

Possible hooks:

```lua
FeedManager.MarkFeedActivity(feed, frames)
FeedManager.SetStaticMode(enabled)
```

## 12.3 `event_manager.lua`

Use cheats for:

```text
HEREWEGO
ALLSEE
BIGHEAD
```

Hooks:

```lua
if Cheats.IsEnabled("HEREWEGO") then
    shiftFrame = shiftFrame + 2
else
    shiftFrame = shiftFrame + 1
end

if Cheats.IsEnabled("ALLSEE") then
    FeedManager.MarkFeedActivity(event.feed, 90)
end

if Cheats.IsEnabled("BIGHEAD") then
    applyBigHeadToEventSubject(event.subject)
end
```

## 12.4 `score_screen.lua`

Use cheats for:

```text
NIGHTOWL
assisted score display
best score suppression
```

Hooks:

```lua
local window = event.window
if Cheats.IsEnabled("NIGHTOWL") then
    window = window + window / 2
end

if not Cheats.IsAssisted() then
    Persist.Set("best_score", caught)
end
```

---

# 13. Implementation Notes Per Cheat

## BIGHEAD

```text
Apply on event subject activation.
Use alternate mesh or scale head object.
Cosmetic.
Best score allowed.
```

## HEREWEGO

```text
Multiply shiftFrame advancement.
Does not alter feed switching.
Mark assisted or challenge mode.
Do not skip events.
```

## ALLSEE

```text
When event fires, feed label indicator pulses.
Assist cheat.
Do not reveal event name.
Do not auto-log.
```

## STATIC

```text
Longer feed-switch static.
Occasional random tiny static flickers.
Cosmetic.
Avoid hiding events.
Best score allowed.
```

## NIGHTOWL

```text
Increase score windows.
Assist cheat.
Do not reveal events.
Suppress or mark best score.
```

---

# 14. Jam-Safe Implementation Order

## Step 1 — Cheat registry

Add a tiny table:

```lua
CHEAT_DEFS = {
    BIGHEAD = { assisted = false },
    HEREWEGO = { assisted = true },
    ALLSEE = { assisted = true },
    STATIC = { assisted = false },
    NIGHTOWL = { assisted = true }
}
```

## Step 2 — Toggle function

Add:

```lua
Cheats.Toggle(code)
Cheats.IsEnabled(code)
Cheats.IsAssisted()
```

## Step 3 — Debug/test activation

For jam, activate cheats through simple title-screen sequences or dev flags.

## Step 4 — Implement STATIC

Lowest risk. Only UI/static timing.

## Step 5 — Implement NIGHTOWL

Small scoring change.

## Step 6 — Implement HEREWEGO

Small event-clock change, test full shift.

## Step 7 — Implement ALLSEE

Small UI hint when events fire.

## Step 8 — Implement BIGHEAD

Depends on actual figure mesh structure. If risky, use alternate event subject mesh.

---

# 15. What Not To Add for The Monitor

Do not add these movement/RPG cheats:

```text
MOONBOOTS
TINYMODE as player scale
NOCLIP
GHOSTCAM free camera
SPEEDRUN movement boost
INFINITE HEALTH
INFINITE ITEMS
```

They do not fit The Monitor’s design.

If `TINYMODE` is ever reused, it should affect event figures only, not a player avatar.

---

# 16. IDE-Agent Prompt

Use this prompt to correct the cheat implementation for The Monitor.

```text
You are helping me add cheat-code support to The Monitor, a PSXSplash jam game.

Important:
The Monitor has no player movement. The player is seated at a security station. Controls are disabled for movement. D-pad Left/Right switches CCTV feeds, Cross logs the current feed/time, and Start restarts after the shift.

Do not implement movement, gravity, jump, collision, noclip, player scaling, or third-person camera cheats.

Existing planned cheats:
- BIGHEAD
- HEREWEGO

Use these Monitor-specific meanings:
- BIGHEAD: oversized heads for visible humanoid/block-figure event subjects.
- HEREWEGO: faster shift timeline / replay-speed mode, not movement speed.

Add three Monitor-specific cheats:
- ALLSEE: subtle feed activity hints when an event fires on another feed.
- STATIC: extra CRT/static effects and stronger feed-switch interference.
- NIGHTOWL: wider scoring windows / more forgiving logging.

Implementation rules:
- Keep cheats optional.
- Keep normal shift gameplay intact.
- Use only shipped Lua APIs.
- Do not invent Player movement APIs.
- Do not use unshipped systems like Save.WriteSlot, Dialog.Show, World.SetTimeOfDay, Audio.SetPitch, or Audio.Play3D.
- Cosmetic cheats may still allow best_score persistence.
- Assist/timing cheats should mark the run as assisted and either suppress best_score or label it clearly.
- Do not auto-log events.
- Do not reveal event names with ALLSEE.
- Do not let STATIC obscure subtle events too heavily.
- Keep everything jam-safe and small.

Suggested implementation:
1. Add `cheats.lua` with:
   - Cheats.IsEnabled(code)
   - Cheats.Set(code, enabled)
   - Cheats.Toggle(code)
   - Cheats.IsAssisted()
2. Add cheat definitions:
   - BIGHEAD: cosmetic
   - HEREWEGO: assisted/timing
   - ALLSEE: assisted/hint
   - STATIC: cosmetic
   - NIGHTOWL: assisted/scoring
3. Wire:
   - `event_manager.lua`:
     - HEREWEGO shift speed multiplier
     - ALLSEE feed activity marker on event fire
     - BIGHEAD event subject visual change if safe
   - `feed_manager.lua`:
     - STATIC longer switch static
     - ALLSEE feed label activity marker
   - `score_screen.lua`:
     - NIGHTOWL scoring window multiplier
     - assisted-run label / best-score suppression
4. Add title-screen or score-screen-only cheat entry so normal feed switching does not accidentally enter cheats.
5. Add small UI feedback:
   - `BIGHEAD ENABLED`
   - `STATIC ENABLED`
   - etc.

Final response:
- Summary
- Files changed
- What is implemented
- What is scaffolded only
- How to test
- Risks/TODOs
```

---

# 17. Bottom Line

For **The Monitor**, the correct cheat set is:

```text
BIGHEAD   = spooky figures have giant heads
HEREWEGO  = faster shift timeline
ALLSEE    = feed activity hints
STATIC    = stronger CRT/static chaos
NIGHTOWL  = more forgiving scoring windows
```

This keeps cheats aligned with the actual game:

```text
fixed CCTV feeds
no movement
event observation
logging accuracy
CRT atmosphere
score screen replayability
```

The earlier movement-based cheat ideas belong to the future RPG/tooling side, not this jam project.
