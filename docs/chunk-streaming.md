# Chunk streaming — design + patch

Closes `REF-GAP-5` in
`docs/ps1_large_rpg_optimization_reference.md`:

> 5. **No first-class chunk container.** A `PS1Chunk` authoring
>    node + chunk archive writer would express the reference's
>    chunk definition directly: one struct, not six.

And consolidates the two Phase 2.5 bullets that currently sit
separately in `ROADMAP.md`:

> - [ ] `Scene.LoadChunk(index, origin)` / `Scene.UnloadChunk(id)`
>       — partial scene overlay for streaming worlds. **[runtime]**
> - [ ] **`PS1Chunk` authoring node (`REF-GAP-5`).** Editor-side
>       container for a single streamable chunk: geometry set +
>       resident texture pages + NPC set + script set + audio
>       profile + effect budget. One struct, not six.

This is the prerequisite under three other patch docs in this
series — `lod-design.md` flags it as a future companion,
`disc-layout.md` makes it a hard dependency, and the
demo-blueprint's procedural-dungeon section gates on it. Worth
writing carefully because everything else snaps to its shape.

Drop this file at `docs/chunk-streaming.md`.

## Goal

Author a world bigger than fits in RAM. The player traverses
chunks; the runtime loads adjacent chunks in the background and
evicts distant ones. No loading screens for in-world transitions;
explicit transitions (door → dungeon, fast travel) get full-scene
loads via the existing `Scene.Load` path.

Non-goal: GTA-shaped continuous streaming with sub-second
chunk-radius management. That's a Phase 2.6+ concern once we
actually have a real game running and measuring. Phase 2.5
delivers the primitive; "streaming policy" sits on top of it.

## What's "a chunk"

The optimization reference defines it precisely. A chunk owns:

- Geometry set (meshes + BVH)
- Resident texture pages (atlas slice)
- NPC set (GameObjects with scripts)
- Script set (Lua files)
- Audio profile (resident clips for this area)
- Effect budget (max particle / spawn caps applied while resident)

These six already exist as scene-level concepts. A chunk is just
a scene that can be loaded *additively* on top of an existing
scene's resident state rather than swapping it wholesale.

The mental model: a "scene" is a hub state (menu, dungeon entrance,
photo mode). A "chunk" is a piece of one scene that streams in.
A typical game has 1–5 scenes and ~5–30 chunks per scene.

## What's already in place

- **Splashpack triplet format** (`.splashpack + .vram + .spu`).
  A chunk uses the same three-file shape — same loader code path,
  same writer infrastructure.
- **`Scene.Load(N)`** in the Lua API does a full-scene swap today.
  Chunk loading is the additive variant; the file-loading half is
  identical.
- **VRAM packer** computes per-scene texture-page placement.
  Chunks need a slot-reservation extension so the packer knows
  which VRAM regions are "shared base" vs "per-chunk transient."
- **BVH builder** runs per-scene. Per-chunk BVHs nest into the
  whole-scene cull pass with no structural change — see "BVH
  composition" below.
- **PCdrv passthrough** makes chunk loading nearly instant during
  emulator iteration, so authors can test streaming without
  burning CDs. Real-CD loading times come up via `disc-layout.md`.

## Design

Two halves: authoring (PS1Godot side) and runtime (psxsplash side,
`[runtime]` ask).

### Authoring — `PS1Chunk` node

New node, parent type `Node3D`:

```csharp
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_chunk.svg")]
public partial class PS1Chunk : Node3D
{
    [ExportGroup("Identity")]
    // Globally unique within a project. Used by Scene.LoadChunk
    // and Disc-layout ordering.
    [Export] public int ChunkIndex { get; set; } = 0;

    // Display name for editor / debug. Defaults to node name.
    [Export] public string DisplayName { get; set; } = "";

    [ExportGroup("World placement")]
    // Origin in world units. The chunk's geometry positions are
    // relative to this. Loading two chunks places them at their
    // declared origins; player-position queries select the
    // chunk(s) the player is inside.
    [Export] public Vector3 Origin { get; set; } = Vector3.Zero;

    // AABB extent for "which chunk is the player in" queries.
    // Authored as a Vector3 size, centered on Origin.
    [Export] public Vector3 Bounds { get; set; } = new Vector3(64, 16, 64);

    [ExportGroup("Streaming")]
    // Adjacency hints (shared with disc-layout.md). Other chunks
    // this one transitions to. Disc-layout uses these to place
    // adjacent chunks physically near each other on the ISO;
    // runtime uses them for predictive preloading.
    [Export] public Godot.Collections.Array<PS1Chunk>? Neighbors { get; set; }

    // 0.0–1.0 weight per neighbor — primary path vs side route.
    [Export] public Godot.Collections.Array<float>? NeighborWeights { get; set; }

    [ExportGroup("Residency")]
    // Which texture pages stay resident across chunk transitions
    // (player model, common UI, shared environment trim). These
    // are placed in the "base scene" VRAM region and survive
    // chunk unload. Everything else gets a per-chunk VRAM slot.
    [Export] public Godot.Collections.Array<Texture2D>? SharedTextures { get; set; }

    [ExportGroup("Budgets")]
    // From the optimization reference's "scene budgeting" section.
    // Per-chunk caps the editor warns against on overflow.
    [Export] public int TargetTris { get; set; } = 800;
    [Export] public int MaxActors { get; set; } = 8;
    [Export] public int MaxEffects { get; set; } = 4;
    [Export] public int MaxTexturePages { get; set; } = 6;
}
```

Authoring workflow:

1. Top-level `PS1Scene` contains the always-resident pieces — the
   player, the HUD canvas, shared atlases, the boot script.
2. Under `PS1Scene`, drop one or more `PS1Chunk` nodes. Each
   chunk's children are normal PS1 nodes (`PS1MeshInstance`,
   `PS1MeshGroup`, `PS1AudioClip`, `PS1NavRegion`, scripts).
3. Author the chunk's content with positions relative to the
   chunk's `Origin`. The Godot viewport renders all chunks at
   their declared origins simultaneously so authors see the
   composed world — at export time chunks are separated.
4. Set `Neighbors` on each chunk to declare adjacency. The dock
   panel grows a chunk-graph view that shows the connection
   topology (Phase 3 polish — see `vram-viewer.md`).

### Exporter — chunk archive writer

`SceneCollector` already walks a scene and produces a `SceneData`
snapshot. Extend it:

```csharp
public sealed class SceneData
{
    // Existing fields:
    public List<SceneObject> Objects = new();
    public List<PSXTexture> Textures = new();
    // …
    
    // New: per-chunk decomposition. Empty if scene has no PS1Chunk
    // children (single-scene fallback — current behavior).
    public List<ChunkData> Chunks = new();
    public ChunkData? BaseScene = null;  // Always-resident pieces
}

public sealed class ChunkData
{
    public required int Index { get; init; }
    public required string DisplayName { get; init; }
    public required Vector3 Origin { get; init; }
    public required Vector3 Bounds { get; init; }
    public required int[] NeighborIndices { get; init; }
    public required float[] NeighborWeights { get; init; }
    
    // Same shape as SceneData — meshes, textures, audio, nav,
    // scripts. Just scoped to one chunk.
    public List<SceneObject> Objects = new();
    public List<PSXTexture> Textures = new();
    public List<AudioClipRecord> AudioClips = new();
    public List<LuaFileRecord> LuaFiles = new();
    public List<ColliderRecord> Colliders = new();
    public List<NavRegionRecord> NavRegions = new();
}
```

The writer emits each chunk as its own triplet:

```
build/
  scene_0.splashpack  scene_0.vram  scene_0.spu        ← base scene (resident)
  chunk_0.splashpack  chunk_0.vram  chunk_0.spu        ← optional, streamed
  chunk_1.splashpack  chunk_1.vram  chunk_1.spu
  chunk_2.splashpack  chunk_2.vram  chunk_2.spu
  …
```

Single-scene scenes (no `PS1Chunk` children) emit only the
`scene_N` triplet — same as today. Chunk archives are pure addition.

### Format — splashpack v25

Chunk archives are splashpacks with one new header flag and one
new field. `SPLASHPACKFileHeader` grows 8 bytes:

```cpp
// At end of header (after the LOD additions if those land first):
uint8_t  isChunk;          // 1 = additive chunk, 0 = full scene
uint8_t  pad_chunk;
uint16_t chunkIndex;
int32_t  chunkOriginX;     // fp12, player-world space
int32_t  chunkOriginY;
int32_t  chunkOriginZ;     // 12 bytes for the origin
```

Wait — that's too much for a single 8-byte addition. Let me split:
the origin lives in a per-chunk side table, the header just carries
the flag and index:

```cpp
uint8_t  isChunk;          // 1 = additive chunk
uint8_t  pad_chunk;
uint16_t chunkIndex;
uint32_t pad_chunk2;
```

Origin + bounds + neighbor list live in a chunk metadata
section in the splashpack body, addressed from the existing
`nameTableOffset`-style pattern.

Backward compat: `isChunk = 0` means "this is a regular full scene"
— old splashpacks load with that interpretation. New chunk
archives set `isChunk = 1` and the SceneManager routes them
through the additive path.

### Runtime — `Scene.LoadChunk` / `Scene.UnloadChunk`

Lua API:

```lua
-- Load chunk N. Returns true on success, false on memory exhaustion
-- or invalid index. Loading is synchronous in v1 (a frame hitch is
-- acceptable for deliberate transitions); async lands in a follow-up.
local ok = Scene.LoadChunk(3)

-- Unload chunk N. Frees the GameObjects, audio clips, scripts,
-- nav regions, BVH nodes, and texture/CLUT slots owned by that
-- chunk. Player is responsible for not being inside the chunk
-- they unload — the runtime warns and refuses if so.
Scene.UnloadChunk(3)

-- Query helpers.
local current = Scene.GetCurrentChunk()        -- index or -1 if base-only
local loaded  = Scene.GetLoadedChunks()        -- array of indices
local pos     = Vec3.new(...)
local inside  = Scene.PointInChunk(pos)        -- index or -1

-- Predictive helper. Returns chunk N plus its declared neighbors.
-- Used by streaming policy scripts to know what to preload.
local set = Scene.GetChunkSetForPlayer()
for i = 1, #set do
    if not Scene.IsChunkLoaded(set[i]) then
        Scene.LoadChunk(set[i])
    end
end
```

Authors write streaming policy in Lua. A typical update loop:

```lua
function onUpdate(self, dt)
    local current = Scene.GetCurrentChunk()
    if current ~= self.lastChunk then
        -- Player crossed a chunk boundary. Unload the chunk we left
        -- if it's not a neighbor of the one we entered.
        local want = Scene.GetChunkSetForPlayer()
        for _, loaded in ipairs(Scene.GetLoadedChunks()) do
            if not Tables.Contains(want, loaded) then
                Scene.UnloadChunk(loaded)
            end
        end
        for _, c in ipairs(want) do
            if not Scene.IsChunkLoaded(c) then
                Scene.LoadChunk(c)
            end
        end
        self.lastChunk = current
    end
end
```

That's maybe 15 lines of Lua and it's the entire streaming policy
for a typical exploration game. Authors with different needs
(predictive radius, manual triggers, area-locked sections) write
their own loops against the same primitives.

### SceneManager — additive load path

`scenemanager.cpp` currently does a wholesale swap in `loadScene`:
free the current scene, load the new one. The chunk path needs to
parallel this without freeing:

```cpp
class SceneManager {
public:
    void LoadScene(int index);              // existing, wholesale swap
    void RequestSceneLoad(int index);       // existing, deferred swap

    // New — additive load. Does NOT free current state.
    bool LoadChunk(int chunkIndex);
    bool UnloadChunk(int chunkIndex);
    int  GetCurrentChunk(psyqo::Vec3 playerPos) const;

private:
    // Currently one of each. After chunks, the "base scene" stays
    // in the original slot; chunks live in parallel slots indexed
    // by chunk ID.
    SplashpackSceneSetup m_baseScene;
    struct ChunkSlot {
        uint8_t inUse;
        uint16_t chunkIndex;
        SplashpackSceneSetup setup;
        // VRAM regions / SPU regions claimed by this chunk
        // (so unload can return them to the allocator)
    };
    ChunkSlot m_chunkSlots[MAX_CONCURRENT_CHUNKS];  // typically 4
};
```

`MAX_CONCURRENT_CHUNKS` is the most chunks resident at once —
typically 4 (current + 3 immediate neighbors). Authored per project.

When `LoadChunk` runs:

1. Validate the chunk index against the manifest.
2. Find a free chunk slot, error if none.
3. Read `chunk_N.splashpack + .vram + .spu` via the existing
   file loader.
4. Allocate VRAM regions for the chunk's textures within the
   "per-chunk slot" reserved at scene init (see "VRAM packing"
   below).
5. Upload textures + audio to their reserved regions.
6. Register the chunk's GameObjects with the renderer's object
   list at indices offset by the slot.
7. Splice the chunk's BVH into the scene's master BVH (see
   below).
8. Run each chunk script's `onChunkLoad` callback.

Unload is the reverse: walk in unload order (scripts → renderer
→ VRAM → SPU → file buffer), null out the slot.

### VRAM packing for chunks

The hard part. VRAM is 1 MB, and chunks need to fit alongside
each other without colliding.

Authoring-time partition: `PS1Scene` exposes a new
`ChunkVramReservation` property (default 256 KB of VRAM reserved
for chunk content). The packer divides this into N equal slots
where N = `MAX_CONCURRENT_CHUNKS`. Each slot is a fixed region
of VRAM. A chunk's textures pack into one slot.

Constraint: each chunk's textures must fit in one slot. The
exporter warns at export if a chunk's texture footprint exceeds
the slot size. Authors split too-big chunks.

Trade-off: with 4 concurrent chunks × 64 KB each, that's 256 KB
of VRAM dedicated to chunks. The base scene gets ~ 512 KB minus
framebuffers. For a project that wants bigger chunks, drop
`MAX_CONCURRENT_CHUNKS` to 2 and get 128 KB per chunk.

Atlas-page granularity matters. A "slot" is N contiguous
texture pages (64×256 px each). Authors can see the slot layout
in the VRAM viewer (`vram-viewer.md`).

### BVH composition

Each chunk has its own BVH built at export. At runtime, the
master BVH is a tiny top-level structure with one leaf per
loaded chunk; each leaf points at the chunk's BVH. The renderer
recursively descends into chunk BVHs when their root AABB
passes the frustum test.

This is structurally identical to the existing
`PS1Room` + cell subdivision pattern, just at a coarser
level. No new bvh.cpp code; the composition lives in
SceneManager which feeds the renderer a multi-BVH list.

Implementation: extend `Renderer::SetBVH` to take a vector of
`(BVHManager*, originOffset)` pairs. The renderer walks each
BVH and applies its origin offset to the resulting tri-refs.

### Player-position tracking

`Scene.GetCurrentChunk(playerPos)` does a linear scan of loaded
chunk AABBs. For 4 concurrent chunks that's 4 AABB tests per
query — negligible. For 30+ concurrent chunks (unlikely on
PS1 but possible) we'd want a spatial index, but that's not
v1.

The `PointInChunk` query returns the deepest-containing chunk
(smallest AABB). Bounds overlaps between adjacent chunks are
expected — a player straddling a boundary is "in" the smaller
of the two.

### What survives a chunk unload

Chunk-scoped, freed on unload:
- GameObjects defined in the chunk
- Lua scripts defined in the chunk
- Audio clips marked as chunk-resident
- Textures packed in the chunk's VRAM slot
- Nav regions defined in the chunk
- The chunk's BVH

Base-scene-scoped, survives:
- Player GameObject
- HUD canvas + fonts
- Shared textures listed in `PS1Chunk.SharedTextures` (resolved
  to the base scene's VRAM region at export)
- Persistent state (`Persist.*` Lua values, save data)
- Music sequencer voice reservation
- The scene's master script

Anything else: authoring error if it tries to bridge chunks.
The exporter warns on detection (e.g., a Lua variable
referencing a GameObject by name that's in a different chunk).

## Implementation stages

Five stages, each shippable. Stages 1–3 deliver the core
primitive; 4–5 are quality-of-life and policy.

### Stage 1 — `PS1Chunk` node + naive exporter

PS1Godot side. No runtime work; existing `loadScene` keeps
working.

- Add `PS1Chunk` node + icon.
- Extend `SceneCollector` to recognize chunks and produce
  `ChunkData` per chunk.
- `SplashpackWriter` writes the v25 format with `isChunk` flag.
  Per-chunk archives go to `build/chunk_N.*`.
- For now, treat chunks as "labels only" — exporter emits the
  archives but no runtime consumes them yet. Authors can
  preview the export but not stream.

Verifiable: hex-dump the chunk splashpack, confirm structure
matches the format spec.

### Stage 2 — Runtime `Scene.LoadChunk` (additive)

psxsplash side — `[runtime]` ask. Tracked in
`docs/psxsplash-improvements.md` (see suggested entry below).

- Extend SceneManager with chunk slots + VRAM reservation.
- Implement `LoadChunk` / `UnloadChunk` / `GetCurrentChunk`.
- Extend Renderer for multi-BVH composition.
- Bind Lua API surface.
- Demo: a two-chunk scene where pressing a button loads /
  unloads the second chunk. No real streaming yet — just
  proves the additive primitive works.

### Stage 3 — Default streaming policy script

Ship `addons/ps1godot/templates/streaming_policy.lua` —
a drop-in policy script that handles the "load neighbors of
current chunk" pattern. Authors add it to their scene root and
their world streams.

- Auto-load chunks on player chunk-boundary cross.
- Predictive preload of `N` neighbors deep (default 1).
- Idle eviction of chunks not in the neighbor set after
  `K` seconds away (default 3).

Verifiable: a 6-chunk linear world — player walks from
chunk 0 to chunk 5, intermediate chunks load and unload
silently.

### Stage 4 — VRAM viewer integration

Build on `vram-viewer.md`'s per-scene residency view:

- Per-chunk VRAM slot indicators in the viewer.
- "Texture overflow per chunk" warning row.
- Chunk graph visualization in the dock — nodes for chunks,
  edges for `Neighbors` declarations.

### Stage 5 — Async chunk load

Currently `LoadChunk` is synchronous (blocks the frame while
the CD seeks and reads). Async would split it across frames:

- `Scene.LoadChunkAsync(N, callback)` queues the load.
- SceneManager spreads the read + upload across multiple
  frames using the time-sliced task scheduler (Phase 2.5
  bullet, already roadmapped).
- Callback fires when the chunk is fully ready.

For most games this is overkill — sync loads at chunk
boundaries are fine if the player has cover (a doorway, an
elevator, an animation). Add when a project needs it.

## Open questions / tradeoffs

**What's the natural chunk size?** Eyeballing the optimization
reference's budgets and PS1 VRAM: ~800 tris per chunk, ~6
texture pages, ~8 actors, ~4 audio clips. A typical chunk is
"one town square," "one corridor segment," "one cave room
group." Bigger than a single room, smaller than a whole
dungeon level. Authors who try to make every chunk feel like
a "level" will struggle; ones who think of chunks as "scene
fragments the streamer can deal with" will be fine.

**Cross-chunk references.** A door in chunk A pointing at a
spawn point in chunk B. Doesn't work via direct GameObject
references — chunk B might not be loaded. Solution: use
named entities + `Entity.FindByName(name)` which returns nil
if not loaded. Authors check for nil and trigger the load
first. Documented in the chunk authoring guide.

**Save/load state across chunk boundaries.** Persistent
state (open chests, killed enemies) needs to survive chunk
unload. Solution: each chunk has an authored "state schema"
(small key/value list) saved into `Persist` on unload and
restored on reload. Authoring surface: `PS1ChunkPersistentField`
sub-resources on the chunk. Minor but real — could be its own
follow-up doc.

**What if the player teleports?** Fast travel, cutscene
warp, debug commands. The Lua-driven policy approach handles
it cleanly: after teleport, run the policy script's "check
which chunks the player is in" pass — it sees a sudden change
and unloads/loads as needed. The frame hitch from a synchronous
load is acceptable mid-cutscene.

**Hierarchical chunks.** A chunk-of-chunks for huge open
worlds (e.g., a continent that decomposes into regions that
decompose into chunks). Not v1. Resist the urge to over-design
— the demo blueprint's scale doesn't warrant it. Add only
if a real game pushes against the flat-chunk-list ceiling.

**Multi-disc games.** Out of scope for v1, same as in
`disc-layout.md`. Each disc would carry a subset of chunks
plus the base scene; disc swap loads a fresh chunk manifest.
The chunk primitive is disc-agnostic; only the manifest +
build path care.

**Chunks affecting global state.** Music, weather, ambient
audio. Solution: those live in the base scene, with Lua
hooks that switch state on chunk transitions (e.g.,
`Music.Play("forest")` when entering a forest chunk).
Chunks don't own global state by design.

## Suggested entry for `docs/psxsplash-improvements.md`

> ### N+M. Additive chunk loading (`Scene.LoadChunk` /
> `UnloadChunk`)
>
> **Problem.** SceneManager loads scenes as wholesale state
> swaps — `loadScene` frees the current scene and replaces
> it. There's no path to load partial scene data on top of
> an existing scene while keeping the player and base state
> alive.
>
> **Why we care.** Multi-chunk worlds need additive load:
> walk into chunk B's area, B's geometry/textures/scripts
> load on top of the player's existing state, A's data
> evicts when out of range. Wholesale swap can't express this
> without spawning a fresh player every transition.
>
> **Proposed direction.** Extend SceneManager with N parallel
> chunk slots (typically 4). New API surface:
> `LoadChunk(index)` / `UnloadChunk(index)` /
> `GetCurrentChunk(playerPos)`. Each chunk is a regular
> splashpack with an `isChunk` header flag; the loader
> routes it through the additive path instead of the
> wholesale swap. VRAM allocation reserved per-chunk-slot at
> scene init via a new `chunkVramReservation` header field;
> chunk textures pack into their slot, freed on unload.
> Renderer extends to multi-BVH composition (one top-level
> BVH per loaded chunk; existing per-chunk BVHs unchanged).
> Full design: `docs/chunk-streaming.md`.
>
> **Status.** Filed. Splashpack format bump to v25 required.
> PS1Godot exporter side (Stage 1) ships independently of
> upstream. Local runtime patch lands as
> `patches/psxsplash/chunk-loading.patch` until upstreamed.
>
> **Evidence.** _(empty until Stage 1 ships and we have
> chunk archives to test against)_

## Changelog

- `2026-05-11` — Document created. Fifth patch doc in the
  series. Closes `REF-GAP-5`. Prerequisite cited by
  `lod-design.md` (future companion), `disc-layout.md`
  (hard dependency), `tiered-simulation.md` (sibling
  concern), `vram-viewer.md` (per-chunk surface),
  `prerendered-meshes.md` (no direct dependency but
  composes well for chunk-local pickup pools).
