# PS1Scene

Root node of a PS1 scene. Carries scene-wide settings — fog, player
physics, audio clip list, music sequences, sub-scene array — that
end up in the splashpack header.

<!-- SCREENSHOT: nodes/ps1-scene-inspector.png — PS1Scene selected, inspector visible -->

## Where it goes

The **root** of every scene that will export to PSX. One per
`.tscn`. The exporter walks down from this node; anything not
under a `PS1Scene` root won't make it into the splashpack.

To promote a fresh `Node3D` root: select root → Inspector → script
icon → **Extends → PS1Scene**.

## Key fields

- **Fog** — color, near/far, density. The exporter stamps these into
  the splashpack header; the runtime applies them per-frame.
  Editor preview requires setting matching fog on the material (a
  Phase 1 limitation — Phase 1's subviewport work will
  auto-propagate scene fog to all PS1 materials).
- **GteScaling** — Godot units per PSX unit. Default `4`. Affects
  how authored transforms map to PSX fixed-point coordinates at
  export time.
- **Player** — height, radius, move speed, jump height, gravity.
  Drives the runtime's CharacterController constants for whichever
  [`PS1Player`](ps1-player.md) ends up in the scene.
- **AudioClips** — array of [`PS1AudioClip`](ps1-audio-clip.md)
  resources referenced by Lua and dialogue graphs.
- **MusicSequences** — array of `.mid` references for sequenced BGM.
- **SubScenes** — array of `.tscn` references loadable via
  `Scene.Load(N)` from Lua.

Hover any field for the in-Inspector tooltip with field-by-field
guidance.

## Workflows

- A `PS1Scene` with no other PS1 nodes still exports cleanly
  (empty scene). Use this to verify the exporter is healthy
  before troubleshooting a complex scene.
- Sub-scenes are independent `.tscn` files referenced via the
  `SubScenes` array. They package alongside the main scene in
  the splashpack but only load when `Scene.Load(N)` fires.

## Related

- [PS1Godot panel](../../docks/ps1godot-panel.md) — Run-on-PSX
  exports from the active `PS1Scene`.
- [`reference/splashpack-format.md`](../../reference/splashpack-format.md)
  — what the scene becomes on disk.
