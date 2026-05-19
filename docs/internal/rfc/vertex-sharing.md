# Vertex sharing & mesh format — design + patch

**Status (2026-05-17):** Stages 1 + 2 shipped as splashpack v31
(commits `06abec9` + `dc899d1`, host-side roundtrip verifier
`77999d2`). The shipped design differs from this doc in two ways
worth knowing before reading further:

1. **Option B, not Option A.** Vertices carry `pos + uv + color`
   (12 B) and faces are 3 × u16 indices + per-face normal/tpage/
   CLUT (20 B). UV+color sharing is included — triangles that
   differ in UV or color at a shared position get distinct vertex
   entries. See `psxsplash-main/src/mesh.hh` (`Vertex`, `Face`,
   `MeshBlob`, `expandTri`).
2. **Hard cut, not coexistence flag.** v31 is the cutover; the
   splashpack loader hard-asserts `version >= 32` and there is no
   format-flag fallback to triangle-soup. The legacy `Tri[]`
   layout is retained only for skinned meshes (per-tri-vertex bone
   indices block dedup).

Stage 0's "rtpst audit" concern is moot — the static-mesh hot path
in `renderer.cpp:1059` already uses `Kernels::rtpt()` (3-vertex
GTE transform); only the skinned-mesh path uses per-vertex `rtps`
because each bone needs a distinct matrix.

Stage 3 (pre-transform vertex cache in scratchpad) deferred —
overlaps with `scratchpad-cache.md`'s allocation-map work; revisit
once profiling.md lands and the cost is measurable.

The hidden 3× win nobody's used. The current mesh format is
triangle-soup: each `Tri` carries its own three vertices, even
when adjacent triangles share corners. A cube authored as 12
triangles has 36 vertex positions; 8 are unique. That's 4.5×
waste in GTE transform cost.

This doc designs an indexed vertex format and the runtime path
that uses it.

Drop this file at `docs/vertex-sharing.md`.

## The math

A unit cube:
- Vertices: 8 unique.
- Triangles: 12.
- Triangle-soup format: 12 × 3 = 36 vertex positions stored.
- Indexed format: 8 vertex positions + 12 × 3 = 36 indices.

GTE transform cost per vertex: ~20 cycles for `rtps` (single
vertex transform). With triangle-soup:
- 36 transforms × 20 = 720 cycles per cube.

With indexed sharing:
- 8 transforms × 20 = 160 cycles, plus index dereference per
  vertex (~5 cycles each).
- Total: 160 + 36 × 5 = 340 cycles per cube.

That's **52% saving** for a cube. Real meshes are worse than
cubes — a typical authored character has ~40% vertex
duplication, ~60% shared. Savings scale with the shared
fraction.

For a 5000-triangle scene at 30% vertex sharing:
- Soup format: 15000 transforms × 20 = 300,000 cycles/frame.
- Indexed: ~10500 transforms × 20 = 210,000 cycles, plus
  15000 index dereferences × 5 = 75,000 cycles.
- Total: 285,000 cycles vs 300,000.

Modest at 30% sharing. At 60% sharing (typical hand-modeled
content):
- Indexed: 6000 transforms × 20 = 120,000 + 75,000 indices =
  195,000 cycles.
- Savings vs soup: 105,000 cycles = ~10% of frame budget.

Bigger wins for less-shared content because the GTE transform
dominates. The actual savings depend heavily on mesh authoring
quality — well-built meshes with continuous topology save more.

## What's in place

- **`Tri` format**: three `Vertex` structs (position + UV +
  color), inline in the `Tri`. ~48 bytes per triangle.
- **`PS1MeshInstance`** authoring node references a Godot
  `Mesh`. The exporter walks each surface, emits one `Tri`
  per triangle.
- **GTE transform via `rtps`** — one vertex at a time. The
  runtime calls it three times per triangle in
  `processTriangle`.
- **GTE has an `rtpst` instruction** — three vertices in one
  call. The runtime should be using this; check whether
  PSYQo's wrappers do.

The triangle-soup format is the right shape for the *current*
indexed-vertex-less rendering path. The format change is what
enables the indexed path.

## Design

### Indexed mesh format

New mesh layout for v25 splashpack:

```cpp
struct IndexedMesh {
    uint16_t vertexCount;
    uint16_t triangleCount;
    Vertex*  vertices;      // vertexCount × 24 bytes (pos + uv + color)
    Index3*  indices;       // triangleCount × 6 bytes (3 × uint16)
};

struct Index3 {
    uint16_t v0;
    uint16_t v1;
    uint16_t v2;
};
```

Vertex storage: `vertexCount × 24` bytes (each vertex carries
position, UV, color, even if shared — UVs and colors vary per
triangle corner so they're stored per-vertex, not per-triangle).

Index storage: `triangleCount × 6` bytes.

For a 100-vertex 50-triangle mesh:
- Triangle soup: 50 × 48 = 2400 bytes.
- Indexed: 100 × 24 + 50 × 6 = 2700 bytes.

Wait — for small meshes with high sharing the indexed format is
*bigger* (you pay for the indices). The win is purely in transform
cost. For storage, the formats are comparable.

For a 200-vertex 400-triangle mesh:
- Soup: 400 × 48 = 19200 bytes.
- Indexed: 200 × 24 + 400 × 6 = 7200 bytes.

Storage starts mattering at higher sharing ratios. Good.

### The UV-color problem

The big wrinkle: PS1 textures use *per-vertex* UVs and colors
that often differ between two triangles sharing a vertex
position. Position is shared; UV+color is not.

Three options:

**Option A: Position-only sharing.** Indices point at positions;
UV+color is per-triangle-corner. The per-triangle storage looks
like:

```cpp
struct Tri {
    uint16_t posIdx[3];      // 6 bytes
    UV uv[3];                // 6 bytes (2 bytes each)
    Color color[3];          // 12 bytes (4 bytes each, including STP bit)
    // ... clut + tpage info
};
```

Position transforms run once per unique position. UV/color don't
need transformation, so per-triangle storage is fine.

This is the right answer. Implementation effort is moderate
but the saving is concentrated where it matters.

**Option B: Full vertex sharing.** Indices point at full
vertices (position + UV + color). Only triangles that share
identical UV+color at a corner get sharing. Lower sharing
ratio, simpler runtime.

In practice, hand-authored meshes have UV seams everywhere
(character textures wrapping, hard color edges). Sharing rate
drops from ~60% to ~15%. Not worth the complexity.

**Option C: Sub-meshes by material.** Group triangles by
material (same CLUT + tpage), share within group. Real PS1
games did this. Tighter sharing, slightly harder authoring.

For v1, ship Option A. Re-evaluate Option C if profiling shows
the format is still a bottleneck.

### Runtime per-triangle path

Updated `processTriangle`:

```cpp
void processTriangle(const Tri& tri, const Vertex* vertices) {
    const Vertex& v0 = vertices[tri.posIdx[0]];
    const Vertex& v1 = vertices[tri.posIdx[1]];
    const Vertex& v2 = vertices[tri.posIdx[2]];
    
    // Transform via rtpst (3-vertex GTE)
    psyqo::GTE::writeUnsafe<psyqo::GTE::Register::VXY0>(...);
    // (write all three vertices)
    psyqo::GTE::Kernels::rtpst();
    
    // ... NCLIP, near/far, sub-pixel checks (visibility-culling.md)
    
    // Read transformed values
    int32_t nclip = psyqo::GTE::readNCLIP();
    if (nclip < 0) return;
    // ... compute OT bucket, write primitive ...
}
```

The pattern: per-triangle, look up three vertices via index,
load into GTE registers, transform.

**Vertex cache.** If the same vertex is used by adjacent
triangles, we re-transform it. The GTE doesn't cache; each
RTPS run is independent. Mitigation: order triangles such
that adjacent triangles share vertices and the GTE pipeline
absorbs the cost via register reuse. Hard to optimize for
without compiler help; defer.

A more aggressive approach is to transform *all* visible
vertices once at the start of the object, then per-triangle
just looks up the pre-transformed screen-space data. Costs
scratchpad or stack RAM per visible vertex (8 bytes per
SXY/SZ). For a 100-vertex mesh: 800 bytes, fits in
scratchpad's hot region. Worth exploring; deferred to v2
because it changes the renderer's data flow.

### Exporter

The hard part is detecting shared vertices in the source mesh.
Godot's `Mesh` exposes vertices already indexed (it uses index
buffers natively for rendering). The exporter reads the index
data directly:

```csharp
// In SceneCollector / mesh export:
var arrays = surface.GetArrayData();
Vector3[] positions = arrays[Mesh.ArrayType.Vertex];
Vector2[] uvs = arrays[Mesh.ArrayType.TexUv];
Color[] colors = arrays[Mesh.ArrayType.Color];
int[] indices = arrays[Mesh.ArrayType.Index];

// Build indexed format
var meshOut = new IndexedMesh
{
    VertexCount = positions.Length,
    TriangleCount = indices.Length / 3,
    Vertices = positions.Select(p => Quantize(p)).ToArray(),
    // ...
};
```

Godot's index buffer doesn't account for the position-only-
sharing trick — it has unique-vertex indices. For Option A,
the exporter needs a separate pass that *de-duplicates by
position*: builds a position → index map, emits unique
positions, builds per-triangle position indices.

Pseudocode:

```csharp
var posMap = new Dictionary<Vector3, int>();
var uniquePositions = new List<Vector3>();
var triIndices = new List<(int, int, int)>();

for (int t = 0; t < triangleCount; t++) {
    int i0 = indices[t * 3];
    int i1 = indices[t * 3 + 1];
    int i2 = indices[t * 3 + 2];
    
    int p0 = GetOrAddPosition(posMap, uniquePositions, positions[i0]);
    int p1 = GetOrAddPosition(posMap, uniquePositions, positions[i1]);
    int p2 = GetOrAddPosition(posMap, uniquePositions, positions[i2]);
    
    triIndices.Add((p0, p1, p2));
    // UVs and colors stored per-triangle-corner (still indexed by i0/i1/i2)
}
```

### Backwards compatibility

Existing splashpacks use the triangle-soup format. Two
strategies:

**Strategy A: Format coexistence.** Splashpack header gains a
flag bit `usesIndexedMeshes`. Old splashpacks: flag clear,
loader reads triangle-soup. New: flag set, loader reads
indexed. Renderer dispatches to the appropriate path.

**Strategy B: One-way migration.** All new splashpacks are
indexed. The exporter always produces indexed. The runtime
only reads indexed. Old splashpacks fail the version check.

Pick A for the transition period; B once everything's settled.
The format coexistence flag lives in the existing version-
header field.

### Indexed BVH

The BVH currently references triangles directly. With indexed
meshes, the BVH leaf needs (triangleIndex, meshObjectId) —
which can resolve to (vertexIndices) at render time.

```cpp
struct BVHLeaf {
    uint16_t objectIndex;     // GameObject in m_gameObjects
    uint16_t triangleIndex;   // index into that object's triIndices
};
```

The BVH cull pass returns leaves in the same shape as today;
the per-triangle path looks up the vertex data via the
indirection.

## Implementation stages

Three stages.

### Stage 1 — Exporter produces indexed meshes

PS1Godot side. No runtime change yet (runtime keeps reading
triangle-soup).

- Add de-duplication pass after `Mesh.GetArrayData`.
- Emit both formats — runtime can use either based on the
  format flag.
- Verify deterministic ordering (same source mesh always
  produces same vertex list).

### Stage 2 — Runtime indexed path

psxsplash side. `[runtime]` ask.

- New code path in `processTriangle` that takes vertices +
  indices.
- Flag-bit-driven dispatch in `RenderWithBVH` and
  `RenderWithRooms`.
- BVH updated to work with indexed format.

Verifiable: load an indexed-format splashpack, render
identically to the triangle-soup version (same pixels output),
with reduced GTE transform count visible in the profiler.

### Stage 3 — Vertex pre-transform optimization

The aggressive variant: transform all visible vertices once
per object at the start, then per-triangle just looks up
pre-transformed screen-space data. Bigger win for high-share
meshes; bigger memory footprint.

Defer until Stage 2's wins are measured and indexed format is
proven stable.

## Open questions / tradeoffs

**Index width.** uint16 supports up to 65,536 vertices per
mesh. Real PS1 meshes have far fewer. uint8 would save bytes
but limits per-mesh vertex count to 256 — too tight for
characters. Stick with uint16.

**Cache locality of vertex array.** Random access into the
vertex array hurts cache. Mitigation: vertices stored in
position order (Hilbert curve or similar spatial sort). The
exporter sorts. Probably minor effect; measure first.

**Skinned meshes.** Per-bone transforms make vertex sharing
harder — a vertex's transform depends on its weighted bone
attachments. Two vertices at the same position with different
bone weights are *not* shareable. Skinned meshes likely stay
triangle-soup or use a custom indexed format. Defer to
skinned-mesh-specific design.

**Animated meshes.** Frame-blend morph targets (rare on PS1)
make vertex positions dynamic. Same problem as skinning;
defer.

**UV+color sharing missed.** Option A shares positions only.
A mesh where adjacent triangles share UV+color (smooth-shaded
regions) doesn't benefit. Adding sub-mesh-by-material grouping
(Option C) would help but at authoring complexity cost.
Document the limitation; revisit if profiling shows it
matters.

**Memory cost of the indirection.** For very small meshes
(< 20 vertices), the indexed format adds more bytes than it
saves. Mitigation: the exporter could choose per-mesh whether
to emit indexed or soup. Heuristic: "if shared fraction <
20%, emit soup." Conservative threshold; refine if needed.

**Compression of vertex data.** PSn00bSDK demos use
quantized vertex positions (i16 instead of i32). PSYQo's
`FixedPoint<12>` is 32-bit. Storage win: ~50% on positions.
Defer to a separate "compact vertex format" doc — the
indexed-format work is separately valuable.

**Runtime decode cost.** Index lookup adds ~5 cycles per
vertex per triangle. For a 500-triangle scene at 60% share,
that's 1500 lookups × 5 = 7500 cycles. Compare to ~10k
cycles saved on transforms — net positive but small. The
wins scale with share ratio; small meshes are nearly
break-even.

**Inertia.** The triangle-soup format has been in every
splashpack since v1. Migrating involves an exporter change,
a runtime change, and a re-export of all existing demos.
Plan for a transition period where both formats coexist;
deprecate triangle-soup eventually.

**Splashpack version impact.** Add the format flag in the
v25 bump (alongside chunk-streaming and the other format
changes in this design series). One coordinated change.

**GTE rtpst usage.** Audit current PSYQo wrappers — does the
runtime already use `rtpst` (3-vertex) or only `rtps`
(1-vertex)? If only `rtps`, switching to `rtpst` alone is
a smaller, independent win — and doesn't require the indexed
format. Do that audit/migration first as Stage 0.

## Suggested entries

### For `docs/psxsplash-improvements.md`

> ### N+M. Indexed vertex format for shared vertices
>
> **Problem.** Mesh storage is triangle-soup: each `Tri`
> carries its own three vertex positions even when adjacent
> triangles share corners. Typical hand-authored meshes have
> 30–60% vertex duplication; the GTE re-transforms each
> redundant vertex.
>
> **Proposed direction.** Indexed format with per-mesh
> position-list + per-triangle position-indices. UV+color
> stay per-triangle-corner. Estimated 10% of frame budget
> recovered for typical scenes. Full design:
> `docs/vertex-sharing.md`.
>
> **Status.** Filed. Format bump (v25) rolls in coordinated
> with chunk-streaming, LOD, and other v25 additions.

### For `ROADMAP.md`

> - [ ] **Audit GTE wrapper usage — rtpst.** Confirm the
>       runtime uses 3-vertex transforms rather than 1-vertex.
>       Smallest standalone win; precedes indexed format
>       work.
> - [ ] **Indexed vertex format.** Per-mesh position list +
>       per-triangle position-indices. Format coexistence
>       flag in v25 splashpack. Full design:
>       `docs/vertex-sharing.md`.

## Changelog

- `2026-05-11` — Document created. Eighteenth patch doc in
  the series. Pairs with `visibility-culling.md` (saves
  transforms on culled triangles too) and
  `scratchpad-cache.md` (pre-transformed vertex cache in
  scratchpad — deferred Stage 3).
