# Modifying the runtime

`psxsplash-main/` is our fork of upstream
[psxsplash](https://github.com/psxsplash/psxsplash) (see
[architecture § divergence](architecture.md#how-much-weve-diverged-from-upstream-psxsplash)
for what changed). This page covers the contracts and conventions
for changes inside that tree.

If you're just authoring a scene, you don't need this page — the
plugin side handles every common workflow.

## What lives in `psxsplash-main/`

```
psxsplash-main/
├── Makefile               GNU make build, PCdrv default,
│                          LOADER=cdrom for ISO builds
├── src/
│   ├── main.cpp           entry point + main loop
│   ├── splashpack.{hh,cpp}    binary loader + format definitions
│   ├── renderer.cpp       GPU primitive submission
│   ├── camera.cpp         player + cutscene camera
│   ├── scenemanager.cpp   multi-scene + portal-culling
│   ├── luaapi.{hh,cpp}    Lua bindings — 24 namespaces
│   ├── lua.cpp            Lua VM wiring + hot-swap + REPL
│   ├── audiomanager.cpp   SPU + CDDA + XA + PS2M
│   ├── controls.cpp       input
│   ├── collision.cpp      collision + BVH
│   ├── navregion.cpp      nav mesh
│   ├── worldcollision.cpp world-vs-player physics
│   ├── animation.cpp      PS1Animation playback
│   ├── cutsceneplayer.cpp PS1Cutscene multi-track playback
│   ├── dialoguerunner.cpp PS1Graph dialogue walker
│   ├── ui.cpp             UI canvas rendering
│   ├── ps2m.cpp           sequenced music
│   ├── fileloader.cpp     PCdrv vs CDROM file IO
│   └── profiler.cpp       in-runtime perf counters
└── third_party/           tracked vendor deps (nugget, etc.)
```

Compiled with MIPS gcc (`mipsel-none-elf-gcc` /
`mipsel-linux-gnu-gcc`) on top of
[psyqo](https://github.com/grumpycoders/pcsx-redux/tree/main/src/mips/psyqo).
See [`contributing/building.md`](building.md#psxsplash-runtime) for
the build command.

## The luaapi.hh contract

The structured signature comments above each `lua_State*` binding
in `luaapi.hh` feed **three** downstream tools. Anything you add
here propagates everywhere — but only if you follow the format.

```cpp
// Audio.PlaySfx(name, volume?, pan?) -> channelId
// Plays SPU-routed clips. Logs a warning if `name` was authored
// as XA/CDDA — call PlayMusic for those instead.
static int Audio_PlaySfx(lua_State* L);
```

Three consumers:

1. **`gen_api_data.py`** (in
   `godot-ps1/addons/ps1godot/scripting/`) — runs on every SCons
   build, emits `ApiData.gen.cpp` for the in-Godot autocomplete +
   hover.
2. **`LuaApiStubGenerator.cs`** (in
   `godot-ps1/addons/ps1godot/tools/`) — emits EmmyLua stubs for
   external editors (Rider, VS Code).
3. **`scripts/py/gen_lua_api_docs.py`** — emits the per-namespace
   pages under `docs/lua-api/`. CI's
   [drift gate](ci.md#docsyml-mkdocs-site) catches a missed regen.

### Required shape

```
// <Namespace>.<MethodName>(<args>) [-> <returntype>]
// <optional description lines, any number, each starting with //>
static int <Namespace>_<MethodName>(lua_State* L);
```

- **Namespace** — leading capital, no underscore. (`Audio`,
  `Camera`, `SkinnedAnim`.)
- **MethodName** — typically PascalCase. The signature regex is
  permissive about case.
- **`(args)`** — required even when empty. `Dialog.Stop()` parses;
  `Dialog.Stop` doesn't.
- **`-> retval`** — optional. Convention: describe the type
  (`channelId` / `boolean` / `nil`) rather than naming it.
- **Description lines** — plain English, no Markdown. Anything
  after the first non-`//` line ends the doc block.

Match the existing patterns above neighbors. The regex lives in
`gen_api_data.py` and is duplicated in `gen_lua_api_docs.py` —
see the
[Lua API generator's review notes](https://github.com/BuffJesus/PS1Godot/blob/main/docs/internal/handoff-2026-05-19-docs-site-completion.md){ target="_blank" }
on the DRY debt.

### After editing luaapi.hh

```bash
# In godot-ps1/addons/ps1godot/scripting/
scons -j8         # ApiData.gen.cpp regenerates as a side effect

# In repo root
python scripts/py/gen_lua_api_docs.py
git add docs/lua-api/
```

Both regens are idempotent. CI's drift gate fails the build if
the committed `docs/lua-api/` doesn't match what the regen
produces from the current `luaapi.hh`.

## Splashpack format bumps

We're at **v32**. The format-version contract:

- The loader hard-asserts `version >= 32`. Older splashpacks won't
  load. We're the sole consumer; no compat layer needed.
- The on-disk header structure must match the in-code struct
  size **byte-for-byte**. `static_assert` in `splashpack.hh`
  enforces the size; if you reshuffle a field, the assert fires
  at compile time.
- **Append-only** discipline. Each bump (v21–v32 so far)
  appended new fields at the end of the header. Two exceptions
  (v30 quaternion skin poses, v31 vertex-pool MeshBlob) did
  mid-section reshuffles for size wins — don't follow those
  unless your bump's savings are comparable.

### Bumping checklist

1. Update `SPLASHPACK_VERSION` in `splashpack.hh`.
2. Add new fields at the **end** of the relevant struct.
3. Update the `static_assert` byte count to match.
4. Update `SplashpackWriter.cs` (in
   `godot-ps1/addons/ps1godot/exporter/`) to emit the new bytes.
5. Add a fixture test on the C# side asserting the writer
   produces the expected byte count.
6. Bump the loader's struct read in `splashpack.cpp` to consume
   the new fields.
7. Test against the demo scene end-to-end (export + boot in
   PCSX-Redux) before committing.

If you forget step 3 the C++ compile fails. If you forget step 4
or 6 the runtime mis-parses subsequent fields and the symptoms
are confusing (visual artifacts, audio routing wrong, etc.).

## The audio routing contract

`PS1AudioClip.Route` resolves at export time. The runtime expects
exactly one route per clip — if a clip claims to be SPU but the
header bytes say XA, the playback path crashes or silently
falls back.

The resolution logic mirrors between two files:

- **Exporter side:** `SceneCollector.ResolveAudioRoute` in
  `godot-ps1/addons/ps1godot/exporter/`.
- **Runtime side:** `audiomanager.cpp` switches on the resolved
  route to pick the SPU / XA / CDDA / PS2M path.

If you change one without the other, the
[Audio Routing dock](../docks/audio-routing.md)'s "resolves to"
column lies. Keep the two in sync.

## When you don't need to touch the runtime

Most plugin work doesn't reach into the runtime:

- **New custom nodes** — add C# files under
  `godot-ps1/addons/ps1godot/nodes/`, wire them into the
  exporter, no runtime change unless they need a new on-disk
  format.
- **New docks** — pure plugin-side; no runtime contract.
- **New Lua API** — only requires a runtime change. Add the
  binding in `luaapi.{hh,cpp}`, regenerate the autocomplete /
  EmmyLua / docs surfaces (one command each).
- **New graph kind** — exporter compiles graphs to Lua tables;
  the runtime walker is generic. You add a graph kind without
  touching the runtime if your new walker logic can be expressed
  in Lua.

The runtime changes when you're adding a primitive the format /
playback model doesn't already support — a new audio routing
backend, a new rendering trick, a new collision shape, etc.

## Patches against upstream

Long-term plan (per CLAUDE.md): extract our edits into
`patches/psxsplash/` and apply at build time so the vendored
source tree can periodically re-sync with upstream. Today the
edits land directly on `psxsplash-main/` and we don't pull from
upstream. Either approach is valid; the directory-direct flow is
simpler while we keep diverging fast.

## Related

- [Architecture § divergence](architecture.md#how-much-weve-diverged-from-upstream-psxsplash)
  — what we've added beyond upstream.
- [Building § psxsplash runtime](building.md#psxsplash-runtime)
  — the build command.
- [CI § docs.yml](ci.md#docsyml-mkdocs-site) — the drift gate
  that catches a missed `gen_lua_api_docs.py` regen.
