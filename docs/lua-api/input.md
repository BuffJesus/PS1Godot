<!-- gen_lua_api_docs:generated -->
# `Input`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

4 entries.

## Methods

### `Input.IsPressed(button) -> boolean` { #input-ispressed }

True only on the frame the button was pressed

### `Input.IsReleased(button) -> boolean` { #input-isreleased }

True only on the frame the button was released

### `Input.IsHeld(button) -> boolean` { #input-isheld }

True while the button is held down

### `Input.GetAnalog(stick) -> x, y` { #input-getanalog }

Returns analog stick values (-128 to 127)
