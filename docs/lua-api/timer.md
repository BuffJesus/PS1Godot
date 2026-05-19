<!-- gen_lua_api_docs:generated -->
# `Timer`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

1 entries, 1 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Timer.GetFrameCount() -> number` { #timer-getframecount }

Returns total frames since scene start

**Example**

```lua
-- before hiding. Otherwise just hide immediately.
local stayed = Timer.GetFrameCount() - enterTick
```

_Source: `godot-ps1/demo/scripts/checkered_ambient.lua` line 29._
