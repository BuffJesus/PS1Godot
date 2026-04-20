# PS1Godot

Author PlayStation 1 games in Godot.

PS1Godot is a Godot 4.x editor plugin (C# / .NET) that lets you design PS1
scenes in Godot and export them to the [psxsplash](https://github.com/psxsplash/psxsplash)
runtime, which runs on real PS1 hardware and in
[PCSX-Redux](https://github.com/grumpycoders/pcsx-redux).

It is a Godot-native rethink of
[SplashEdit](https://github.com/psxsplash/splashedit) (Unity). Same binary
format, different editor, deliberately better UX.

## Current state

**Phase 1 (in-editor PS1 preview): done.** PS1 look (vertex snap, 2× color
modulate, nearest filter, fog) renders in Godot's viewport. Stretch goals
landed too: subdivision tool, texture compliance analyzer, low-res
compositor preview, PS1Lua as a first-class Godot script language via
GDExtension.

**Phase 2 (splashpack exporter MVP): in progress, bullets 1–6, 8, 9, 10
(MVP), and 11 running on PSX.** The Godot demo scene exports to a valid
splashpack, boots in PCSX-Redux, and plays: intro cutscene with narrator,
third-person camera follows the player, humanoid avatar tracks + turns
with player input, interactive cubes with branching dialog, animated
skinned mesh, audio with per-clip residency, in-editor dock with live
scene-budget bars. Remaining Phase 2 bullets: 7 (nav beyond flat) and
12 (rooms/portals). See `ROADMAP.md`.

**What works right now**
- PS1 spatial shader + default material, vertex jitter, fog
- Custom nodes: `PS1Scene`, `PS1MeshInstance`, `PS1SkinnedMesh`,
  `PS1Camera`, `PS1Player`, `PS1AudioClip`, `PS1TriggerBox`,
  `PS1UICanvas`, `PS1UIElement`, `PS1Animation`, `PS1Cutscene`
- Splashpack **v21** exporter (meshes with bone weights, textures with
  CLUT quantization, per-object colliders, flat nav regions,
  source-text Lua scripts, ADPCM audio with Residency flags,
  editor-driven camera + avatar rigs from `PS1Player` child nodes)
- Skinned meshes with per-vertex rigid bone assignment + baked
  animation clips sampled at author-set FPS
- First-person / third-person camera mode switching via
  `Camera.SetMode()` Lua API
- Runtime `\n` word-wrap for dialog and narrator text
- Dockable **PS1Godot** panel with triangle/VRAM/SPU budget bars,
  dependency-detection setup section, and primary Run-on-PSX CTA
- PS1 UI prefab templates (`dialog_box`, `menu_list`, `hud_bar`,
  `toast`) authored as drop-in `.tscn` scenes
- One-click **PS1Godot: Run on PSX** (export + launch PCSX-Redux)
- PS1Lua script language in Godot's Create Script dropdown

**What doesn't yet**
- Non-trivial nav regions (DotRecast port or manual polygon auth,
  Phase 2 bullet 7)
- Rooms / portals for interior-scene culling (Phase 2 bullet 12)
- Cutscenes polish beyond MVP (rotation/scale tracks, camera-bug
  reproduction — Phase 2 bullet 10 option B)
- WYSIWYG UI canvas editor, dialog tree editor, `PS1Theme.tres`
  (Phase 3 § UI authoring experience)
- F5-to-play binding, VRAM viewer dock, project templates
  (Phase 3)
- Phase 0.5 install-buttons on the setup dock
- Zero-setup `PS1Godot.zip` drop (Phase 4)

## Getting started

1. **Set up your environment** → [`SETUP.md`](SETUP.md)
2. **Walk through your first scene** → [`docs/tutorial-hello-cube.md`](docs/tutorial-hello-cube.md)
3. **Understand the pipeline** → [`CLAUDE.md`](CLAUDE.md)
4. **See what's next** → [`ROADMAP.md`](ROADMAP.md)

The reference source trees (`godot-master/`, `pcsx-redux-main/`,
`splashedit-main/`) are **not included** in this repo — clone them
separately if you want to read them. Only `psxsplash-main/` is vendored,
because we carry local patches against it (tracked in
`docs/psxsplash-improvements.md`).

## Repo layout

```
PS1Godot/
├─ godot-ps1/              ← the Godot 4.x .NET project (open this in Godot)
│  ├─ addons/ps1godot/     ← plugin: nodes, shaders, exporter, PS1Lua GDExtension
│  └─ demo/                ← sample scene exercising the pipeline end-to-end
├─ psxsplash-main/         ← vendored PS1 runtime + our local patches
├─ scripts/                ← launch / build helpers (.cmd)
├─ docs/                   ← format reference, tutorials, upstream-improvement tracker
├─ CLAUDE.md               ← architecture + conventions (also loaded by Claude Code)
├─ GLOSSARY.md             ← PS1 / PSYQo / splashpack vocabulary
├─ ROADMAP.md              ← phased plan, Phase 2.5/2.6 stretch
└─ SETUP.md                ← env setup walkthrough (Godot + MIPS toolchain + PCSX-Redux)
```

## License

TBD. The vendored `psxsplash-main/` keeps its upstream license
(`psxsplash-main/LICENSE`). Our original plugin + scripts + docs will be
declared before first public release.
