<!-- gen_lua_api_docs:generated -->
# `Vec3`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

12 entries, 2 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Vec3.new(x, y, z) -> {x, y, z}` { #vec3-new }

Construct a Vec3 table. Most APIs that take positions / directions
accept any {x,y,z} table; this is just the canonical builder.

**Example**

```lua
-- MUZZLE_AHEAD steps past the 3rd-person rig to the player's front.
local spawnPos = Vec3.new(
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 99._

### `Vec3.add(a, b) -> {x, y, z}` { #vec3-add }

Component-wise sum. Use to translate a position by an offset.

### `Vec3.sub(a, b) -> {x, y, z}` { #vec3-sub }

Component-wise difference (a - b). Use to compute "from b toward a"
as a direction; pair with Vec3.normalize for a unit vector.

### `Vec3.mul(v, scalar) -> {x, y, z}` { #vec3-mul }

Scale each component by `scalar`. Use to extend a direction vector
by a distance, or to invert (multiply by -1).

### `Vec3.dot(a, b) -> number` { #vec3-dot }

Scalar dot product. Returns positive when vectors point the same
way, zero when perpendicular, negative when opposite. Combine with
Vec3.normalize for "is this enemy in front of me" checks.

### `Vec3.cross(a, b) -> {x, y, z}` { #vec3-cross }

Vector cross product, returning a vector perpendicular to both
inputs. Right-hand rule (Y-up). Useful for "build a side vector
from forward + up" or surface-normal calculations.

### `Vec3.length(v) -> number` { #vec3-length }

Euclidean length. Slower than lengthSq because of the sqrt; prefer
lengthSq when you only need to compare distances.

### `Vec3.lengthSq(v) -> number` { #vec3-lengthsq }

Squared length — faster than length() because it skips the sqrt.
Use this for distance comparisons (squared compares are stable
since sqrt is monotonic on non-negative values).

### `Vec3.normalize(v) -> {x, y, z}` { #vec3-normalize }

Returns the unit-length vector pointing in the same direction as
`v`. Returns the zero vector when input length is ~0.

**Example**

```lua
return Vec3.normalize(Vec3.new(fx, 0, fz))
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 78._

### `Vec3.distance(a, b) -> number` { #vec3-distance }

Euclidean distance between two points. Slower than distanceSq.

### `Vec3.distanceSq(a, b) -> number` { #vec3-distancesq }

Squared distance between two points. Faster than distance(); use
for "is enemy within range" checks where you can square the range
once instead of square-rooting every frame.

### `Vec3.lerp(a, b, t) -> {x, y, z}` { #vec3-lerp }

Linear interpolation: returns a when t=0, b when t=1, blend in
between. Useful for smooth camera follow, animation tween,
anything that needs to move from one position to another over
time without writing the math each call.
