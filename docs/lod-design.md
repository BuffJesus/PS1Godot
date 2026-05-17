# LOD meshes — design + patch

**Status (2026-05-17):** No implementation yet. PS1MeshInstance /
PS1MeshGroup have no Lod1Mesh/Lod2Mesh exports, splashpack has
no LodEntry/LodTable section, runtime has no `IsLodEnabled` flag /
`SelectLODs` / `LodRuntimeState`. The doc names "v24 splashpack"
as the target bump — that version slot is long gone (we're at
v32); a real implementation just appends to v33+. Sized as
~3 commits: Stage 1 (exporter + writer + format bump), Stage 2
(runtime LOD selection + BVH skip), Stage 3 (dock LOD coverage
readout + per-mesh tri-count badges). Deferred to its own
session — pairs naturally with a "large-scene perf" focus.

Design doc for the LOD bullet in `ROADMAP.md` § Rendering options:

> **LOD meshes per `PS1MeshInstance`.** `LODs` = ordered
> `(Mesh, distanceMeters)[]`. Exporter packs every LOD into the
> atlas; runtime swaps by distance to camera with a small
> hysteresis band. PS1 poly budgets are small enough that a naïve
> two-or-three-step swap is all anyone actually wants. **[runtime]**

This expands the bullet into a concrete cut. Authoring + exporter changes
land in PS1Godot; runtime selection is a `[runtime]` ask filed against
psxsplash. Drop this file at `docs/lod-design.md`.

## Goal

Reduce visible-triangle count at distance without losing the silhouette.
LOD pays for itself anywhere a scene has more than a handful of mid-poly
props on screen at once — exactly the regime the large-RPG reference
warns about (`docs/ps1_large_rpg_optimization_reference.md` § "Ordering
tables / object-count discipline").

Non-goal: continuous / progressive LOD. PS1 budgets reward discrete
swaps with hard distance thresholds. Two extra levels per object (LOD0
authored + up to LOD1 / LOD2) is the ceiling worth shipping.

## What's already in place

The renderer has three render paths today
(`psxsplash-main/src/renderer.cpp`):

1. `Render(objects)` — fallback, no culling. Walks every active object
   and submits all its triangles.
2. `RenderWithBVH(objects, bvh)` — exterior scenes. BVH returns
   triangle refs grouped by object; the loop transforms each object
   once and submits visible triangles.
3. `RenderWithRooms(objects, rooms, …)` — interior scenes. Portal walk
   feeds a per-room tri-ref renderer that reuses the same per-object
   transform pattern.

All three converge on the same inner pair —
`setupObjectTransform(obj, cameraPosition)` once, then
`processTriangle(obj->polygons[i], …)` per triangle. That's the seam
LOD selection has to slot into.

GameObject is 92 bytes with a hard `static_assert` in
`gameobject.hh`. The `polygons` pointer and `polyCount` are mutable
runtime state (skinned-mesh setup already overwrites them in
`scenemanager.cpp` after upload). LOD swaps will use the same trick.

## Design

### Authoring (PS1Godot side)

New export group on `PS1MeshInstance`:

```csharp
[ExportGroup("PS1 / LOD")]
[Export] public Mesh? Lod1Mesh { get; set; } = null;
[Export(PropertyHint.Range, "1.0,200.0,0.5,suffix:m")]
public float Lod1Distance { get; set; } = 12.0f;

[Export] public Mesh? Lod2Mesh { get; set; } = null;
[Export(PropertyHint.Range, "1.0,200.0,0.5,suffix:m")]
public float Lod2Distance { get; set; } = 30.0f;
```

Flat properties, not an array sub-resource. Two reasons: PS1 only ever
wants ≤ 2 LOD steps, and Godot's array-of-resource inspector adds two
clicks per entry over an inspector slot. Authors who want zero LODs
leave the meshes null — the exporter ignores the LOD block.

Distances are author-facing meters; exporter converts to the same fp12
units the renderer uses for object position.

`PS1MeshGroup` gets the same properties (it shares the GameObject
emission path).

Validation at export time:
- `Lod2Distance > Lod1Distance` — warn otherwise.
- Each LOD mesh must use a subset of the LOD0 mesh's texture pages —
  no new atlas entries permitted on LOD1/2, since the point is to
  cull triangles, not to bloat VRAM. Exporter warns and rejects new
  pages.
- Triangle count must strictly decrease per step. Author error
  otherwise; warn loudly.

### Splashpack format (v24)

GameObject is frozen. LOD data lives in a side table, exactly like
skinned meshes / UI models / animations.

Header additions (currently 152 B → 160 B):

```cpp
// At end of SPLASHPACKFileHeader, after uiModelCount/uiModelTableOffset:
uint16_t lodEntryCount;
uint16_t pad_lod;
uint32_t lodTableOffset;
```

One row per LOD-enabled object:

```cpp
struct SPLASHPACKLodEntry {
    uint16_t gameObjectIndex;     // Which GameObject this is for
    uint8_t  lodCount;            // 1 or 2 (LOD1 only, or LOD1+LOD2)
    uint8_t  _pad0;
    uint32_t lod1PolygonsOffset;  // .splashpack byte offset
    uint16_t lod1PolyCount;
    uint16_t _pad1;
    int32_t  lod1DistSqFp24;      // (meters * 4096)² in i32-safe range
    uint32_t lod2PolygonsOffset;  // 0 if lodCount=1
    uint16_t lod2PolyCount;       // 0 if lodCount=1
    uint16_t _pad2;
    int32_t  lod2DistSqFp24;
};
static_assert(sizeof(SPLASHPACKLodEntry) == 32, "SPLASHPACKLodEntry must be 32 bytes");
```

LOD triangle streams append after the existing per-object triangle
streams the writer already emits. Same `Tri` format, same atlas
indices — they share the LOD0 atlas entries by construction (see
validation above).

Squared distances stored to avoid an `isqrt` on every object every
frame. The fp24 of a 200 m threshold is `(200·4096)² ≈ 6.7e11` — fits
i32 with headroom. Authors who set distances beyond ~360 m would
overflow; the exporter clamps and warns. (Realistic PS1 view
distances are well under that, especially through fog.)

### Runtime selection (psxsplash side)

GameObject gets one new flag bit (currently bits 0–5 used):

```cpp
typedef Utilities::BitSpan<bool, 6> IsLodEnabled;
```

SceneManager loads the LOD table into a parallel runtime state array:

```cpp
struct LodRuntimeState {
    uint16_t gameObjectIndex;
    uint8_t  currentLevel;   // 0 = LOD0, 1 = LOD1, 2 = LOD2
    uint8_t  _pad;
    // Cached pointers for fast swap (avoids one indirection per frame)
    Tri*     lod0Polygons;   uint16_t lod0PolyCount;
    Tri*     lod1Polygons;   uint16_t lod1PolyCount;
    Tri*     lod2Polygons;   uint16_t lod2PolyCount;
    int32_t  lod1DistSqFp24;
    int32_t  lod2DistSqFp24;
};
```

The renderer calls a new `SelectLODs(cameraPosition)` once at the top
of each render pass, before any iteration. For each entry:

```cpp
int32_t dx = obj->position.x.raw() - cameraPosition.x.raw();
int32_t dy = obj->position.y.raw() - cameraPosition.y.raw();
int32_t dz = obj->position.z.raw() - cameraPosition.z.raw();
int64_t dSq = (int64_t)dx*dx + (int64_t)dy*dy + (int64_t)dz*dz;

uint8_t newLevel = 0;
if (state.lod2PolyCount && dSq > state.lod2DistSqFp24) newLevel = 2;
else if (state.lod1PolyCount && dSq > state.lod1DistSqFp24) newLevel = 1;

// 5% hysteresis to stop flicker at the boundary
if (newLevel != state.currentLevel) {
    int32_t boundary = (newLevel > state.currentLevel)
        ? thresholdFor(newLevel)
        : thresholdFor(state.currentLevel);
    int64_t hysteresis = boundary / 20;  // 5% of the squared distance
    bool crossedHard = (newLevel > state.currentLevel)
        ? (dSq > boundary + hysteresis)
        : (dSq < boundary - hysteresis);
    if (crossedHard) state.currentLevel = newLevel;
}

// Swap polygons in place — render loops index obj->polygons/polyCount
switch (state.currentLevel) {
  case 0: obj->polygons = state.lod0Polygons; obj->polyCount = state.lod0PolyCount; break;
  case 1: obj->polygons = state.lod1Polygons; obj->polyCount = state.lod1PolyCount; break;
  case 2: obj->polygons = state.lod2Polygons; obj->polyCount = state.lod2PolyCount; break;
}
```

That's the entire runtime hot path: one per-LOD-object distance test
per frame, plus a pointer swap on level transitions.

### BVH interaction — the actual hard part

The BVH (`bvh.cpp`) was built against LOD0 triangles. Its triangle
refs are valid only for LOD0. Two options:

**Option A (chosen): LOD objects bypass the BVH triangle stream and
render via the per-object path.**

Once `IsLodEnabled` is set, the BVH-cull loop skips that object
(same shape as the existing `isDynamicMoved()` / `isSkinned()` /
`isUIModelTarget()` skips around `renderer.cpp:RenderWithBVH`). The
existing dynamic-moved fallback loop already does whole-object
frustum cull → draw all triangles; LOD'd objects flow through that
unchanged. The BVH builder excludes LOD-enabled objects from its
input set.

Cost: LOD-enabled objects lose triangle-level frustum culling and
get only whole-object AABB culling. Acceptable, because LOD-enabled
objects are by definition smaller-bounded props for which AABB cull
is already most of the win — and LOD1/2 have fewer triangles to
process anyway. The BVH stays compact (only static high-detail
geometry).

**Option B (rejected): rebuild the BVH against the active LOD set
on every swap.** No. The BVH is built once at scene load and we'd be
churning it every time the player crosses a distance threshold.

A future optimization would be a small per-LOD BVH per object, but
that's not worth shipping in v1.

### Interior scenes

`RenderWithRooms` shares the per-object render shape with the BVH
path (via `renderTriRefs`). LOD-enabled objects need the same skip
in the room-cell tri-ref loop, plus a second per-room pass that
renders LOD-enabled objects whose AABB intersects the active room's
AABB. Cheap (whole-object test against authored room boxes) and
preserves portal occlusion for the non-LOD'd majority.

Hint to the author: LODs matter less in interiors than exteriors.
Most interior props are room-scoped and rarely viewed from far
enough to benefit. Document this in the LOD authoring guide; don't
build aggressive features around it.

## Implementation stages

Each stage ships independently and unlocks something visible.

### Stage 1 — Authoring + exporter, no runtime (PS1Godot)

- Add Lod1Mesh/Distance, Lod2Mesh/Distance to `PS1MeshInstance` and
  `PS1MeshGroup`.
- `SceneCollector` resolves the LOD meshes, runs them through
  `PSXMesh.FromGodotMesh` with the same atlas and texture indices as
  LOD0 (asserts no new pages — emits warning + skips LOD entry on
  violation).
- `SceneObject` grows two optional `PSXMesh?` fields + their
  thresholds.
- `SplashpackWriter` bumps version to 24, writes the new header
  fields, emits an LOD table after the UI model table, appends the
  LOD triangle streams after the existing per-object streams.
- Old runtimes will fail the version assert. That's the contract.

Verifiable without runtime support: hex-dump the splashpack, confirm
the LOD table and triangle streams exist and reference valid atlas
entries. No visible change in-game yet (runtime ignores the new
section).

### Stage 2 — Runtime selection (psxsplash, [runtime])

Filed against `docs/psxsplash-improvements.md` as a new entry — see
suggested text below. Until it lands upstream, sits as a local patch
in `patches/psxsplash/` per the project's policy.

- `gameobject.hh`: add `IsLodEnabled` bit + accessor.
- `splashpack.hh / .cpp`: parse `lodEntryCount` + `lodTableOffset`,
  populate `LodRuntimeState[]` in `SplashpackSceneSetup`.
- `scenemanager.cpp`: in `InitializeScene`, walk the LOD table,
  cache LOD0 polygons/polyCount per entry (since LOD0 is the
  GameObject's authored mesh), resolve LOD1/2 polygon pointers from
  the splashpack base + their offsets, set the
  `IsLodEnabled` flag on each target GameObject. Exclude LOD-enabled
  objects from BVH input.
- `renderer.cpp`: add `SelectLODs(cameraPosition)` called from
  `Render`, `RenderWithBVH`, and `RenderWithRooms` before the main
  iteration. Add `isLodEnabled()` skips in the BVH and room-cell
  tri-ref loops; the existing dynamic-pass fallback picks them up
  naturally.

### Stage 3 — Tooling polish (PS1Godot, post-runtime)

- Plugin panel: scene budget bar gains a "LOD coverage" readout —
  what fraction of total scene triangles are LOD-eligible, what the
  effective triangle count is at "typical view distance" (author-
  configured on `PS1Scene`).
- Per-object inspector hint: triangle count comparison badge
  ("LOD0: 412 → LOD1: 96 (-77 %)") next to the LOD fields. Pulled
  directly from Godot's mesh data; no exporter round-trip needed.
- Optional: a "Generate LOD" editor action that runs Godot's
  `MeshUtils.GenerateLod` on the LOD0 mesh at 50 % / 20 % ratios and
  fills the Lod1/Lod2 slots. Authors override with hand-authored
  meshes when the auto result is ugly.

## Open questions / tradeoffs

**Screen-space vs world-space LOD.** Screen-space (object bounding
sphere radius / distance) is more correct: a big distant building
should stay at LOD0 longer than a small distant prop, because the big
one occupies more pixels. World-space distance treats them the same.

For v1, world-space with per-object authored distances is enough.
Authors compensate for size by setting bigger distances on bigger
props. If this becomes a real friction point (e.g. a town scene where
authors are tweaking 30 distances individually), revisit with
screen-space.

**Distance to camera position vs view-space Z.** Camera position is
the obvious metric and matches the per-object AABB cull's natural
frame. View-space Z would be marginally cheaper (one rtps already
runs for AABB cull) but the obvious metric matters more here — keep
it euclidean.

**LOD0 in the table too?** No. GameObject.polygons is LOD0 by
convention. LOD table entries are LOD1+. Keeps the no-LOD common
case zero-overhead and the runtime selector trivially correct when
`currentLevel == 0`.

**Skinned meshes.** Out of scope for v1. Skinned LOD would mean
parallel bone-index tables per LOD level — meaningful work and
unclear payoff (skinned characters tend to be on-screen at varying
distance for short periods). Reassess if a real workload demands it.

**Hysteresis %.** 5 % of squared distance is a guess. Worth a real
test once the runtime lands: walk a debug camera radially through a
swap boundary and log frame-by-frame level transitions. The
right number is "smallest that prevents flicker on a 30 fps camera
moving at typical walk speed."

## Suggested entry for `docs/psxsplash-improvements.md`

A new entry in the "High leverage" or "Medium" block:

> ### N+M. Per-object LOD mesh selection
>
> **Problem.** psxsplash renders every GameObject at one detail level
> for the lifetime of a scene. Large exteriors hit the OT and CPU at
> distance even when most props are tiny on screen. BVH triangle-cull
> helps but doesn't reduce triangle work below "one full-detail mesh
> per visible object."
>
> **Why we care.** Large-world scenes (the `ps1_large_rpg_optimization_reference`
> regime) need a way to drop poly count for distant props without
> rebuilding the scene. Junkrunner64 on N64 showed how much LOD pays
> for itself on a contemporary CPU/poly budget; PS1 wins are
> proportionally similar.
>
> **Proposed direction.** Side-table `SPLASHPACKLodEntry[]` in the
> splashpack pointing at additional triangle streams per object,
> plus a header field for the table count + offset. Add an
> `IsLodEnabled` GameObject flag. SceneManager populates a parallel
> `LodRuntimeState[]` cache at load. Renderer runs a `SelectLODs`
> pass before each frame that picks LOD0 / 1 / 2 by squared distance
> with hysteresis and swaps the GameObject's `polygons` /
> `polyCount` in place. LOD-enabled objects are excluded from BVH
> input and flow through the existing dynamic-pass per-object render
> path (whole-object AABB cull then draw all). Full design:
> `docs/lod-design.md`.
>
> **Status.** Filed (this doc). PS1Godot side (Stage 1) ships
> independently of upstream. Local runtime patch lands as
> `patches/psxsplash/lod-runtime.patch` until upstreamed.
>
> **Evidence.** _(empty until PS1Godot Stage 1 ships and we have a
> real splashpack to test against.)_

## Changelog

- `2026-05-11` — Document created. No code yet; this is the design
  draft. Stage 1 to follow.
