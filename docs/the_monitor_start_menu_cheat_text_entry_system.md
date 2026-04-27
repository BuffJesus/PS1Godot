# The Monitor Cheat Text Entry System

**Project:** The Monitor — PSXSplash Game Jam 2026  
**Scope:** cheat-code entry through the starting menu only  
**Important constraint:** no in-shift cheat entry and no player movement cheats.

The Monitor uses fixed CCTV feeds. During the shift, the player should only be focused on:

```text
switching feeds
watching events
logging observations
surviving the shift timer
```

Cheat entry therefore belongs on the starting menu, not during gameplay.

---

## 1. Core Rule

Cheats are entered as **text codes on the starting menu screen only**.

There is no button-sequence cheat entry during the shift.

This prevents normal CCTV controls from accidentally activating cheats while the player is switching feeds.

---

## 2. Supported Codes

Initial planned cheat set:

```text
BIGHEAD
HEREWEGO
ALLSEE
STATIC
NIGHTOWL
```

Meanings:

| Code | Effect | Type | Assisted run? |
|---|---|---|---:|
| BIGHEAD | Event figures have oversized heads | Cosmetic | No |
| HEREWEGO | Faster shift timeline / replay-speed mode | Timing | Yes |
| ALLSEE | Subtle feed activity hints | Assist | Yes |
| STATIC | Stronger CRT/static chaos | Cosmetic | No |
| NIGHTOWL | Wider scoring windows | Assist | Yes |

---

# 3. Starting Menu Flow

Recommended starting menu:

```text
START SHIFT
CHEATS
CREDITS
```

Selecting `CHEATS` opens a simple text-entry screen.

Example:

```text
ENTER CODE:
_

[Cross] Confirm
[Circle] Back
```

If the code is valid and currently disabled:

```text
BIGHEAD ENABLED
```

If the code is valid and already enabled:

```text
BIGHEAD DISABLED
```

If the code is invalid:

```text
UNKNOWN CODE
```

---

# 4. Text Entry Rules

Cheat text entry rules:

```text
case-insensitive
spaces ignored
maximum length capped
starting menu only
session/run scoped by default
no checks during the shift
```

Recommended normalization:

```text
"bighead"   -> "BIGHEAD"
"big head"  -> "BIGHEAD"
" BigHead " -> "BIGHEAD"
```

---

# 5. Input Method Options

## Option A — On-Screen Character Picker

Best console-authentic option.

```text
A B C D E F G H I J
K L M N O P Q R S T
U V W X Y Z

[DEL] [OK] [BACK]
```

Controls:

```text
D-pad      move cursor
Cross      select character / confirm selected button
Circle     delete or back
Start      confirm, optional
```

Why this is preferred:

```text
works on emulator
works on real hardware
does not require keyboard support
fits old-console cheat-code vibes
safe for jam builds
```

---

## Option B — Keyboard Text Input

Fastest for development.

Use only if the runtime/editor path already supports keyboard text input.

Pros:

```text
quick to implement in dev
easy testing
```

Cons:

```text
less console-authentic
may not work on real hardware
may not match PS1 input assumptions
```

---

## Option C — Cycle Letters

Simple fallback if UI grid is too much.

```text
D-pad Up/Down: change current letter
D-pad Left/Right: move cursor
Cross: confirm
Circle: delete/back
```

This is slower to use but very easy to implement.

---

# 6. Recommended Jam Implementation

For the jam, use:

```text
Option A — On-screen A-Z character picker
```

If that is too risky, use:

```text
Option C — Cycle letters
```

Do not implement in-shift button sequences.

---

# 7. Cheat State Model

Keep state tiny.

```lua
CHEAT_DEFS = {
    BIGHEAD  = { assisted = false, label = "Big Head" },
    HEREWEGO = { assisted = true,  label = "Here We Go" },
    ALLSEE   = { assisted = true,  label = "All See" },
    STATIC   = { assisted = false, label = "Static" },
    NIGHTOWL = { assisted = true,  label = "Night Owl" },
}

Cheats = Cheats or {
    enabled = {},
    assisted = false
}
```

Core functions:

```lua
function Cheats.IsEnabled(code)
    return Cheats.enabled[code] == true
end

function Cheats.Set(code, value)
    local def = CHEAT_DEFS[code]
    if def == nil then
        return false
    end

    Cheats.enabled[code] = value == true

    if value == true and def.assisted == true then
        Cheats.assisted = true
    end

    return true
end

function Cheats.Toggle(code)
    return Cheats.Set(code, not Cheats.IsEnabled(code))
end

function Cheats.IsAssisted()
    return Cheats.assisted == true
end
```

---

# 8. Code Normalization

Pseudo-code:

```lua
function Cheats.Normalize(code)
    -- Implementation depends on available Lua string helpers.
    -- Goal:
    -- 1. uppercase
    -- 2. remove spaces
    -- 3. trim leading/trailing whitespace

    code = string.upper(code)
    code = string.gsub(code, "%s+", "")
    return code
end
```

If the runtime Lua subset lacks some string helpers, keep entry grid uppercase-only and prevent spaces from being entered.

Then normalization can be minimal:

```lua
function Cheats.Normalize(code)
    return code
end
```

---

# 9. Enter Code Function

```lua
function Cheats.EnterCode(rawCode)
    local code = Cheats.Normalize(rawCode)

    if CHEAT_DEFS[code] == nil then
        UIMessage.Show("UNKNOWN CODE")
        return false
    end

    Cheats.Toggle(code)

    if Cheats.IsEnabled(code) then
        UIMessage.Show(code .. " ENABLED")
    else
        UIMessage.Show(code .. " DISABLED")
    end

    return true
end
```

If `UIMessage.Show` does not exist, route to the project’s existing UI text element or debug/status line.

---

# 10. Assisted Run Behavior

These cheats mark the run as assisted:

```text
HEREWEGO
ALLSEE
NIGHTOWL
```

These cheats are cosmetic and may still allow best-score persistence:

```text
BIGHEAD
STATIC
```

When assisted cheats are active, recommended score behavior:

```text
do not write best_score
or write score with assisted label only
```

Recommended pre-shift warning:

```text
ASSISTED SHIFT — BEST SCORE WILL NOT BE SAVED
```

Score screen note:

```text
CHEATS USED
```

or:

```text
ASSISTED SHIFT
```

---

# 11. Active Cheat Display

On the starting menu or before starting the shift, show active cheats.

Example:

```text
ACTIVE CHEATS:
BIGHEAD
STATIC
```

If none are active:

```text
ACTIVE CHEATS:
NONE
```

If assisted cheats are active:

```text
ASSISTED SHIFT
BEST SCORE DISABLED
```

---

# 12. Start Shift Rule

When `START SHIFT` is selected:

```lua
if Cheats.IsAssisted() then
    -- show assisted warning or mark score screen
end

-- If using scene loading:
Scene.Load("monitor_shift")
```

If The Monitor stays in one scene and changes UI/game state instead:

```lua
GameState.mode = "shift"
EventManager.ResetShift()
FeedManager.Reset()
Score.Reset()
```

Use whichever pattern matches the current project.

---

# 13. Integration Points

## Starting menu script

Responsibilities:

```text
show menu
open cheat entry screen
show active cheat list
start shift
```

## Cheat entry UI

Responsibilities:

```text
hold text buffer
render current buffer
handle character picker/cycle input
call Cheats.EnterCode(buffer)
show result message
```

## Event manager

Uses:

```text
HEREWEGO
ALLSEE
BIGHEAD
```

Examples:

```lua
-- HEREWEGO
local step = 1
if Cheats.IsEnabled("HEREWEGO") then
    step = 2
end
shiftFrame = shiftFrame + step
```

```lua
-- ALLSEE
if Cheats.IsEnabled("ALLSEE") then
    FeedManager.MarkFeedActivity(event.feed, 90)
end
```

```lua
-- BIGHEAD
if Cheats.IsEnabled("BIGHEAD") then
    EventSubjects.ApplyBigHead(event.subject)
end
```

## Feed manager

Uses:

```text
STATIC
ALLSEE
```

Examples:

```text
STATIC:
  longer feed switch static
  small random static flickers

ALLSEE:
  pulse feed label when event fires elsewhere
```

## Score screen

Uses:

```text
NIGHTOWL
Cheats.IsAssisted()
```

Example:

```lua
local window = event.window
if Cheats.IsEnabled("NIGHTOWL") then
    window = window + window / 2
end
```

Best score:

```lua
if not Cheats.IsAssisted() then
    Persist.Set("best_score", caught)
end
```

---

# 14. Do Not Do These

Do not add:

```text
in-shift button-sequence cheat detection
movement cheats
jump cheats
gravity cheats
noclip
free camera cheat
third-person camera cheat
player scale cheat
collision-changing cheats
```

Those belong to future projects, not The Monitor.

---

# 15. Validation Rules

PS1 Doctor or project validation can eventually warn on:

```text
cheat has no handler
cheat has no display label
cheat text entry screen missing
assisted cheat does not mark assisted flag
cheat code exceeds max text-entry length
duplicate cheat code
invalid characters in cheat code
cheat entry enabled during shift
best_score saved during assisted run
```

Example warning:

```text
Cheat "ALLSEE" is marked assisted, but score_screen.lua does not check Cheats.IsAssisted().
Best score may be saved incorrectly.
```

---

# 16. Jam-Safe Implementation Order

## Step 1 — Add cheat definitions

```lua
CHEAT_DEFS = {
    BIGHEAD = { assisted = false },
    HEREWEGO = { assisted = true },
    ALLSEE = { assisted = true },
    STATIC = { assisted = false },
    NIGHTOWL = { assisted = true },
}
```

## Step 2 — Add cheat state functions

```lua
Cheats.IsEnabled
Cheats.Set
Cheats.Toggle
Cheats.IsAssisted
Cheats.EnterCode
```

## Step 3 — Add starting menu Cheats option

Open cheat entry screen.

## Step 4 — Add text-entry buffer

Use on-screen A-Z picker or cycle-letter fallback.

## Step 5 — Add feedback messages

```text
CODE ENABLED
CODE DISABLED
UNKNOWN CODE
```

## Step 6 — Wire cosmetic cheats first

```text
STATIC
BIGHEAD if event-subject mesh supports it safely
```

## Step 7 — Wire assist/timing cheats

```text
NIGHTOWL
HEREWEGO
ALLSEE
```

## Step 8 — Score screen handling

Suppress or label best score when assisted.

---

# 17. IDE-Agent Prompt

Use this prompt to implement the corrected cheat-entry system.

```text
You are helping me add cheat-code support to The Monitor, a PSXSplash jam game.

Important:
The Monitor has no player movement. The player is seated at a security station. During the shift, D-pad Left/Right switches CCTV feeds, Cross logs the current feed/time, and Start restarts after the shift.

Cheats are entered only through a text-entry screen on the starting menu.

Do not implement button-sequence cheat entry during gameplay.
Do not check cheat sequences while switching CCTV feeds.
Do not allow cheat activation during the shift.
Do not implement movement, gravity, jump, collision, noclip, player scaling, or third-person camera cheats.

Valid cheat codes:
- BIGHEAD
- HEREWEGO
- ALLSEE
- STATIC
- NIGHTOWL

Cheat meanings:
- BIGHEAD: oversized heads for visible humanoid/block-figure event subjects.
- HEREWEGO: faster shift timeline / replay-speed mode.
- ALLSEE: subtle feed activity hints when an event fires on another feed.
- STATIC: extra CRT/static effects and stronger feed-switch interference.
- NIGHTOWL: wider scoring windows / more forgiving logging.

Implement or scaffold:
1. Starting menu option: CHEATS.
2. Cheat text-entry UI.
3. Code normalization:
   - uppercase
   - ignore spaces if string helpers exist
4. Toggle behavior:
   - entering an enabled cheat disables it
   - entering a disabled cheat enables it
5. Feedback:
   - CODE ENABLED
   - CODE DISABLED
   - UNKNOWN CODE
6. Assisted-run flag:
   - HEREWEGO, ALLSEE, NIGHTOWL mark run assisted
   - assisted runs should suppress or label best_score
7. Active-cheat list on starting menu or before shift start.
8. No cheat entry during the shift.

Implementation preference:
- Use an on-screen A-Z picker with D-pad and Cross.
- If too risky, implement a simpler cycle-letter fallback.
- Do not rely on keyboard input unless it already exists and is clearly dev-only.

Rules:
- Keep it jam-safe.
- Use only shipped Lua APIs.
- Do not invent unshipped APIs.
- Keep cosmetic cheats session-only.
- Keep assisted/timing cheats honest on score screen.
- Do not let STATIC obscure subtle events too heavily.
- Do not let ALLSEE reveal event names or auto-log events.

Final response:
- Summary
- Files changed
- What is implemented
- What is scaffolded only
- How to test
- Risks/TODOs
```

---

# 18. Bottom Line

For The Monitor, the cheat system should be:

```text
starting-menu only
text-entry based
simple toggle behavior
clear feedback
active cheat display
assisted-run score handling
no movement cheats
```

Best first implementation:

```text
CHEATS menu option
A-Z text entry
Cheats.EnterCode(...)
Cheats.IsAssisted()
score-screen best_score suppression
```

This gives the jam game a classic cheat-code feel without interfering with the core CCTV gameplay.
