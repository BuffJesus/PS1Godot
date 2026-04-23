# PS1 Lua authoring cheatsheet

Quick-reference for the gotchas that bite every first-time author of a
PS1Godot scene script. Read once; come back when something doesn't
behave the way Godot taught you to expect.

## Decimals work. Godot rewrites them for you at export.

```lua
Camera.Shake(0.06, 6)       -- rewriter emits FixedPoint.newFromRaw(246)
Vec3.new(0.5, 1.0, 2.25)    -- three FixedPoint.newFromRaw calls
Physics.Raycast(pos, dir, 1.25)  -- all literals go through
```

**Caveats — the rewriter currently skips these forms:**

- Scientific notation: `1e-5`, `1.5e2` — write the raw integer, or use `FixedPoint.newFromRaw(raw_fp12)`.
- Bare trailing dot: `5.` — write `5.0`.
- Bare leading dot: `.5` — write `0.5`.

If you hit `malformed number near '<tok>'` at boot, the rewriter missed
your token. The runtime prints a hint block with the fix. Worst case:
`FixedPoint.newFromRaw(integer * 4096 + fractional_raw)` works for any
fp12 value by hand.

## Y is inverted. +Y points DOWN on PS1.

```
  -Y   (up — toward the ceiling / sky)
   ▲
   │
   ●──►  +X  (right)
  ╱
 ╱
+Z  (away from camera)
 ▼
+Y   (down — toward the floor)
```

- Jumping subtracts from `pos.y`.
- A camera "above" the player has a SMALLER y than the player.
- Melee-hitbox "below head to feet" spans `[cam.y, cam.y + 2]`, not `[cam.y - 2, cam.y]`.

This matches the runtime's worldcollision.cpp: gravity is `velocity.y +=
gravity_per_frame` (positive), and "ground" is the LARGEST y your
collider can reach.

## `Player.GetPosition()` in third-person returns the CAMERA head.

Not the player body. The third-person rig sits `rigOffset` behind the
player — typically `(0, 1, 3)` — so `Player.GetPosition()` is the
camera eye, not where the avatar is standing.

**Positioning a projectile spawn / melee hitbox:**

```lua
local MUZZLE_AHEAD = 4   -- rigOffset.z (3) + 1 for chest-front
local MUZZLE_DROP  = 1   -- camera is at head height; chest is +1y
local cam = Player.GetPosition()
local fwd = flatForward()          -- horizontal camera facing
local spawn = Vec3.new(
    cam.x + fwd.x * MUZZLE_AHEAD,
    cam.y + MUZZLE_DROP,            -- +Y = DOWN (see above)
    cam.z + fwd.z * MUZZLE_AHEAD)
```

First-person mode returns the camera eye too — no offset needed there.

## fp12 types: integer vs FixedPoint vs raw

Three numeric forms show up in the Lua API:

| Form | Example | When to use |
|------|---------|-------------|
| Plain integer | `6`, `120` | Frame counts, indices, tags, enum values |
| FixedPoint | `Camera.Shake(0.5, …)` (rewritten) or `FixedPoint.new(2, 2048)` | World-space positions, distances, shake intensity — anything where fractions matter |
| Raw fp12 int | `Camera.ShakeRaw(2048, 10)` | When the API takes an integer but the value represents an fp12 fraction (same bits as a FixedPoint, no metatable overhead) |

**Mixing in arithmetic:** `FixedPoint + integer` works (auto-shifts the
integer). `integer + FixedPoint` does NOT — the integer's metamethod
fires first and has no knowledge of FixedPoint. Put the FixedPoint on
the left:

```lua
local y = pos.y + 0.5       -- OK: pos.y is FixedPoint, 0.5 → FixedPoint
local y = 0.5 + pos.y       -- ERROR: Cannot add FixedPoint to this type
```

## Input.IsPressed vs Input.IsHeld

| Function | Semantics | Use for |
|----------|-----------|---------|
| `Input.IsPressed(Input.L2)` | Edge-triggered (fires on the frame the button went down) | Shoot, jump, interact, menu-open — one-tap-one-action |
| `Input.IsHeld(Input.L2)` | Level-triggered (true while the button is down) | Sprint, aim, charge-meters — continuous |

Naming note: `IsPressed` is the one people expect to be "is it down
right now?" but it's the `JustPressed` semantics. Don't use it in a
sprint loop or you'll sprint for exactly one frame.

## Debug.Log is slow. Do not spam it.

The runtime's `printf` goes through the PS1's TTY UART, which is
synchronous. A few prints per second is fine. A print per frame (or
per missed attack) produces visible stutter. If you need frame-level
diagnostics, pipe them through Scene.PauseFor or a shake so the
feedback is visual, not console.

## Reference: `combat_showcase.lua`

`godot-ps1/demo/scripts/combat_showcase.lua` applies every convention
above. Copy-paste snippets from it when starting a new gameplay
script — it's the closest thing to a known-good template.
