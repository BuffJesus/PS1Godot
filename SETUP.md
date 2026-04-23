# Development environment setup

Goal: from a fresh clone, be able to edit PS1 scenes in Godot, build the
psxsplash runtime, and iterate in PCSX-Redux with a one-click loop.

Target time to boot: **~15 minutes** for a new machine.

## Prerequisites

| Tool | Purpose | Where | Phase needed |
|------|---------|-------|--------------|
| Godot **.NET / Mono** build (currently pinned: **4.7.0-dev.5**) | Editor + plugin | [godotengine.org/download](https://godotengine.org/download/) (stable) or [downloads.tuxfamily.org/godotengine](https://downloads.tuxfamily.org/godotengine/) (dev) | Phase 1 |
| .NET 8 SDK | C# compilation | [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download) | Phase 1 |
| JetBrains Rider (with **Godot Support** plugin) | IDE + C# debug | JetBrains Toolbox | Phase 1 |
| `mipsel-none-elf` toolchain | Cross-compile psxsplash | `pcsx-redux-main\mips.ps1` | Phase 0 |
| GNU `make` | Build psxsplash | MSYS2 or Git Bash | Phase 0 |
| PCSX-Redux | Run PS1 code | [pcsx-redux downloads](https://distrib.app/pub/org/pcsx-redux/project/dev-win-x64) | Phase 0 |
| `mkpsxiso` | Build real-hardware ISO | [github.com/Lameguy64/mkpsxiso](https://github.com/Lameguy64/mkpsxiso) | Phase 3 |

## One-time setup

### 1. Install Godot .NET + .NET SDK

1. Download the Godot .NET build (currently pinned: **4.7.0-dev.5**).
2. Extract to `D:\Programs\Godot_v4.7-dev5_mono_win64\` (the default the launch
   scripts assume). If you extract elsewhere, set `GODOT_EXE` to override.
3. Install the .NET 8 SDK if you don't already have it. Verify with
   `dotnet --version` → should print `8.x.x`.
4. **Run the env bootstrap** — sets `GODOT_EXE`, `GODOT_NUPKGS`, and
   `PCSX_REDUX_EXE` in one shot:
   ```bat
   scripts\bootstrap-env.cmd
   ```
   Close and re-open terminals / Rider for the values to be visible.

> **Why GODOT_NUPKGS matters.** The 4.7-dev.5 SDK is not on nuget.org; it
> ships inside the Godot install at `GodotSharp\Tools\nupkgs\`.
> `godot-ps1\NuGet.Config` references `%GODOT_NUPKGS%` so Rider and CLI
> `dotnet build` find it. Without it, you'll see
> `error NU1102: Unable to find package Godot.NET.Sdk with version (>= 4.7.0-dev.5)`.
> Godot itself injects the path internally, so opening in the editor works
> even without the env var — but any build outside the editor won't.

### 2. Install the MIPS toolchain (for building psxsplash)

From any PowerShell prompt (not admin — user-scope install only):

```powershell
powershell -c "& { iwr -UseBasicParsing https://bit.ly/mips-ps1 | iex }"
```

This form installs **silently** to `%APPDATA%\mips\` (typically
`C:\Users\<you>\AppData\Roaming\mips\`) — no progress output, no folder
picker unless your Windows username contains a space (which would force the
installer to prompt for a space-free path). Installation is user-scope and
modifies your user PATH only.

If you'd rather install elsewhere (e.g., `D:\Programs\mips` to match the
Godot convention), use the explicit form — download `mips.ps1` first, then:

```powershell
powershell -ExecutionPolicy Unrestricted -File mips.ps1 self-install D:\Programs\mips
```

The destination must have **no spaces** in the path (installer constraint).

After install, **open a new terminal** (existing ones won't see the new
PATH), then download a toolchain version. Use whatever version the tool
recommends (run `mips ls-remote` to see all available) — psxsplash's
Makefile has no version pin and newer is generally fine:

```
mips install 15.2.0
```

Verify:

```
mipsel-none-elf-gcc --version
```

If `make` isn't available, install [MSYS2](https://www.msys2.org/) and add
`C:\msys64\usr\bin` to your PATH. Or use Git Bash which ships with make.

### 3. Install PCSX-Redux

1. Download `pcsx-redux-HEAD-win-x64.zip` from the
   [distrib.app page](https://distrib.app/pub/org/pcsx-redux/project/dev-win-x64).
2. Extract to `C:\tools\pcsx-redux\`. The executable lands at
   `C:\tools\pcsx-redux\pcsx-redux.exe`.
3. Set the `PCSX_REDUX_EXE` environment variable to the full path, or edit
   `scripts\launch-emulator.cmd`.
4. First run: set OpenBIOS as the BIOS in `Configuration → Emulation`.
   OpenBIOS is bundled; use it to avoid copyright issues.

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

See [`docs/lua-editor-setup.md`](docs/lua-editor-setup.md) for the
step-by-step — takes about 5 minutes.

## Per-clone bootstrap

```bash
# From the workspace root:
scripts\launch-editor.cmd
```

First launch takes 30–60s while Godot imports assets and builds C#. Expected
output in Godot's Output dock: `[PS1Godot] Plugin enabled.`

Then open `demo/demo.tscn` — you should see a jittering cube on a flat floor
with nearest-neighbor rendering. That's the PS1 shader working.

## Phase 0 verification

When all of the below succeed, you're ready for Phase 1 preview work and
Phase 2 exporter work.

- [ ] `scripts\launch-editor.cmd` opens Godot. Plugin is listed as enabled
      in **Project → Project Settings → Plugins**.
- [ ] `demo/demo.tscn` loads and renders with visible vertex snapping.
- [ ] `scripts\build-psxsplash.cmd` produces `godot-ps1\build\psxsplash.elf`.
- [ ] `scripts\launch-emulator.cmd` boots the empty psxsplash runtime in
      PCSX-Redux. (Will show "no splashpack found" or similar until Phase 2.)
- [ ] Rider can open `PS1Godot.sln`, build, and set a breakpoint in
      `PS1GodotPlugin.cs` that hits when the plugin enables.

## Environment variables (summary)

| Var | What | Default if unset |
|-----|------|------------------|
| `GODOT_EXE` | Full path to the .NET Godot executable | `C:\tools\Godot\Godot_mono.exe` |
| `GODOT_NUPKGS` | Full path to `<Godot>/GodotSharp/Tools/nupkgs` (required for Rider/CLI restore of dev builds) | — must be set |
| `PCSX_REDUX_EXE` | Full path to `pcsx-redux.exe` | `C:\tools\pcsx-redux\pcsx-redux.exe` |

Set them user-scoped so all tooling picks them up:

```bat
setx GODOT_EXE "C:\tools\Godot\Godot_v4.7-dev5_mono_win64.exe"
setx GODOT_NUPKGS "C:\tools\Godot\GodotSharp\Tools\nupkgs"
setx PCSX_REDUX_EXE "C:\tools\pcsx-redux\pcsx-redux.exe"
```

(Close and re-open terminals after `setx` for the change to take effect.)

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
