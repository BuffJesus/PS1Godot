# Debugging environment — design + patch

When something breaks on PSX today, the author sees: a black
screen, a crashed emulator, or worse — silent misbehavior with
no signal. The runtime's error path is "assert and freeze" which
isn't actionable from Godot.

This doc designs the debugging story: Lua tracebacks surface
back to the editor with clickable source jumps, C++ asserts get
a structured error overlay, and the dock shows a rolling log
that authors actually use.

Drop this file at `docs/debugging.md`.

## Goal

When a Lua script throws, the author sees in the Godot dock:

```
✗ test_cube.lua:42 — attempt to index nil value (local 'enemy')
   in onUpdate, called from per-object dispatcher
   [Jump to source]  [Copy traceback]  [Continue?]
```

Click "Jump to source," the editor opens `test_cube.lua` at line
42. Click "Continue," the runtime resumes (with the broken
object suspended). Total time from "something broke" to "I'm
fixing it": ~3 seconds.

Same for C++ asserts: `psyqo::Kernel::assert` failures push a
structured error to the dock instead of freezing in BIOS.

Non-goal: full interactive debugger with breakpoints and step.
PCSX-Redux already has a GDB stub for C++ debugging; integrating
its UI with Godot's is a substantial project that 95% of
authors don't need. The integration story for "I need real GDB"
is documented but not automated; the daily-driver story is the
error-overlay flow above.

## What's in place

- **`psyqo::Kernel::assert(cond, msg)`** halts the runtime with
  a string on failure. The PS1 BIOS exception handler renders
  the message and freezes. Useful but: the author has to look
  at the emulator window to see the message, the line number
  isn't surfaced, and the only recovery is full restart.
- **`Debug.Log(...)` Lua API** writes printf-style messages.
  Routes through `ramsyscall_printf` to PCSX-Redux's stdout,
  which the launcher script captures to `build/pcsx.log`.
  Functional but raw — no severity levels, no filtering, no
  in-editor display.
- **`Debug.Profile("label", fn)`** is on the roadmap (Phase
  2.5 `Debug.Profile` bullet); not yet shipped.
- **PCSX-Redux GDB stub** exists in the emulator and can be
  attached to from VS Code or Rider with `mipsel-none-elf-gdb`.
  Documented in pcsx-redux's repo. Independent of PS1Godot —
  authors who need it use it directly.
- **`scripts/launch-emulator.cmd`** captures stdout to
  `build/pcsx.log`. The dock could tail this file, but doesn't
  today.

The piece that ties it all together is missing: a structured
error pipeline that surfaces what happened, where, and what to
do about it.

## Design

Four layers: Lua-level error capture, C++ assert handling,
PCdrv-based reporting, and editor display.

### Layer 1 — Lua error capture

Lua's protected call (`pcall`) wraps a function and returns
an error object instead of crashing on throw. The runtime's
per-object dispatcher already calls Lua callbacks; today it
calls them unprotected.

Change the dispatcher to wrap each call:

```cpp
// In psxsplash::Lua::InvokeCallback(obj, name):
bool ok = m_lua.protectedCall(/* ... existing args ... */);
if (!ok) {
    const char* err = m_lua.getError();  // top-of-stack string
    // Format: "<file>:<line>: <message>\nstack traceback:\n..."
    ErrorPipeline::ReportLuaError(obj, name, err);
    
    // Mark the object as "errored" so its callbacks stop firing.
    // Author can re-enable via dock or by editing the source
    // (hot-reload re-enables automatically).
    obj->setErrored(true);
    
    // Don't propagate. Other objects keep running.
}
```

`ErrorPipeline::ReportLuaError` writes a structured record to a
PCdrv error file (see Layer 3) and adds a one-line entry to the
in-runtime error ring (a circular buffer of last N=16 errors,
for the on-screen overlay).

The errored object's `onUpdate` callback no longer fires —
prevents 60-times-per-second error spam from one broken script.
`onTierChanged`, `onEnable`, `onCollision` still fire (these
are one-shot signals; if one of them also errors, the object
stays errored). The dock surfaces a "3 objects errored"
indicator with a list of names.

**Stack traceback.** Lua's `debug.traceback` is the standard way.
Bundle it with the error message:

```lua
-- Set this once at VM init:
local handler = function(err)
    return debug.traceback(err, 2)
end
-- Then every protected call uses xpcall:
local ok, err = xpcall(callback, handler, self, dt)
```

`err` becomes `"<file>:<line>: <msg>\nstack traceback:\n\t..."`.
The dock parses out the file/line for the jump button; the
rest displays as-is.

**Source path resolution.** Compiled `.luac` carries the
original source path as a debug field (Lua's `lua_Debug.source`).
The runtime extracts and reports it; the editor maps it to a
`res://` path via the path-to-luaindex mapping the splashpack
already carries.

### Layer 2 — C++ assert handling

`psyqo::Kernel::assert` is the runtime's fail-fast primitive.
Replace its default handler with one that routes through the
error pipeline before freezing:

```cpp
// In main.cpp or a new debughandler.cpp:
psyqo::Kernel::registerExceptionHandler([](const char* msg) {
    // Capture the current call site if possible via $ra register.
    uint32_t ra;
    asm volatile("move %0, $ra" : "=r"(ra));
    
    ErrorPipeline::ReportCppAssert(msg, ra);
    
    // Flush the error file before halting so the editor catches it.
    PCDRV::Flush();
    
    // Now do the actual halt. Override the default freeze with an
    // on-screen error overlay if rendering is still alive.
    OnScreenErrorOverlay::Show(msg);
    while (true) {}  // halt
});
```

`OnScreenErrorOverlay` is a tiny new render pass that ignores
the normal scene rendering and just draws the error string on a
solid color background. Authors looking at the emulator window
see what happened immediately, not after they alt-tab to Godot.

The `$ra` (return address) capture gives the call site address.
For ELF-built binaries (`build-psxsplash.cmd` produces both ELF
and ps-exe), the address resolves to a file+line via
`addr2line`. The dock runs this resolution lazily when the
author clicks "Jump to source" on a C++ assert error.

### Layer 3 — PCdrv error pipeline

Both layers feed into one file: `build/.debug/errors.jsonl`
(JSON-lines format, append-only):

```jsonl
{"ts":12345,"sev":"lua","file":"test_cube.lua","line":42,"msg":"attempt to index nil","traceback":"..."}
{"ts":12389,"sev":"cpp","addr":"0x80012abc","msg":"assertion failed: visibleCount < MAX"}
{"ts":12401,"sev":"log","msg":"player health = 75","src":"player.lua:18"}
```

`PCDRV::OpenFile("/build/.debug/errors.jsonl", APPEND)` returns
a handle the runtime caches across the session. Writes are
append-only and PCdrv-buffered. The editor tails the file.

Lua `Debug.Log` also writes to this file with `sev = "log"`, so
the editor's log view shows logs and errors in one timeline.
Existing stdout routing stays as a fallback (real-hardware
users without PCdrv).

**Severity levels:**

- `lua` — Lua error with traceback.
- `cpp` — C++ assertion failure.
- `log` — `Debug.Log` from script.
- `warn` — `Debug.Warn` (new — author-marked non-fatal issue).
- `perf` — performance probe output (covered in
  `profiling.md`).

The dock filters by severity. Authors who want a clean log can
hide `log` entries and see only errors+warnings.

### Layer 4 — Editor display

A new tab in the dock: **Errors**. Scrollable list of recent
errors (newest at top), with per-entry expand for full
traceback.

```
✗ 12:34:56  Lua: test_cube.lua:42 — attempt to index nil
   ▼ Stack traceback
     in onUpdate (test_cube.lua:42)
     called from per-object dispatcher
   [Jump to source]   [Copy traceback]   [Dismiss]
   
⚠ 12:34:58  Warn: combat.lua:78 — bullet pool exhausted (size=4)
   [Jump to source]   [Dismiss]
   
ℹ 12:35:01  Log: player.lua:18 — player health = 75
```

Color: red for Lua/Cpp errors, amber for warnings, neutral for
logs. The dock's main header gets a small error indicator
("3 errors" in red) so authors notice without opening the tab.

**Click-to-jump.** "Jump to source" resolves the file path,
opens it in Godot's script editor, scrolls to the line. Uses
Godot's `ScriptEditor::edit(script, line)` API.

**Tailing.** The dock polls the errors file once per second via
PCdrv's directory-listing API (or via plain `File.read_at`
since the file lives on the host filesystem). Updates the list
without polling overhead — only new lines parse.

**Persistence.** The errors file accumulates across sessions.
A "Clear errors" button truncates it. Authors get history of
recent debugging sessions for free; the dock shows the last
50 entries by default but can scroll back further.

### Layer 5 — Interactive recovery

The "Continue?" button on Lua errors: re-enables the errored
object. Next frame, its `onUpdate` runs again. If the bug is
fixed (via hot-reload from `iteration-loop.md`), it works. If
not, it errors again — same dialog appears with the new
traceback.

For C++ asserts: no "Continue" — the runtime is in an unknown
state. Only path is full restart. The dock surfaces a
"Restart emulator" button alongside the error.

### Layer 6 — Watch variables

Author tags interesting values from Lua:

```lua
-- In onUpdate:
Debug.Watch("playerHealth", self.health)
Debug.Watch("enemyCount", #Entity.FindAllByTag(TAG_ENEMY))
```

Values get sent via the same PCdrv error pipeline as `sev =
"watch"` records. The dock has a "Watch" tab showing latest
value per tag, updating in real-time. Cheap (one PCdrv write
per Debug.Watch per frame; only watched values cost anything).

A common pattern: temporarily inject `Debug.Watch` calls
during debugging, remove them when fixed. Same idiom as `print`
debugging but with the dock view.

## Implementation stages

Six stages. Stage 1 ships the foundation; later stages add
sophistication.

### Stage 1 — Lua error capture

The most-felt improvement. Almost everything else builds on it.

- Wrap callback invocation in `xpcall` with traceback handler.
- Errored-object flag in GameObject (one new bit).
- Error ring buffer in runtime (16 entries).
- PCdrv error file open + write.

Editor side: a minimal "Errors" tab in the dock that just
tails the file and displays raw entries. No formatting, no
jump button yet. Authors see "something broke" with line
numbers.

### Stage 2 — Editor display polish

- Parse JSON-lines into structured entries.
- Color coding by severity.
- "Jump to source" button using Godot's script editor API.
- Expand/collapse traceback per entry.
- Error count indicator in dock header.

The "useful debugging UI" stage.

### Stage 3 — C++ assert handling

- Custom assert handler that routes through PCdrv.
- `$ra` capture for call-site addresses.
- `addr2line` lookup in the editor (lazy on click).
- On-screen error overlay so authors looking at PSX see it too.

After this stage, no class of error is silent. Lua throws,
C++ asserts, log messages — all surfaced in the dock.

### Stage 4 — `Debug.Watch` + log levels

- `Debug.Watch(tag, value)` Lua API.
- `Debug.Warn(msg)` for author-marked non-fatal issues.
- "Watch" tab in dock with live updating values.
- Severity filter in error list.

The "I'm in the middle of debugging" workflow stage.

### Stage 5 — Interactive recovery

- "Continue?" button on Lua errors → re-enable errored object.
- "Dismiss" hides the entry without re-enabling.
- "Restart emulator" button shortcut for C++ asserts.
- Auto-dismiss errors that get fixed via hot-reload.

### Stage 6 — GDB integration documentation

Not new code — just a docs page that explains:

- How to attach `mipsel-none-elf-gdb` to PCSX-Redux's stub.
- Recommended launch.json / Rider runConfig for breakpoint
  debugging.
- What's debuggable (most of the runtime; not GTE/SPU/GPU
  register state).
- Use-cases where GDB beats the dock-based flow (memory
  corruption, hardware-specific bugs, deep dives).

The 95% case is the dock flow. GDB is the escape hatch.

## Open questions / tradeoffs

**Performance of xpcall.** Wrapping every callback in xpcall
adds overhead — one extra Lua C-API call per invocation. On
PS1, this is non-trivial. Mitigation: only enable error
trapping in dev builds (PCdrv-enabled runtime). Release builds
(CD-ROM target) revert to unprotected calls and rely on
"author has tested everything" before shipping. Per-callback
opt-out via a flag if release builds want per-script trapping.

**Errored objects accumulate.** A flaky script errors on every
update; the errored flag prevents repeated errors, but if 20
objects error during one session, the dock fills up. UI: a
"3 errored objects" header with a click-to-list and
click-to-recover-all button. Auto-recovery on hot-reload of
the affected script.

**Where does `Debug.Log` go now?** Today it goes to stdout
(emulator console). The PCdrv pipeline replaces this — but
the stdout path stays as fallback for non-PCdrv runs (real
hardware, headless tests). Authors see logs in the dock when
iterating, in stdout when running on hardware.

**Log volume.** A loop that calls `Debug.Log` every frame
floods the file. Rate-limiting: log calls at sev=log get
rate-limited per-source-line (max 10/second per source
location). Errors and warnings aren't rate-limited. Authors
who genuinely want a per-frame log set `Debug.SetVerbose(true)`
explicitly.

**File size.** The errors file accumulates across sessions.
Mitigation: rotate at 1 MB — `errors.jsonl` → `errors.jsonl.1`,
fresh file starts. Dock reads from current + recent rotated.

**Real-hardware debugging.** PCdrv doesn't exist on real
hardware. Errors there print to the on-screen overlay only;
no rolling log, no editor connection. Document this in the
"hardware testing" section of SETUP.md. The intended flow:
debug in emulator, ship to hardware as a last-mile check.

**Source jump for compiled bytecode.** Lua's `lua_Debug.source`
carries the original source file path even after compilation.
But: the path is the build-time path, not necessarily the
res:// path Godot knows. Mitigation: the editor maintains a
map of "compiled-source-path → res:// path" built during
export. Stale after project rename; refresh on every export.

**Multi-threaded interactions.** PS1 is single-threaded — no
concurrency hazards in the error pipeline itself. The PCdrv
file writes are synchronous from the runtime's perspective.

**Stack overflow.** Lua stack overflow throws a recoverable
error → goes through xpcall → reported normally. C++ stack
overflow corrupts memory and may not produce a clean trace.
Mitigation: psyqo offers a stack-canary check that runs
periodically; enable in dev builds.

**Production builds.** Some games will want to ship release
builds with no debugging. Flag: `PSXSPLASH_DEBUG_PIPELINE=0`
in the Makefile disables the entire pipeline at compile time.
Saves ~5 KB of runtime, removes overhead. Default-on for
PS1Godot dev; default-off for ship builds.

## Suggested entries

### For `docs/psxsplash-improvements.md`

> ### N+M. Structured error pipeline (Lua + C++)
>
> **Problem.** Errors today are silent: Lua scripts that throw
> get caught somewhere (or aren't), `psyqo::Kernel::assert`
> freezes the runtime with the message in the emulator window,
> `Debug.Log` writes to stdout that authors only see when
> reading log files manually. None of this surfaces in the
> editor where authors live.
>
> **Why we care.** Iteration speed (from
> `docs/iteration-loop.md`) bottlenecks on "what went wrong."
> Without structured error reporting, authors spend their
> time diagnosing instead of fixing.
>
> **Proposed direction.** Append-only JSON-lines error file on
> PCdrv (`build/.debug/errors.jsonl`). All paths feed in: Lua
> errors via `xpcall` wrappers around callbacks; C++ asserts
> via a custom handler that captures `$ra` for call sites;
> `Debug.Log` and new `Debug.Warn` / `Debug.Watch` API.
> Editor tails the file and renders structured entries with
> source-jump buttons. Full design: `docs/debugging.md`.
>
> **Status.** Filed.

### For `ROADMAP.md`

> - [ ] **Debugging environment — structured error pipeline.**
>       Lua `xpcall` traceback capture, custom C++ assert
>       handler, PCdrv error log, editor dock display with
>       source-jump buttons. `Debug.Watch(tag, value)` for live
>       inspection. Full design: `docs/debugging.md`.

## Changelog

- `2026-05-11` — Document created. Tenth patch doc in the
  series. Pairs with `iteration-loop.md` (errors are most
  felt when iterating fast) and `profiling.md` (shared PCdrv
  reporting pipeline).
