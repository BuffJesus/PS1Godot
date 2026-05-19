<!-- gen_lua_api_docs:generated -->
# `SkinnedAnim`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

5 entries, 2 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `SkinnedAnim.Play(objectName, clipName)` { #skinned-anim-play }

or SkinnedAnim.Play(objectName, clipName, {loop=bool, onComplete=fn})
Plays a bone-driven clip on a skinned mesh (Mixamo character,
creature with rig, etc.). `objectName` is the GameObject name
that owns the rig; `clipName` is the authored clip on that rig.
Replaces any clip already playing on that object. Note that
Mixamo clip names with dots are rewritten to underscores at
export time — match the exporter output, not the FBX label.

**Example**

```lua
SkinnedAnim.Play("Player", "mixamo_com", { loop = true })
```

_Source: `godot-ps1/demo/scripts/test_logger.lua` line 248._

### `SkinnedAnim.Stop(objectName)` { #skinned-anim-stop }

Halts the rig's current clip. The mesh stays frozen on the last
rendered frame — call SkinnedAnim.BindPose to reset to T-pose.

### `SkinnedAnim.IsPlaying(objectName) -> boolean` { #skinned-anim-isplaying }

True while the rig is animating. Returns false for unknown
objects or after Stop / BindPose.

### `SkinnedAnim.GetClip(objectName) -> string or nil` { #skinned-anim-getclip }

Name of the clip the rig is currently playing, or nil if the
object isn't skinned / has no clip set. Useful for state-machine
logic ("if not idle, queue idle").

### `SkinnedAnim.BindPose(objectName) -> nil` { #skinned-anim-bindpose }

Stop any active clip and render the mesh in its bind pose (T-pose)
with identity bone matrices. Use for idle / title-screen states where
frame 0 of a walk clip would show a mid-stride pose.

**Example**

```lua
-- walk-cycle frame, which would leave the character mid-stride.
SkinnedAnim.BindPose("Player")
```

_Source: `godot-ps1/demo/scripts/test_logger.lua` line 253._
