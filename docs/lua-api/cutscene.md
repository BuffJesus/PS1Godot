<!-- gen_lua_api_docs:generated -->
# `Cutscene`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

2 entries, 1 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Cutscene.Stop()` { #cutscene-stop }

Immediately ends the active cutscene. The onComplete callback is
NOT fired (those only run on natural finish).

### `Cutscene.IsPlaying() -> boolean` { #cutscene-isplaying }

True while a cutscene is running. Use to gate input
(`if not Cutscene.IsPlaying() then ... end`) so the player can't
act mid-scene.

**Example**

```lua
-- walk on the first post-cutscene frame).
if not Cutscene.IsPlaying() then
```

_Source: `godot-ps1/demo/scripts/test_logger.lua` line 239._
