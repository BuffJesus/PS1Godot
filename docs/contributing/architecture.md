# Architecture

A two-page mental model for anyone modifying the codebase: how the
parts fit, where the integration contracts live, and what
constraints are non-negotiable.

## The pipeline at a glance

```mermaid
flowchart LR
    A["<b>Godot editor</b><br/>+ PS1Godot plugin<br/>C# / .NET<br/><i>you author here</i>"]
    B["<b>psxsplash runtime</b><br/>C++ on psyqo<br/><i>forked + heavily extended</i>"]
    C["<b>PCSX-Redux</b><br/>or real PS1"]
    A -->|splashpack .bin<br/>(binary scene)| B
    B -->|MIPS ELF| C
```

Three independent pieces:

- **The editor plugin** (`godot-ps1/addons/ps1godot/`) — C#/.NET,
  runs inside Godot. Provides custom nodes, an exporter, the
  Run-on-PSX one-click flow, the PS1Lua script-language extension,
  in-editor authoring docks (Graph, Doctor, VRAM viewer, UI canvas
  editor, etc.).
- **The runtime** (`psxsplash-main/`) — C++ on top of
  [psyqo](https://github.com/grumpycoders/pcsx-redux/tree/main/src/mips/psyqo),
  cross-compiled to MIPS. Loads a splashpack binary and plays the
  game. **Forked-in-practice from upstream
  [psxsplash](https://github.com/psxsplash/psxsplash) and heavily
  extended** — see [the divergence section below](#how-much-weve-diverged-from-upstream-psxsplash).
- **The emulator** (PCSX-Redux) — standard PS1 emulator with the
  PCdrv backend, which lets the runtime read files from the host
  filesystem during iteration. For ISO / real-hardware builds, the
  runtime is rebuilt with `LOADER=cdrom` and packaged with
  `mkpsxiso`.

The boundary between the three is the **splashpack binary format**.
Everything else is implementation detail you can change in one piece
without touching the others.

## The splashpack binary format

This is the integration contract. The format is documented in detail
at [`reference/splashpack-format.md`](../reference/splashpack-format.md).

Current version: **v32**. The loader hard-asserts `version >= 32`;
older exports won't load. The exporter side is in
`godot-ps1/addons/ps1godot/exporter/SplashpackWriter.cs`; the loader
side is in `psxsplash-main/src/splashpack.{hh,cpp}`.

Three files per export, all written alongside each other:

| File | Contents |
|---|---|
| `scene.splashpack` | Header + live scene structures (meshes, colliders, BVH, nav, Lua, UI, cutscenes, skin data) |
| `scene.splashpack.vram` | Texture atlas pixels + CLUTs + UI font pixels — DMA'd into PS1 VRAM |
| `scene.splashpack.spu` | Audio ADPCM bulk data — DMA'd into PS1 SPU RAM |

Splitting by destination memory lets the runtime fire each blob at
the right hardware without parsing it byte-by-byte. The
`.splashpack` references offsets into the other two.

### When you change the format

Each bump appends to the end of the header so older exports parse
fine up to their version count. Two exceptions (v30 and v31) did
mid-section reshuffles for size wins that justified the rewrite cost
— don't follow those unless your bump's savings are comparable.

A `static_assert` in `splashpack.hh` locks the C++ struct size to
the on-disk byte count; the Godot writer must produce bytes that
match. If you change one side without the other, the static_assert
fails at compile time on the runtime side, or the loader silently
mis-parses fields shifted by your change. Bake a fixture test on
the C# side too — `SplashpackWriter` has unit tests with frozen
byte counts.

## Repo layout

```
godot-master/             vendored Godot 4.x source — reference only,
                          day-to-day work uses prebuilt Godot Mono editor
pcsx-redux-main/          PCSX-Redux + psyqo + MIPS tooling — read-only reference
psxsplash-main/           PS1-side C++ runtime — FORK, heavily extended past upstream
splashedit-main/          Original Unity plugin — read-only reference, what we are replacing
godot-ps1/                THIS IS THE PROJECT — Godot 4 .NET project
  addons/ps1godot/        plugin C# code (exporter, nodes, docks, tools)
  addons/ps1godot/
    scripting/            PS1Lua GDExtension (C++), godot-cpp bindings
  demo/                   demo scene + assets shipped with releases
  lua/                    Lua scripts attached to demo nodes
docs/                     this docs site
scripts/                  Python launchers (run.py + .cmd/.sh shims)
                          and build helpers (build-release.py, etc.)
```

Reference trees (`godot-master`, `pcsx-redux-main`, `splashedit-main`)
are read-only — kept around for browsing source, not modified.

`psxsplash-main` started as a vendor of upstream `psxsplash` but
has diverged enough to qualify as a fork in practice. We extend it
freely; upstream-compatibility isn't a goal. See the "divergence"
section at the bottom of this page for what changed.

## Language and tooling decisions

- **Plugin, not engine fork.** Godot's `EditorPlugin`,
  `CompositorEffect`, `EditorImportPlugin`, `ResourceFormatSaver`,
  and `ScriptLanguageExtension` cover everything we need. A fork
  would mean perpetual merge conflicts against a 1M-LOC codebase for
  no concrete capability we can't already hit. We re-open the
  question only when we hit a specific wall.
- **Plugin language: C#.** SplashEdit's exporter logic (texture
  quantization, VRAM packing, binary writer, ADPCM conversion, BVH
  build) ports line-by-line from its existing C#. GDScript is too
  slow for bit-level work.
- **GDExtension (C++) reserved for hot paths.** Phase 4. Shares a
  language family with psxsplash, so code can be shared if useful.
  Currently used for PS1Lua only.
- **Lua is a first-class Godot script** via
  `ScriptLanguageExtension`, not a bolt-on asset format. Attaches
  directly to nodes like GDScript or C# would.
- **Godot version: 4.7-dev** at the moment; minimum supported is
  **4.4** (for `CompositorEffect`). The C# project pins
  `Godot.NET.Sdk/4.7.0-dev.5`, which is a pre-release SDK only
  available inside the matching Godot install — `NuGet.Config`
  surfaces it via the `GODOT_NUPKGS` env var so Rider/CLI restore
  works.
- **MIPS toolchain:** `mipsel-none-elf` on Windows via the `mips.ps1`
  script shipped in pcsx-redux; `gcc-mipsel-linux-gnu` on Linux.
  Needed only when rebuilding `psxsplash` itself.

## Constraints that aren't negotiable

These are baked into the PS1 hardware. If a design step would
violate one, the design needs to change.

- **Vertex positions are fixed-point.** `psyqo::FixedPoint<12, ...>`,
  16-bit signed integer part. Floats don't exist at runtime. The GTE
  (geometry transform engine) is fixed-point all the way through.
- **Textures are paletted or 16bpp direct.** 4bpp (16 colors), 8bpp
  (256 colors), or 16bpp BGR555. Anything bigger gets quantized at
  export — see `addons/ps1godot/exporter/TexturePacker.cs`.
- **VRAM is 1 MB, arranged as 1024×512 16-bit pixels.** Framebuffers
  + texture pages + CLUTs all live in the same space. Run out and
  the next allocation fails the export gate.
- **SPU RAM is 512 KB.** Audio clips are ADPCM-encoded (~3.5:1
  compression vs PCM). The exporter packs them, the loader DMAs
  them in.
- **The screen is 320×240.** UI canvases are authored against that
  reference. Internal rendering happens at native resolution; the
  editor can preview at upscaled resolutions but the runtime
  doesn't.

The exporter enforces these via the **Doctor** validator dock and
the export gate in `Run on PSX`. Adding a new node kind that doesn't
respect these constraints lands a Doctor warning at minimum, an
export-block at worst.

## Conventions

- **Don't edit the read-only reference trees** (`godot-master`,
  `pcsx-redux-main`, `splashedit-main`). They're there to browse
  source, not to fork. `psxsplash-main` is the exception — that
  one's our fork and edits land directly.
- **Splashpack format is the contract.** Any change to the writer
  requires a matching reader change (or a version bump + compat
  branch in the loader). Bake the struct sizes into the C# tests.
- **PS1 constraints are non-negotiable.** Flag any design step that
  ignores them; don't paper over.
- **Don't over-abstract.** Mirror SplashEdit's structure rather than
  re-architecting, at least through Phase 2. SplashEdit was one
  person's project that grew; copying its shape minimizes friction
  when porting logic.
- **Conventional commits** + small focused commits. See
  [`CONTRIBUTING.md`](https://github.com/BuffJesus/PS1Godot/blob/main/CONTRIBUTING.md){ target="_blank" }
  if it exists; otherwise look at recent commit messages on `main`
  for the project's style.

## Where to read what

| Topic | File |
|---|---|
| Splashpack format details | [`reference/splashpack-format.md`](../reference/splashpack-format.md) |
| Upstream psxsplash improvements | [`reference/psxsplash-improvements.md`](../reference/psxsplash-improvements.md) |
| Custom nodes' source | `godot-ps1/addons/ps1godot/nodes/*.cs` |
| Exporter (Godot → splashpack bytes) | `godot-ps1/addons/ps1godot/exporter/SplashpackWriter.cs` |
| Lua runtime bindings | `psxsplash-main/src/luaapi.{cpp,hh}` |
| psyqo primitives (GTE, fixed-point, GPU prims) | `pcsx-redux-main/src/mips/psyqo/` |
| Plugin's authoring docks | `godot-ps1/addons/ps1godot/ui/PS1*Dock.cs` |
| CI workflows | [`contributing/ci.md`](ci.md) |

## Phase status (snapshot)

The project's roadmap is split into phases:

- **Phase 1** — Godot-side PS1 visual look (vertex jitter, fog,
  CLUT-style shading). Done.
- **Phase 2** — Splashpack exporter MVP. Done. The Godot demo
  exports a valid splashpack, boots in PCSX-Redux, and plays an
  interactive scene with cutscenes, music, dialog, third-person
  camera, sequenced BGM, multi-scene teleport, portal-culled
  interiors, and skinned meshes. v32 of the format. One nav-region
  improvement still pending.
- **Phase 3** — Authoring quality-of-life. F5/Run-on-PSX,
  Doctor validator, Graph authoring (Dialogue/FSM/Quest/BT),
  VRAM viewer dock, WYSIWYG UI canvas editor, in-editor Lua REPL.
  In progress; most major features have landed.
- **Phase 4+** — Real-hardware flash testing, USB transport,
  bench-mode protocol.

The live source of truth is
[`ROADMAP.md`](https://github.com/BuffJesus/PS1Godot/blob/main/ROADMAP.md){ target="_blank" };
this page intentionally summarizes rather than tracks day-to-day
status.

## How much we've diverged from upstream psxsplash

The directory's still called `psxsplash-main/` but the codebase is
a meaningful fork. Highlights of what's here that isn't upstream:

### Splashpack format

- Upstream still emits **v20**. We're at **v32** — twelve format
  bumps between 2026-04-20 and 2026-04-27 alone. We're the sole
  consumer of v21+.
- v21 split the export into three files (`scene.splashpack` +
  `.vram` + `.spu`) so the runtime can DMA each blob into the
  right hardware without parsing.
- v22–v32 layered: sequenced music + per-channel pitch shifting,
  UI 3D-model HUD widgets, audio routing (SPU / XA / CDDA / PS2M),
  XA sidecar table, scene-wide instrument banks, sound macros +
  sound families, quaternion-encoded skin animation poses (v30
  saved ~42% on the skin section), vertex-pool MeshBlob (v31 saved
  ~50% on static meshes), separated background tone + explicit fog
  near/far in GTE-Z space (v32).

### Runtime features

Net new beyond upstream:

- **Lua scripting** integrated through `psyqo-lua`, with the
  `luaapi.{hh,cpp}` exposing 24 namespaces / 145 entries — Audio,
  Camera, Dialog, Entity, FSM, Music, Persist, Quest, Scene,
  SkinnedAnim, UI, etc. None of this is in upstream.
- **Sequenced music** via the PS2M binary format + a per-channel
  pitch-shift runtime + voice reservation that keeps dialog from
  stealing music notes.
- **Sound macros + sound families** — multi-clip variation
  dispatch.
- **Cutscenes** with multi-track camera + object + audio
  timelines.
- **Skinned meshes** with per-frame baked poses.
- **Portal-culled rooms** for interior scenes.
- **Multi-scene** support via `Scene.Load(N)`.
- **Audio-aware dialog auto-hide** via `Audio.GetClipDuration`.
- **CDDA path** end-to-end + XA scaffolding for streaming.

### Editor side

The Godot plugin side has no upstream counterpart at all — it's a
ground-up rethink of the original SplashEdit Unity plugin. So:

- The whole PS1Graph editor (Dialogue / FSM / Quest / BT).
- PS1 Doctor validator + the export-gate.
- VRAM Viewer, Audio Routing, Quest Journal, Lua REPL, Lua API
  Cheatsheet, Graph Find, References docks.
- WYSIWYG UI Canvas editor.
- F5 / Run-on-PSX one-click flow.
- PS1Lua as a first-class Godot script language via
  `ScriptLanguageExtension`.
- ISO build pipeline (`tools/build_iso/build_iso.py`).

### Sync posture

We don't pull from upstream. If they ship something genuinely
useful (a real-hardware bug fix, a psyqo update), we cherry-pick;
if not, the divergence keeps growing. There's no formal merge cadence.

Anyone working in `psxsplash-main/` should treat it the same way
as the editor plugin — direct edits, conventional commits, no
need to think about upstream compatibility unless the change is
genuinely a portable bug fix.
