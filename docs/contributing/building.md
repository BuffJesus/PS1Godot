# Building from source

Three independent artifacts get built on a developer machine. Most
contributors only need to build the one they're modifying. The CI
matrix builds everything on every push so missing local toolchains
is fine until your change actually needs them.

| Artifact | Built from | Required toolchain | Output |
|---|---|---|---|
| **PS1Lua GDExtension** | `godot-ps1/addons/ps1godot/scripting/` | Python + SCons + a C++ compiler | `build/libps1lua.<platform>.<target>.<ext>` |
| **C# plugin** | `godot-ps1/` (root `.csproj`) | .NET 8 SDK + Godot install (for the SDK nupkgs) | `.godot/mono/temp/bin/.../<asm>.dll` |
| **psxsplash runtime** | `psxsplash-main/` | `mipsel-*-gcc` + GNU make | `psxsplash.ps-exe` (PCdrv) or `psxsplash.elf` + ISO (CDROM) |

The release zip combines all three. See `scripts/py/build-release.py`.

## PS1Lua GDExtension

The GDExtension exposes Lua as a first-class Godot script language —
syntax highlighting, autocomplete, the works. Most users get the
prebuilt binaries with the plugin zip (see
[releases](https://github.com/BuffJesus/PS1Godot/releases)). You only
need to build it from source if you're modifying the extension's
C++ code or want a debug build for breakpoints.

### Prerequisites

```bash
pip install scons
```

Plus a C++ compiler:

- **Windows** — Visual Studio Build Tools (MSVC) or MinGW
- **Linux** — `gcc`/`g++` (any reasonably modern version)
- **macOS** — Xcode Command Line Tools (`xcode-select --install`)

### godot-cpp checkout

The first build needs godot-cpp (Godot's C++ binding generator).
It's not in the repo because the source tree is ~1 GB. Clone it
into the `lib/` directory under the scripting tree, pinned to the
exact SHA CI uses (see
[`contributing/ci.md`](ci.md) for the current pin and how to bump):

```bash
cd godot-ps1/addons/ps1godot/scripting
mkdir -p lib
git clone https://github.com/godotengine/godot-cpp.git lib/godot-cpp
git -C lib/godot-cpp checkout 4862a9dcf1471c9ea19680b9faadb5b6a9432092
```

### Build

```bash
cd godot-ps1/addons/ps1godot/scripting
scons platform=<windows|linux|macos> target=<editor|template_debug|template_release> -j8
```

First build is slow (~10 minutes, mostly compiling godot-cpp's
generated bindings). Subsequent rebuilds touch only your changes —
incremental compile is ~5 seconds for typical edits.

Three `target` values exist:

- `editor` — Built with editor-runtime symbols. Loads inside the
  Godot editor for in-editor scripting + autocomplete. The
  most-edited target during development.
- `template_debug` — Goes into exported game builds with debug
  symbols + the Godot remote debugger.
- `template_release` — Stripped, optimized, ships in release
  exports.

Output lands at
`godot-ps1/addons/ps1godot/scripting/build/libps1lua.<platform>.<target>.<ext>`.
The `.gdextension` manifest in the same dir tells Godot which file
to load per platform + target combination.

### Re-running the autocomplete generator

SCons auto-runs `gen_api_data.py` against
`psxsplash-main/src/luaapi.hh` on every build to produce
`src/ApiData.gen.cpp`. You don't invoke it manually unless you want
to inspect the output — change `luaapi.hh` and the next `scons` pass
picks it up.

## C# plugin

The C# plugin is the editor-side authoring surface — custom nodes,
exporter, docks, the Run-on-PSX button. Godot builds it
automatically when you open the project; you only need to drive
`dotnet build` directly for headless CI or troubleshooting.

### Prerequisites

- **.NET 8 SDK** — `dotnet --version` reports `8.x.y` after install.
- **Godot install matching the pinned SDK** — the project uses
  `Godot.NET.Sdk/4.7.0-dev.5`, which only ships inside the Godot
  install at `GodotSharp/Tools/nupkgs/`. The `NuGet.Config` at the
  repo root references `$(GODOT_NUPKGS)`, an environment variable
  that must point at that folder.

```bash
# Replace with the path inside YOUR Godot install
export GODOT_NUPKGS=/path/to/Godot/GodotSharp/Tools/nupkgs
# Windows PowerShell:
$env:GODOT_NUPKGS = "C:\path\to\Godot\GodotSharp\Tools\nupkgs"
```

If a `dotnet restore` errors with `NU1101: Unable to find package
Godot.NET.Sdk`, `GODOT_NUPKGS` isn't set or points at the wrong
location.

### Build

```bash
cd godot-ps1
dotnet restore
dotnet build --no-restore --nologo --verbosity minimal
```

The Godot editor does the same thing under the hood when you hit
the hammer icon. Errors land in the editor's Output dock and in
`dotnet build`'s stdout, so prefer whichever has the better signal
for the problem you're chasing.

## psxsplash runtime

The runtime is the C++ side that runs on the PS1 hardware (or
PCSX-Redux). You only need to build it from source if you're
modifying `psxsplash-main/src/` — for everything else, the prebuilt
ELF in `dist/psxsplash-runtime-*.zip` is fine.

### Prerequisites

A MIPS cross-compiler:

- **Linux** — `apt install gcc-mipsel-linux-gnu binutils-mipsel-linux-gnu`
- **Windows** — the PCSX-Redux `mips.ps1` script (installs
  `mipsel-none-elf-gcc` via MSYS2-ish layout)
- **macOS** — `brew tap pcsx-redux/mips && brew install mips`

Plus GNU `make` (any recent version).

### Build

```bash
cd psxsplash-main
make
# or, for ISO / real-hardware builds:
make LOADER=cdrom
```

Output:

- **Default (PCdrv)** — `psxsplash.ps-exe`. The runtime reads scene
  files from the host filesystem via PCSX-Redux's PCdrv backend.
  Faster iteration — no ISO rebuild between exports.
- **`LOADER=cdrom`** — same `psxsplash.ps-exe`, built to read from
  a CD-ROM image. Needed for real-hardware testing and ISO builds.

The launcher (`scripts/run.py build-psxsplash`) wraps `make` with
the right `PREFIX=` for your platform's MIPS toolchain so you don't
need to remember whether your system has `mipsel-none-elf-gcc` or
`mipsel-linux-gnu-gcc`.

```bash
python scripts/run.py build-psxsplash
python scripts/run.py build-psxsplash --loader=cdrom
```

### Building an ISO

`tools/build_iso/build_iso.py` packages a CDROM-built runtime + an
exported splashpack into a BIOS-bootable `.bin/.cue` pair via
[mkpsxiso](https://github.com/Lameguy64/mkpsxiso). Install mkpsxiso
on your PATH and run:

```bash
python tools/build_iso/build_iso.py path/to/scene.splashpack -o game.cue
```

Output `.bin/.cue` boots on real PS1 hardware and in PCSX-Redux's
ISO mode.

## Putting it together: release zips

`scripts/py/build-release.py <version>` packages a version into two
zip files under `dist/`:

```bash
python scripts/py/build-release.py v0.5.1
```

Produces:

- **`PS1Godot-plugin-<version>.zip`** — the plugin tree under
  `addons/ps1godot/`, including the prebuilt GDExtension binaries
  from `scripting/build/`. The script excludes godot-cpp source,
  intermediate `.obj` files, and SCons scratch files via substring
  matches on the archive path (see the `PLUGIN_EXCLUDE_SUBSTRINGS`
  list).
- **`psxsplash-runtime-<version>.zip`** — whichever `psxsplash.ps-exe`
  variants exist (PCdrv build, CDROM build, or both). Users who
  don't want to install the MIPS toolchain can drop these into
  their workspace and skip the runtime build step.

### Release flow

The full flow when cutting a tag is documented in [`ci.md`](ci.md);
the short version is:

1. Tag commits get the GDExtension matrix run automatically.
2. Download each artifact zip from CI; extract its single binary
   into `godot-ps1/addons/ps1godot/scripting/build/`.
3. Run `build-release.py <version>`.
4. Attach `dist/PS1Godot-plugin-<version>.zip` and
   `dist/psxsplash-runtime-<version>.zip` to the GitHub Release.

## Troubleshooting

**SCons can't find godot-cpp.**
The `lib/godot-cpp/` clone is missing or empty. See "godot-cpp
checkout" above.

**`scons` reports "compatibility version 4.4 not supported".**
godot-cpp checkout is on the wrong branch. The pinned SHA targets
the `godot-4.4-stable` branch line; verify with
`git -C lib/godot-cpp log -1 --format=%H`.

**`dotnet restore` fails with NU1101 on Godot.NET.Sdk.**
`GODOT_NUPKGS` isn't set, or points at the wrong directory.
Should be the `nupkgs/` folder inside the Godot install,
specifically the path that contains `Godot.NET.Sdk.<version>.nupkg`.

**Make reports "mipsel-none-elf-gcc: command not found".**
The MIPS toolchain isn't on PATH, or your platform uses a different
prefix. Either install via the prerequisites above, or invoke
through `scripts/run.py build-psxsplash` which auto-detects.

**Runtime builds but boots to a black screen in PCSX-Redux.**
If you built `LOADER=cdrom` but are running the PCdrv flow, the
runtime is looking for files on a non-existent CD. Rebuild without
the `LOADER=cdrom` (default is PCdrv) and re-launch.
