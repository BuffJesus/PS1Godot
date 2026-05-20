<!-- gen_lua_api_docs:generated -->
# `Input`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

4 entries, 2 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Input.IsPressed(button) -> boolean` { #input-ispressed }

True only on the frame the button was pressed

**Example**

```lua
-- would put this behind an Options menu + persisted preference.
if Input.IsPressed(Input.SELECT) then
```

_Source: `godot-ps1/demo/scripts/test_logger.lua` line 215._

### `Input.IsReleased(button) -> boolean` { #input-isreleased }

True only on the frame the button was released

### `Input.IsHeld(button) -> boolean` { #input-isheld }

True while the button is held down

### `Input.GetAnalog(stick) -> x, y` { #input-getanalog }

Returns analog stick values for the requested stick.
`stick` accepts the named constants `Input.LEFT_STICK` (0,
default) or `Input.RIGHT_STICK` (1). x and y are
FixedPoint<12> in the range [-1.0, 1.0]; multiply by your
sensitivity factor before applying to camera/movement state.
Common pattern (twin-stick camera):
local rx, ry = Input.GetAnalog(Input.RIGHT_STICK)
Camera.SetH(Camera.GetH() + rx / 8)

**Example**

```lua
-- Direction: left stick if held; otherwise backstep from facing.
local lx, ly = Input.GetAnalog(Input.LEFT_STICK)
```

_Source: `godot-ps1/demo/scripts/boss_smoke_player.lua` line 65._
