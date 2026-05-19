# Development environment setup

Goal: from a fresh clone, be able to edit PS1 scenes in Godot, build the
psxsplash runtime, and iterate in PCSX-Redux with a one-click loop.

Target time to boot: **~15 minutes** for a new machine.

**Supported platforms:** Windows, Linux, macOS. The launcher
(`scripts/run.py`) is Python-based and works on all three; the
`.cmd` / `.sh` shims wrap it for muscle memory. See
[`docs/internal/rfc/linux-support.md`](https://github.com/BuffJesus/PS1Godot/blob/main/docs/internal/rfc/linux-support.md){ target="_blank" } for the
underlying design.

## Prerequisites

| Tool | Purpose | Windows | Linux | macOS |
|------|---------|---------|-------|-------|
| Godot **.NET / Mono** build (pinned: **4.7.0-dev.5**) | Editor + plugin | [godotengine.org/download](https://godotengine.org/download/) | same — pick the Linux .NET tarball | same — pick the macOS .NET universal |
| .NET 8 SDK | C# compilation | [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download) | `apt install dotnet-sdk-8.0` (or [.deb / .rpm](https://dotnet.microsoft.com/download)) | `brew install --cask dotnet-sdk` |
| **Python 3.10+** | Launcher (`scripts/run.py`) | [python.org](https://www.python.org/) | system python3 or `apt install python3` | system python3 or `brew install python` |
| MIPS toolchain | Cross-compile psxsplash | PCSX-Redux `mips.ps1` (`mipsel-none-elf-gcc`) | `apt install gcc-mipsel-linux-gnu binutils-mipsel-linux-gnu` | `brew tap pcsx-redux/mips && brew install mips` |
| GNU `make` | Build psxsplash | MSYS2 or Git Bash | `apt install build-essential` | comes with Xcode CLT |
| PCSX-Redux | Run PS1 code | [dev-win-x64 zip](https://distrib.app/pub/org/pcsx-redux/project/dev-win-x64) | [AppImage / .deb](https://github.com/grumpycoders/pcsx-redux/releases) | [.dmg](https://github.com/grumpycoders/pcsx-redux/releases) |
| `mkpsxiso` (Phase 3+) | Real-hardware ISO | [GitHub releases](https://github.com/Lameguy64/mkpsxiso) | same | same — or `brew install mkpsxiso` if a tap exists |
| JetBrains Rider | IDE + C# debug | JetBrains Toolbox | JetBrains Toolbox | JetBrains Toolbox |

`scripts/run.py` auto-detects either MIPS prefix (`mipsel-none-elf-`
on Windows/macOS, `mipsel-linux-gnu-` on Debian/Ubuntu) and passes
the matching `PREFIX=...` to `make`, so contributors don't need to
patch the Makefile.

## One-time setup

### 1. Install Godot .NET + .NET SDK

1. Download the Godot .NET build (currently pinned: **4.7.0-dev.5**).
2. Extract anywhere — set `GODOT_EXE` to the executable's full path:
   - **Windows:** `setx GODOT_EXE "D:\Programs\Godot...\Godot_v4.7-dev5_mono_win64.exe"`
   - **Linux:** `export GODOT_EXE=~/.local/bin/godot` in `~/.bashrc` (or wherever you put it).
   - **macOS:** `export GODOT_EXE=/Applications/Godot.app/Contents/MacOS/Godot` in `~/.zshrc`.
3. Install the .NET 8 SDK if you don't already have it. Verify with
   `dotnet --version` → should print `8.x.x`.
4. **Inspect what the launcher resolves** — prints the current values
   and flags anything missing:
   ```bash
   python scripts/run.py bootstrap-env
   ```
   On Windows you can also run the `.cmd` shim
   (`scripts\bootstrap-env.cmd`); on Linux / macOS the `.sh` shim
   (`./scripts/bootstrap-env.sh`). Both route through the same Python
   entry point.

> **Why GODOT_NUPKGS matters.** The 4.7-dev.5 SDK is not on nuget.org; it
> ships inside the Godot install at `GodotSharp\Tools\nupkgs\`.
> `godot-ps1\NuGet.Config` references `%GODOT_NUPKGS%` so Rider and CLI
> `dotnet build` find it. Without it, you'll see
> `error NU1102: Unable to find package Godot.NET.Sdk with version (>= 4.7.0-dev.5)`.
> Godot itself injects the path internally, so opening in the editor works
> even without the env var — but any build outside the editor won't.

### 2. Install the MIPS toolchain (for building psxsplash)

Pick the path for your OS:

**Windows.** From any PowerShell prompt (not admin — user-scope install only):

```powershell
powershell -c "& { iwr -UseBasicParsing https://bit.ly/mips-ps1 | iex }"
```

Installs silently to `%APPDATA%\mips\`. After install, open a new
terminal, then `mips install 15.2.0` (or whatever `mips ls-remote`
recommends — psxsplash's Makefile has no version pin). Verify with
`mipsel-none-elf-gcc --version`.

If you'd rather install elsewhere, download `mips.ps1` first then run
`powershell -ExecutionPolicy Unrestricted -File mips.ps1 self-install
D:\Programs\mips`. The path must have **no spaces** (installer constraint).

If `make` isn't on PATH, install [MSYS2](https://www.msys2.org/) and
add `C:\msys64\usr\bin`, or use Git Bash which ships with make.

**Linux (Debian / Ubuntu).** The distro toolchain works as-is:

```bash
sudo apt install gcc-mipsel-linux-gnu binutils-mipsel-linux-gnu build-essential
```

Verify with `mipsel-linux-gnu-gcc --version`. Other distros: install the
equivalent (`mipsel-linux-gnu-gcc-*`, `binutils-mipsel-linux-gnu-*`).
`scripts/run.py` auto-detects either the `none-elf` or `linux-gnu` prefix
and passes `PREFIX=mipsel-linux-gnu` to make so no Makefile edit is
needed.

**macOS.** The PCSX-Redux project maintains a Homebrew tap with the
same `mipsel-none-elf-gcc` binary the Windows installer ships:

```bash
brew tap pcsx-redux/mips
brew install mips
```

`make` ships with Xcode CLT (`xcode-select --install`).

### 3. Install PCSX-Redux

- **Windows.** Download `pcsx-redux-HEAD-win-x64.zip` from the
  [distrib.app page](https://distrib.app/pub/org/pcsx-redux/project/dev-win-x64).
  Extract anywhere; set `PCSX_REDUX_EXE` to the full `.exe` path.
- **Linux.** Grab the AppImage or `.deb` from
  [GitHub releases](https://github.com/grumpycoders/pcsx-redux/releases).
  Either chmod+x the AppImage and `export PCSX_REDUX_EXE=/path/to/pcsx-redux.AppImage`,
  or `dpkg -i` the `.deb` and let `run.py` find `pcsx-redux` on PATH.
- **macOS.** Install the `.dmg` from GitHub releases; the binary lives
  at `/Applications/PCSX-Redux.app/Contents/MacOS/pcsx-redux`. That's
  the path `run.py` checks by default — no env var needed if you used
  the standard install location.

First run on any OS: set **OpenBIOS** in `Configuration → Emulation`.
OpenBIOS is bundled; use it to avoid retail-BIOS copyright issues.

### 4. Install Rider + Godot plugin

1. In Rider: **Settings → Plugins → Marketplace**, search for **Godot Support**
   (by JetBrains), install, restart.
2. Open `godot-ps1\PS1Godot.sln` in Rider. The plugin auto-detects the
   `project.godot` sibling and offers a **Godot Editor** run configuration.
3. The generated run configuration launches Godot's editor. To get
   F5-launch-and-debug working:
   - Add a second configuration of type **.NET Executable** pointed at
     `%GODOT_EXE%` with argument `--path $ProjectFileDir$ --remote-debug tcp://127.0.0.1:23685`.
   - Or simpler: use the Godot plugin's own "Play" button once the
     `PS1GodotPlugin` is enabled in Godot.

### 5. Configure the external editor for Lua scripts

Godot's built-in editor opens `.lua` files as plain text — no
highlighting or completion. Point Godot at Rider (or VS Code, or your
preferred editor) so double-clicking a Lua script pops it open with
full language support, and run the API-stub generator so the external
editor picks up PS1Godot-specific completions.

See [`reference/lua-editor-setup.md`](../reference/lua-editor-setup.md) for the
step-by-step — takes about 5 minutes.

## Per-clone bootstrap

From the workspace root, any of the equivalent invocations:

```bash
# Cross-platform canonical form:
python scripts/run.py launch-editor

# Or the platform-native shim:
scripts\launch-editor.cmd        # Windows
./scripts/launch-editor.sh       # Linux / macOS
```

First launch takes 30–60s while Godot imports assets and builds C#.
Expected output in Godot's Output dock: `[PS1Godot] Plugin enabled.`

Then open `demo/demo.tscn` — you should see a jittering cube on a flat floor
with nearest-neighbor rendering. That's the PS1 shader working.

## Phase 0 verification

When all of the below succeed, you're ready for Phase 1 preview work and
Phase 2 exporter work. Substitute the platform-appropriate shim
(`launch-editor.cmd` on Windows, `./launch-editor.sh` on Linux/macOS)
or always use the canonical `python scripts/run.py <action>` form.

- [ ] `python scripts/run.py launch-editor` opens Godot. Plugin is
      listed as enabled in **Project → Project Settings → Plugins**.
- [ ] `demo/demo.tscn` loads and renders with visible vertex snapping.
- [ ] `python scripts/run.py build-psxsplash` produces
      `godot-ps1/build/psxsplash.{ps-exe,elf}`.
- [ ] `python scripts/run.py launch-emulator` boots the empty psxsplash
      runtime in PCSX-Redux. (Will show "no splashpack found" or
      similar until Phase 2.)
- [ ] Rider can open `PS1Godot.sln`, build, and set a breakpoint in
      `PS1GodotPlugin.cs` that hits when the plugin enables.

## Environment variables (summary)

| Var | What | Default if unset (per OS) |
|-----|------|---------------------------|
| `GODOT_EXE` | Full path to the .NET Godot executable | Windows: `D:\Programs\Godot_v4.7-dev5_mono_win64\Godot_v4.7-dev5_mono_win64.exe` · Linux: `~/.local/bin/godot` · macOS: `/Applications/Godot.app/Contents/MacOS/Godot` (each falls back to `which godot` if absent) |
| `GODOT_NUPKGS` | Full path to `<Godot>/GodotSharp/Tools/nupkgs` (required for Rider/CLI restore of dev builds) | — must be set |
| `PCSX_REDUX_EXE` | Full path to PCSX-Redux executable | Windows: `C:\tools\pcsx-redux\pcsx-redux.exe` · Linux: `~/.local/bin/pcsx-redux` · macOS: `/Applications/PCSX-Redux.app/Contents/MacOS/pcsx-redux` (each falls back to `which pcsx-redux`) |

Set them user-scoped so all tooling picks them up:

```bat
:: Windows (persistent, user-scope; reopen terminals after):
setx GODOT_EXE "C:\tools\Godot\Godot_v4.7-dev5_mono_win64.exe"
setx GODOT_NUPKGS "C:\tools\Godot\GodotSharp\Tools\nupkgs"
setx PCSX_REDUX_EXE "C:\tools\pcsx-redux\pcsx-redux.exe"
```

```bash
# Linux / macOS — append to ~/.bashrc or ~/.zshrc, then `source` it:
export GODOT_EXE="$HOME/.local/bin/godot"
export GODOT_NUPKGS="$HOME/.local/share/godot/GodotSharp/Tools/nupkgs"
export PCSX_REDUX_EXE="$HOME/.local/bin/pcsx-redux"
```

`python scripts/run.py bootstrap-env` prints the resolved values so you
can sanity-check.

## Troubleshooting

**"C# build failed" on Godot launch.**
Godot needs the .NET SDK on PATH. Run `dotnet --version`; if it errors, install
the SDK and restart.

**`NU1102: Unable to find package Godot.NET.Sdk with version (>= 4.7.0-dev.5)`.**
Your `GODOT_NUPKGS` env var is unset or points at the wrong directory. It
must point at the `nupkgs` folder *inside* your Godot install — the one that
contains `Godot.NET.Sdk.4.7.0-dev.5.nupkg`. `setx GODOT_NUPKGS "..."` then
restart Rider/terminals.

**"The type `PS1Scene` could not be resolved."**
Godot hasn't built the C# assembly yet. In the editor menu:
**Project → Tools → C# → Create C# solution** (if prompted), then
**Build → Build Solution** (hammer icon, top-right). Reopen the scene.

**PCSX-Redux errors on `-pcdrvbase`.**
Make sure the directory exists. `scripts\launch-emulator.cmd` creates it
implicitly via `build-psxsplash.cmd`, but if you call PCSX-Redux directly,
create `godot-ps1\build\` first.

**Rider doesn't debug into Godot.**
Verify the Godot plugin is installed. Rider's detection is solution-based, so
always open `PS1Godot.sln`, not individual `.cs` files.
