<!-- gen_lua_api_docs:generated -->
# `Dialog`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

3 entries, 0 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Dialog.RunGraph(table)` { #dialog-rungraph }

start interpreting a compiled PS1Graph
dialogue table. Table shape is { entry = "n0", nodes = { ... } }
as emitted by PS1GraphCompiler.CompileDialogue.

### `Dialog.Stop()` { #dialog-stop }

abort any in-progress dialogue. Cheap no-op when
nothing is running.

### `Dialog.IsActive() -> boolean` { #dialog-isactive }

*No description.*
