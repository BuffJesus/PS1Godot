<!-- gen_lua_api_docs:generated -->
# Lua API

PS1Lua exposes a runtime-bound C++ API to game scripts. The
binding surface lives in `psxsplash-main/src/luaapi.hh` and is
consumed three ways:

- **In the Godot editor** — the PS1Lua language extension reads
  the same signatures for autocomplete and hover.
- **In external editors** (Rider, VS Code) — `LuaApiStubGenerator` emits EmmyLua stubs from the same source.
- **On this docs site** — `scripts/py/gen_lua_api_docs.py`
  parses the structured `// Namespace.Method(...)` comments
  and writes one page per namespace.

**24 namespaces, 145 entries** across the surface.

## Namespaces

| Namespace | Entries | Page |
| --- | --- | --- |
| `Animation` | 1 | [`Animation`](animation.md) |
| `Audio` | 15 | [`Audio`](audio.md) |
| `Camera` | 14 | [`Camera`](camera.md) |
| `Controls` | 2 | [`Controls`](controls.md) |
| `Convert` | 2 | [`Convert`](convert.md) |
| `Cutscene` | 2 | [`Cutscene`](cutscene.md) |
| `Debug` | 3 | [`Debug`](debug.md) |
| `Dialog` | 1 | [`Dialog`](dialog.md) |
| `Entity` | 17 | [`Entity`](entity.md) |
| `GameState` | 7 | [`GameState`](game-state.md) |
| `Input` | 4 | [`Input`](input.md) |
| `Interact` | 2 | [`Interact`](interact.md) |
| `Math` | 11 | [`Math`](math.md) |
| `Music` | 7 | [`Music`](music.md) |
| `Persist` | 2 | [`Persist`](persist.md) |
| `Physics` | 1 | [`Physics`](physics.md) |
| `Player` | 3 | [`Player`](player.md) |
| `Random` | 5 | [`Random`](random.md) |
| `Scene` | 3 | [`Scene`](scene.md) |
| `SkinnedAnim` | 5 | [`SkinnedAnim`](skinned-anim.md) |
| `Sound` | 3 | [`Sound`](sound.md) |
| `Timer` | 1 | [`Timer`](timer.md) |
| `UI` | 22 | [`UI`](ui.md) |
| `Vec3` | 12 | [`Vec3`](vec3.md) |

## Calling convention

All entries are static methods on global tables. From a
`PS1LuaScript`-bound script:

```lua
-- Get the camera's current world position
local px, py, pz = Camera.GetPosition()

-- Play a clip; returns the active voice id or nil
local v = Audio.PlaySfx("door_creak")

-- Fixed-point math (PS1 GTE uses Q12.20)
local d = FixedPoint.Mul(velocity, dt)
```

Coroutines, closures, and standard Lua tables work as
normal — the PS1Lua runtime is psyqo-lua atop the PS1's
MIPS CPU. No JIT; expect interpreted-Lua speed budgets.
