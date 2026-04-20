# PS1Godot

Goal: author PlayStation 1 games in **Godot** instead of Unity, reusing the existing
[psxsplash](https://github.com/psxsplash/psxsplash) runtime and [PCSX-Redux](https://github.com/grumpycoders/pcsx-redux) toolchain.

## Repo layout

This directory is a workspace, not yet a Godot project. The four subdirectories are
**vendored reference sources** — treat them as read-only unless we decide to fork one.

| Path | What | Role |
|------|------|------|
| `godot-master/` | Godot engine source (4.x) | Reference only. Day-to-day we use a prebuilt Godot **Mono/.NET** editor, not this tree. |
| `pcsx-redux-main/` | PCSX-Redux emulator + PSYQo library + MIPS tooling | Test target and toolchain host. `src/mips/psyqo` is what psxsplash is built on. |
| `psxsplash-main/` | PS1-side C++ runtime | **Consumed as-is.** Loads a splashpack `.bin` on the PS1 and runs the game. |
| `splashedit-main/` | Unity editor package (C#) | **The thing we are replacing.** Port its exporter logic to a Godot editor plugin. |

The actual Godot integration project lives in `godot-ps1/` — a Godot 4.x .NET
project with our plugin at `godot-ps1/addons/ps1godot/`.

## Architecture — how the pieces connect

```
┌──────────────────┐   splashpack .bin   ┌──────────────────┐   MIPS elf   ┌─────────────┐
│  Godot editor    │ ──────────────────▶ │  psxsplash       │ ───────────▶ │  PCSX-Redux │
│  + PS1Godot      │   (binary scene)    │  runtime (C++)   │              │  or real PS1│
│  plugin (C#)     │                     │  on PSYQo        │              │             │
└──────────────────┘                     └──────────────────┘              └─────────────┘
```

The **splashpack binary format** is the integration contract. Current version is
**v20** (see `splashedit-main/Runtime/PSXSceneWriter.cs` and `psxsplash-main/src/splashpack.{hh,cpp}`).
The loader hard-asserts `version >= 20`; older exports won't load.

**v20 splits the export into three files**, all written alongside each other:

| File | Contents |
|------|----------|
| `scene.splashpack` | Header + live scene structures (meshes, colliders, BVH, nav, Lua, UI, cutscenes) |
| `scene.splashpack.vram` | Texture atlas pixels + CLUTs + UI font pixels (uploaded to PS1 VRAM) |
| `scene.splashpack.spu` | Audio ADPCM bulk data (uploaded to PS1 SPU RAM) |

Splitting by destination lets the runtime DMA each blob into the right memory
region without parsing. The `.splashpack` file references offsets into the other
two.

Magic bytes are `"SP"`, header is **120 bytes** (see `SPLASHPACKFileHeader` in
`splashpack.cpp`). Struct layouts are load-bearing — `static_assert` sizes in
`splashpack.hh` are the source of truth, and the Godot writer must match them
bit-for-bit.

A human-readable extract of the format lives in `docs/splashpack-format.md`.

## Language & tooling decisions

- **Plugin, not engine fork.** Godot's editor API exposes everything we need
  (`EditorPlugin`, `CompositorEffect`, `EditorImportPlugin`, `ResourceFormatSaver`,
  `ScriptLanguageExtension`). A fork would cost perpetual merge conflicts against
  a 1M-LOC codebase for no concrete capability we can't already hit. Re-open only
  if we hit a specific wall.
- **Plugin language: C#.** Godot supports it via the Mono/.NET build; SplashEdit's
  exporter logic (texture quantization, VRAM packing, binary writer, ADPCM
  conversion, BVH build) ports line-by-line from C#. GDScript is too slow for
  bit-level work.
- **GDExtension (C++) reserved** for hot paths when iteration time hurts
  (Phase 4). Same language family as psxsplash, so shared code is possible.
- **Lua is a first-class Godot script** via `ScriptLanguageExtension`, not a
  bolt-on asset. Attaches directly to nodes like GDScript or C# does.
- **Godot version: 4.7.0-dev.5** (user's current pick). Minimum supported is
  still 4.4+ (for `CompositorEffect`). Pin a stable minor once Phase 1 lands.
  The dev SDK ships only inside the Godot install at `GodotSharp/Tools/nupkgs/`;
  `godot-ps1/NuGet.Config` exposes it via the `GODOT_NUPKGS` env var for
  Rider/CLI restore.
- **MIPS toolchain:** `mipsel-none-elf` on Windows via the `mips.ps1` script
  shipped in pcsx-redux. Needed only when rebuilding psxsplash itself.
- **Emulator for iteration:** PCSX-Redux with PCdrv backend (`PCDRV_SUPPORT=1`)
  so the game reads files from the host filesystem without an ISO.
- **ISO / real hardware:** `mkpsxiso` + `LOADER=cdrom` build of psxsplash.
  Defer to Phase 3+.

## Conventions for working in this repo

- **Do not edit the vendored trees** (`godot-master`, `pcsx-redux-main`,
  `psxsplash-main`, `splashedit-main`) unless explicitly forking. If a fix is
  needed, prefer upstreaming. If that's not viable, create a patch file in
  `patches/` and apply at build time.
- **Splashpack format is the contract.** Any change to the writer requires a
  matching reader change (or version bump + compat branch in the C++ loader).
  Bake struct sizes into tests.
- **PS1 constraints are non-negotiable.** Vertex positions are fixed-point
  (`psyqo::FixedPoint<12, ...>`), textures are 4bpp/8bpp CLUT or 16bpp direct,
  VRAM is 1 MB arranged as 1024×512 16-bit. If a design step ignores these,
  flag it.
- **Don't over-abstract.** SplashEdit was one person's project that grew large;
  mirror its structure rather than re-architecting, at least through Phase 2.

## Shell / platform notes

- Windows 11, bash shell (Git Bash style). Use forward slashes and Unix redirects
  (`/dev/null`, not `NUL`).
- IDE is JetBrains Rider (fits the C# choice).
- Godot editor launches standalone; there is no Unity-style Control Panel yet —
  that's a Phase 2 deliverable.

## Where to look for what

| Need to understand… | Read… |
|---|---|
| Improvements we'd like upstreamed to psxsplash | `docs/psxsplash-improvements.md` |
| The on-disk scene format | `splashedit-main/Runtime/PSXSceneWriter.cs` + `psxsplash-main/src/splashpack.hh` |
| How Unity walks the scene to collect exporters | `splashedit-main/Runtime/PSXSceneExporter.cs` |
| Texture quantization + VRAM packing | `splashedit-main/Runtime/TexturePacker.cs`, `ImageProcessing.cs` |
| Collision / BVH build | `splashedit-main/Runtime/BVH.cs`, `PSXCollisionExporter.cs` |
| Runtime rendering loop | `psxsplash-main/src/renderer.cpp`, `main.cpp` |
| Lua API exposed to games | `psxsplash-main/src/luaapi.{cpp,hh}` |
| PSYQo primitives (GTE, fixed-point, GPU prims) | `pcsx-redux-main/src/mips/psyqo/` |

## Current status

Phase 1 scaffold complete: Godot .NET project, plugin skeleton, PS1 spatial shader,
custom nodes, demo scene, launch scripts. Awaiting Godot install to verify and
move on to the preview polish (low-res subviewport, import presets) and Phase 2
(exporter). See `ROADMAP.md` for the full plan and `SETUP.md` for env setup.
