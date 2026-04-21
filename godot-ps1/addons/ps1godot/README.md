# PS1Godot addon

Godot 4.x editor plugin for authoring PlayStation 1 games. Targets the
[psxsplash](https://github.com/psxsplash/psxsplash) runtime via the splashpack
binary format (currently **v22**, with editor-driven player rigs +
sequenced music).

## Status

- [x] PS1 spatial shader (vertex snap, 2× color modulate, distance fog, nearest filter)
- [x] Low-resolution editor preview via `CompositorEffect`
- [x] Custom nodes: `PS1Scene`, `PS1MeshInstance`, `PS1SkinnedMesh`,
      `PS1Camera`, `PS1Player`, `PS1AudioClip`, `PS1MusicSequence`,
      `PS1MusicChannel`, `PS1TriggerBox`, `PS1UICanvas`, `PS1UIElement`,
      `PS1Animation`, `PS1AnimationKeyframe`, `PS1AnimationTrack`,
      `PS1Cutscene`, `PS1Room`, `PS1PortalLink`, `PS1NavRegion`,
      `PS1Theme`
- [x] Splashpack exporter (v22; meshes, textures with CLUT quantization,
      colliders, BVH, nav regions, rooms/portals, cutscenes, animations,
      skinned meshes, UI canvases, ADPCM audio, sequenced music)
- [x] PS1Lua as a first-class Godot script language (`ScriptLanguageExtension`
      via GDExtension)
- [x] Dockable PS1Godot panel with budget bars + Run-on-PSX button
- [x] Demo scene exercising the full pipeline end-to-end
- [ ] Texture import plugin (CLUT quantization warnings — Phase 3)
- [ ] F5-to-play → PCSX-Redux debugger attach (Phase 3)
- [ ] VRAM viewer dock (Phase 3)
- [ ] WYSIWYG UI canvas editor + dialog tree editor (Phase 3)

See `../../ROADMAP.md` for the full plan.

## Contents

| Path | Purpose |
|------|---------|
| `PS1GodotPlugin.cs` | `EditorPlugin` entry |
| `nodes/PS1Scene.cs` | Scene root; holds fog/player/audio/music config |
| `nodes/PS1MeshInstance.cs` | Mesh node with PS1 material + collision/Lua refs |
| `nodes/PS1MusicSequence.cs` | Authoring resource for sequenced BGM (`.mid` + bindings) |
| `nodes/PS1MusicChannel.cs` | Per-MIDI-channel binding to an audio clip |
| `nodes/PS1AudioClip.cs` | ADPCM clip with residency flag |
| `shaders/ps1.gdshader` | The PS1 look — apply to any MeshInstance3D material |
| `shaders/ps1_default.tres` | Ready-made material using the shader |
| `exporter/SplashpackWriter.cs` | Writes the v22 splashpack triplet (.splashpack/.vram/.spu) |
| `exporter/MidiParser.cs` | SMF format-0/1 parser → flat note + tempo events |
| `exporter/PS1MSerializer.cs` | Packs parsed MIDI + bindings into the PS1M binary blob |
| `exporter/SceneCollector.cs` | Walks a Godot scene and produces a `SceneData` snapshot |
| `scripting/` | PS1Lua GDExtension (`ScriptLanguageExtension`) |
| `ui_templates/` | Drop-in `.tscn` UI templates (dialog box, menu list, HUD bar, toast) |
