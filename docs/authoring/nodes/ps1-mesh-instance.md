# PS1MeshInstance

A `MeshInstance3D` tagged for PS1 export. Auto-applies the PS1
shader and carries the export-time metadata needed to convert the
mesh into something the PSX renderer can consume.

<!-- SCREENSHOT: nodes/ps1-mesh-instance-inspector.png -->

## Where it goes

Anywhere under a [`PS1Scene`](ps1-scene.md). Multiple per scene is
the norm — every visible solid (floor, walls, props, vehicles)
typically becomes one.

Promote via script icon → **Extends → PS1MeshInstance** on any
`MeshInstance3D`, or use **Quick Actions → Convert mesh to PS1** in
the [PS1Godot panel](../../docks/ps1godot-panel.md).

## Key fields

- **TextureBitDepth** — 4bpp / 8bpp / 16bpp. Default 8bpp (256-color
  CLUT, conventional middle ground). 4bpp halves VRAM at the cost
  of palette flexibility; 16bpp is direct-color, used for
  finely-shaded surfaces.
- **VertexLightingMode** — Baked / Flat / None. Default Baked
  (uses Godot's computed per-vertex lighting). Flat applies a
  single tint; None disables vertex coloring.
- **Collision** — None / Static / Trigger. Default None — opt in
  when geometry should block the player. Static creates a collision
  surface; Trigger fires
  `onTriggerEnter` / `onTriggerExit` on the attached Lua.
- **Interactable** — `bool`. When true, reveals InteractRadius,
  ShowPrompt, PromptCanvasName, and the ScriptFile slot.
  Interaction fires when the player presses Triangle within
  InteractRadius.
- **ScriptFile** — `.lua` to dispatch `onCreate` / `onUpdate` /
  `onInteract` / `onTriggerEnter` / `onTriggerExit` against.

## Workflows

- **Auto-applied material** — `_Ready` assigns
  `addons/ps1godot/shaders/ps1_default.tres` if `MaterialOverride`
  is empty. Editor-time preview needs the override set manually
  (drag the .tres onto the slot).
- **Scale baking** — non-identity scale on the transform bakes
  into the exported triangles. Author meshes at intended size
  with `(1, 1, 1)` scale to avoid surprises.
- **Lua attachment** — for interactive cubes, doors, NPCs, etc.
  The runtime auto-tracks `PS1MeshInstance` children of
  [`PS1Player`](ps1-player.md) so a player avatar moves without
  any Lua wiring.

## Related

- [`authoring/fixed-cameras.md`](../fixed-cameras.md) for
  fixed-camera authoring patterns.
- [Audio Routing dock](../../docks/audio-routing.md) — if
  `ScriptFile` calls `Audio.PlaySfx`, the routing dock shows
  what route the clip will resolve to.
