<!-- gen_lua_api_docs:generated -->
# `Player`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

4 entries, 2 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Player.SetPosition(pos) -> nil` { #player-setposition }

Teleports the player pawn to PSX-runtime world coords. `pos`
is either a Vec3 table OR three raw fp12 numbers — the runtime
accepts both forms in the same call slot.
NOTE: coords are PSX-frame (post-export Y/Z flip and
/gteScaling), NOT Godot units. Convert from Godot:
x_psx = x_godot/4, y_psx = -y_godot/4, z_psx = -z_godot/4.
Use for spawn-points, scene-load placement, dodge / dash
motion overrides, cutscene blocking, debug warps.

**Example**

```lua
Player.SetPosition(Vec3.new(p.x + stepX, p.y, p.z + stepZ))
```

_Source: `godot-ps1/demo/scripts/boss_smoke_player.lua` line 145._

### `Player.GetPosition() -> Vec3` { #player-getposition }

Returns the player's current world position as a Vec3 in PSX
coords. Use for distance checks, "is the player in this room"
tests, save-state capture. Output is a fresh Vec3 table —
mutating it doesn't move the player.

**Example**

```lua
-- input automatically.
local p = Player.GetPosition()
```

_Source: `godot-ps1/demo/scripts/boss_smoke_fog_gate.lua` line 18._

### `Player.SetRotation({x, y, z})` { #player-setrotation }

Sets the player's facing as a Euler triplet in pi-fractions
(1.0 = π, same convention as Entity.SetRotation*). Affects
movement direction (forward becomes +Z in local space). Use for
teleporting "facing the new room" so the camera doesn't snap.

### `Player.GetRotation() -> Vec3` { #player-getrotation }

Returns the player's Euler rotation as a Vec3 (x=pitch, y=yaw,
z=roll) in pi-fractions. Use to orient the camera off-rig or
record facing for save-state.
