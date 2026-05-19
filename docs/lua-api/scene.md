<!-- gen_lua_api_docs:generated -->
# `Scene`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

3 entries, 2 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Scene.Load(sceneIndex)` { #scene-load }

Requests a scene transition to the given index (0-based).
The actual load happens at the end of the current frame.

**Example**

```lua
Scene.Load(1)
```

_Source: `godot-ps1/demo/scripts/intro_splash.lua` line 35._

### `Scene.GetIndex() -> number` { #scene-getindex }

Returns the index of the currently loaded scene.

### `Scene.PauseFor(frames) -> nil` { #scene-pausefor }

Hit-stop / freeze. Holds gameplay tick (animation, cutscene, skin,
collision, Lua onUpdate, controls, player movement) for `frames`
frames while keeping render + camera shake + music alive. Souls /
Hades-style impact crunch. Stacks via max(remaining, requested).

**Example**

```lua
Scene.PauseFor(4)
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 134._
