# PS1 Lua REPL

Under **PS1 Tools → Lua REPL**. Fire ad-hoc Lua snippets at the
running PSX runtime without rebuilding or restarting it.

<!-- SCREENSHOT: docks/repl.png — REPL input + scrollback showing roundtrip -->

## How it works

The dock writes your snippet to `<build>/repl.lua` and bumps
`<build>/repl.ver` with a monotonic byte stamp. The psxsplash
runtime polls those files every ~0.5 s — the same cadence as Lua
hot-swap — and `pcall`s any new version in the global Lua
environment. See `psxsplash-main/src/lua.cpp`'s `TryRepl`.

The transport is **PCdrv** — PCSX-Redux mounts the host filesystem
as a fake CD-ROM that the runtime can read. On real-hardware ISO
builds this path is missing; the REPL only works in the
PCdrv-emulator flow.

## What you can do with it

Any code that's legal in a scene's Lua script:

```lua
-- Inspect a global
Debug.Log("active scene index: " .. tostring(Scene.GetActive()))

-- Play an audio clip without authoring a script
Audio.PlaySfx("door_creak")

-- Tweak a value live
Persist.Set("debug_god_mode", true)
```

Closures and upvalues persist across REPL calls — anything you
assign to a global variable is accessible from the next snippet.
Local variables (`local x = 5`) live only for the duration of the
single snippet.

## Scrollback

The dock keeps a local history of the snippets you've sent + a
"→ sent (see PSX console for result)" line for each. Slice 1 is
fire-and-forget — the runtime doesn't yet write a response file
back, so observation happens through PCSX-Redux's stdout console.

Slice 2 plan: a `repl.out` file the runtime writes to, polled by
the dock and rendered as response lines below each prompt.

## When to use it

- **Quick state inspection** — `Debug.Log(Persist.Get("x"))` to
  see a flag value without scripting it into the scene.
- **One-shot triggers** — fire `Scene.Load(2)` to test a teleport
  without walking to the trigger.
- **Live debugging** — set up state, then exercise the gameplay
  code from a known configuration.

Don't use it for anything you want repeatable — the REPL has no
persistence between sessions. Edits that should stick belong in
a `.lua` file referenced by a node.

## Related

- [Lua API reference](../lua-api/index.md) — every callable that
  the REPL accepts is documented there.
- [`reference/lua-cheatsheet.md`](../reference/lua-cheatsheet.md)
  — quick reference for the most-used bindings.
