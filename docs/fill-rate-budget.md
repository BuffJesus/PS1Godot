# Fill rate & overdraw budget — design + patch

The constraint nobody's named yet. PS1 fill rate — pixels-per-
second the GPU can draw — is the real ceiling on most scenes,
not triangle count. Authors stay under triangle budget, watch
the OT pressure, and still drop frames because the GPU spent
60% of the frame drawing pixels nobody saw.

This doc names the budget, measures it, and gives authors a
surface to manage it.

Drop this file at `docs/fill-rate-budget.md`.

## The number

PS1 GPU fill rate is approximately **33 megapixels per second**
for opaque flat-shaded primitives. Textured + Gouraud-shaded
drops it to ~28 Mpix/s. Semi-transparent (additive / subtractive
blend) drops it further to ~15 Mpix/s for primitives that
overlap.

At 30 fps and 320×240 resolution:

- Per-frame budget: 33,000,000 / 30 = **1.1 megapixels**.
- Screen area: 320 × 240 = **76,800 pixels**.
- That's ~14× the screen area as a hard ceiling.

For textured + Gouraud:
- Per-frame budget: 28,000,000 / 30 = **933,000 pixels**.
- Screen area: 76,800.
- That's ~12× the screen area.

In practice, with VBlank overhead and other GPU work (sprite
rendering for UI, framebuffer clears), authors get **6–8×
screen area** as the practical overdraw ceiling.

A scene that paints every pixel exactly once = 1×. A scene
where every pixel is painted 6× = at the ceiling. A scene where
every pixel is painted 12× = dropping frames.

## What's in place

- Triangle count budget on `PS1Scene` (`TargetTris`).
- VRAM budget on the dock.
- OT pressure visualization (in `vram-viewer.md`).
- Nothing about fill rate.

The runtime has no awareness of pixels-painted-per-frame. The
dock has no surface for "your scene is fill-rate-bound." Authors
discover fill problems by feel — the demo runs at 30 fps, the
new scene runs at 22 fps, "must be too many triangles" — and
the fix usually involves reducing triangles when the actual
problem was overdraw.

## What overdraw looks like in PS1 scenes

Three patterns produce overdraw:

**1. Back-to-front painting with no depth rejection.** PS1 has
no Z-buffer; the OT sorts primitives back-to-front and the GPU
paints in that order. Every pixel that ends up behind something
opaque has still been drawn — wasted work. A typical outdoor
scene with sky → distant hills → mid-distance → near terrain →
player has the player's silhouette drawing over 4 already-painted
pixels per pixel. Average overdraw: ~3×.

**2. Semi-transparent primitives.** A particle, fog billboard,
or window doesn't just overdraw — it overdraws into the
slower transparent path. A single transparency layer effectively
costs ~2× a flat fill. Three layers = 6× a flat fill.

**3. Large background quads.** A sky billboard or fog plane
that covers most of the screen at distance gets fully overdrawn
by everything closer. Cheap to render once, but the entire 320×240
of "sky" gets repainted by mid-distance content. The visible
sky might be 20% of the final frame; the other 80% was drawn
twice.

The optimization reference touches this obliquely:

> Battle or action scenes can destroy performance and VRAM
> discipline if they are treated like a separate unrestricted
> game.
> Rules:
> - cap concurrent effects
> - carefully budget transparency-like effects and overdraw-
>   heavy visuals
> — `docs/ps1_large_rpg_optimization_reference.md`

"Overdraw-heavy visuals" is the gap — there's no system that
helps authors budget for it.

## Design

Four pieces: a fill-rate model, a dock visualization, runtime
instrumentation, and authoring guidance.

### Piece 1 — Fill-rate model

The exporter walks the scene's triangles and computes a *static
upper bound* on overdraw. For each triangle:

1. Project to screen space at a representative camera position.
2. Compute screen-space area (in pixels).
3. Multiply by a transparency cost factor:
   - Opaque: 1.0
   - Semi-transparent (additive/subtract): 1.5
   - Semi-transparent (alpha): 1.5

Sum per-triangle area × factor → static estimate of total
pixels-painted-per-frame from this scene.

Compare to budget (6–8× screen area) → ratio → status.

```
Fill rate estimate (camera at scene origin, default FoV):
  Opaque pixels:       348,000  (4.5× screen)
  Transparent pixels:   84,000  (1.1× screen, 1.5× cost factor)
  Estimated total:     474,000  (6.2× screen, 95% of budget)
  Status: ⚠ at budget limit
```

**Limitations.** This is a static estimate at one camera angle.
Real frames vary as the player moves. The estimate is "upper
bound if camera frames the worst-case viewpoint." Authors who
get green at one viewpoint should sample multiple. The dock
can run several sample camera positions and report the worst.

**Camera samples.** `PS1Scene` exposes
`FillRateSampleCameras: NodePath[]` — a list of cameras to
sample. Default: just the main camera. Authors add a few
worst-case viewpoints (the spot with the biggest semi-transparent
particle effect, the spot looking at the most overdraw, etc.).

### Piece 2 — Dock visualization

A new row in the dock's budget surface, alongside triangles and
VRAM:

```
Fill rate
  ████████████████████░░░ 6.2× (budget 8×)
  ⚠ Near limit — consider occluders or smaller semi-transparent regions
```

When over budget:

```
Fill rate
  █████████████████████████ 11.4× (budget 8×, OVER)
  ✗ Will likely drop frames. Top contributors:
     - particle_smoke (3× alone)
     - fog_billboard (1.5×)
     - water_surface (1.2×, transparent)
```

The "top contributors" list helps authors prioritize. Click a
contributor → focus the source object.

### Piece 3 — Runtime measurement

The static estimate is a starting point. Real measurement comes
from the runtime, hooked into the profiler.

**What we can measure on PS1.** The GPU doesn't expose a "pixels
drawn" counter directly. Two paths:

- **Indirect via VBlank.** If the GPU finishes the frame before
  VBlank, fill rate is fine. If it doesn't, frames drop. The
  `vblankWaitMicros` field in `FrameProfile` from `profiling.md`
  is already this signal — positive = ahead of budget, zero =
  at the ceiling.
- **Estimate via triangle area.** The runtime walks the
  triangles being inserted into the OT, sums their screen-space
  area, reports the per-frame total. Cost: ~10 cycles per
  triangle (one cross-product + accumulate). At ~2000 visible
  triangles, that's 20,000 cycles per frame — 2% of frame
  budget. Toggleable; on by default in dev builds.

The runtime estimate updates per frame. The dock's profiler
view shows fill-rate-per-frame as a sparkline alongside fps.
Authors see spikes in real time.

### Piece 4 — Authoring guidance

The optimization reference's "memorable effect timing" advice
becomes concrete: *one big transparent quad for 8 frames > many
small transparent quads for the whole scene*. Authors need
guidelines:

- **Semi-transparent particles**: budget < 2% of screen area
  combined. A 20×20 px particle ≈ 0.5% of screen; budget 4 of
  them as a soft cap.
- **Background billboards**: if it covers > 30% of screen and
  gets overdrawn by closer content, consider replacing with a
  smaller billboard or a pre-rendered sky.
- **Water surfaces**: transparent water is expensive. Opaque
  reflective surfaces (mirror-trick rendering) cost ~half.
  Document the technique.
- **Fog**: software fog (lerp vertex colors toward fog color
  in the vertex shader equivalent) is free. Fog billboards
  are expensive. Default to vertex-color fog.

Bundle into `docs/fill-rate-authoring.md` (sibling reference
doc — not patch).

## Implementation stages

Four stages.

### Stage 1 — Static fill rate estimate ✅ shipped 2026-05-16

Shipped as a live-tick estimate in `SceneStats`/`PS1GodotDock`
instead of an export-time pass — gives authors immediate dock
feedback. AABB-bounding-rect approximation (per-triangle pass
deferred); first `Camera3D` in tree-order as the viewpoint
(per-camera sampling = Stage 2); UI sprites + sky billboards
not yet included.

- `FillRateBudgetScreenAreas = 8.0` + `TranslucentFillCostFactor = 1.5`
  in `SceneStats`.
- New "Fill rate" row in `PS1GodotDock` shows `X× / 8× screen`
  with the existing budget-bar coloring.
- "AABB upper bound" suffix on the dock label so the
  approximation is honest.

Verifiable: open the demo scene with a Camera3D placed, see
the dock row populate. Flip a few `PS1MeshInstance.Translucent`
flags on big meshes, watch the bar move.

### Stage 2 — Multi-camera sampling (deferred)

- `FillRateSampleCameras` property on `PS1Scene`.
- Exporter runs estimate at each sample camera.
- Dock shows worst-case + average.

### Stage 3 — Runtime measurement (blocked on profiling.md)

psxsplash side. `[runtime]` ask.

- Per-triangle area accumulation in OT insertion.
- New field in `FrameProfile` (`paintedPixelsThisFrame`).
- Reported via PCdrv to the profiler dock.

### Stage 4 — Authoring guidance integration (blocked on Pass-4 occluders)

- "Top fill-rate contributors" list in the dock.
- Click-to-focus on source object.
- Auto-suggestion: "consider marking these large meshes as
  occluders" — feeds into `visibility-culling.md` Pass 4.

Stages 2–4 are scoped as a follow-up session — Stage 2 is
small but pairs naturally with Stages 3 + 4 once profiling
and occluders land.

## Open questions / tradeoffs

**Static estimate accuracy.** Without simulating the BVH cull,
the estimate over-counts (assumes everything draws). Mitigation:
estimate also runs the BVH cull at the sample camera positions
and counts only the surviving triangles. Closer to truth, more
expensive at export.

**Transparency cost factors.** The 1.5× factor for semi-trans
is a hand-wave. Real cost depends on overlap density and blend
mode. Document as approximate; refine if real measurements show
otherwise.

**Sub-pixel rejection interaction.** Tiny triangles
(`visibility-culling.md` Pass 3) contribute zero pixels after
the GPU clips them, but the static estimate counts them based
on screen-space area before clipping. Mitigation: the estimate
clamps triangle area to a minimum of 1 pixel; the rejection
pass handles them at runtime.

**Resolution scaling.** PS1 can run 256×224, 320×240, 384×240,
512×240 — the fill budget changes with resolution. The dock
should know the project's target resolution and scale the
budget accordingly. Most projects use 320×240; non-default
needs an authoring surface (already exists in `PS1Scene`).

**PAL vs NTSC.** PAL runs at 25 fps not 30, giving authors more
fill per frame (1.32 Mpix at 25 Hz × 33 Mpix/s = 1.32 Mpix
budget vs NTSC's 1.1). The dock could show both for projects
targeting both regions.

**Occluder integration.** Occluders from
`visibility-culling.md` reduce fill rate by hiding occluded
content. The static estimate should respect occluder volumes
when computing the post-occlusion visible set. Real measurement
already does (it counts triangles surviving cull).

**Threshold colors.** Green < 50% of budget, amber 50–80%,
red 80%+, dark red over budget. Author-tunable thresholds in
project settings.

**Doesn't account for GPU command setup.** The fill rate is
the pixel rate. The GPU also has per-primitive setup cost
(~50 cycles per textured triangle on top of fill). For very
small triangles, setup dominates over fill. Document; the
estimate is "pixel-bound" not "setup-bound."

**What about UI sprites?** UI canvases use `PS1Sprite`
primitives which are pure quads. They have fixed pixel sizes
and contribute predictable fill cost. Include in the estimate;
UI elements should appear in the "top contributors" list when
they're large enough to matter.

## Suggested entries

### For `ROADMAP.md`

> - [ ] **Fill rate & overdraw budget.** Static fill estimate
>       at export time + runtime per-frame measurement.
>       Dock visualization, "top contributors" list, multi-
>       camera sampling. Authoring guidance for
>       semi-transparent budgets. Full design:
>       `docs/fill-rate-budget.md`.

## Changelog

- `2026-05-11` — Document created. Sixteenth patch doc.
  Names the unnamed bottleneck. Pairs with
  `visibility-culling.md` (occluders reduce fill) and
  `profiling.md` (runtime measurement plumbing).
