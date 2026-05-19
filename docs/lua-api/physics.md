<!-- gen_lua_api_docs:generated -->
# `Physics`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

1 entries.

## Methods

### `Physics.OverlapBox({x,y,z}, {x,y,z} [, tag]) -> array of object handles` { #physics-overlapbox }

AABB-vs-AABB overlap query against active Solid colliders. Optional
tag filter (0/nil = no filter). Used for melee swings, area damage,
pickup proximity. Result table is empty if no hits. Hard-capped at 16
results to bound the Lua table allocation on PSX RAM.
