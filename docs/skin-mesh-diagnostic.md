# Skin mesh / fragmented-grey-patches diagnostic

> **Resolved 2026-04-26 — root cause was suspect #1 (vertex snap).**
> Permanent fix landed: `addons/ps1godot/shaders/ps1_skinned.tres`
> (snap_enabled=false variant) is now auto-applied by `PS1SkinnedMesh`
> on `_EnterTree`. World geometry keeps the snap on. Suspects #2 / #3
> below are kept as a reference for future skinned-mesh issues.

The Kenney `hallway_figure` shipped into the Godot scene editor with
fragmented grey/white patches across the body — same pattern that landed
in PSX export. Editor render showed it too, so the cause was at the
authoring layer, not splashpack export. Three suspects to test in order.

## Suspect #1 — vertex snap collapsing close verts (CHEAPEST)

The PS1 shader (`addons/ps1godot/shaders/ps1.gdshader`) snaps each vertex
to a 320×240 NDC grid via `snap_enabled`. At editor camera distance + a
small mesh, multiple verts collide on the same grid cell and emit
degenerate triangles — the visible "fragments."

**Diagnostic:** the material `ps1_kenney_skin.tres` was edited to
`snap_enabled = false` on 2026-04-26. Reopen the monitor scene in the
editor and look at `Feed01_Hallway/hallway_figure/Char/Root/Skeleton3D/hallway_figure`:

- If the body cleans up → snap is the cause. Permanent fix: introduce a
  skinned-mesh material variant (`ps1_skinned.gdshader` or a `bool
  is_skinned` uniform) that disables snap or scales `snap_resolution`
  inversely to view-space distance. **Don't** disable snap globally — we
  want the PS1 wobble on static geometry.
- If still fragmented → snap is innocent; revert
  `snap_enabled = true` and move to suspect #2.

## Suspect #2 — unweighted vertices snapping to bone 0

Per memory `project_skinned_mesh_gotchas.md`, vertices with all-zero bone
weights silently default to bone 0, dragging chunks of the mesh to the
root joint and producing the spike/fragment pattern.

**Diagnostic script:** `godot-ps1/dev/inspect_skin_weights.gd`.

Run from the godot-ps1 directory:

```bash
"$GODOT_EXE" --headless --path godot-ps1 --script dev/inspect_skin_weights.gd
```

It loads `monitor.tscn`, walks every ArrayMesh surface, and reports:

- Total vertex count
- Unweighted vertex count (`sum(weights) ~= 0`)
- Partially weighted (`sum < 0.95`)
- Invalid bone indices (`>= 45` for the Kenney rig)

Verdict thresholds the script applies:

- Unweighted > 5% of verts → "WARNING: high unweighted count, likely
  cause of grey patches"
- Partial > 20% → "WARNING: significant partial weighting"
- Otherwise → "OK"

If the script reports unweighted/partial verts, the fix is **at import**:

- Re-import the FBX with weight cleanup (Godot's FBX importer has a
  "Force Disable Mesh Compression" advanced flag — try that), OR
- Pre-process the FBX with Blender to fix orphan verts before import,
  OR
- Patch the runtime to clamp unweighted verts (make them invisible
  rather than collapsing to bone 0). Less ideal — hides art bugs.

## Suspect #3 — modulate_scale 2× overbright (LEAST LIKELY)

The shader does `vertex_color.rgb * modulate_scale=2.0` to translate
PSYQo's "128 = 1.0" convention into Godot's "1.0 = 1.0" convention. If
the FBX import baked vertex_color = white (1.0), the result is 2.0 →
overbright clipped to white. This *would* explain a uniformly-bright
surface, **not** fragmented patches — but bright patches against
correctly-sampled face/hands could superficially look like fragmentation.

**Diagnostic:** temporarily set `shader_parameter/modulate_scale = 1.0`
in `ps1_kenney_skin.tres`. If brightness normalizes but fragmentation
persists, suspect #2 is still in play.

## What's been done

- Snap test applied to `ps1_kenney_skin.tres` (jam-dir, gitignored) on
  2026-04-26; user confirmed the figure cleaned up — **suspect #1
  confirmed**.
- Permanent fix shipped: `addons/ps1godot/shaders/ps1_skinned.tres`
  (snap_enabled=false variant of the PS1 shader). `PS1SkinnedMesh`
  auto-applies it on `_EnterTree` when no MaterialOverride is set,
  same pattern PS1MeshInstance uses for `ps1_default.tres`.
- World geometry keeps `snap_enabled=true` — the PSX wobble lives there.
- Diagnostic script remains at `godot-ps1/dev/inspect_skin_weights.gd`
  for future skinned-mesh issues that aren't snap-related.

## Related dev helper: bulk TilingUV applicator

`godot-ps1/dev/apply_tiling_uv.gd` walks `monitor.tscn`, finds every
`PS1MeshGroup` whose descendants match the Kenney offender list (the 21
meshes the UV linter flagged), and sets `TilingUV=true` on each group.
Run from Godot's `File → Run...` menu. Idempotent. Mutes the linter
without changing rendered output — see `docs/ps1-texture-strategy.md`
on the TilingUV caveat (PSX still doesn't wrap; muting is intent-only).
