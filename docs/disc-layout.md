# Disc-layout-aware ISO build — design + patch

Closes `REF-GAP-10` from `docs/ps1_large_rpg_optimization_reference.md`:

> 10. **Disc layout is undefined.** The reference's "disc layout
>     matters" section has no analogue in the writer — we emit one
>     splashpack per scene with no adjacency / seek-cost awareness.
>     Relevant when Phase 2.5 chunk streaming and Phase 3 ISO build
>     (`mkpsxiso`) land; flag for that point, not now.

And the matching `ROADMAP.md` bullet:

> - [ ] ISO build path via `mkpsxiso` for real-hardware testing.
>       **Amendment (`REF-GAP-10`):** disc-layout-aware. Place
>       adjacent-chunk archives physically close on the disc to
>       reduce seeks. Only matters once Phase 2.5 chunk streaming
>       is real.

This is the technique GTA III and San Andreas used on PS2 — pack
related world data sequentially so the laser doesn't bounce around
the disc when the player crosses a chunk boundary. The PS1's CD-ROM
seeks are worse than PS2's (slower drive, no DVD), so the technique
matters more here, not less.

Drop this file at `docs/disc-layout.md`.

## Goal

When a player walks from chunk A into chunk B, the read for B's
`.splashpack + .vram + .spu` triplet costs a short forward seek
(milliseconds), not a long head-fly across the disc (hundreds of
milliseconds). Same for any other adjacency: town → adjacent
district, dungeon room → next room, field → loading transition.

Non-goal: matching what GTA's IMG / IPL / IDE system did in detail.
Most of GTA's tricks are already covered by PS1Godot's existing
structure (splashpack triplets are the IMG equivalent; the
exporter's per-chunk packaging is the IPL/IDE equivalent). The gap
is purely **physical disc ordering**, which is one XML file passed
to mkpsxiso.

## What GTA actually did (and what doesn't apply here)

Three techniques worth naming so we can adopt the relevant ones:

1. **IMG archives.** GTA packed many small files into one big
   sequential blob with a directory header. Avoided per-file
   filesystem overhead and let the CD do long sequential reads.
   *Already handled here:* a chunk's three sidecar files
   (`.splashpack + .vram + .spu`) are already a small IMG-shaped
   bundle. Loader reads them sequentially via the existing
   `fileloader_cdrom` path.
2. **Disc-order layout.** Authors specified which files went where
   on disc; related zones lived next to each other. Player
   movement between adjacent zones cost a short forward seek.
   *This is the gap.* mkpsxiso supports an XML layout that orders
   files in the same way; we just need to emit it from our scene
   knowledge.
3. **Hot-asset duplication.** A handful of files (player model,
   HUD textures, common SFX) lived in multiple physical locations
   on disc so the seek to fetch them was always short.
   *Partially applies.* For a PS1-scale game we'd duplicate maybe
   2–3 files at most; not the bulk of the work, but worth a
   small authoring surface.

What doesn't apply: GTA's runtime streaming-ring (load chunks
within radius N of the player, evict those outside) is way more
machinery than a PS1 needs. Our chunks are coarser — typically one
loaded at a time, with the next pre-loaded during a deliberate
transition. That's much easier to lay out optimally.

## What's already in place

- **`fileloader_cdrom.cpp`** in psxsplash reads files from a real
  ISO via `psyqo::ISO9660Parser`. The per-file plumbing exists; the
  question is which file lives where.
- **The splashpack triplet pattern** (`.splashpack + .vram + .spu`)
  per scene means every chunk is already three sibling files that
  belong together. They want to be stored as a contiguous triplet,
  and they want triplets-of-adjacent-chunks to be stored as a
  contiguous block.
- **`PS1Chunk`** (planned, REF-GAP-5) — the authoring node for a
  single streamable chunk. The natural home for adjacency hints.
- **`Scene.LoadChunk` / `Scene.UnloadChunk`** (planned) — the
  runtime API that consumes chunks. Doesn't care about disc
  layout; just asks for a chunk by index, lets the loader find it.
- **PCdrv passthrough** for emulator iteration entirely skips
  the CD path, so authors iterating in PCSX-Redux feel no seek
  cost at all. Real-disc testing happens once per release, not
  every iteration. Disc layout is a release-time concern, not a
  dev-loop concern.

## Design

The whole story lives in one new build step: `scripts/build-iso.py`
takes the export output (`build/scene_*.splashpack` + sidecars) and
emits an mkpsxiso XML layout that orders files for minimum seek
cost.

### Authoring surface — adjacency hints on PS1Chunk

When `PS1Chunk` lands (Phase 2.5), it grows two optional fields:

```csharp
[ExportGroup("PS1 / Streaming")]
// Other chunks this one transitions to. The build-iso step uses
// this to place adjacent chunks physically near each other on the
// disc. Bidirectional links are inferred — if A lists B as a
// neighbor, B is treated as adjacent to A even if it doesn't list
// A back. Empty array = chunk has no known neighbors (use for
// disconnected scenes like menu / credits).
[Export] public Godot.Collections.Array<PS1Chunk>? Neighbors { get; set; }

// Optional weight (0.0–1.0) for how often this transition is
// expected. 1.0 = primary path (a town's main street to its
// market), 0.3 = secondary (an optional side alley). The build-iso
// step uses this to prioritize which neighbors get closest. Default
// 1.0 = treat all neighbors equally.
[Export] public Godot.Collections.Array<float>? NeighborWeights { get; set; }
```

Authors who don't set anything get reasonable defaults — adjacent
chunks in scene index order are treated as weight 1.0 neighbors,
which approximates a linear progression (the common case for level-
based games). Authors who care can override.

Plus one global setting on `PS1Scene`:

```csharp
[ExportGroup("PS1 / ISO Build")]
// Files that should be duplicated to keep them close to every
// chunk that uses them. Typical entries: a UI font, a common SFX
// bank, the boot music. Each entry adds its byte size N times to
// the ISO (where N = duplicate count chosen by the build-iso
// step, typically 2–3).
[Export] public Godot.Collections.Array<Resource>? HotAssets { get; set; }
```

### Adjacency graph → file ordering

`build-iso.py` builds a weighted graph: nodes are chunks, edges are
neighbor declarations with weights. Then it linearizes the graph
into a 1D file order that minimizes the sum of `weight × distance`
for every edge.

For small chunk counts (~≤ 20), brute-force enumerate permutations
and pick the best. For larger graphs, a greedy traversal works
fine: start at the chunk marked as the player's start point, walk
its highest-weight neighbor, then that chunk's highest-weight
unvisited neighbor, and so on. Backtrack when stuck.

Pseudocode:

```python
def linearize(chunks, edges, start_chunk):
    order = [start_chunk]
    visited = {start_chunk}
    while len(visited) < len(chunks):
        last = order[-1]
        # Pick the highest-weight unvisited neighbor of `last`.
        candidates = [(w, c) for (a, c, w) in edges
                       if a == last and c not in visited]
        if candidates:
            _, next_chunk = max(candidates)
        else:
            # Stuck — pick any unvisited chunk closest in the
            # graph (BFS from `last` along visited nodes).
            next_chunk = nearest_unvisited(last, visited, edges)
        order.append(next_chunk)
        visited.add(next_chunk)
    return order
```

For a 12-chunk overworld with branching paths this finishes in
milliseconds. Even if the chosen order is 5% suboptimal vs the
brute-force minimum, it's still vastly better than alphabetical or
scene-index order.

### File triplets stay together

Each chunk owns three sibling files: `chunk_N.splashpack`,
`chunk_N.vram`, `chunk_N.spu`. They're loaded together (the runtime
DMAs each to its respective memory region in one transition), so
they must be stored as a contiguous triplet on disc. The linearized
chunk order is the triplet order — for chunk position `k` in the
linear order, the three files appear sequentially:

```
chunk_3.splashpack
chunk_3.vram
chunk_3.spu
chunk_8.splashpack         ← neighbor of 3
chunk_8.vram
chunk_8.spu
chunk_12.splashpack        ← neighbor of 8
…
```

The `.splashpack` lands first because the loader reads it first (it
contains the header that tells the loader how big the sidecars
are). Within the triplet, sidecar order doesn't matter much, but
matching the load order avoids one tiny intra-triplet seek.

### Hot-asset duplication

Files listed in `PS1Scene.HotAssets` get N copies on the disc, one
near each cluster of chunks. The build script:

1. Identifies clusters of chunks separated by long gaps in the
   linearization (a sequence of unrelated chunks pushed apart by
   the graph linearizer).
2. Inserts a copy of each hot asset at the start of each cluster.
3. At load time, the runtime's existing `LoadFile(name)` finds the
   first occurrence in the directory — which by ISO9660 walk order
   is the closest copy to the chunk that triggered the load.

For a 20-chunk game with two natural clusters and one 12 KB hot UI
font, that's an extra 12 KB on the disc — irrelevant on a 650 MB
medium. The seek win on every font lookup pays it back many times.

Default: zero hot assets. Authors opt in. The dock surfaces a "Hot
asset candidates" report based on cross-chunk file references after
the first ISO build, so authors can see which files are getting
referenced from every chunk and consider promoting them.

### mkpsxiso XML emission

mkpsxiso reads an XML config that includes file ordering. The
emitted layout looks like:

```xml
<iso_project image_name="game.bin" cue_sheet="game.cue">
  <track type="data">
    <identifiers ...></identifiers>
    <directory_tree>
      <!-- System / boot files first, ISO9660 convention -->
      <file name="SYSTEM.CNF" type="data" source="boot/SYSTEM.CNF" />
      <file name="MAIN.EXE"   type="data" source="build/psxsplash.ps-exe" />

      <!-- First chunk's resident hot assets -->
      <file name="UIFONT.DAT" type="data" source="build/ui_font.bin" />

      <!-- Chunks in linearized order, each as a contiguous triplet -->
      <file name="C03.SP"  type="data" source="build/chunk_3.splashpack" />
      <file name="C03.VRM" type="data" source="build/chunk_3.vram" />
      <file name="C03.SPU" type="data" source="build/chunk_3.spu" />
      <file name="C08.SP"  type="data" source="build/chunk_8.splashpack" />
      …
    </directory_tree>
  </track>
</iso_project>
```

mkpsxiso writes files to the ISO in document order. So the XML's
job is to spell out the linearized order. Everything else
(CD-XA sync, sector counts, TOC) mkpsxiso handles.

`build-iso.py` emits this XML to `build/game.xml`, then shells out
to mkpsxiso. The XML is regenerated on every build (cheap), so
authors changing chunk adjacency in Godot see the new layout
reflect on the next ISO.

### Boot / shell location

The PS1 boot loader and main `.ps-exe` always live near the start
of the disc (ISO9660 convention + BIOS requirements). The
chunk-order optimization starts after the boot files. The first
chunk in the linearization should be the player's starting chunk —
declared on `PS1Scene` via the existing player-start fields
(already known to the exporter). This minimizes the seek from
"BIOS finishes" → "first chunk loads."

## Implementation stages

Each stage shippable on its own. Stages 1–2 deliver basic
disc-layout awareness; stage 3 is the polish that makes it
authorable rather than guessed.

### Stage 0 (prerequisite) — Phase 2.5 chunk streaming lands

`PS1Chunk` node + `Scene.LoadChunk` runtime + chunk archive writer.
No disc layout work happens until these exist; otherwise there's
nothing to lay out. Tracked elsewhere — see ROADMAP § Dynamic
content creation and the REF-GAP-5 entry in the optimization
reference.

### Stage 1 — Basic ISO build via mkpsxiso

Independent of chunk streaming. Stand up the ISO build path with
naive (scene-index-order) file layout. Real-hardware testing
becomes possible.

- `scripts/build-iso.py` — discovers `build/*.splashpack` + sidecars,
  emits a minimal mkpsxiso XML, shells out to mkpsxiso.
- New in-editor button: "Build ISO" alongside "Build psxsplash" /
  "Launch emulator" in the PS1Godot dock.
- `SETUP.md` gains a "mkpsxiso install" row (it was already a
  Phase 3 prereq).

Verifiable: author hits "Build ISO," burns the result, plays the
game on real hardware (or in Mednafen with no PCdrv). No layout
optimization yet; just proves the pipeline works.

### Stage 2 — Adjacency-aware layout

Once Stage 1 + chunk streaming are both real:

- `PS1Chunk.Neighbors` + `NeighborWeights` authoring properties.
- Linearization algorithm in `build-iso.py`.
- Triplet-grouping in the XML emission.
- Stats output: total chunk pairs, average weighted distance,
  worst pair (longest seek between adjacent chunks).

Verifiable: author flips chunk A's "Neighbors" entry between empty
and `[B]`, rebuilds ISO, diffs `game.xml`. With the neighbor set,
chunk A's triplet appears next to chunk B's. Without it, they
appear in arbitrary order.

### Stage 3 — Hot-asset duplication + reporting

- `PS1Scene.HotAssets` array.
- `build-iso.py` clusters chunks and inserts hot-asset copies
  per cluster.
- Dock report: "Files referenced by ≥ N chunks" with a one-click
  "Promote to hot asset" action.

Verifiable: a UI font referenced by all 12 chunks appears 2–3
times in the ISO directory. Strip the duplication, time a seek
from chunk 0 to a chunk-11-triggered font load on real hardware,
re-enable, re-measure — the duplicated version is meaningfully
faster.

### Stage 4 — Measurement + iteration

The hard one. Two paths:

1. **Emulator-side seek simulator.** PCSX-Redux's CD timing model
   includes seek time. Run an automated test that loads every
   chunk transition and reports total CD time. Add as a CI check
   so layout regressions are visible.
2. **Real-hardware profile harness.** Build a special ROM that
   walks the chunk graph in order, times each transition via the
   PS1's hardware clock, and dumps results via PCdrv to a host
   CSV. One-off effort that produces a measurement baseline.

Stage 4 is optional for shipping; the heuristic linearization is
already strictly better than random ordering and good enough for
most games. Add measurement once a real game hits the wall.

## Open questions / tradeoffs

**ISO9660 directory walks are also a cost.** Even with files in the
right order, opening a file by name costs a directory traversal
that can seek. Mitigation: keep chunk files in the root directory
(no nested folders), and rely on the loader's ability to cache the
directory entries. For ~30 files total this is fine; for a
1000-chunk world the directory itself would need attention.

**Two-track layout.** mkpsxiso supports a separate CDDA audio track
for redbook music. If/when CD-DA music ships (not yet planned for
PS1Godot — sequenced music covers the use case), the layout
becomes 2D: the data track gets the chunk ordering above, the
audio track gets a separate "play this track next" prediction
problem. Defer; sequenced music is the project's BGM strategy.

**Author error: contradicting adjacency.** If chunk A lists B as a
neighbor but the linearizer can't place them close (because B is
also a strong neighbor of C and D), what happens? The script
warns and places them as close as it can. Authors can over-author
adjacency hints; the build is robust to it. Worst case is the
output isn't optimal, never that it's wrong.

**Multi-disc games.** Out of scope for v1. FF7-shaped games would
need chunk-set-to-disc assignment plus the existing per-disc
layout. Add only if a real project needs it; the math is the same
problem at one level higher.

**Verifying the win is hard without real hardware.** Most authors
won't burn CDs to A/B-test layout. PCSX-Redux's accurate-timing
mode is the next-best thing — the loader's wall-clock for a chunk
load reflects the simulated seek. Document this and lean on it as
the verification path. The chunk-walk profile harness from Stage 4
is the rigorous version.

**Does it matter at PS1Godot's current scale?** Honest answer:
not yet. Today's demo is a single scene loaded once at boot,
running entirely from PCdrv. The win materializes when there's a
multi-chunk game streaming during play. We're filing this design
so that when that game exists, the layout work is small and
additive rather than a big retrofit.

## Suggested ROADMAP additions

Replace the existing one-line Phase 3 bullet:

> - [ ] ISO build path via `mkpsxiso` for real-hardware testing.
>       **Amendment (`REF-GAP-10`):** disc-layout-aware. Place
>       adjacent-chunk archives physically close on the disc to
>       reduce seeks. Only matters once Phase 2.5 chunk streaming
>       is real.

With this expanded set:

> - [ ] **ISO build path via `mkpsxiso`.** `scripts/build-iso.py`
>       discovers exported scenes/chunks and emits an mkpsxiso XML
>       layout. In-editor "Build ISO" button alongside Build /
>       Launch. Naive scene-index file ordering in v1. Real-hardware
>       testing path; sits next to the existing PCdrv path for
>       emulator iteration.
> - [ ] **Disc-layout-aware ISO (`REF-GAP-10`).** Adjacency hints
>       on `PS1Chunk` (Neighbors + weights). `build-iso.py`
>       linearizes the chunk graph and emits file triplets in
>       seek-optimal order. Hot-asset duplication for cross-chunk
>       references. Full design: `docs/disc-layout.md`. Gated on
>       Phase 2.5 chunk streaming.

## Changelog

- `2026-05-11` — Document created. Fourth self-contained patch
  doc in the series (after `lod-design.md`, `linux-support.md`,
  `prerendered-meshes.md`). Closes `REF-GAP-10` in the
  optimization reference. Implementation gated on Phase 2.5
  chunk streaming landing.
