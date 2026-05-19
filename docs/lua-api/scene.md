<!-- gen_lua_api_docs:generated -->
# `Scene`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

3 entries.

## Methods

### `Scene.Load(sceneIndex)` { #scene-load }

Requests a scene transition to the given index (0-based).
The actual load happens at the end of the current frame.

### `Scene.GetIndex() -> number` { #scene-getindex }

Returns the index of the currently loaded scene.

### `Scene.PauseFor(frames) -> nil` { #scene-pausefor }

Hit-stop / freeze. Holds gameplay tick (animation, cutscene, skin,
collision, Lua onUpdate, controls, player movement) for `frames`
frames while keeping render + camera shake + music alive. Souls /
Hades-style impact crunch. Stacks via max(remaining, requested).
