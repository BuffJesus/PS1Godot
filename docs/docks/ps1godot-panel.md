# PS1Godot panel

The right-side dock — `PS1GodotDock` — is the front door for every
plugin action. Everything else in the strip is a deeper view of one
of the things this panel surfaces in compact form.

<!-- SCREENSHOT: docks/ps1godot-panel.png — full dock vertical, all sections visible -->

## Sections, top to bottom

### Scene budgets

Triangle count, VRAM usage, and SPU RAM usage as live progress bars
against PS1 hardware limits. Bars are green under 70%, amber 70–90%,
red over 90%. Hover a bar for the exact numbers; click a bar to jump
to a more detailed view ([VRAM Viewer](#) for VRAM, [Audio
Routing](#) for SPU).

Numbers reflect the **last successful export**. If you haven't
exported since opening the scene, the bars show "no data". F5 once
to populate.

### Run-on-PSX

The big **▶ Run on PSX** button is the canonical iteration loop:
export the scene → build the runtime (if needed) → launch
PCSX-Redux pointing at the result. ~3 seconds in the happy path on
a warm cache, ~15 seconds on a cold launch.

**Auto-run on save** (checkbox below the button) re-runs the whole
flow every time you `Ctrl+S` a `.tscn`. Useful for tight iteration
on a Lua script or a UI canvas tweak; turn off when authoring
intensive geometry where every save would burn the cache.

The button refuses to launch if Doctor's last export reported any
**Blocker** severity offender. See [PS1 Doctor](doctor.md) for the
severity taxonomy and how to clear blockers.

### Quick Actions

Four shortcuts to operations you'd otherwise reach through the
menu / RMB:

- **Convert mesh to PS1** — adds `PS1MeshInstance` script + the
  default material to a stock `MeshInstance3D`. Saves the
  drag-script-from-FileSystem step.
- **Frame selected model** — sets the editor camera framing so a
  newly-imported FBX is on screen at a reasonable distance.
- **Bake vertex lighting** — runs the per-vertex lighting baker
  on selected meshes (Phase 2 stretch goal).
- **Open VRAM viewer** — same as clicking the VRAM section's
  detail icon. Jumps to the [VRAM Viewer](#) bottom-panel tab.

<!-- SCREENSHOT: docks/run-button.png — close-up of the Run on PSX button -->

### Setup summary

Detection status for the four external dependencies the plugin
needs: Godot version, .NET SDK, PCSX-Redux binary, MIPS toolchain.
Each shows a green check + version string when found, or a red
cross with a "fix this" link when missing.

If the plugin enables but Setup shows any red rows, the next
Run-on-PSX click will fail with a meaningful error and refuse to
spin up a PCSX-Redux process pointed at an incomplete runtime.

### Top offenders

A compact 8-row list of the most severe warnings from the last
export. Click a row to focus the offending node in the SceneTree;
double-click to open it in the Inspector.

If the list is capped at 8 and you've got more, **PS1 Doctor**
shows the full list. The cap is intentional — anything past row 8
needs a real validator view, not a compact one.

### VRAM thumbnail

A small (256×128 px) thumbnail of the last export's VRAM layout,
clickable to open the full [VRAM Viewer](#) at native 1024×512
resolution. Quick "did my last edit blow out an atlas?" check
without leaving the panel.

## Workflow

The intended loop:

1. Author your scene (3D viewport).
2. Glance at the budget bars to confirm you haven't blown a hard
   limit.
3. Run on PSX.
4. If something looks wrong on PSX, the panel's top offenders +
   Doctor + the relevant detail dock tell you what to fix.
5. Repeat.

Auto-run on save tightens steps 3–4 into a passive background
loop — save in Godot, alt-tab to PCSX-Redux, see the change.
