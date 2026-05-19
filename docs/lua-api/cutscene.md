<!-- gen_lua_api_docs:generated -->
# `Cutscene`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

2 entries.

## Methods

### `Cutscene.Stop()` { #cutscene-stop }

Immediately ends the active cutscene. The onComplete callback is
NOT fired (those only run on natural finish).

### `Cutscene.IsPlaying() -> boolean` { #cutscene-isplaying }

True while a cutscene is running. Use to gate input
(`if not Cutscene.IsPlaying() then ... end`) so the player can't
act mid-scene.
