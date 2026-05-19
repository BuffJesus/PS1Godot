<!-- gen_lua_api_docs:generated -->
# `Persist`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

2 entries, 0 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Persist.Get(key) -> number or nil` { #persist-get }

Reads a numeric value previously stored with Persist.Set.
Returns nil if the key was never set. Persistent storage is
RAM-only (cleared on power-cycle) — survives Scene.Load but not
a console reset. 16 slots total, 32-char key max.

### `Persist.Set(key, value)` { #persist-set }

Stores a number under `key` so it survives Scene.Load. Use for
run-state that crosses scene boundaries (player HP, score,
cutscene-flags, inventory counts). Silently no-ops when the
16-slot table is full. Long-term saves need a real save system.
