<!-- gen_lua_api_docs:generated -->
# `Camera`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

19 entries, 6 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Camera.GetPosition() -> {x, y, z}` { #camera-getposition }

Active camera's world-space position. Coordinates are PSX-runtime
units, not Godot editor units (mesh export divides Godot positions
by GteScaling and Y/Z-flips them; Lua camera coords are post-flip).

### `Camera.SetPosition(x, y, z)` { #camera-setposition }

Teleports the active camera to the given world-space position.
Coords are PSX-runtime units (post-export, post-flip).

### `Camera.GetRotation() -> {x, y, z}` { #camera-getrotation }

Active camera's Euler rotation as a Vec3 of pi-fractions
({pitch, yaw, roll}, each 1.0 = π).

### `Camera.SetRotation(x, y, z)` { #camera-setrotation }

Sets active camera's Euler rotation in pi-fractions
(1.0 = π, 0.5 = 90°). Order is {pitch, yaw, roll}. NOTE: positive
pitch tilts the view UP in psyqo's matrix; to look DOWN at a
floor pass a small or negative pitch.

### `Camera.GetForward() -> {x, y, z}` { #camera-getforward }

Returns the camera's forward direction as a unit Vec3 (post-rotation
local Z). Useful for "shoot from camera" or "project NPC onto camera
facing" math.

**Example**

```lua
-- since the player facing matches camera in 3p mode.
local f = Camera.GetForward()
```

_Source: `godot-ps1/demo/scripts/boss_smoke_player.lua` line 69._

### `Camera.MoveForward(step)` { #camera-moveforward }

Translates the camera by `step` units along its forward direction.
Positive step = forward, negative = backward.

### `Camera.MoveBackward(step)` { #camera-movebackward }

Translates the camera by `step` units along its backward direction.
Equivalent to MoveForward(-step) but reads more clearly in scripts.

### `Camera.MoveLeft(step)` { #camera-moveleft }

Translates the camera by `step` units along its left side (the
negative-X local axis after rotation).

### `Camera.MoveRight(step)` { #camera-moveright }

Translates the camera by `step` units along its right side
(positive-X local axis after rotation).

### `Camera.FollowPsxPlayer()` { #camera-followpsxplayer }

Switch to follow-player mode: the camera tracks PsxPlayer using the
configured rig offset (PS1Player camera offset + avatar offset). Use
to return to "default" behaviour after a manual SetPosition / LookAt.

### `Camera.SetMode("first"|"third")` { #camera-setmode }

flips between 1st and 3rd-person
camera. Avatar mesh (if any) auto-hides in 1st-person.

**Example**

```lua
Camera.SetMode("third")
```

_Source: `godot-ps1/demo/scripts/realm_init.lua` line 18._

### `Camera.GetH() -> number` { #camera-geth }

Returns the current GTE projection H register (the "screen-distance"
value). Affects perceived focal length / FOV. Default is ~320.

### `Camera.SetH(h) -> nil` { #camera-seth }

Sets the GTE projection H register, clamped to [1, 1024]. Higher H
= narrower FOV (telephoto); lower = wider (fisheye). Use for
cutscene zoom, scope-aim-down-sights, or aesthetic FOV pulses.

### `Camera.Shake(intensity, frames) -> nil` { #camera-shake }

Adds random per-frame jitter to the camera position for `frames` frames,
decaying linearly to zero. `intensity` is FP12 max offset in world
units (e.g., 0.2 for a punchy hit, 0.05 for footstep ambience).

### `Camera.ShakeRaw(rawFp12, frames) -> nil` { #camera-shakeraw }

Same as Camera.Shake but takes intensity as a raw FP12 integer
(4096 = 1.0 world unit). Useful from psxlua scripts that can't
parse decimal literals like 0.04 — pass 164 to get 164/4096 ≈
0.04 world units of shake.

**Example**

```lua
-- was causing perceptible frame dips. Shake alone is the feedback.
Camera.ShakeRaw(SHAKE_WHIFF, 5)
```

_Source: `godot-ps1/demo/scripts/combat_showcase.lua` line 185._

### `Camera.LockOn(target) -> nil` { #camera-lockon }

Engage soft-lock on the given entity. Each frame the runtime
overrides player yaw to face the target, so the third-person
camera tracks it and stick input becomes target-relative
(left-stick X strafes orthogonal to player→target instead of
camera-forward). Right-stick yaw + L1/R1 rotation are visually
suppressed while locked. Auto-unlocks if the target gets
destroyed or deactivated.

**Example**

```lua
Camera.LockOn(boss)
```

_Source: `godot-ps1/demo/scripts/boss_smoke_fog_gate.lua` line 21._

### `Camera.LockOff() -> nil` { #camera-lockoff }

Drop lock-on. Camera + input return to default behavior on
the next frame.

**Example**

```lua
Camera.LockOff()
```

_Source: `godot-ps1/demo/scripts/boss_smoke_brain.lua` line 154._

### `Camera.IsLocked() -> boolean` { #camera-islocked }

True while a lock target is active.

**Example**

```lua
if Camera.IsLocked() then
```

_Source: `godot-ps1/demo/scripts/boss_smoke_player.lua` line 47._

### `Camera.GetLockTarget() -> object or nil` { #camera-getlocktarget }

Returns the currently-locked entity handle, or nil when
unlocked.
