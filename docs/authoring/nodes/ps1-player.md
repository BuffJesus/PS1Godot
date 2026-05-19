# PS1Player

The player spawn point. Place one per scene where you want the
player to appear; the exporter reads its world transform into the
splashpack's playerStart fields.

<!-- SCREENSHOT: nodes/ps1-player-inspector.png -->

## Where it goes

Direct child of [`PS1Scene`](ps1-scene.md). One per scene. Position
roughly at the player's hip height (the physics body anchors to
feet; Y = 1 puts feet on the floor).

## Key fields

- **CameraRigStyle** — `FirstPerson` / `ThirdPerson`. Authoring
  intent; runtime support landed in Phase 2.5. The exporter stamps
  the chosen mode so the runtime picks it up.
- **YawOnSpawn** — initial facing direction (Y-axis rotation).

## Child structure

The runtime auto-tracks specific child types:

- **`Camera3D` (or [`PS1Camera`](ps1-camera.md)) child** —
  authored as the third-person offset in player-local space.
  `(0, 1, 3)` is a reasonable starting offset.
- **[`PS1MeshInstance`](ps1-mesh-instance.md) child** — the visible
  avatar. Position + yaw track the player automatically every
  frame; no Lua wiring needed.

```
Scene (PS1Scene)
└── PS1Player
    ├── Camera3D (third-person offset)
    └── MeshInstance3D (avatar — humanoid or box)
```

## Movement constants

Don't live on this node — they're on the parent `PS1Scene` under
the Player group (Height, Radius, MoveSpeed, JumpHeight, Gravity).
Lets a single scene have one consistent feel across multiple
spawns.

## Workflows

- **Multi-spawn** — multiple `PS1Player` nodes are legal; only the
  first one's transform is used at spawn but the others can be
  teleport targets via Lua's `Player.SetPosition`.
- **Avatar swaps** — swap the child `MeshInstance3D` to switch
  player models. Useful for character-select between scenes.

## Related

- [Lua API → Player](../../lua-api/player.md)
- [Lua API → Controls](../../lua-api/controls.md)
- [PS1Camera](ps1-camera.md) — when used as the third-person
  offset.
