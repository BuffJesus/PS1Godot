<!-- gen_lua_api_docs:generated -->
# `Controls`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

4 entries, 1 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Controls.StartIFrames(target, frames) -> nil` { #controls-startiframes }

Set the target's invulnerability window to `frames`. Counts
down at 60 Hz inside the SceneManager game tick. While > 0,
Stats.DealDamage skips this entity. Calling again before the
window expires OVERWRITES (not adds to) the remaining frames.

### `Controls.IsInvulnerable(target) -> boolean` { #controls-isinvulnerable }

True if the target has any i-frames remaining. Cheap lookup;
safe to call per frame.

### `Controls.SetEnabled(bool)` { #controls-setenabled }

Master switch for player input. When false, the player pawn
ignores stick + button events but the camera, cutscenes, music,
and Lua onUpdate keep running. Use during dialogue, menus, or
hit-stun. Pair with Controls.IsEnabled to gate UI actions.

**Example**

```lua
-- with the SetEnabled(false) at the bottom of onCreate.
Controls.SetEnabled(true)
```

_Source: `godot-ps1/demo/scripts/test_logger.lua` line 273._

### `Controls.IsEnabled() -> boolean` { #controls-isenabled }

True if the player input pipeline is currently active.
