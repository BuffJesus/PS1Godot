<!-- gen_lua_api_docs:generated -->
# `Entity`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

18 entries, 11 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Entity.FindByScriptIndex(index) -> object or nil` { #entity-findbyscriptindex }

Finds first object with matching Lua script file index

### `Entity.FindByIndex(index) -> object or nil` { #entity-findbyindex }

Gets object by its array index

**Example**

```lua
local victim = Entity.FindByIndex(hit.object)
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 129._

### `Entity.Find(name) -> object or nil` { #entity-find }

Finds first object with matching name (user-friendly)

### `Entity.GetCount() -> number` { #entity-getcount }

Returns total number of game objects

### `Entity.SetActive(object, active)` { #entity-setactive }

Sets object active state (fires onEnable/onDisable)

**Example**

```lua
Entity.SetActive(self, false)
```

_Source: `godot-ps1/demo/scripts/boss_smoke_brain.lua` line 178._

### `Entity.IsActive(object) -> boolean` { #entity-isactive }

True if the object is currently active (visible + ticking).

### `Entity.GetPosition(object) -> {x, y, z}` { #entity-getposition }

World-space position as a Vec3 table. Components are FixedPoint<12>.

**Example**

```lua
-- cleaner but we already have dx/dz handy.
local b = Entity.GetPosition(self)
```

_Source: `godot-ps1/demo/scripts/boss_smoke_brain.lua` line 117._

### `Entity.SetPosition(object, {x, y, z})` { #entity-setposition }

Teleports the object to the given world-space position. Does NOT
run any physics resolve — use Physics.Raycast / OverlapBox first
if you need to avoid clipping into walls.

**Example**

```lua
Entity.SetPosition(self, Vec3.new(
```

_Source: `godot-ps1/demo/scripts/boss_smoke_brain.lua` line 119._

### `Entity.GetRotationY(object) -> number` { #entity-getrotationy }

Yaw rotation in "pi fractions": 1.0 = π radians = 180°. So 0.5 = 90°,
0.25 = 45°. NOT raw radians — matches Entity.SetRotationY's input.

### `Entity.SetRotationY(object, angle) -> nil` { #entity-setrotationy }

Sets yaw rotation in "pi fractions" (1.0 = π, 0.5 = 90°, 0.25 = 45°).
The PS1Godot runtime uses pi-fraction angles everywhere to dodge
floating-point conversion overhead on PSX hardware.

**Example**

```lua
Entity.SetRotationY(self, heading)
```

_Source: `godot-ps1/demo/scripts/boss_smoke_brain.lua` line 52._

### `Entity.ForEach(callback) -> nil` { #entity-foreach }

Calls callback(object, index) for each active game object. Useful
for global iteration like "stop every enemy" or "log every NPC".
Skips inactive objects so pool reserves are invisible to the loop.

### `Entity.GetTag(object) -> number` { #entity-gettag }

Returns the gameplay tag (0 = untagged). Tags group objects by
role for FindByTag / Spawn / FindNearest queries.

**Example**

```lua
if victim ~= nil and Entity.GetTag(victim) == TAG_ENEMY then
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 130._

### `Entity.SetTag(object, tag)` { #entity-settag }

Reassigns the gameplay tag. Pass 0 to clear. Tag 0 is reserved
for "untagged" — Entity.Spawn rejects tag 0 lookups.

**Example**

```lua
-- should return 0 or error; belt-and-braces, just null out.
Entity.SetTag(lockedEnemy, TAG_ENEMY)
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 197._

### `Entity.FindByTag(tag) -> object or nil` { #entity-findbytag }

Returns the first ACTIVE GameObject whose tag matches.

### `Entity.Spawn(tag, {x,y,z} [, rotY]) -> object or nil` { #entity-spawn }

Finds the first INACTIVE GameObject whose tag matches, activates it
(fires onEnable), and writes the new position/rotation. Returns the
object handle, or nil if the pool is exhausted or tag is 0.

rotY uses the "pi fraction" convention shared with Entity.SetRotationY:
1.0 = π radians = 180°. So 0.5 = 90°, 0.25 = 45°. NOT raw radians.

Pool pattern: author places N copies of a template prefab with
StartsInactive=true + matching Tag in the editor; Spawn draws from
that pool. Per-spawn reset logic should live in the template's
onEnable hook (not onCreate, which fires once at scene init).

**Example**

```lua
local bullet = Entity.Spawn(TAG_BULLET, spawnPos)
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 104._

### `Entity.Destroy(object) -> nil` { #entity-destroy }

Deactivates the object (fires onDisable). Lets the pool re-use it on
the next Entity.Spawn with the same tag.

**Example**

```lua
Entity.Destroy(victim)
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 132._

### `Entity.FindNearest({x,y,z}, tag) -> object or nil` { #entity-findnearest }

Linear scan of active GameObjects with matching tag, returns the
closest. For lock-on, "closest enemy" AI queries, etc.

**Example**

```lua
local boss = Entity.FindNearest(p, TAG_BOSS)
```

_Source: `godot-ps1/demo/scripts/boss_smoke_player.lua` line 53._

### `Entity.SetRotationY(self, heading)` { #entity-setrotationy }

makes the entity face the target.

**Example**

```lua
Entity.SetRotationY(self, heading)
```

_Source: `godot-ps1/demo/scripts/boss_smoke_brain.lua` line 52._
