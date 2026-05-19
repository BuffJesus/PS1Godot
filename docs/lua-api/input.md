<!-- gen_lua_api_docs:generated -->
# `Input`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

4 entries, 1 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

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

Returns analog stick values (-128 to 127)
