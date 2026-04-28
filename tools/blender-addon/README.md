# PS1Godot Blender add-on

Companion add-on for the [PS1Godot](../../godot-ps1) project. Lets you
tag Blender objects with the same PS1 authoring metadata
(`MeshRole`, `DrawPhase`, `ShadingMode`, `AlphaMode`, `TexturePageId`,
`CLUTId`, …) that the Godot exporter consumes, and validate scenes
against PS1 constraints in-place — before the asset ever lands in
Godot.

> **Status:** Phase 1 (skeleton). Object/material metadata + a basic
> scene validator ship today. JSON sidecar export, vertex-color tools,
> collision helpers, animation metadata, and PS1Godot manifest
> round-trip are in
> [docs/ps1godot_blender_addon_integration_plan.md](../../docs/ps1godot_blender_addon_integration_plan.md)
> § 23 Phases 2+.

## Install

The add-on is a standard Blender Python package. Two options:

### Option 1 — Symlink for development (recommended)

From a Blender 4.x scripts dir, symlink (or copy) the package folder:

```text
%APPDATA%\Blender Foundation\Blender\4.x\scripts\addons\ps1godot_blender\
  → tools/blender-addon/ps1godot_blender/
```

On Windows (admin PowerShell):

```powershell
New-Item -ItemType SymbolicLink `
  -Path  "$env:APPDATA\Blender Foundation\Blender\4.0\scripts\addons\ps1godot_blender" `
  -Target "D:\path\to\PS1Godot\tools\blender-addon\ps1godot_blender"
```

Then in Blender: `Edit → Preferences → Add-ons`, search "PS1Godot",
enable.

### Option 2 — Zip-install

Zip the `ps1godot_blender/` folder and use Blender's
`Edit → Preferences → Add-ons → Install…` to install the zip.

Symlink is preferred during active development since you don't have to
re-zip after every change.

## Use

After enabling, the **PS1Godot** tab appears in the 3D Viewport's
N-panel sidebar:

- **PS1Godot Project** — scene-wide settings (project root, default
  ChunkId / DiscId, metadata version) + the *Validate Scene* button.
- **PS1 Asset Metadata** — per-object fields for the active selection.
  Stable IDs at the top, render policy box (mesh role / export mode /
  draw phase / shading mode / alpha mode), authoring note at the
  bottom.
- **PS1 Material** — per-material fields (texture page, CLUT, atlas
  group, format, alpha mode) for the active material slot.

Validation reports findings via the operator info area (status bar) +
console. Today's checks:

- Duplicate `mesh_id` / `asset_id` across objects.
- Mesh has materials but no UV layer.
- UVs far outside `[0,1]` (PSX won't wrap).
- `ShadingMode = VertexColor / BakedLighting` but no vertex color layer.
- More than four materials on one mesh (atlas cleanup hint).
- `DynamicRigid` + `MergeStatic` (the merge would freeze it in place).
- Triangle count > 1000 on one mesh.
- Material has no `texture_page_id`.
- Material is 16bpp without `approved_16bpp`.
- Source image is larger than 256×256 (PSX page max — Godot will
  auto-downscale).

These overlap with the Godot-side validators
(`TextureValidationReport`, `AnimationLinter`, `MeshLinter`) on
purpose: catching the issue in Blender means it never travels into the
Godot pipeline.

## Round-trip

Phase 1 only writes Blender-side state — the metadata persists in the
`.blend` file as scene + object + material `ps1godot` PointerProperty
groups. Phase 2 will add the JSON sidecar export so PS1Godot can read
the same metadata when consuming an FBX/GLB derived from this `.blend`.

The wire identifiers (the first item of each enum tuple in
`properties.py`, e.g. `"StaticWorld"` / `"OpaqueStatic"` /
`"VertexColor"`) are the round-trip contract. Don't rename them once a
project ships.

## Smoke test

`tools/blender-addon/test_register.py` registers the add-on against a
factory-startup Blender, exercises the PropertyGroups + validator, and
unregisters. Useful for catching breaks after Blender API bumps:

```text
"C:\Programs\Blender\blender.exe" --background --factory-startup ^
  --python tools\blender-addon\test_register.py
```

(Substitute your Blender path on macOS/Linux.) Last verified green
2026-04-27 against Blender 5.0.0. Exits with code 1 on any
registration / round-trip / validator failure so it can plug into CI.

## Layout

```text
ps1godot_blender/
  __init__.py         — bl_info, register/unregister plumbing
  properties.py       — PropertyGroups (Scene / Object / Material) + enums
  panels/
    project_panel.py  — PS1Godot Project (scene-wide settings + actions)
    metadata_panel.py — PS1 Asset Metadata (per-object) + PS1 Material (per-material slot)
  operators/
    validate_scene.py — PS1GODOT_OT_validate_scene
  utils/              — (placeholder for Phase 2 helpers)
```

Each panel / operator file exposes `register()` / `unregister()`; the
top-level `__init__.py` iterates the modules in dependency order so
property groups land before the panels that draw them.

## Vertex-color lighting

The **PS1 Vertex Lighting** panel ships five bake operators —
vertex-color is the cheapest "real" lighting path on PSX, and these
turn ~30 minutes of manual paint into one click:

- **Create Vertex Color Layer** — adds a `Col` byte-color attribute at
  corner domain on every selected mesh, white-filled at the PSX 0.8
  cap.
- **Bake Directional Light** — `saturate(dot(normal, sun_dir)) ×
  sun_color + ambient_color × ambient_strength`. Quick scene-less
  bake when you just want the basic "lit from above" look. Sun
  direction + colors live as scene properties so iterating is
  one-tweak-one-click.
- **Bake from Scene Lights** — walks every visible Light object
  (`SUN` / `POINT` / `SPOT`), accumulates Lambertian diffuse with
  inverse-square falloff + cone cutoff. Mirrors what SplashEdit's
  `PSXLightingBaker` does — author drops a key + fill light pair
  into the scene, clicks once.
- **Apply Ambient Tint** — multiply existing colors by the scene's
  ambient color. Use after a directional bake for global mood
  (night-mode washes, faction palettes).
- **Clear Vertex Lighting** — reset to white. Lets you redo a bake
  without piling artifacts.

All bakes clamp to **0.8** on each channel, not 1.0. PSX hardware
2× semi-trans blend would otherwise white-out any mesh later tagged
`AlphaMode = SemiTransparent`. Matches SplashEdit's
`Runtime/PSXLightingBaker.cs:79-83` behaviour for the same reason.

The corresponding Godot-side bake operators are tracked in
[`docs/ps1godot-lighting-plan.md`](../../docs/ps1godot-lighting-plan.md)
— phases L1 (scene-lights bake), L2 (vertex AO), L3 (PSX preview
shader with 5-bit quantization + dither), L4 (bake-stack UI).

## Roadmap

See
[docs/ps1godot_blender_addon_integration_plan.md](../../docs/ps1godot_blender_addon_integration_plan.md)
for the full plan. Phase progression:

- ✅ **Phase 1** — Metadata + validation skeleton.
- ✅ **Phase 2** — JSON sidecar export.
- ⏳ Phase 3 — Mesh validation deep-dive (vertex format, index format, atlas usage).
- ✅ **Phase 4** — Vertex-color lighting bake tools (5 operators —
  see *Vertex-color lighting* above).
- ⏳ Phase 5 — Material / texture page workflow (4bpp/8bpp preview).
  *Phase 5 metadata round-trip already shipped via PS1MaterialMetadata
  on the Godot side; only the texture-page preview remains.*
- ⏳ Phase 6 — Collision-helper authoring.
- ⏳ Phase 7 — Animation metadata + event markers.
- ✅ **Phase 8** — PS1Godot manifest import / round-trip ID
  preservation (Godot writer + Blender importer both shipped).
- ⏳ Phase 9 (optional) — Direct binary mesh-bank preview export.
