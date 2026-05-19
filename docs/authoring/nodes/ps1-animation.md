# PS1Animation

A named single-track timeline that drives one target GameObject
over a fixed number of frames.

<!-- SCREENSHOT: nodes/ps1-animation-inspector.png -->

## Where it goes

Anywhere under a [`PS1Scene`](ps1-scene.md). Use for spinning
objects, bobbing platforms, swinging doors — any deterministic
motion driven by a known authored timeline.

For multi-track choreography (camera + object + audio in sync),
use [`PS1Cutscene`](ps1-cutscene.md) instead.

## Key fields

- **AnimationName** — the string Lua passes to start the animation
  (e.g. `Animation.Play("bob")`).
- **TrackType** — `Camera` / `Object` / `Audio`. Camera tracks drive
  the runtime's singleton camera. Object tracks drive a named
  [`PS1MeshInstance`](ps1-mesh-instance.md). Audio tracks fire
  keyframed audio events.
- **TargetObjectName** — required for Object tracks. Names the
  `PS1MeshInstance` (its node name, not its path) that this
  timeline drives.
- **FrameCount** — total length in 60Hz frames.
- **Loop** — repeat after FrameCount or stop.

## Child structure

Keyframes are `PS1AnimationKeyframe` child nodes. Author them by
adding child nodes (not via an array editor) so the SceneTree
shows the structure visually + Godot's undo/redo applies cleanly
to keyframe edits.

```
RotatingCube (MeshInstance3D)
└── PS1Animation (spin track, TargetObjectName = "RotatingCube")
    ├── PS1AnimationKeyframe (frame 0: rotation 0°)
    ├── PS1AnimationKeyframe (frame 30: rotation 90°)
    ├── PS1AnimationKeyframe (frame 60: rotation 180°)
    └── PS1AnimationKeyframe (frame 90: rotation 270°)
```

Keyframe value interpretation depends on TrackType (position for
Object, look-at + position for Camera, clip name for Audio).

## Workflows

- Start from Lua: `Animation.Play("spin")` after the targeted
  animation's name matches.
- Stop: `Animation.Stop("spin")`.
- Single-shot vs looping: set `Loop = false` for one-shot;
  `Animation.Stop` returns the target to its bind-pose transform.

## Related

- [Lua API → Animation](../../lua-api/animation.md)
- [PS1Cutscene](ps1-cutscene.md) — multi-track timeline.
