# PS1Camera

A `Camera3D` tagged for PS1 export. Auto-attaches a
`PS1PixelizeEffect` compositor so the editor viewport shows the
320×240 PSX look without manual setup.

<!-- SCREENSHOT: nodes/ps1-camera-inspector.png -->

## Where it goes

Anywhere under a [`PS1Scene`](ps1-scene.md). For player-driven
gameplay, place a `Camera3D` child under a
[`PS1Player`](ps1-player.md) and promote it (the runtime rotates
that camera's offset by the player's yaw each frame). For fixed
cameras, position a standalone `PS1Camera` and the exporter writes
its transform as the scene's initial camera position.

**Exactly one PS1Camera per scene** is the intent — the exporter
uses the first one it finds.

## Key fields

Standard `Camera3D` fields: FOV, near, far, transform. Plus the
auto-attached `PS1PixelizeEffect` compositor.

PSX-typical values:

- **FOV** `72°` — PS1 games typically ran 60–90°.
- **Near** `0.2`
- **Far** `60`

## Workflows

- **Disable the editor PSX preview** — Inspector → Compositor →
  Effects → clear. Re-add via **Tools → Materials → Toggle PS1
  Preview**.
- **Player-relative camera** — drop a `Camera3D` child under a
  `PS1Player` at `(0, 1, 3)` (1 unit up, 3 units back). Promote
  to `PS1Camera`. The runtime treats this as the third-person
  offset.
- **Camera mode switch** — Lua's `Camera.SetMode("first" |
  "third")` flips between first-person (camera at player head)
  and third-person (using the authored offset).

## Related

- [`authoring/fixed-cameras.md`](../fixed-cameras.md) — fixed
  camera patterns (Resident Evil / Final Fantasy authoring style).
- [Lua API → Camera](../../lua-api/camera.md) — runtime camera
  control.
