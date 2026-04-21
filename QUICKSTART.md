# PS1Godot — quickstart

**What this is:** a Godot plugin that lets you author PlayStation 1 games
in the Godot editor and run them on actual PS1 hardware (or PCSX-Redux).
Scenes, meshes, textures, Lua scripting, audio, UI, and MIDI-driven
background music all pack into a "splashpack" binary that the
[psxsplash](https://github.com/psxsplash/psxsplash) runtime loads and plays.

It's a Godot-native rethink of [SplashEdit](https://github.com/psxsplash/splashedit)
(which is a Unity plugin for the same runtime). Same binary format,
different editor, better authoring UX.

**Status:** Phase 2 complete. You can author a full PS1 scene (meshes with
CLUT textures, colliders, nav, interactive objects, UI canvases, cutscenes,
skinned meshes, dialog-driven Lua scripts, and sequenced BGM) in Godot, hit
one button, and see it running in PCSX-Redux. See [`ROADMAP.md`](ROADMAP.md)
for what's shipped vs. pending.

---

## Try it in ~15 minutes

You'll need Windows 10/11 for the 1-click path. (Linux/macOS is viable —
all the parts are cross-platform — but the launcher scripts are `.cmd`
files. Contributions welcome.)

### 1. Install the four tools

- **[Godot 4.7.x .NET build](https://godotengine.org/download/)** (the C#
  / Mono variant, not the default). Extract to `D:\Programs\Godot\` or
  wherever — you'll point the launcher at it in step 3.
- **[.NET 8 SDK](https://dotnet.microsoft.com/download)**. Verify with
  `dotnet --version`.
- **MIPS toolchain** (cross-compiler for the PS1):
  ```powershell
  powershell -c "& { iwr -UseBasicParsing https://bit.ly/mips-ps1 | iex }"
  ```
  Then in a **new** terminal: `mips install 15.2.0`.
- **[PCSX-Redux](https://distrib.app/pub/org/pcsx-redux/project/dev-win-x64)**.
  Extract to `C:\tools\pcsx-redux\`.

### 2. Get the code

**Two paths depending on what you want to do:**

**Path A — "I just want to use the plugin in my own Godot project":**
grab the latest zips from the
[Releases page](https://github.com/BuffJesus/PS1Godot/releases):

1. Download `PS1Godot-plugin-<version>.zip`, extract into your Godot
   project's `addons/` folder, and enable **PS1Godot** in
   **Project → Project Settings → Plugins**.
2. Download `psxsplash-runtime-<version>.zip` and drop `psxsplash.ps-exe`
   into your project's `build/` folder so the Run-on-PSX button has a
   runtime to launch.

You can skip straight to step 5 with this path. The MIPS toolchain is
only needed if you want to modify the PS1-side runtime yourself.

**Path B — "I want to clone the full repo with the demo, tests, and
tools":**

```bash
git clone --recursive https://github.com/BuffJesus/PS1Godot.git
cd PS1Godot
```

`--recursive` matters — psxsplash has git submodules (`nugget`,
`psyqo-lua`). Missing them silently breaks the runtime build.

### 3. Wire up environment variables

From the repo root:

```bat
scripts\bootstrap-env.cmd
```

This sets `GODOT_EXE`, `GODOT_NUPKGS`, and `PCSX_REDUX_EXE`. Close and
reopen terminals so `setx` values take effect. If your install paths
differ from the defaults, edit the script or set the three vars yourself
(see [`SETUP.md`](SETUP.md)).

### 4. Build the PS1 runtime

```bat
scripts\build-psxsplash.cmd
```

Cross-compiles the C++ runtime with the MIPS toolchain and drops the
binary at `godot-ps1\build\psxsplash.ps-exe`. Takes 30–60s on first run.

### 5. Open Godot, export the demo, see it running

```bat
scripts\launch-editor.cmd
```

First open takes ~60s (C# assembly build + asset import). When the
Output dock shows `[PS1Godot] Plugin enabled.`:

1. Open **`demo/demo.tscn`** in the FileSystem dock.
2. Find the **PS1Godot** panel (dockable — bottom-right by default).
3. Click the big red **▶ Run on PSX** button.

The plugin exports a splashpack, launches PCSX-Redux, and loads it. You
should see an intro cutscene with narration, land next to a spinning
green cube, and hear sequenced music playing. Press **Triangle** next
to either cube to interact, walk through the portal-culled room in the
distance, or the teleport trigger to hop to a second scene.

---

## What's in the demo

| Feature | How it's wired |
|---|---|
| PS1 shader (vertex jitter, 2× color modulate, affine warp, fog) | `addons/ps1godot/shaders/ps1.gdshader` on every mesh |
| Custom nodes | `PS1Scene`, `PS1MeshInstance`, `PS1SkinnedMesh`, `PS1Player`, `PS1Room`, `PS1PortalLink`, `PS1AudioClip`, `PS1MusicSequence`, `PS1TriggerBox`, `PS1UICanvas`, `PS1Animation`, `PS1Cutscene`, … |
| Lua scripting | Drop `.lua` on any node; PS1Lua is a first-class Godot script language |
| Rooms + portals | Two rooms + a portal link between them; runtime culls everything you can't see through the portal |
| Cutscenes | Multi-track timelines: camera position + rotation tracks, object animation, synced voice clips |
| Sequenced music | Drop a `.mid` + short sample WAVs; bind each MIDI channel to a sample; `Music.Play("song")` from Lua |
| Dialog | UI canvases with Triangle-to-interact; audio-aware auto-hide (box stays up until the voice clip actually finishes); auto-ducks music while on screen |
| Multi-scene | `Scene.Load(N)` swaps between scenes packed together — demo has a "checkered realm" reachable via a teleport trigger |

The intro cutscene + the music are both MVP features that landed in the
sequenced-music session. The details of that format are documented in
[`docs/sequenced-music-format.md`](docs/sequenced-music-format.md).

---

## Authoring your own scene

Once the demo runs, two tutorials get you building your own:

**→ [`docs/tutorial-hello-cube.md`](docs/tutorial-hello-cube.md)** —
look-and-feel: set up a PS1-shaded scene in Godot's viewport, see the
jitter and fog and color clamp working.

**→ [`docs/tutorial-basic-scene.md`](docs/tutorial-basic-scene.md)** —
interactive slice of the demo: floor + third-person player + a cube you
walk up to and press Triangle on to trigger a Lua-driven dialog line.
Ends with a working splashpack booting in PCSX-Redux.

And the pipeline architecture / file-layout conventions live in
[`CLAUDE.md`](CLAUDE.md) — worth a skim before modifying anything
substantial.

---

## Troubleshooting

**"PCSX-Redux boots to a black screen and doesn't load the game."** Your
runtime binary at `godot-ps1\build\psxsplash.ps-exe` is probably stale
against the current splashpack format. Re-run `scripts\build-psxsplash.cmd`.

**"C# build failed" when launching Godot.** Install the .NET 8 SDK and
verify `dotnet --version` prints `8.x.x`. Restart terminals afterwards.

**`NU1102: Unable to find package Godot.NET.Sdk`.** `GODOT_NUPKGS` isn't
set or points at the wrong folder. It must be
`<GodotInstall>\GodotSharp\Tools\nupkgs`. Fix with `setx` and reopen
terminals. Full detail in [`SETUP.md`](SETUP.md).

**Music plays but WAVs sound muffled / wrong.** Godot 4.4+ defaults WAV
imports to lossy QOA compression; the splashpack needs raw PCM. The
project overrides this in `project.godot` (`[importer_defaults] wav =
{ "compress/mode": 0 }`) so new WAV drops come in uncompressed — but
existing `.wav.import` files from before the override need to be
re-imported (delete the cached `.sample` blob in `.godot/imported/` and
reopen the project).

More troubleshooting in [`SETUP.md`](SETUP.md). If something's busted and
isn't listed, file an issue or ping me.

---

## Full setup detail

Everything above is the short path. If you're setting up a dev machine
or hitting something weird, [`SETUP.md`](SETUP.md) is the complete
walkthrough with environment-variable tables and Phase 0 verification
checklist.

🤖 Parts of this codebase and documentation are AI-assisted
(Claude Code). The music sequencer pipeline in particular was built
in a single session — see the recent commit log for the audit trail.
