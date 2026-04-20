# PS1Godot addon

Godot 4.x editor plugin for authoring PlayStation 1 games. Targets the
[psxsplash](https://github.com/psxsplash/psxsplash) runtime via the splashpack
binary format.

## Status

- [x] PS1 spatial shader (vertex snap, 2× color modulate, distance fog, nearest filter)
- [x] Custom nodes: `PS1Scene`, `PS1MeshInstance`, `PS1Camera`
- [x] Demo scene
- [ ] Low-resolution subviewport (CompositorEffect)
- [ ] Texture import plugin (CLUT quantization warnings)
- [ ] Lua `ScriptLanguageExtension`
- [ ] Splashpack exporter
- [ ] F5-to-play → PCSX-Redux integration
- [ ] VRAM viewer dock

See `../../ROADMAP.md` for the full plan.

## Contents

| Path | Purpose |
|------|---------|
| `PS1GodotPlugin.cs` | `EditorPlugin` entry |
| `nodes/PS1Scene.cs` | Scene root; holds fog/player/scene-type config |
| `nodes/PS1MeshInstance.cs` | Mesh node with PS1 material + collision/Lua refs |
| `nodes/PS1Camera.cs` | Camera constrained to PS1-appropriate settings |
| `shaders/ps1.gdshader` | The PS1 look — apply to any MeshInstance3D material |
| `shaders/ps1_default.tres` | Ready-made material using the shader |
