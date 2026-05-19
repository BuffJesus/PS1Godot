<!-- gen_lua_api_docs:generated -->
# `Debug`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

3 entries, 1 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Debug.Log(message)` { #debug-log }

Writes `message` to the PSX debug console (visible in PCSX-Redux's
log pane via the printf hook). Free-form string; numbers are
auto-stringified. Strip Debug.Log calls before shipping — they
cost cycles on real hardware.

**Example**

```lua
Debug.Log("combat: L2 shot fired")
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 113._

### `Debug.DrawLine(start, end, color)` { #debug-drawline }

Queues a 1-frame debug line from `start` to `end` (Vec3 tables) in
the given color. Drawn next render pass; vanishes after one frame.
Use for AI raycasts, navigation graphs, hit-test visualisation.

### `Debug.DrawBox(center, size, color)` { #debug-drawbox }

Queues a 1-frame debug box at `center` (Vec3) with `size` (Vec3,
full extents) in `color`. Drawn next render pass. Use for AABB
queries, trigger volume preview, level-design checks.
