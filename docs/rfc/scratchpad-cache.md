# Scratchpad & cache discipline — design + patch

PS1's R3000A has a 1 KB instruction cache and, more
interestingly, a 1 KB scratchpad — a region of fast SRAM at
`0x1F800000` that operates as a directly-addressable single-
cycle-access region. The scratchpad is not a cache: it's manual.
Most PS1 games use it for hot per-frame state (DMA buffers,
inner-loop variables, ordering-table roots).

Today, the runtime doesn't use the scratchpad at all. Everything
lives in regular RAM with normal cache behavior. This isn't
broken — just suboptimal. This doc designs scratchpad allocation
strategy + cache-friendly data layout.

Drop this file at `docs/scratchpad-cache.md`.

## The numbers

R3000A cache layout:

- **Instruction cache**: 4 KB, 1-way associative, 16-byte lines.
  Hit: 1 cycle. Miss: ~16 cycles (8 cycle RAM read + 8 cycle
  refill).
- **Data cache**: doesn't really exist. R3000A has a "data
  cache" register but it's the scratchpad. PS1 documentation
  often confuses this — there's no real per-line data caching,
  just one fast 1 KB region.
- **Scratchpad**: 1 KB at `0x1F800000`. Single-cycle read/write,
  same as registers. No coherence cost, no eviction. Manually
  managed.
- **Main RAM**: 2 MB at `0x00000000`. Read: ~3 cycles uncached,
  1 cycle cached (instruction only). Write: 1 cycle (write
  buffer absorbs).

For inner-loop variables that get hit 1000+ times per frame, the
difference is dramatic: scratchpad-resident state is ~3× faster
than RAM-resident on reads. For a typical render frame doing
~5000 OT inserts (each touching multiple shared variables), the
hot path runs noticeably faster.

## What's in place

- **PSYQo's `psyqo::Scratchpad` namespace** exposes the
  scratchpad region but the runtime doesn't currently reference
  it.
- **OT root pointers** live in RAM. Updated every primitive
  insertion (~5000 times per frame in busy scenes).
- **Per-frame state** (current camera, current rotation matrix,
  current OT root) lives in C++ static state, RAM.
- **DMA buffer** is in RAM with DMA driver managing it.

So the scratchpad is unused. Greenfield.

## Design

Allocate the 1 KB scratchpad to specific hot uses, ranked by
benefit.

### Allocation (1 KB total)

```
0x1F800000 +    0  - 32  : OT root pointers (8 × 4 bytes)
0x1F800000 +   32  - 64  : Current camera rotation (1 Matrix33)
0x1F800000 +   64  - 96  : Current camera translation (1 Vec3 + flags)
0x1F800000 +   96  - 192 : Frustum planes (6 planes × 16 bytes)
0x1F800000 +  192  - 320 : Per-frame counters / scratch ints
0x1F800000 +  320  - 576 : Hot scratch — inner-loop locals
0x1F800000 +  576  - 768 : DMA chain head pointer + small primitives
0x1F800000 +  768  - 1024: Reserved for Lua VM hot path
```

Each region has a typed accessor:

```cpp
namespace psxsplash::Scratchpad {

inline OTRoot* otRoots() {
    return reinterpret_cast<OTRoot*>(0x1F800000);
}

inline psyqo::Matrix33& cameraRotation() {
    return *reinterpret_cast<psyqo::Matrix33*>(0x1F800020);
}

inline psyqo::Vec3& cameraTranslation() {
    return *reinterpret_cast<psyqo::Vec3*>(0x1F800040);
}

inline FrustumPlanes& frustumPlanes() {
    return *reinterpret_cast<FrustumPlanes*>(0x1F800060);
}

}  // namespace
```

Access via these accessors instead of regular variables. Compiler
emits `lw` from the scratchpad address — single cycle.

### Top wins by region

**OT roots in scratchpad.** Every OT bucket insert reads + writes
the bucket's root pointer. With ~16 OT buckets used per frame ×
~5000 inserts, that's 10,000 root-pointer touches. At 3 cycles
per touch in RAM vs 1 in scratchpad, savings: ~20,000 cycles per
frame = 2% of frame budget recovered for nothing.

**Camera rotation in scratchpad.** Every triangle transform reads
the camera rotation matrix (9 values). With ~5000 visible
triangles per frame, that's 45,000 matrix reads. RAM-resident:
~135,000 cycles. Scratchpad: ~45,000 cycles. Savings: ~3% of
frame budget.

**Frustum planes in scratchpad.** BVH cull tests every node
against 6 frustum planes. With ~200 BVH node visits per frame,
that's 1200 plane reads. Saving here is smaller but the
frustum-plane data structure also gets accessed from portal
walk and occluder projection — total ~5000 reads, ~10,000
cycles saved.

### Cache-friendly data layout

Beyond scratchpad, *RAM layout* affects effective performance.
Two principles:

**Co-locate hot fields.** GameObject is 92 bytes — fits in 6
cache lines (16 bytes each). The fields used per-frame
(position, rotation, polygons pointer, polyCount, flags) should
be in the *first* cache line so a single load brings them all
in. Cold fields (script index, tag, AABB data) can be in later
lines.

Audit `gameobject.hh`:

```cpp
struct GameObject {                              // ─── cache line 0 ─
    psyqo::Vec3 position;          // 12 bytes
    uint32_t flags;                //  4 bytes
    psyqo::Matrix33* rotationPtr;  //  4 bytes
    Tri* polygons;                 //  4 bytes
    uint16_t polyCount;            //  2 bytes
    uint16_t tier;                 //  2 bytes
    // ─── cache line 1 ─
    // cooler fields ...
};
```

Hot path reads only the first 28 bytes — one cache line per
object. With 64 visible objects per frame, that's 64 line fills
× 16 cycles = 1024 cycles. Acceptable.

**Sequential access patterns.** The renderer's per-object loop
walks objects in `m_gameObjects` array order. That array should
match the order objects are inserted into the OT — sequential
access plays nicely with prefetch. Easy win: no change needed
beyond ensuring the BVH-cull output stays in spatial order.

### Instruction cache

The 4 KB I-cache is tight. Hot inner loops (the per-triangle
submit) should fit. The current renderer's inner loop is small
(< 1 KB compiled), so it fits easily. The risk is in larger
helper functions called from the inner loop — if
`processTriangle` calls 3 KB of helper code, the I-cache thrashes.

Audit the per-triangle path:

```
processTriangle (entry)
  -> rtpst (PSYQo wrapper, ~200 bytes)
  -> nclip check (~50 bytes)
  -> nearfar reject (~80 bytes)
  -> subpixel reject (~120 bytes, new)
  -> compute OT bucket (~150 bytes)
  -> push to OT chain (~200 bytes)
```

Total: ~800 bytes. Fits comfortably in I-cache.

The pattern to avoid: deep call chains through unrelated code
in the inner loop. Audit the existing renderer; ensure helpers
called from the per-triangle loop are inlined or are themselves
tiny.

### Compiler hints

Three patterns help the compiler produce better code:

**Mark scratchpad accessors as `inline`.** Already in the design.

**Use `__attribute__((hot))` on per-frame functions.** Tells
GCC to place them in a "hot" section for better I-cache
locality. Modest effect but free.

**Avoid function pointers in inner loops.** The runtime's
script callbacks use function pointers — they're fundamentally
not inlinable. Mitigation: don't call Lua per-triangle. The
existing dispatcher already groups Lua callbacks at the
per-object level, not per-triangle. Good.

## Implementation stages

Three stages.

### Stage 1 — Scratchpad allocation map + accessors ✅ shipped 2026-05-16

Header-only — `psxsplash-main/src/scratchpad.hh`. No consumers yet.

- `psxsplash::Scratchpad` namespace with the full 1 KB map
  (OT roots, camera rotation, camera translation, frustum
  planes, counters, hot scratch, DMA chain, Lua VM reserve).
- Typed accessors return references pinned at hardware
  addresses — MIPS compiler emits `lw` from a known offset,
  no indirection.
- `static_assert` chain enforces the map fills exactly 1024
  bytes and PSYQo type sizes (Matrix33=36, Vec3=12) stay
  within their region.
- Doc's original Matrix33 slot was 32 B; corrected to 48 B
  (Matrix33 itself is 36 B, padded to 16-byte alignment).

Stages 2 and 3 (consumer migration + GameObject reorder) are
deferred — each touches the renderer hot path and wants
host-mode-testing coverage first.

### Stage 2 — Migrate hot accesses

- OT root operations now read from scratchpad.
- Camera rotation copy from RAM into scratchpad at frame start.
  (One Matrix33 copy = 9 word writes = 9 cycles. Negligible.)
- Frustum plane computation writes to scratchpad.
- All inner-loop reads come from scratchpad.

Verifiable: profile a "stress" scene before / after. The
`gteTransformMicros` field in `FrameProfile` should drop a few
percent.

### Stage 3 — GameObject field reorder + audit

PS1Godot exporter side too (the splashpack format is partly
determined by C# layout):

- Audit GameObject and reorder for hot-fields-first.
- Static assert that hot fields fit in 32 bytes.
- Splashpack writer mirrors the new layout (or stays separate
  if the on-disk format wants different ordering).

Subtle but real win for cache-line discipline.

## Open questions / tradeoffs

**Scratchpad fragmentation.** With 1 KB total and multiple
subsystems wanting space, fragmentation is real. Mitigation:
the allocation map above is *fixed* — every subsystem knows
its addresses statically. No dynamic allocation. New uses of
scratchpad need a code review to update the map.

**Scratchpad conflicts with libc.** Some PS1 libc
implementations use scratchpad themselves. PSYQo is clean here
(reserves nothing), but if the runtime ever links other libs
this could surface. Document; pin to PSYQo.

**Scratchpad lifetime.** Scratchpad persists across context
switches (no OS-style switching on PS1), so the allocation is
stable for the runtime's lifetime.

**Cache behavior on uncached reads.** Reading from
`0xA0000000`-based addresses (the uncached mirror of RAM) is
sometimes faster than cached reads for unique access patterns.
Not relevant for the hot scratchpad path but might apply for
"read-once" data (loaded splashpack data). Defer; measure
first.

**Profile overhead vs scratchpad accesses.** The profiler
infrastructure (from `profiling.md`) adds per-frame work that
itself touches global state. If those globals are in regular
RAM, the profiler's own cost is higher. Put profiler counters
in scratchpad too — small allocation, big proportional
saving (the profiler runs ~100 times per frame).

**Production builds.** The scratchpad map should stay
consistent across debug / release builds — no `#ifdef`s that
move addresses. Authors building both should get the same
performance characteristics.

**ARM Linux emulators.** Some emulators (e.g., DuckStation
on ARM Linux) might emulate scratchpad less efficiently than
the original silicon. Document; the win is real on hardware
and on PCSX-Redux, may be smaller elsewhere.

**Inner-loop function size and inlining.** Modern GCC is
aggressive about inlining; check that `processTriangle`
inlines its small callees by examining the disassembly. If
GCC isn't inlining as expected, add explicit
`__attribute__((always_inline))` to the helpers.

**Verifying correctness.** Migrating to scratchpad shouldn't
change behavior — same data, different storage. Host-mode
tests (`host-mode-testing.md`) should catch any subtle issue.
On host, scratchpad accessors point to regular memory; the
test verifies the same OT contents are produced.

**What if a region grows?** Adding a new entry to the
allocation map that overflows 1 KB needs a hard error at
compile time. `static_assert` on each region's offset +
size keeps this checked.

## Suggested entries

### For `docs/psxsplash-improvements.md`

> ### N+M. Scratchpad allocation for hot per-frame state
>
> **Problem.** The R3000A's 1 KB scratchpad SRAM is not used.
> Hot per-frame variables (OT roots, camera matrices, frustum
> planes) live in regular RAM and pay 3× the access cost vs
> scratchpad-resident.
>
> **Proposed direction.** Fixed allocation map for the 1 KB,
> typed accessors per region, migrate hot paths. Estimated
> savings: 3–5% of frame budget recovered with no algorithmic
> change. Full design: `docs/scratchpad-cache.md`.
>
> **Status.** Filed.

### For `ROADMAP.md`

> - [ ] **Scratchpad + cache discipline.** Map the R3000A's
>       1 KB scratchpad to hot per-frame state (OT roots,
>       camera rotation, frustum planes, profile counters).
>       Audit GameObject field ordering for cache-line
>       discipline. Full design: `docs/scratchpad-cache.md`.

## Changelog

- `2026-05-11` — Document created. Seventeenth patch doc.
  Pairs with `profiling.md` (counters live in scratchpad
  for cheap profile overhead).
