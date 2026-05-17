# Project template + first-run experience — design + patch

Closes the roadmap bullet:

> - [ ] Project template (`PS1 Game`) installable into Godot's
>       project manager.
> — `ROADMAP.md`

And the implicit gap that nobody's named: "I've cloned the repo,
now what?" Currently authors follow `SETUP.md` (24 KB of
detailed instructions), install five tools, run scripts, hope
the env vars worked. New contributors burn 30–60 minutes
before they see anything run.

This doc designs the onboarding: from `git clone` (or "I want
to start a new project") to seeing the demo in PCSX-Redux in 5
minutes, with the editor walking the user through anything that
breaks.

Drop this file at `docs/project-template.md`.

## Goal

Two paths, both target 5 minutes to first-run:

**Path A — Cloning the dev repo to contribute.**
```
git clone --recursive https://github.com/BuffJesus/PS1Godot
cd PS1Godot
python scripts/bootstrap.py
```
The bootstrap script verifies prerequisites, downloads any
missing tools, sets env vars, builds the runtime, opens Godot.
The editor's first-run panel highlights the dock's
Run-on-PSX button.

**Path B — Starting a fresh project with PS1Godot.**
Open Godot's Project Manager. Click "Create New Project" →
"From Template" → "PS1 Game". Godot creates a project with the
PS1Godot plugin pre-installed, a hello-world scene loaded, and
a one-click "Build Runtime + Run" button.

Both paths converge on the same first-run experience inside
the editor: a guided panel that shows what works, what needs
setup, and where to go next.

Non-goal: hiding the platform's actual complexity. PS1
development genuinely requires a MIPS toolchain, emulator,
Godot .NET build, and assorted CLI tools. We can't make those
disappear, but we can detect them, install what's auto-
installable, and produce useful errors for the rest.

## What's in place

- **`SETUP.md`** is the comprehensive walkthrough — 24 KB,
  ~300 lines, every detail. Reference material; not a path
  to first run.
- **`QUICKSTART.md`** is the "15-minute path" — already an
  improvement over `SETUP.md`. Still requires manual tool
  installation in five separate steps.
- **`scripts/bootstrap-env.cmd`** sets the three env vars
  (`GODOT_EXE`, `GODOT_NUPKGS`, `PCSX_REDUX_EXE`) on Windows.
  Linux/macOS equivalent doesn't exist (covered by
  `linux-support.md`).
- **`PS1GodotPlugin._EnterTree`** registers everything when
  the plugin is enabled. Detects the absence of dependencies
  (in a limited way) and logs to the console.
- **`PS1GodotDock`** has a "Setup" section that detects
  missing pieces and shows status. Foundation for the
  first-run panel — needs expansion.
- **`godot-ps1/demo/`** has a working demo scene that
  exercises the full pipeline. The bootstrap target.

The work is in: (1) automating prerequisite checks and
installs, (2) creating a project template, (3) building a
first-run guide inside the editor, (4) refactoring `SETUP.md`
into a reference rather than a procedure.

## Design

Four pieces: bootstrap script, project template, first-run
panel, and self-test.

### Bootstrap script

`scripts/bootstrap.py` — the cross-platform replacement for
`scripts/bootstrap-env.cmd`. Cross-platform per
`linux-support.md`. Runs as:

```
python scripts/bootstrap.py [--no-install] [--check]
```

What it does, in order:

1. **Detect Python version.** Refuses if < 3.10 (we use
   modern typing).
2. **Detect Godot.** Looks for `GODOT_EXE` env var; if missing,
   searches common paths per OS. If still missing, prompts the
   user with the download URL or attempts auto-download via
   the GitHub releases API.
3. **Detect .NET SDK.** `dotnet --version` available + 8.x.
   If missing, prints install URL.
4. **Detect MIPS toolchain.** Either `mipsel-none-elf-gcc` or
   `mipsel-linux-gnu-gcc` on PATH. If missing, runs the
   pcsx-redux installer (Windows / macOS) or prints the apt
   command (Linux).
5. **Detect PCSX-Redux.** Looks for `PCSX_REDUX_EXE` env var
   or searches common paths. If missing, prints download URL
   per OS.
6. **Detect `mkpsxiso`.** Optional — needed only for ISO
   builds (Phase 3). Note presence/absence without failing.
7. **Build the runtime.** Runs `scripts/build-psxsplash.cmd`
   (via Python launcher per `linux-support.md`) and confirms
   `build/psxsplash.ps-exe` exists.
8. **Build the GDExtension.** Runs scons inside
   `scripting/`. Skips on Windows if a prebuilt DLL exists in
   the repo.
9. **Set env vars.** Writes them per-OS:
   - Windows: `setx GODOT_EXE …` (current implementation).
   - Linux/macOS: appends `export …` lines to
     `~/.bashrc` / `~/.zshrc` (with a "would you like me to"
     prompt).
10. **Open Godot.** Auto-launches the editor with the
    `godot-ps1/` project open.

Output: a checklist that tells the user what's installed, what
was just installed by bootstrap, and what they still need to
do manually. Each row is green / amber / red with a one-line
explanation:

```
✓ Python 3.11                                        ready
✓ Godot 4.7.0-dev.5 (.NET)                          ready
✓ .NET 8.0.404 SDK                                  ready
✓ mipsel-none-elf-gcc 15.2.0                         ready
✗ PCSX-Redux                                         NOT FOUND
    Download from https://distrib.app/.../pcsx-redux
    or set PCSX_REDUX_EXE to its location
✓ Built psxsplash.ps-exe (1247 KB)                   ready
- mkpsxiso                                           optional, needed for ISO builds
```

The `--check` mode runs detection without doing anything else.
Useful for CI / scripts. `--no-install` skips the auto-install
steps but still detects everything.

Exit code 0 = ready to run; non-zero = something needs the
user's attention.

### Project template

A Godot project template is a `.zip` archive Godot's Project
Manager can import. The template:

- Contains a `project.godot` with PS1Godot listed as an
  enabled plugin.
- Bundles the PS1Godot plugin (same as the
  `PS1Godot-plugin-<version>.zip` distributed via releases).
- Includes a hello-world scene (`scenes/hello.tscn`) that's a
  single PS1MeshInstance cube + a PS1Player + a PS1Camera.
- Includes a sample Lua script (`scripts/cube.lua`) that
  spins the cube and prints "hello" to the console.
- Has the right `display` settings (320×240 viewport, integer
  upscale to 1280×960 for editing).
- Includes a `README.md` for the new project explaining
  next-steps.

The template lives in `dist/PS1Godot-template-<version>.zip`
and gets attached to GitHub Releases alongside the plugin
and runtime zips. Godot can also import the template URL
directly via the Project Manager.

Adding it to Godot's Project Manager "official templates" list
requires a Godot upstream PR; that's a longer game. For now,
ship the .zip and document the import flow:

1. Open Godot Project Manager.
2. Click "Import" → browse to
   `PS1Godot-template-<version>.zip`.
3. Pick a destination folder, click "Create."
4. The new project opens with the demo cube ready to run.

### First-run panel

Inside the editor, the dock detects "this is the first time"
based on the absence of a `.ps1godot-firstrun-done` file in
the project directory. On first run, the dock displays a
guided panel instead of the standard view.

The panel has four sections:

**1. Welcome.**

```
Welcome to PS1Godot 🎮

You're about to author a PlayStation 1 game in Godot. This
editor talks to a real PS1 runtime (psxsplash) — the same
runtime ships on actual silicon.

Quickstart: hit the big red "Run on PSX" button above to
launch the demo scene. PCSX-Redux opens with your scene
running.
```

**2. Health check.**

The same dependency-detection rows the bootstrap script
shows, but rendered as the dock's status panel. Each missing
dependency has a "Fix it" button that runs the appropriate
remediation:

- Missing GODOT_EXE → "Set env var to current Godot path"
- Missing MIPS toolchain → "Open install instructions"
- Missing PCSX-Redux → "Open download page"
- Missing PCdrv config → "Run setup script"

**3. Guided tour.**

A small numbered checklist:

```
☐ 1. Hit Run on PSX — see your scene running.
☐ 2. Edit cube.lua — change the spin speed.
☐ 3. Hit Run on PSX again — see the new behavior.
☐ 4. Open the docs panel — learn what else this supports.
```

Each item, when completed, ticks itself. Detection: hooks into
the dock's existing event signals (Run-on-PSX clicked, file
saved, etc.).

**4. Where to go next.**

Links to the project's living documentation:
`docs/tutorial-hello-cube.md`, `docs/tutorial-basic-scene.md`,
`docs/api-showcase.md`, ROADMAP.md. Each link opens in the
user's external browser.

When all four guided-tour items are checked, the panel
transitions to the standard dock view and writes the
`.ps1godot-firstrun-done` sentinel. Subsequent project opens
go straight to the standard view.

### Self-test

A new menu item: `PS1Godot → Run Self-Test`. Runs a
deterministic sequence:

1. Loads the demo scene.
2. Exports it.
3. Verifies the splashpack format (using `host-mode-testing.md`
   primitives where possible).
4. Builds the runtime.
5. Launches the emulator with the splashpack loaded.
6. Sends a synthetic input sequence via PCdrv (press
   Triangle, wait, observe).
7. Reads the runtime's PCdrv log file.
8. Verifies expected log entries appear (e.g., "interaction
   triggered").
9. Reports pass/fail.

The self-test is the "is my environment broken" diagnostic.
Authors run it when something seems off; it produces a
detailed report of which step failed and what the diagnostic
output was.

A simplified version runs automatically on first-run-panel
completion to validate the setup before declaring the user
"done."

### Refactor `SETUP.md`

After this doc lands, `SETUP.md` becomes reference material,
not a procedure:

- **Move the procedural content** (install steps, env vars,
  verification checklist) into `scripts/bootstrap.py` output
  + the first-run panel.
- **Keep the reference content** (table of prerequisites with
  versions, troubleshooting tables, links to upstream docs).
- **Link from the first-run panel** to specific `SETUP.md`
  sections for users who hit issues.

`QUICKSTART.md` follows the same shift: the procedural "do
this then this" becomes "use the project template," and the
reference content (file layout explanation, terminology) stays.

## Implementation stages

Five stages. Stage 1 is the smallest standalone win; later
stages add automation.

### Stage 1 — Bootstrap script (cross-platform)

The smallest single win.

- `scripts/bootstrap.py` with detection + reporting.
- `--check` mode for CI.
- Status output (green / amber / red rows).
- Per-OS install hints (manual download where automated
  install isn't viable).

Verifiable: a new contributor on Linux clones the repo, runs
`python scripts/bootstrap.py`, sees a clear list of what's
missing and what's ready. No more "did setx work?" guessing.

### Stage 2 — Project template

- `scripts/build-template.py` produces the template `.zip`.
- Template includes the plugin, a hello-world scene, a Lua
  script, project settings.
- README in the template explains the editor's first-run
  panel.

Verifiable: download the template zip from a release, import
into Godot, see the cube spin in editor preview.

### Stage 3 — First-run panel

- Dock detects first run via sentinel file.
- Renders the four-section panel.
- "Fix it" buttons for common dependency issues.
- Guided tour checklist with hooks into existing dock signals.

Verifiable: clone fresh, open in Godot, see the panel. Check
items, panel transitions to standard view.

### Stage 4 — Self-test

- Self-test runner in `scripts/run-selftest.py`.
- Synthetic input via PCdrv.
- Result reporting.
- Editor menu item to invoke.

Verifiable: run self-test on a fresh setup, get a green
report. Sabotage the setup (delete the runtime), re-run,
get a red report with the specific failure.

### Stage 5 — Docs reorganization

- `SETUP.md` becomes reference material.
- `QUICKSTART.md` becomes "use the template, here's what's
  in it."
- New `docs/troubleshooting.md` collects the failure modes
  from real onboarding sessions.

Pure documentation pass; no code changes.

## Open questions / tradeoffs

**Auto-install vs. manual install.** Auto-installing a MIPS
toolchain on a user's machine without consent is uncomfortable.
Bootstrap defaults to "detect + instruct," with auto-install
as opt-in via `--install-missing`. The first-run panel asks
for explicit consent before each auto-install action.

**Python as the bootstrap language.** It's the lowest-friction
cross-platform choice. Authors who don't have Python need to
install it first — a thinner barrier than installing Godot.
Alternative: a Go or Rust binary, but then we'd need to
distribute pre-built bootstrap binaries per OS, which is more
work than "user installs Python once." Stick with Python.

**Version mismatch detection.** Godot updates. .NET updates.
Bootstrap should detect when installed versions don't match
the project's expected versions (e.g., Godot 4.6 when the
project needs 4.7-dev.5). Show a clear "you have X, project
needs Y" message. Don't auto-downgrade.

**Self-test reliability.** PCdrv synthetic input could be
flaky — the emulator might be too slow on a CI runner,
timeouts inconsistent, etc. Make the self-test forgiving:
generous timeouts (30 s for the full run), retry the input
sequence twice, report "passed within 2 retries" rather than
hard pass/fail.

**Multi-user systems.** A shared dev machine where different
users have different Godot installs. The bootstrap detects
per-user env vars; multiple users on the same machine each
run bootstrap once with their own settings.

**Project template versioning.** The template embeds a specific
PS1Godot plugin version. Authors who create a project with v1.0
and try to upgrade to v1.1 of the plugin face a migration. The
template + first-run panel doesn't address upgrades — that's
a separate "plugin version migration" story (probably a follow-
up doc).

**First-run-done detection across re-clones.** The sentinel
file lives in the project directory, gitignored. Re-cloning a
project shows the first-run panel again. That's intentional —
each fresh clone is effectively a new setup. Authors who skip
it tick all four items immediately.

**The dock's existing setup section.** The "Setup" section of
the dock today is a static list; the first-run panel
supersedes it. After first run, the dock falls back to a
simpler "Project status" row that shows green ✓ when all
deps are present, with a link to re-open the first-run panel
manually.

**Bootstrap script ownership.** Where do `python scripts/bootstrap.py`
exit codes go? Standard: 0 = ready, 1 = manual action needed,
2 = bootstrap itself failed. Documented in the script's
`--help`. The first-run panel parses these codes when run
during automated self-test.

**Online dependency on first run.** Auto-install paths fetch
binaries from upstream releases. Offline / firewalled users
can't use these. Document: offline users follow the
`SETUP.md` manual procedure. First-run panel detects no-
network and gracefully degrades (just shows download URLs
instead of "Click to install").

**Network-disabled CI.** CI runners may not have full
internet. Mitigation: cache the prerequisites between runs;
fail fast if a missing prerequisite has no offline install
path. Document the "headless CI" setup in `host-mode-testing.md`'s
CI section.

**Project template + editor version coupling.** A 4.7-built
template doesn't import cleanly into 4.6. Pin the template to
a specific Godot version range in its metadata. Authors
upgrading Godot get a clear message instead of a confusing
import error.

**Template size.** The current plugin zip is ~1 MB. Bundling
it into a template plus the hello-world scene puts the
template at ~1.5 MB. Reasonable download. If it grows past
10 MB, consider lazy-loading the GDExtension binary (not
bundled, downloaded on first open).

## Suggested entries

### For `ROADMAP.md`

Replace the existing one-line bullet with:

> - [ ] **Cross-platform bootstrap script (`scripts/bootstrap.py`).**
>       Detects + installs prerequisites, sets env vars per-OS,
>       builds the runtime, opens Godot. Status output with
>       red/amber/green rows. Full design:
>       `docs/project-template.md`.
> - [ ] **Project template (`PS1 Game`).** Installable zip
>       Godot Project Manager can import. Includes pre-enabled
>       PS1Godot plugin, hello-world scene, sample Lua script,
>       project settings.
> - [ ] **First-run experience panel.** Dock shows guided
>       walkthrough on first project open: welcome, health
>       check, four-step tour, next-steps links. Transitions
>       to standard view on completion.
> - [ ] **Self-test runner.** `PS1Godot → Run Self-Test`
>       deterministic environment validation: export → build
>       → run → verify expected behavior. Hooks into PCdrv
>       for synthetic input + result reading.

## Changelog

- `2026-05-11` — Document created. Fourteenth patch doc in
  the series. Closes the "project template" roadmap bullet
  and addresses the unwritten onboarding gap. Pairs with
  `linux-support.md` (cross-platform script) and
  `debugging.md` (self-test result reporting).
