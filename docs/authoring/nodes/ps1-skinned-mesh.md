# PS1SkinnedMesh

A [`PS1MeshInstance`](ps1-mesh-instance.md) that additionally
carries skeleton + animation data. The bind-pose writes through the
standard mesh pipeline; per-triangle bone indices + baked animation
frames emit as a side-table the runtime indexes by GameObject.

<!-- SCREENSHOT: nodes/ps1-skinned-mesh-inspector.png -->

## Where it goes

Same as `PS1MeshInstance` — anywhere under a
[`PS1Scene`](ps1-scene.md). Typically used for humanoid characters,
articulated props, or anything with a Godot `Skeleton3D` driving
deformation.

## Key fields

Inherits every field from `PS1MeshInstance`. Additional surface:

- **Skeleton** — reference to the `Skeleton3D` driving the bone
  transforms. Auto-detected from the imported FBX/GLTF in most
  cases.
- **AnimationLibrary** — array of named clips. Each clip becomes a
  baked-frames table at export time and is callable from Lua via
  `SkinnedAnim.Play("name")`.
- **BakeFps** — frames per second to sample the source animation
  at. Default 30 (PSX-typical). Higher rates produce smoother
  motion at the cost of frame-table size.

## Runtime API

```lua
-- Start a clip
SkinnedAnim.Play(self, "walk")
-- Layer / blend / stop — see lua-api/skinned-anim
```

The full surface is at [Lua API → SkinnedAnim](../../lua-api/skinned-anim.md).

## Workflows

- Import an FBX with skeleton + animation tracks. Godot creates a
  `MeshInstance3D` with a child `Skeleton3D` and an
  `AnimationPlayer`. Promote the `MeshInstance3D` to
  `PS1SkinnedMesh`; the inspector lets you reference the
  `AnimationPlayer`'s clips for baking.
- Per-vertex bone weights collapse to **one bone per vertex** at
  export — the runtime doesn't blend. Bone choice = the vertex's
  most-influential bone in the original weights. This matches
  PSX-era character animation.

## Stage status

Stage 0–1 (current): node type + property surface + export wiring
land. Stages 2+ refine the per-frame data format for size /
playback-cost wins.

## Related

- [Lua API → SkinnedAnim](../../lua-api/skinned-anim.md)
- [PS1Animation](ps1-animation.md) — for transform-only animation
  (no bones).
