<!-- gen_lua_api_docs:generated -->
# `Player`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

3 entries, 1 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Player.GetPosition() -> Vec3` { #player-getposition }

Returns the player's current world position as a Vec3 in PSX
coords. Use for distance checks, "is the player in this room"
tests, save-state capture. Output is a fresh Vec3 table —
mutating it doesn't move the player.

**Example**

```lua
local p = Player.GetPosition()
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 204._

### `Player.SetRotation({x, y, z})` { #player-setrotation }

Sets the player's facing as a Euler triplet in pi-fractions
(1.0 = π, same convention as Entity.SetRotation*). Affects
movement direction (forward becomes +Z in local space). Use for
teleporting "facing the new room" so the camera doesn't snap.

### `Player.GetRotation() -> Vec3` { #player-getrotation }

Returns the player's Euler rotation as a Vec3 (x=pitch, y=yaw,
z=roll) in pi-fractions. Use to orient the camera off-rig or
record facing for save-state.
