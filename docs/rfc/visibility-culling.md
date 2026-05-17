# Visibility & culling — design + patch

The renderer has two culling systems today: `RenderWithBVH` does
object-and-triangle frustum cull via a static BVH for exterior
scenes, and `RenderWithRooms` does portal-based room culling for
interiors. Both are correct and ship. But the cull happens at one
layer — coarse spatial — and then every surviving triangle pays
the full GTE-transform + OT-insertion cost.

This doc designs the rejection passes that sit between "this
object's AABB passes the frustum" and "this triangle gets an OT
entry." Five additions, each cheap, each layered.

Drop this file at `docs/visibility-culling.md`.

## Goal

For a typical exterior scene with 5000 authored triangles, the
current path runs GTE transform + OT insert on ~1500 surviving
triangles. After this doc's work, the same scene runs ~800 — the
other 700 are rejected by cheaper checks before they cost a full
GTE round.

The principle: *every triangle that doesn't draw should fail the
cheapest possible test*. Frustum cull at the object AABB is
cheapest; backface cull at the post-transform NCLIP is
next-cheapest; near/far Z reject is in the same tier; sub-pixel
size reject is slightly more expensive. PVS and occluders are
amortized — built once at export, paid for in cheaper runtime
checks.

Non-goal: a fully visibility-correct system. PS1-era games used
approximate visibility and accepted occasional pop-in / over-draw
of marginal cases. We follow the same tradeoff.

## What's in place

- **BVH frustum cull** (`bvh.cpp`). Builds a binary AABB tree
  once at scene load. `cullFrustum` returns triangle refs whose
  parent AABBs intersect the camera frustum. Object-level and
  triangle-level: BVH leaves are individual triangles, so
  per-triangle cull comes free as part of the tree descent.
- **Portal walk** (`renderer.cpp:RenderWithRooms`). Visits rooms
  reachable through frustum-intersecting portals. Each room's
  cell subdivision (~5 m cubes) gives per-cell AABB cull within
  the room.
- **GTE perspective transform** via PSYQo's `rtps`. Transforms a
  vertex from world to screen space; the GTE writes a Z value
  that the runtime uses to compute OT bucket.
- **NCLIP** — the GTE has a free instruction that returns a
  signed value indicating triangle winding in screen space.
  Negative = backface. **Already wired** in `renderer.cpp`
  `processTriangle` (leaf path, ~line 284) and in the skinned-
  mesh path (~line 1464). Static-mesh leaves reject backfaces;
  skinned-mesh path keeps both windings (FBX/GLTF rigs often
  ship mirrored bones) and only drops zero-area degenerates.
- **Near/far Z reject** — `processTriangle` already short-
  circuits when all three SZ values are behind camera (~line
  180) or beyond the fog wall (~line 182). Skinned-mesh path
  has the equivalent at ~line 1442.

So: BVH/portal cull, NCLIP backface, and near/far Z are all
shipped. What remains from this doc is sub-pixel rejection
(Pass 3) and occluder volumes (Pass 4). Pass 5 (PVS) stays
deferred until we have >30-room scenes.

## The five rejection passes

### Pass 1 — Backface cull via NCLIP

**What.** After transforming a triangle's three vertices through
RTPST (the GTE's three-vertex perspective transform), call
NCLIP. NCLIP returns the cross product of the 2D screen-space
edges. Sign tells winding; the runtime can reject negative
(backfacing) without doing any further work.

**Cost.** NCLIP is one GTE instruction, ~8 cycles. Compare to a
full OT insertion (variable, but at least 20+ cycles for the
DMA chain write + bucket math) and the actual GPU draw (much
more). Saving even half the backface triangles is a clear win.

**Implementation.** In `processTriangle` (where each triangle
gets submitted to the OT), insert the NCLIP check immediately
after RTPST:

```cpp
// Existing: RTPST already transformed the three vertices
psyqo::GTE::Kernels::rtpst();   // pseudo
int32_t nclip = psyqo::GTE::readNCLIP();
if (nclip < 0) {
    // Backface — skip OT insertion entirely
    return;
}
// ... existing OT insertion ...
```

PSYQo wraps the GTE registers; the actual call is
`psyqo::GTE::Kernels::nclip()` followed by reading the MAC0
pseudo-register. The trick is making sure the runtime's
existing post-transform code doesn't accidentally clobber MAC0
before the read.

**Authoring surface.** None. Backface cull is universally a
win for opaque double-sided-by-default meshes. Some meshes need
double-sided rendering (foliage, banners, capes). Add a flag
bit on the GameObject (`isDoubleSided`) that skips NCLIP and
draws both sides. Default false; authors flip on for the rare
cases.

**Expected savings.** ~50% of surviving triangles for closed-
hull meshes (cubes, characters, props). Less for open meshes
(terrain, walls). Across a typical scene: ~30–40% reduction in
OT pressure and GPU draw cost.

### Pass 2 — Near/far Z rejection

**What.** After transformation, a triangle's Z values lie in
some range. The GTE writes screen Z to SZ0/SZ1/SZ2. If all
three are below the near plane (< 0 or some authored near
threshold), the triangle is behind the camera. If all three
exceed the far plane (> 2^14, the GTE's screen-Z saturation
point), the triangle is beyond render distance.

**Why it's not free already.** The GTE's hardware clip is
incomplete — it saturates rather than rejects, which produces
artifacts at near-plane crossing. The runtime needs an explicit
check.

**Cost.** Three signed comparisons + one short-circuit. ~6
cycles per triangle.

**Implementation.** Same location as NCLIP, just before OT
insertion:

```cpp
int16_t sz0 = psyqo::GTE::readSZ0();
int16_t sz1 = psyqo::GTE::readSZ1();
int16_t sz2 = psyqo::GTE::readSZ2();

// All three behind near plane — reject
if (sz0 < kNearZ && sz1 < kNearZ && sz2 < kNearZ) return;
// All three past far plane — reject
if (sz0 > kFarZ && sz1 > kFarZ && sz2 > kFarZ) return;
```

`kNearZ` is small but non-zero (the GTE doesn't define the
exact value; conventionally ~1). `kFarZ` is authored — typically
matches the fog far distance for visual consistency.

**Authoring surface.** `PS1Camera.FarZ` already exists for
fog. Reuse it as the far rejection threshold. New optional
`NearZ` for authors with extreme close-up cameras (most use
the default).

**Tradeoff vs partial clipping.** Triangles that *straddle* the
near plane (one vertex behind, two ahead) need true clipping —
splitting the triangle at the plane. Cheap rejection misses
these. Two paths:

- **Cheap path: accept the artifact.** Triangles that straddle
  the near plane just don't render, briefly. Player sees a
  popping at close range. PS1-era games did this all the time;
  the fix is "don't let the camera get that close to geometry."
- **Real path: clip in software.** Split the triangle into two
  fragments at the near plane, draw both. Expensive (extra GTE
  transform per fragment) and rare. Defer to v2.

Default: cheap path. Document the camera-distance rule.

**Expected savings.** ~10% for outdoor scenes (mostly far-Z
rejection that the BVH didn't already catch); ~5% for indoor
(BVH and portals already do most of the work).

### Pass 3 — Sub-pixel rejection

**What.** After perspective divide, a triangle might project to
a region smaller than one pixel — all three vertices land in
the same pixel due to fp12 truncation and distance. Drawing
it is wasted; the GPU will spend setup time on a triangle that
contributes zero visible pixels (or one pixel via the rasterizer's
"any triangle covering the center of a pixel gets the pixel"
rule, which itself is a draw cost).

**How to detect.** Compute the bounding box of the three
screen-space vertices. If `(maxX - minX) <= 1` and
`(maxY - minY) <= 1`, the triangle is sub-pixel.

**Cost.** Six comparisons + four arithmetic ops. ~12 cycles.

**Why it's worth it.** Far-Z prunes "behind the fog" content,
but anything that survives far-Z and is small at distance still
produces tiny on-screen triangles. A 5000-triangle scene at
distance can produce hundreds of sub-pixel tris that each cost
~50 GPU cycles. Sub-pixel rejection saves OT slot + GPU work.

**Implementation.** Right after the near/far check:

```cpp
int16_t sx0 = psyqo::GTE::readSXY0(); // packed XY
int16_t sx1 = psyqo::GTE::readSXY1();
int16_t sx2 = psyqo::GTE::readSXY2();
// Extract X and Y components, compute bounding box span
int16_t minX = min3(x0, x1, x2);
int16_t maxX = max3(x0, x1, x2);
int16_t minY = min3(y0, y1, y2);
int16_t maxY = max3(y0, y1, y2);
if (maxX - minX <= 1 && maxY - minY <= 1) return;
```

**Tradeoff.** Aggressive thresholds (e.g., reject if span ≤ 2)
cull more but produce visible pop-in at distance. The `≤ 1`
threshold is conservative; tris that genuinely contribute to
the image stay drawn.

**Authoring surface.** A scene-level `SubpixelRejectionThreshold`
that authors can tune (1 = conservative default, 0 = disabled,
2+ = aggressive). Default 1.

**Expected savings.** ~5–15% depending on scene depth. Bigger
wins on dense vegetation, crowds, distant terrain.

### Pass 4 — Occluder volumes

**What.** Authors mark big opaque objects (a wall, a cliff face,
a building shell) as occluders. At render time, the runtime
computes a screen-space rectangle from the occluder's AABB. Any
*subsequent* object whose screen-space AABB lies entirely inside
the occluder's rectangle and is farther away can be rejected.

This is the technique Spyro used famously — large "lump" meshes
in the landscape mark valleys and ridges as occluders for
content behind them.

**Cost.** Per-occluder: one AABB-to-screen projection per
frame (~8 vertex transforms). Per-tested-object: one rectangle-
containment check (~4 comparisons). Run-time scaling: O(occluders ×
objects) — cheap because occluder counts are tiny (5–10 per
scene typically).

**Authoring surface.** New `PS1Occluder` node, a sibling of
`PS1MeshInstance`. Just an AABB volume with no rendering — the
volume marks "treat anything behind this as hidden."

```csharp
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_occluder.svg")]
public partial class PS1Occluder : Node3D
{
    // World-space AABB extent
    [Export] public Vector3 Size { get; set; } = new Vector3(4, 4, 4);

    // Optional: skip occluder if camera is inside its volume
    // (otherwise occluders inside the player's room hide everything).
    [Export] public bool SkipIfCameraInside { get; set; } = true;
}
```

**Implementation phases.**

*Phase 4a — Occluder projection.* Compute each occluder's
screen-space rectangle once per frame. Reject occluders facing
edge-on (very thin in screen space).

*Phase 4b — Per-object test.* For each post-BVH-cull object,
project its AABB to screen space, check if fully inside any
occluder rectangle, check if farther.

*Phase 4c — Per-cell test (rooms).* For room cells, same test
applied at cell granularity. Big rooms benefit; small rooms
don't (the portal walk already handles them).

**Tradeoff.** Authors have to mark occluders. Auto-detection
("any mesh > N square meters is an occluder") doesn't work — a
big translucent banner isn't an occluder. Manual marking is the
right authoring surface; the dock can suggest candidates.

**Expected savings.** Highly scene-dependent. Open-terrain
scenes with no big occlusions: 0%. Urban or canyon scenes with
clear sight-blocking walls: 30–50%.

### Pass 5 — Static PVS (precomputed visibility set)

**What.** Interior scenes with rooms have a finite topology. For
each room, *at export time*, compute which other rooms are ever
visible from it (through any portal arrangement, from any
camera angle within the room). Store the per-room PVS as a
bitfield: one bit per other room.

At runtime, the portal walker uses the PVS as a fast-path:
instead of testing every portal against the frustum, it tests
only portals leading to rooms in the current room's PVS.

**Cost at runtime.** Trivial — one bitfield AND per portal
test. The cost is in the export-time computation.

**Export-time cost.** For each room, enumerate camera positions
within the room (sample the room's AABB), trace portal walks
from each sample, union the reachable rooms. With careful
sampling (8–16 positions per room), the PVS is conservative
(may include rooms that aren't *always* visible) but never
misses.

**Why bother.** The portal walker is already fast — per-room
cell subdivision means each room's visible content checks
small AABBs. PVS only matters for very high room counts
(50+ rooms). For typical PS1-era games (10–20 rooms), the
portal walker is already at its ceiling.

**Recommendation.** Defer until a scene has > 30 rooms and the
profiler shows portal-walk time as a hotspot. Document the
design but don't implement until needed.

**Authoring surface.** None at the node level. A scene-level
`UsePvs` flag in `PS1Scene` enables the export-time
computation.

## What's already covered (don't re-do)

The BVH frustum cull and portal walk are the existing layer.
This doc adds passes that sit between those and the GPU. Don't
attempt to replace BVH — it's the right primitive at the right
layer.

The triangle-level frustum cull *inside* the BVH (per-tri AABB
test against frustum) is also already there. Adding another
per-triangle frustum check on top would be redundant.

## Implementation order

By value/cost ratio. Each independent.

1. ~~**Pass 1 (Backface cull).**~~ Already shipped (NCLIP in
   `processTriangle` leaf path).
2. ~~**Pass 2 (Near/far rejection).**~~ Already shipped (SZ
   short-circuits in `processTriangle` and skinned path).
3. ~~**Pass 3 (Sub-pixel rejection).**~~ Shipped 2026-05-16 via
   `isSubpixel()` in `triclip.hh` and call sites in
   `processTriangle` leaf branch + `renderSkinnedObjects`.
   Hardcoded threshold = 1; per-scene authoring deferred.
4. **Pass 4 (Occluders).** Deferred to its own session — needs
   `PS1Occluder` node, exporter wiring, splashpack format bump
   (v33), and runtime AABB-to-screen projection + per-object
   containment test. Estimated 3–4 commits as its own bundle.
5. **Pass 5 (PVS).** Deferred to "if needed."

## Open questions / tradeoffs

**NCLIP and degenerate triangles.** A truly degenerate triangle
(three colinear vertices) has NCLIP = 0. The runtime should
reject these — they produce no pixels but cost OT slot. Add
`if (nclip <= 0) return;` instead of `< 0`.

**Double-sided meshes.** Authors with double-sided materials
need to either: (a) author both winding orders in the mesh
(2x triangles, no NCLIP needed), or (b) flag the GameObject as
`isDoubleSided` (skip NCLIP, draw all). Default to (b) — the
authoring effort is small (one checkbox) and the runtime cost
is the same as today.

**Near-plane clipping correctness.** The cheap-reject path
produces visible pop-in when geometry crosses the near plane.
For player-attached cameras with collision, this rarely
happens (camera stops before reaching geometry). For
free cameras (photo mode, debug), authors need to know to
keep some distance. Document.

**Sub-pixel threshold per scene type.** Outdoor scenes with
fog can tolerate aggressive thresholds (3, even). Indoor
scenes with close geometry need conservative thresholds (1).
The per-scene `SubpixelRejectionThreshold` lets authors tune.

**Occluder selection authoring UX.** Authors won't always
remember to add occluders. The dock should auto-suggest:
"this scene has 47% over-draw — consider adding occluders
to these large meshes." Builds on the fill-rate budget doc.

**Combining occluders.** Two adjacent occluders should
effectively combine into a larger one (for objects that
straddle their boundary). v1 tests each separately — accepts
the artifact that an object falling between two adjacent
occluders fails the per-occluder containment test. v2 could
project a combined screen-space outline; deferred.

**Occluders and transparency.** A semi-transparent mesh
should not be marked an occluder. Authoring surface enforces
this: occluders are explicit nodes, not derived from mesh
material.

**PVS for non-room scenes.** PVS doesn't apply to
`RenderWithBVH` outdoor scenes — there's no room topology.
For huge outdoor scenes, the better answer is chunk
streaming (already designed) plus occluders.

**Combined rejection ordering.** Order the checks by cost:

```cpp
// In processTriangle, after RTPST:
if (subpixel(sx0, sx1, sx2)) return;  // cheapest
if (nearOrFar(sz0, sz1, sz2)) return;
if (backface(nclip)) return;
// Survived all rejection — insert into OT
```

Actually — backface first, since NCLIP is genuinely the
cheapest (one register read) and rejects the most cases.
Then near/far, then sub-pixel. Microoptimization, but the
profile will show the right answer.

**Measuring the wins.** Each pass should be toggleable at
runtime via a debug flag, so authors can measure the
contribution of each. Adds three flag bits to a debug
configuration. The profiler dock (`profiling.md`) gets a
"culling efficiency" row: percentage of triangles
rejected at each stage. Authors see which culls help their
scene the most.

## Suggested entries

### For `docs/psxsplash-improvements.md`

> ### N+M. Post-transform triangle rejection passes
>
> **Problem.** Every triangle that survives BVH/portal cull
> pays the full OT insertion + GPU submit cost, even
> backfaces, behind-camera triangles, and sub-pixel
> triangles. The GTE has a free NCLIP instruction that the
> runtime doesn't currently call.
>
> **Proposed direction.** Add three cheap rejection passes
> in `processTriangle` between RTPST and OT insertion:
> backface (NCLIP), near/far Z, sub-pixel size. Estimated
> 30–50% reduction in OT pressure and GPU work for typical
> scenes. Full design: `docs/visibility-culling.md`.
>
> **Status.** Filed.

### For `ROADMAP.md`

> - [ ] **Post-transform triangle rejection.** Backface cull
>       (NCLIP), near/far Z reject, sub-pixel rejection in
>       the per-triangle path. Toggleable per-pass for
>       measurement.
> - [ ] **Occluder volumes (`PS1Occluder`).** Author-marked
>       AABB regions; objects behind survive cheap screen-
>       space rectangle test. Pairs with fill-rate doc.

## Changelog

- `2026-05-11` — Document created. Fifteenth patch doc in
  the series.
