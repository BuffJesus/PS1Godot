<!-- gen_lua_api_docs:generated -->
# `Controls`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

2 entries.

## Methods

### `Controls.SetEnabled(bool)` { #controls-setenabled }

Master switch for player input. When false, the player pawn
ignores stick + button events but the camera, cutscenes, music,
and Lua onUpdate keep running. Use during dialogue, menus, or
hit-stun. Pair with Controls.IsEnabled to gate UI actions.

### `Controls.IsEnabled() -> boolean` { #controls-isenabled }

True if the player input pipeline is currently active.
