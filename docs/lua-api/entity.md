<!-- gen_lua_api_docs:generated -->
# `Entity`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

17 entries.

## Methods

### `Entity.FindByScriptIndex(index) -> object or nil` { #entity-findbyscriptindex }

Finds first object with matching Lua script file index

### `Entity.FindByIndex(index) -> object or nil` { #entity-findbyindex }

Gets object by its array index

### `Entity.Find(name) -> object or nil` { #entity-find }

Finds first object with matching name (user-friendly)

### `Entity.GetCount() -> number` { #entity-getcount }

Returns total number of game objects

### `Entity.SetActive(object, active)` { #entity-setactive }

Sets object active state (fires onEnable/onDisable)

### `Entity.IsActive(object) -> boolean` { #entity-isactive }

True if the object is currently active (visible + ticking).

### `Entity.GetPosition(object) -> {x, y, z}` { #entity-getposition }

World-space position as a Vec3 table. Components are FixedPoint<12>.

### `Entity.SetPosition(object, {x, y, z})` { #entity-setposition }

Teleports the object to the given world-space position. Does NOT
run any physics resolve — use Physics.Raycast / OverlapBox first
if you need to avoid clipping into walls.

### `Entity.GetRotationY(object) -> number` { #entity-getrotationy }

Yaw rotation in "pi fractions": 1.0 = π radians = 180°. So 0.5 = 90°,
0.25 = 45°. NOT raw radians — matches Entity.SetRotationY's input.

### `Entity.SetRotationY(object, angle) -> nil` { #entity-setrotationy }

Sets yaw rotation in "pi fractions" (1.0 = π, 0.5 = 90°, 0.25 = 45°).
The PS1Godot runtime uses pi-fraction angles everywhere to dodge
floating-point conversion overhead on PSX hardware.

### `Entity.ForEach(callback) -> nil` { #entity-foreach }

Calls callback(object, index) for each active game object. Useful
for global iteration like "stop every enemy" or "log every NPC".
Skips inactive objects so pool reserves are invisible to the loop.

### `Entity.GetTag(object) -> number` { #entity-gettag }

Returns the gameplay tag (0 = untagged). Tags group objects by
role for FindByTag / Spawn / FindNearest queries.

### `Entity.SetTag(object, tag)` { #entity-settag }

Reassigns the gameplay tag. Pass 0 to clear. Tag 0 is reserved
for "untagged" — Entity.Spawn rejects tag 0 lookups.

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

### `Entity.Destroy(object) -> nil` { #entity-destroy }

Deactivates the object (fires onDisable). Lets the pool re-use it on
the next Entity.Spawn with the same tag.

### `Entity.FindNearest({x,y,z}, tag) -> object or nil` { #entity-findnearest }

Linear scan of active GameObjects with matching tag, returns the
closest. For lock-on, "closest enemy" AI queries, etc.
