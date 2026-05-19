# PS1 nodes

Custom nodes the plugin adds. Every node here is a Godot scene-tree
node tagged for the splashpack exporter — they map one-to-one to
data structures the runtime understands.

## At a glance

| Node | Role | Parent |
|---|---|---|
| [PS1Scene](ps1-scene.md) | Scene root | Scene root |
| [PS1MeshInstance](ps1-mesh-instance.md) | Renderable mesh + collision | anywhere |
| [PS1SkinnedMesh](ps1-skinned-mesh.md) | Skinned mesh + bone animation | anywhere |
| [PS1Camera](ps1-camera.md) | Initial camera transform | child of `PS1Scene` |
| [PS1Player](ps1-player.md) | Player spawn + camera rig | child of `PS1Scene` |
| [PS1Animation](ps1-animation.md) | Named single-track timeline | anywhere |
| [PS1Cutscene](ps1-cutscene.md) | Multi-track timeline | anywhere |
| [PS1AudioClip](ps1-audio-clip.md) | Audio resource with routing | resource (not a node) |
| [PS1TriggerBox](ps1-trigger-box.md) | World-space trigger volume | anywhere |
| [PS1UICanvas](ps1-ui-canvas.md) | Screen-space UI layer | child of `PS1Scene` |
| [PS1Room](ps1-room.md) | Convex interior volume | child of `PS1Scene` |
| [PS1PortalLink](ps1-portal-link.md) | Portal between two rooms | child of `PS1Scene` |
| [PS1Sky](ps1-sky.md) | Background skybox | child of `PS1Scene` |

Not yet documented (live in `godot-ps1/addons/ps1godot/nodes/`):
sound macros (`PS1SoundMacro`, `PS1SoundFamily`), per-channel music
authoring (`PS1MusicChannel`, `PS1MusicSequence`, `PS1Instrument`,
`PS1DrumKit`, `PS1SampleRegion`), nav (`PS1NavRegion`), UI layout
helpers (`PS1UIHBox`, `PS1UIVBox`, `PS1UIAnchor`, `PS1UISpacer`,
`PS1UISizeBox`, `PS1UISlot`, `PS1UIOverlay`, `PS1UIModel`,
`PS1UIElement`, `PS1UIFontAsset`), themes (`PS1Theme`),
animation primitives (`PS1AnimationKeyframe`, `PS1AnimationTrack`,
`PS1AudioEvent`), and mesh helpers (`PS1MeshGroup`,
`PS1MaterialMetadata`, `PS1Backdrop`).

## Conventions

- **Hover tooltips** on every Inspector field. `///` doc-comments on
  the `[Export]` properties surface as Godot 4 tooltips — read those
  for field-by-field detail.
- **Defaults that just work.** A freshly-added node renders / plays /
  collides correctly with no further configuration. Tweak only when
  you have a specific deviation in mind.
- **PS1 constraints are checked at export.** [PS1 Doctor](../../docks/doctor.md)
  warns about VRAM blowouts, missing required nodes, mismatched
  field combinations. Authoring is forgiving; export is strict.
