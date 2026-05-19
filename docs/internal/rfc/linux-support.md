# Linux + macOS support — design + patch

**Status (2026-05-17):** No implementation yet. All five
Windows-only items the audit identifies still apply verbatim:
`scripts/*.cmd` are still pure cmd.exe, `PS1GodotPlugin.cs`
hardcodes `cmd.exe` at three call sites (`735`, `801`, `951`),
`ps1lua.gdextension` only declares `windows.debug.x86_64` /
`windows.release.x86_64` library paths, `build-release.py` only
zips the Windows DLL, and `SETUP.md`/`QUICKSTART.md` still assume
the Windows install paths. Sized as ~4–5 commits — Python
launcher dispatcher (`scripts/run.py` + `.sh` shims) (~1–2),
platform-aware `RunScript` in the plugin (~1), Linux + macOS
gdextension library build + CI matrix (~1–2), docs cross-platform
section (~1). Deferred to its own session — naturally pairs with
`project-template.md` since both build the same cross-platform
shell.

Closes the "Windows-only launch scripts" + "GDExtension only ships
Windows x86_64" items currently filed under "Known limitations" in
`docs/release-notes-20260420.md`, and the matching cleanup item in
`docs/handoff-2026-04-21.md`:

> **Release GDExtension for Linux / macOS** so non-Windows users can
> use the plugin. Needs a CI matrix build eventually; for now, maybe
> cross-compile from Windows + publish as additional assets.

This is tooling + packaging, not runtime — psxsplash already builds
on Linux per its own README (`mipsel-linux-gnu` toolchain). The
splashpack format is endian-defined and platform-independent. The
gap is entirely on the authoring side.

Drop this file at `docs/linux-support.md`.

## Goal

A Linux or macOS contributor can clone the repo, follow `SETUP.md`,
and reach the same one-button Run-on-PSX loop a Windows user gets —
no rewriting scripts, no rebuilding the GDExtension by hand.

Non-goal: ARM Linux out of the gate. Add it once x86_64 Linux + macOS
universal2 are stable; CI cost stays manageable.

## What's Windows-only today

Five concrete items (audit done against `scripts/`,
`addons/ps1godot/`, `SETUP.md`, `QUICKSTART.md`):

1. **`scripts/*.cmd`** — `bootstrap-env.cmd`, `build-psxsplash.cmd`,
   `launch-emulator.cmd`, `build-emulator.cmd`. Pure cmd.exe; uses
   `setlocal`, `setx`, `%~dp0`, backslash paths.
2. **`PS1GodotPlugin.cs:RunScript`** — hardcodes
   `OS.Execute("cmd.exe", new[] { "/c", scriptPath, … })`. The
   in-editor Build / Launch / Export / Analyze buttons all flow
   through this one helper.
3. **`addons/ps1godot/scripting/ps1lua.gdextension`** — declares
   `windows.debug.x86_64` and `windows.release.x86_64` library paths
   only. Linux / macOS Godot installs can't load the extension, so
   PS1Lua scripting is unavailable.
4. **`scripts/build-release.py`** — works fine on Linux (Python +
   stdlib `zipfile`) but only zips the Windows DLL. No bake step
   for the cross-platform libraries.
5. **`SETUP.md` + `QUICKSTART.md`** — install instructions assume
   `mipsel-none-elf` via the Windows-only `mips.ps1` installer and
   PCSX-Redux's Windows binary. The cross-platform paths exist (apt
   / brew packages, native PCSX-Redux builds) but aren't documented.

(1)–(4) are code changes. (5) is docs. None of them touch the
runtime, the splashpack format, or the C# exporter logic.

## Design

### Cross-platform launcher (replaces `scripts/*.cmd`)

One Python entry point — `scripts/run.py` — dispatches every
action the .cmd files do today:

```
python scripts/run.py build-psxsplash
python scripts/run.py launch-emulator
python scripts/run.py bootstrap-env --show
python scripts/run.py export-and-run         # full pipeline
```

Python is already a project dependency (`build-release.py`,
`gen_api_data.py`, `tools/`), and the existing .cmd files are thin
enough that the port is mechanical: each becomes a function in
`scripts/run.py` that uses `subprocess.run` and `pathlib.Path`.

The existing .cmd files don't go away in v1 — they become one-line
shims that call into Python:

```bat
@echo off
python "%~dp0run.py" build-psxsplash %*
```

That keeps muscle memory working (`scripts\build-psxsplash.cmd`
still does what it always did) without doubling the
maintenance surface. Linux / macOS users get a parallel `.sh`
sibling per script for the same reason — `bash scripts/build-psxsplash.sh`
works as expected.

Path resolution becomes pathlib-based:

```python
REPO_ROOT = Path(__file__).resolve().parent.parent
BUILD_OUT = REPO_ROOT / "godot-ps1" / "build"
PSXSPLASH = REPO_ROOT / "psxsplash-main"
```

Environment-variable lookups stay the same (`GODOT_EXE`,
`PCSX_REDUX_EXE`, `MIPS_TOOLCHAIN_PREFIX`) but get cross-platform
defaults:

```python
DEFAULT_PCSX_REDUX = {
    "Windows": Path(r"C:\tools\pcsx-redux\pcsx-redux.exe"),
    "Linux":   Path.home() / ".local/bin/pcsx-redux",
    "Darwin":  Path("/Applications/PCSX-Redux.app/Contents/MacOS/pcsx-redux"),
}[platform.system()]
```

The launcher prints a clear "set $PCSX_REDUX_EXE" error if the
defaults don't match where the user installed.

### `PS1GodotPlugin.cs:RunScript`

Currently:

```csharp
int code = OS.Execute("cmd.exe", new[] { "/c", full }, output, true);
```

Replace with a platform-aware dispatcher:

```csharp
private static int RunScript(string scriptRelative, string label)
{
    // scriptRelative is now the python action name, e.g. "build-psxsplash".
    var pythonScript = Path.Combine(RepoRoot(), "scripts", "run.py");
    if (!File.Exists(pythonScript)) { /* error as today */ }

    var output = new Godot.Collections.Array();
    string interpreter = OS.GetName() == "Windows" ? "python" : "python3";
    int code = OS.Execute(interpreter,
        new[] { pythonScript, scriptRelative }, output, /* readStderr */ true);
    foreach (var line in output) GD.Print(line.AsString().TrimEnd('\r', '\n'));
    return code;
}
```

That's the entire C# change. Callers pass action names instead of
.cmd paths:

```csharp
private void OnBuildPsxsplash() => RunScript("build-psxsplash", "Build psxsplash");
private void OnLaunchEmulator() => RunScript("launch-emulator", "Launch emulator");
```

### GDExtension build matrix

`ps1lua.gdextension` grows entries for every supported binary:

```ini
[configuration]
entry_symbol = "ps1lua_entrypoint"
compatibility_minimum = "4.4"
reloadable = true

[libraries]
windows.debug.x86_64    = "build/libps1lua.windows.template_debug.x86_64.dll"
windows.release.x86_64  = "build/libps1lua.windows.template_release.x86_64.dll"
linux.debug.x86_64      = "build/libps1lua.linux.template_debug.x86_64.so"
linux.release.x86_64    = "build/libps1lua.linux.template_release.x86_64.so"
macos.debug             = "build/libps1lua.macos.template_debug.framework"
macos.release           = "build/libps1lua.macos.template_release.framework"
```

SCons already handles per-platform builds via godot-cpp's
SConstruct — `scons platform=linux target=template_release` Just
Works. No `SConstruct` changes needed. macOS wants a `.framework`
bundle for universal2 (x86_64 + arm64); godot-cpp's SCons recipes
cover that, but the resulting artifact is a directory, not a single
file. The `.gdextension` line above points at the framework; SCons
output path is the directory.

### CI matrix

Add a GitHub Actions workflow `.github/workflows/build-gdextension.yml`:

```yaml
jobs:
  build:
    strategy:
      matrix:
        os: [windows-latest, ubuntu-latest, macos-latest]
        target: [template_debug, template_release]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
        with: { submodules: recursive }
      - uses: actions/setup-python@v5
        with: { python-version: "3.11" }
      - run: pip install scons
      - run: cd godot-ps1/addons/ps1godot/scripting && scons target=${{ matrix.target }} -j2
      - uses: actions/upload-artifact@v4
        with:
          name: ps1lua-${{ matrix.os }}-${{ matrix.target }}
          path: godot-ps1/addons/ps1godot/scripting/build/
```

For releases, `scripts/build-release.py` adds a "fetch CI artifacts"
step: download the matrix outputs from the workflow run, drop them
all into `scripting/build/` before zipping. Or simpler v1: a
contributor downloads the three artifacts from the workflow page
and runs `build-release.py` locally with all three already in
place. CI-as-trigger lands later.

Until CI matrix lands, the release notes call out that Linux / macOS
contributors can build locally via `scons` and the `.gdextension`
will pick up their `.so` / `.framework` automatically. They just
can't get a pre-built one from the release zip.

### MIPS toolchain on non-Windows

`psxsplash-main/README.md` already documents this in one line:
*"`mipsel-none-elf` on Windows, `mipsel-linux-gnu` on Linux"*. Three
install paths to document in `SETUP.md`:

- **Linux (Debian / Ubuntu):**
  `sudo apt install gcc-mipsel-linux-gnu`
- **macOS:**
  `brew tap pcsx-redux/mips && brew install mips`
  (the pcsx-redux project maintains a Homebrew tap; the binary name
  is the same `mipsel-none-elf-gcc` Windows uses)
- **From source:** crosstool-ng recipe documented in psxsplash's
  build docs; defer to upstream for that path.

`scripts/run.py` detects the available toolchain at startup:

```python
TOOLCHAIN_CANDIDATES = ["mipsel-none-elf-gcc", "mipsel-linux-gnu-gcc"]
for candidate in TOOLCHAIN_CANDIDATES:
    if shutil.which(candidate):
        MIPS_GCC = candidate
        break
else:
    fail("No MIPS GCC found. Install mipsel-none-elf-gcc (Windows / macOS) "
         "or gcc-mipsel-linux-gnu (Linux apt).")
```

Then `make` is invoked with `CROSS=$MIPS_GCC_PREFIX` so the
psxsplash Makefile picks up the right toolchain. (Currently the
Makefile hardcodes the prefix via `nugget`; needs one variable
indirection — a `CROSS_PREFIX ?= mipsel-none-elf-` line in the
Makefile, then `make CROSS_PREFIX=mipsel-linux-gnu-`.)

That last bit is a one-line psxsplash change. Either propose
upstream or carry as a local patch — see "Suggested entry" below.

### PCSX-Redux on non-Windows

PCSX-Redux ships native binaries for Linux (AppImage and `.deb`) and
macOS (`.dmg`). The launcher's `-pcdrv -pcdrvbase PATH -loadexe PATH
-fastboot -run` flags are identical across platforms; only the
binary path changes.

The `PCSX_REDUX_EXE` env var pattern works as-is. Document the
default install locations in `SETUP.md` and let the per-platform
default in `run.py` handle the rest.

## Implementation stages

Independent shippable wins. Stage 1 unblocks Linux developers from
day one; later stages polish.

### Stage 1 — Cross-platform launcher

- Add `scripts/run.py` with every action the .cmd files currently
  expose.
- Convert each `scripts/*.cmd` to a one-line shim into Python.
- Add parallel `scripts/*.sh` siblings for Linux / macOS users who
  prefer typing `./scripts/build-psxsplash.sh`.
- Update `PS1GodotPlugin.cs:RunScript` to dispatch to Python by
  action name.
- One-line Makefile change in psxsplash (`CROSS_PREFIX ?= …`); pick
  it up in `run.py`.

Verifiable: a Linux contributor runs `python scripts/run.py
build-psxsplash` from a fresh clone and gets a `psxsplash.ps-exe`.
The in-editor Build button still works on Windows.

### Stage 2 — GDExtension Linux / macOS local builds

- Expand `ps1lua.gdextension` to declare every platform path.
- Add a one-page section to `SETUP.md` for "Building the GDExtension
  yourself" on Linux / macOS.
- Update `scripts/build-release.py` to include any `.so` / `.framework`
  it finds in `scripting/build/` (not Windows-DLL-only as today).

Verifiable: a macOS user clones, runs `scons` in `scripting/`, opens
Godot, and PS1Lua syntax highlighting + autocomplete work.

### Stage 3 — CI matrix + release automation

- Add the GHA workflow above.
- Wire `build-release.py` to either consume CI artifacts or document
  the three-platform-download flow.
- Update `QUICKSTART.md` Path A ("just want to use the plugin") to
  point at the matrix release as a single zip per platform, or one
  fat zip with all three libraries inside (`.gdextension` picks the
  right one — Godot's loader handles the per-platform dispatch).

Verifiable: a Linux user downloads
`PS1Godot-plugin-<version>.zip` from a Release, extracts, opens
their `.tscn`, no rebuild needed.

### Stage 4 — Docs polish

- `SETUP.md`: per-OS install columns instead of "Windows then maybe
  Linux works."
- `QUICKSTART.md`: drop the "you'll need Windows 10/11 for the
  1-click path" caveat from the top once Stage 2 lands. Replace
  with a per-OS install row.
- README badges: "Windows ✓ | Linux ✓ | macOS ✓" once CI is green
  across the matrix.

## Open questions / tradeoffs

**Python as a hard dep.** Already true via `gen_api_data.py` and
`build-release.py` — the C# side of the plugin uses
`OS.Execute("python", …)` for stub regen. Surfacing Python as the
universal launcher front end formalizes what's already happening.
Authors who don't have Python get a clear error from
`RunScript` instead of a silent failure. Net: small UX hit on first
run, big maintenance win after.

**Why not `.cmd` + `.sh` siblings without Python?** Tried in spirit
during the .cmd audit. They drift. Three files per action (Python +
.cmd shim + .sh shim) is the deliberately-redundant minimum that
keeps each platform's "I just want to type the bare filename"
muscle memory working. The actual logic lives in Python.

**Universal binary for macOS?** The framework approach handles this
automatically when SCons builds with `arch=universal`. Slower CI
but one artifact instead of two.

**ARM Linux.** Tracked as a Stage 5+ item — needs `gcc-aarch64-linux-gnu`
build of the GDExtension, no other code changes. Add when there's a
real user request; until then the matrix already covers 95 % of
desktop Linux installs.

**WSL.** Not a target. Linux contributors should use native Linux;
Windows contributors should stay on Windows. WSL pretends to be both
and tends to break the GUI side of the loop (Godot editor across
WSL is painful). Document "if you must, point GODOT_EXE at a Windows
Godot install and run the rest in WSL" but don't optimize for it.

**Godot version drift across platforms.** All three Godot binaries
must be the same version (4.7-dev5 currently). The GDExtension is
`compatibility_minimum = "4.4"` so minor drift is OK; major version
bumps require a coordinated rebuild across the matrix. Document the
pinned version in `SETUP.md` and bump deliberately.

## Suggested entry for `docs/psxsplash-improvements.md`

A small one-line patch worth proposing upstream so non-Windows
contributors don't need a local fork:

> ### N+M. Makefile hardcodes the MIPS toolchain prefix
>
> **Problem.** `psxsplash-main/Makefile` (via `nugget`'s
> `psyqo.mk`) assumes the `mipsel-none-elf-` toolchain prefix.
> Linux distros ship `mipsel-linux-gnu-gcc` instead, requiring a
> local Makefile edit on every checkout.
>
> **Proposed direction.** Replace the hardcoded prefix with
> `CROSS_PREFIX ?= mipsel-none-elf-` so contributors can do
> `make CROSS_PREFIX=mipsel-linux-gnu-` without modifying tracked
> files. Backwards-compatible: omitted variable falls back to the
> current default.
>
> **Status.** Unfiled. One-line change to upstream nugget /
> psyqo.mk; small enough for a drive-by PR.
>
> **Evidence.**
> - `2026-05-11` — Linux developer onboarding requires a manual
>   Makefile edit (or a wrapper script) to find the apt-installed
>   toolchain. Same pattern works on macOS Homebrew installs.

## Changelog

- `2026-05-11` — Document created. Pairs with `lod-design.md` as
  the second self-contained patch doc. No code yet.
