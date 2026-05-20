<!-- gen_lua_api_docs:generated -->
# `Physics`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

3 entries, 3 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Physics.Raycast({x,y,z}, {x,y,z}, maxDist) -> nil on miss, or { object = <goIndex>, distance = <t>, point = {x,y,z} }` { #physics-raycast }

Tests against all Solid colliders (NOT world geometry triangles). Pass
a roughly-unit direction so `distance` is in world units. Safe to call
a few times per frame; linear scan over up to 64 colliders.

**Example**

```lua
local hit = Physics.Raycast(origin, dirV, BULLET_RAYCAST_DIST)
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 127._

### `Physics.OverlapBox({x,y,z}, {x,y,z} [, tag]) -> array of object handles` { #physics-overlapbox }

AABB-vs-AABB overlap query against active Solid colliders. Optional
tag filter (0/nil = no filter). Used for melee swings, area damage,
pickup proximity. Result table is empty if no hits. Hard-capped at 16
results to bound the Lua table allocation on PSX RAM.

**Example**

```lua
local hits = Physics.OverlapBox(minV, maxV, TAG_ENEMY)
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 170._

### `Physics.OverlapBoxDetailed({x,y,z}, {x,y,z}) -> array of { object, multiplier }` { #physics-overlapboxdetailed }

AABB query against per-entity PS1HurtBox AABBs (v34+). Returns
one hit per entity — when multiple hurtboxes on the same entity
overlap, the HIGHEST multiplier wins (best-hit-wins for
authoring "head + body" zones with descending crits). Each hit
is a table { object = <handle>, multiplier = <percent> } where
multiplier is 100 for default, 200 for 2× crit, etc. Capped at
16 results.

**Example**

```lua
local hits = Physics.OverlapBoxDetailed(minV, maxV)
```

_Source: `godot-ps1/demo/scripts/boss_smoke_brain.lua` line 78._
