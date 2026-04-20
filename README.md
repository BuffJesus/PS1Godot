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

**Phase 2 (splashpack exporter MVP): in progress, bullets 1–6 running on
PSX.** The Godot demo scene exports to a valid splashpack, boots in
PCSX-Redux, and plays: player falls, walks with D-pad, jumps, collides
with cubes on static floor, Lua `onUpdate` fires every frame, and audio
plays via `Audio.Play("clip_name")`. Remaining: nav beyond flat floors,
UI canvases, trigger boxes + interactables, cutscenes, skinned meshes,
rooms/portals. See `ROADMAP.md`.

**What works right now**
- PS1 spatial shader + default material, vertex jitter, fog
- Custom nodes: `PS1Scene`, `PS1MeshInstance`, `PS1Camera`, `PS1AudioClip`,
  `PS1TriggerBox`
- Splashpack v20 exporter (meshes, textures with CLUT quantization,
  per-object colliders, flat nav regions, source-text Lua scripts, ADPCM
  audio)
- One-click **PS1Godot: Run on PSX** (export + launch PCSX-Redux)
- PS1Lua script language in Godot's Create Script dropdown

**What doesn't yet**
- Non-trivial nav regions (DotRecast port, Phase 2 bullet 7)
- UI canvases + fonts (Phase 2 bullet 8)
- Trigger boxes + interactables (Phase 2 bullet 9, in flight)
- Cutscenes, skinned meshes, rooms/portals (Phase 2 bullets 10-12)
- F5-to-play / VRAM viewer / budget overlays (Phase 3)
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
