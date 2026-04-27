# PS1 Mesh Binary Format Strategy

**Project target:** PS1Godot / psxsplash / chunk-based PS1-style action RPG  
**Focus:** compact runtime mesh formats, static render groups, chunk ownership, texture-page discipline, collision separation, and exporter validation.

The core rule is:

```text
Do not ship editor mesh formats at runtime.
Export compact binary mesh/chunk data that matches how the PS1 runtime actually renders.
```

Avoid runtime use of:

```text
OBJ
FBX
glTF
JSON mesh dumps
text vertex lists
float-heavy editor data
one file per tiny prop
```

Use compact, fixed-point/integer binary data.

---

## 1. Core Mesh Philosophy

For a PS1-style game, mesh data should be:

```text
small
binary
chunk-local
texture-page-aware
render-order-aware
quantized
grouped
streamable
easy to validate
```

The goal is not maximum format flexibility.

The goal is:

```text
fast loading
predictable memory use
low render submission cost
small disc footprint
good editor validation
```

---

## 2. What the Runtime Actually Needs

The runtime does not need editor objects.

It needs:

```text
vertex positions
optional vertex colors
UVs
primitive/index data
texture page / CLUT references
material flags
draw phase
collision data if applicable
object transforms for dynamic objects
chunk/render-group ownership
```

The runtime generally does **not** need:

```text
Godot node names for every tiny mesh
full editor transforms for static props
float vectors
import metadata
full material graphs
unused normals/tangents
editor-only collision helpers
source file paths
```

Editor/build tools can keep rich metadata. Runtime files should be lean.

---

# 3. Mesh Categories

Use different formats/paths for different mesh roles.

## 3.1 Static world mesh

Examples:

```text
floors
walls
buildings
terrain chunks
large props that never move
baked decals
trim sheets
```

Recommended export:

```text
baked into chunk-local coordinates
merged/grouped by texture page / CLUT / alpha mode / draw phase
no per-object transform at runtime
```

Static world mesh is the highest-value optimization target.

---

## 3.2 Dynamic rigid mesh

Examples:

```text
doors
chests
switches
moving platforms
rotating fans
pickups
scripted props
```

Recommended export:

```text
local vertices
one runtime transform
small mesh record
optional animation track
separate object entry
```

Dynamic objects should remain separate only when they actually move, toggle, animate, collide, or interact.

---

## 3.3 Character / skinned / segmented mesh

Examples:

```text
player
NPCs
enemies
event figures
bosses
```

Recommended export paths:

```text
SkinnedMesh:
  local vertices + skin weights + skeleton reference

SegmentedRigid:
  body-part meshes + bone/parent attachment metadata

SpriteBillboard:
  texture/quad data only
```

For PS1-style projects, segmented rigid characters are often a strong compromise.

---

## 3.4 Collision mesh

Collision should not be the same as render mesh by default.

Use separate simplified collision data:

```text
floors
walls
camera blockers
interaction volumes
trigger boxes
nav regions
encounter volumes
```

Recommended layers:

```text
PlayerCollision
CameraCollision
Interaction
Trigger
Nav
Encounter
```

---

# 4. Binary File Layout

## 4.1 Recommended top-level container

Use a chunk/mesh bank container rather than one loose mesh file per object.

Example:

```text
MESH_BANK
  header
  chunk table
  render group table
  object mesh table
  vertex buffer
  UV buffer
  color buffer
  primitive/index buffer
  material table
  texture reference table
  optional collision table
  optional name/hash table
```

For a larger RPG, this often belongs inside an area/chunk archive:

```text
AREA_TOWN_NORTH.archive
  mesh bank
  texture pages
  CLUTs
  collision
  scripts
  audio references
  animation references
```

---

## 4.2 Suggested magic/version header

```c
struct MeshBankHeader {
    uint32_t magic;          // 'MESH'
    uint16_t version;
    uint16_t flags;

    uint16_t chunkCount;
    uint16_t renderGroupCount;
    uint16_t objectMeshCount;
    uint16_t materialCount;

    uint32_t chunkTableOffset;
    uint32_t renderGroupTableOffset;
    uint32_t objectMeshTableOffset;
    uint32_t materialTableOffset;

    uint32_t vertexDataOffset;
    uint32_t uvDataOffset;
    uint32_t colorDataOffset;
    uint32_t primitiveDataOffset;

    uint32_t collisionDataOffset;
    uint32_t nameTableOffset;
};
```

Keep the header explicit and versioned.

---

## 4.3 Alignment

Use predictable alignment.

Recommended:

```text
2-byte alignment for int16-heavy data
4-byte alignment for table records/offsets
optional 16-byte alignment for DMA-friendly blocks later
```

Do not rely on compiler struct packing unless the runtime and exporter explicitly control it.

Prefer writing fields manually in known endian/order.

---

# 5. Coordinate Format

## 5.1 Use chunk-local coordinates

Static world vertices should be stored relative to the chunk origin.

```text
world position
  ↓ exporter
chunk-local int16 position
  ↓ runtime
chunk transform / camera transform
```

This keeps coordinates small and precise.

## 5.2 Suggested vertex format

```c
struct MeshVertexS16 {
    int16_t x;
    int16_t y;
    int16_t z;
};
```

Start with int16. Consider custom packing only after measurement.

## 5.3 Scale metadata

Each mesh/chunk should define how integer coordinates map to world units.

```text
ChunkScale
GteScaling
PositionScale
```

Do not store floats in runtime mesh data.

---

# 6. UV Format

## 6.1 PS1-friendly UVs

Suggested:

```c
struct MeshUV8 {
    uint8_t u;
    uint8_t v;
};
```

This works well for texture pages up to 256x256.

## 6.2 Atlas/page implications

Each primitive references:

```text
texture page
CLUT
UVs within that page
```

Texture coordinates should already be atlas/page-resolved by export time.

## 6.3 UV warnings

Warn on:

```text
UV outside page bounds
UV islands too close without padding
texture larger than supported primitive/page assumptions
object uses too many texture pages
```

---

# 7. Vertex Color Format

## 7.1 Why vertex colors matter

Vertex colors are the cheapest practical lighting path.

Use them for:

```text
baked lighting
ambient occlusion
fake torch glow
mood tinting
height gradients
static shadowing
```

Runtime path:

```text
final color = texture color * vertex color
```

## 7.2 Suggested color format

```c
struct MeshColorRGBA8 {
    uint8_t r;
    uint8_t g;
    uint8_t b;
    uint8_t a; // optional/flags; often 255
};
```

If alpha is not needed, alpha can be repurposed or omitted.

## 7.3 Missing color behavior

If a mesh expects vertex colors but none exist:

```text
warn
fall back to flat material color
```

Do not silently ignore missing lighting data.

---

# 8. Primitive Format

## 8.1 Prefer primitive records over generic triangles only

Support:

```text
textured triangle
textured quad
flat/untextured triangle
flat/untextured quad
gouraud/vertex-colored variants
semi-transparent variants
sprite/billboard variants maybe separate
```

## 8.2 Triangles vs quads

Triangles are universal.

Quads can be useful for:

```text
walls
floors
billboards
UI planes
simple props
large flat surfaces
```

If runtime supports textured quads efficiently, preserve quads where practical.

If not, triangulate at export and track primitive count.

## 8.3 Suggested primitive record

```c
struct MeshPrim {
    uint8_t type;           // tri, quad, sprite, etc.
    uint8_t flags;          // textured, gouraud, transparent, etc.
    uint16_t materialId;

    uint32_t indexOffset;   // into index buffer
    uint16_t indexCount;    // 3 or 4 usually
    uint16_t reserved;
};
```

This is flexible but may be too large for final runtime. Start readable, then compact if reports show it is worth it.

---

## 8.4 Index sizes

Use:

```text
uint8 indices for meshes/groups with <=255 vertices
uint16 indices otherwise
```

Exporter should choose per render group or mesh.

Metadata:

```text
IndexFormat: U8 | U16
```

Warn when:

```text
mesh barely exceeds 255 vertices and forces U16 indices
```

---

# 9. Material Table

## 9.1 Runtime material should be tiny

Runtime material is not a Godot material.

It should contain only what the PS1 renderer needs.

Suggested:

```c
struct MeshMaterial {
    uint16_t texturePageId;
    uint16_t clutId;
    uint8_t alphaMode;
    uint8_t shadingMode;
    uint8_t drawPhase;
    uint8_t flags;
};
```

## 9.2 Alpha modes

```text
Opaque
Cutout
SemiTransparent
Additive
Subtractive
UI
```

## 9.3 Shading modes

```text
Unlit
FlatColor
VertexColor
BakedLighting
CharacterAmbient
CharacterDirectional
```

## 9.4 Draw phases

```text
Sky/background
Opaque static
Opaque dynamic
Characters
Cutout decals
Transparent effects
UI
```

## 9.5 Material warnings

Warn on:

```text
16bpp texture used in gameplay chunk
semi-transparent material in opaque phase
material uses missing texture page
too many CLUTs for one chunk
```

---

# 10. Render Groups

## 10.1 Why render groups matter

Triangle count alone is not enough.

The runtime should reduce:

```text
object iteration
texture page switches
CLUT switches
draw phase confusion
ordering table pressure
```

Exporter should group static geometry by:

```text
chunk
texture page
CLUT
alpha mode
draw phase
shading mode
```

## 10.2 Suggested render group record

```c
struct RenderGroup {
    uint16_t chunkId;
    uint16_t materialId;

    uint32_t vertexOffset;
    uint16_t vertexCount;
    uint16_t indexFormat;

    uint32_t indexOffset;
    uint32_t primitiveOffset;
    uint16_t primitiveCount;

    uint8_t drawPhase;
    uint8_t flags;
};
```

## 10.3 Static render group example

```text
Chunk: town_square_north
Material: tpage_town_world_01 / town_day_clut / opaque / vertex color
Primitives: floor, walls, crates, doors baked together
```

## 10.4 Benefits

```text
fewer runtime objects
better batching
lower texture-page churn
better budget reporting
easier chunk streaming
```

---

# 11. Dynamic Object Meshes

## 11.1 Keep dynamic objects separate

Dynamic meshes need object entries.

Suggested object mesh record:

```c
struct ObjectMesh {
    uint16_t meshId;
    uint16_t materialId;
    uint16_t vertexCount;
    uint16_t primitiveCount;

    uint32_t vertexOffset;
    uint32_t primitiveOffset;

    int16_t boundsMinX, boundsMinY, boundsMinZ;
    int16_t boundsMaxX, boundsMaxY, boundsMaxZ;
};
```

Runtime object instance can store:

```text
meshId
position
rotation
scale optional
script/entity id
collision id
active flag
```

## 11.2 Dynamic object examples

```text
door
chest
switch
pickup
event figure
moving prop
```

## 11.3 Warning

Do not keep every static prop as a dynamic object.

If it does not move, animate, toggle, collide, or interact, it should probably be part of a static render group.

---

# 12. Bounds and Culling

## 12.1 Store bounds

Store bounds for:

```text
chunks
render groups
dynamic object meshes
collision blocks
camera zones
```

Suggested AABB:

```c
struct AabbS16 {
    int16_t minX, minY, minZ;
    int16_t maxX, maxY, maxZ;
};
```

## 12.2 Use cases

```text
frustum culling
chunk visibility
camera leak validation
collision broadphase
editor warnings
runtime debug overlay
```

## 12.3 Chunk-level culling first

Start with:

```text
chunk visible / not visible
render group visible / not visible
```

Do not start with per-triangle culling.

---

# 13. Collision Format

## 13.1 Keep collision separate

Render mesh is for drawing.

Collision mesh is for gameplay.

Suggested collision types:

```text
floor triangles
wall planes
AABB boxes
trigger boxes
camera blockers
interaction volumes
nav regions
encounter volumes
```

## 13.2 Collision header

```c
struct CollisionBankHeader {
    uint16_t floorTriCount;
    uint16_t wallTriCount;
    uint16_t boxCount;
    uint16_t triggerCount;

    uint32_t floorTriOffset;
    uint32_t wallTriOffset;
    uint32_t boxOffset;
    uint32_t triggerOffset;
};
```

## 13.3 Box volume

```c
struct CollisionBox {
    int16_t minX, minY, minZ;
    int16_t maxX, maxY, maxZ;
    uint16_t type;
    uint16_t id;
};
```

## 13.4 Collision warnings

Warn on:

```text
player spawn outside collision/nav
render mesh has collision enabled but no simplified collision
camera collision missing in third-person chunk
door transition lacks target spawn
```

---

# 14. Texture References

## 14.1 Use IDs, not paths

Runtime mesh data should reference:

```text
texturePageId
clutId
materialId
```

Do not store source paths in runtime mesh binary except maybe debug builds.

## 14.2 Texture table

```c
struct TextureRef {
    uint16_t texturePageId;
    uint16_t clutId;
    uint8_t bpp;
    uint8_t atlasGroup;
    uint16_t flags;
};
```

## 14.3 Build-time mapping

Editor/build system maps:

```text
source texture path → texture page → CLUT → runtime IDs
```

---

# 15. Name/Debug Table

## 15.1 Optional

Runtime release builds can avoid names.

Debug/dev builds can include:

```text
mesh names
render group names
chunk names
material names
source paths
```

## 15.2 Recommended approach

Use IDs in runtime.

Use external manifest for debug:

```text
mesh_debug_manifest.json
```

This avoids bloating the PS1 runtime file.

---

# 16. Chunk Ownership

## 16.1 Every mesh should belong to a chunk

Suggested metadata:

```text
ChunkId
RegionId
AreaArchiveId
DiscId
```

## 16.2 Chunk mesh bank

Example:

```text
MeshBank: town_square_north.meshbank
  ChunkId: town_square_north
  RegionId: home_town
  DiscId: 1
  AreaArchiveId: AREA_TOWN_NORTH
```

## 16.3 Why it matters

Chunk ownership supports:

```text
streaming
budget reports
disc layout
multi-disc validation
camera leak checks
texture residency
audio profile ownership
save/load
```

---

# 17. Disc and Archive Ownership

## 17.1 Mesh data should be archive-aware

Large RPGs need predictable loading.

Do not scatter mesh files across the disc.

Use area archives:

```text
AREA_TOWN_NORTH.archive
  town_square_north.meshbank
  town_interiors.meshbank
  town_collision.bank
  town_textures.timpack
```

## 17.2 Disc metadata

```text
DiscId
ArchiveId
ChunkIds
MeshBankIds
EstimatedSize
```

## 17.3 Validation

Warn when:

```text
chunk references mesh bank not on its disc
mesh bank references texture page not in same archive/common bank
common mesh differs between discs
```

---

# 18. Suggested First Binary Mesh Format

Start simple.

## 18.1 Good first-pass format

```text
MeshBankHeader
MaterialTable
RenderGroupTable
ObjectMeshTable
VertexS16Buffer
UV8Buffer
ColorRGBA8Buffer
IndexBuffer
PrimitiveRecords
CollisionBank optional
```

## 18.2 Do not over-optimize first

Avoid immediately adding:

```text
custom bit packing
complex compression
runtime LOD generation
per-triangle material data if grouped materials work
fancy mesh compression
```

Start readable and deterministic.

Then optimize after reports show real size/performance pressure.

---

# 19. Possible Compact Static Group Format Later

After the first format works, static groups can become tighter.

Example:

```text
StaticGroupHeader:
  materialId
  vertexCount
  primCount
  indexFormat
  flags

vertex data...
uv data...
color data...
primitive stream...
```

This avoids generic table overhead per primitive.

But do this later.

---

# 20. Compression

## 20.1 Runtime RAM format vs disc format

Separate:

```text
disc storage format
runtime loaded format
```

Disc/archive data can be compressed.

Runtime data should be ready to render after load/decompression.

## 20.2 Good compression candidates

```text
area archive compression
meshbank compression on disc
RLE repeated colors
delta-compressed vertices if useful
shared vertex/index buffers
deduplicated materials
```

## 20.3 Avoid early

```text
heavy decompression every frame
complex entropy formats
mesh compression that is hard to debug
```

---

# 21. Exporter Reporting

Per mesh/render group, report:

```text
mesh/group name
chunk id
dynamic/static
vertex count
primitive count
triangle/quad count
index format U8/U16
material id
texture page id
CLUT id
alpha mode
shading mode
estimated bytes
bounds
warnings
```

Per chunk, report:

```text
total vertices
total primitives
static groups
dynamic object meshes
texture pages
CLUTs
collision bytes
mesh bank size
disc/archive owner
```

---

# 22. Exporter Warnings

Add warnings like:

```text
Static prop "crate_07" is exported as dynamic but never moves, toggles, collides, or interacts.
Consider merging into static render group.
```

```text
Render group "town_world_misc" has 260 vertices and uses U16 indices.
Splitting may allow U8 indices and smaller data.
```

```text
Mesh "wall_large" uses 4 texture pages.
Consider atlas/grouping cleanup.
```

```text
Mesh "cave_floor" expects vertex colors but none were found.
Falling back to FlatColor.
```

```text
Chunk "forest_gate" has 37 separate dynamic object meshes.
Review static/dynamic flags.
```

```text
Object "poster_03" uses semi-transparent material and covers a large screen area.
Consider cutout or baked texture.
```

```text
Mesh bank references texture page "town_world_02" but that page is not resident in chunk "town_square_north".
```

---

# 23. Editor Tools

## 23.1 Mesh bank inspector

Show:

```text
static groups
dynamic meshes
vertex count
primitive count
estimated bytes
texture pages
CLUTs
warnings
```

## 23.2 Static grouping preview

Show what will be merged:

```text
Group: town_world_01_opaque
  floor_01
  wall_01
  crate_03
  trim_02
```

## 23.3 Texture page usage view

Show mesh usage by page:

```text
tpage_town_world_01:
  812 triangles
  34 objects
  1 CLUT
```

## 23.4 Collision view

Toggle display:

```text
render mesh
player collision
camera collision
trigger volumes
nav regions
```

## 23.5 Bounds view

Draw:

```text
chunk bounds
render group bounds
dynamic object bounds
camera zones
```

---

# 24. Suggested Implementation Order

## Step 1 — Documentation and metadata

Add mesh format docs and metadata shape.

## Step 2 — Mesh reporting

Add exporter reports:

```text
vertices
primitives
materials
texture pages
CLUTs
dynamic/static counts
estimated bytes
```

## Step 3 — Vertex color export

Make the cheapest lighting path real.

## Step 4 — Static/dynamic classification

Add explicit metadata:

```text
StaticRender
DynamicObject
CollisionOnly
EditorOnly
```

## Step 5 — Static grouping report

Report how geometry could be grouped by material/page/phase.

## Step 6 — First binary mesh bank

Implement simple deterministic mesh bank format.

## Step 7 — Collision bank separation

Move simplified collision into its own section/bank.

## Step 8 — Chunk/archive ownership

Add chunk/disc/archive IDs to mesh banks.

## Step 9 — Compact static group optimization

Only after the simple format works.

---

# 25. Suggested Metadata Fields

## Mesh asset metadata

```text
MeshId
SourcePath
ChunkId
RegionId
DiscId
AreaArchiveId
MeshRole: StaticWorld | DynamicRigid | Skinned | Segmented | CollisionOnly | EditorOnly
ExportMode: MergeStatic | KeepSeparate | CollisionOnly
VertexFormat: S16
UVFormat: U8
ColorFormat: RGBA8 | None
IndexFormat: Auto | U8 | U16
ShadingMode
AlphaMode
DrawPhase
MaterialId
TexturePageId
CLUTId
Bounds
EstimatedBytes
```

## Render group metadata

```text
RenderGroupId
ChunkId
MaterialId
TexturePageId
CLUTId
AlphaMode
ShadingMode
DrawPhase
VertexCount
PrimitiveCount
IndexFormat
Bounds
EstimatedBytes
```

## Collision metadata

```text
CollisionId
ChunkId
CollisionType
Layer
Bounds
Flags
```

---

# 26. IDE-Agent Prompt

Use this prompt to implement the first safe slice.

```text
You are helping me improve the PS1Godot / psxsplash mesh export pipeline toward a compact binary runtime mesh format for a PS1-style chunk-based RPG.

Goal:
Add documentation, metadata, reporting, and safe scaffolding for binary mesh banks. Do not break the current exporter or demo/jam scenes.

Core strategy:
- Do not ship OBJ/FBX/glTF/JSON/text mesh formats at runtime.
- Export compact binary mesh banks.
- Use int16 chunk-local vertex positions.
- Use uint8 UVs where possible.
- Use vertex colors for baked lighting.
- Use uint8 indices for groups with <=255 vertices, uint16 otherwise.
- Separate static world render groups from dynamic object meshes.
- Separate render mesh from collision mesh.
- Group static geometry by chunk, texture page, CLUT, alpha mode, shading mode, and draw phase.
- Reference texture pages/CLUTs/materials by IDs, not paths.
- Add chunk/disc/archive ownership metadata.

Implement or scaffold:
1. Mesh binary format documentation.
2. Mesh metadata fields:
   - MeshId
   - SourcePath
   - ChunkId
   - RegionId
   - DiscId
   - AreaArchiveId
   - MeshRole
   - ExportMode
   - VertexFormat
   - UVFormat
   - ColorFormat
   - IndexFormat
   - ShadingMode
   - AlphaMode
   - DrawPhase
   - MaterialId
   - TexturePageId
   - CLUTId
   - Bounds
   - EstimatedBytes

3. Exporter reporting:
   - static/dynamic mesh counts
   - vertex count
   - primitive count
   - texture page usage
   - CLUT usage
   - index format estimate
   - vertex color presence
   - estimated binary bytes
   - warnings

4. Static grouping report:
   - show what would merge by chunk/material/page/phase
   - do not change runtime output yet unless safe

5. Warnings:
   - static props exported as dynamic unnecessarily
   - mesh expects vertex colors but none found
   - mesh uses too many texture pages
   - group barely exceeds 255 vertices and forces U16 indices
   - collision enabled but no simplified collision mesh exists
   - texture page referenced but not resident in chunk
   - semi-transparent large surface warning

Rules:
- Keep current builds working.
- Prefer reporting and metadata before risky binary rewrite.
- Do not destructively modify source meshes.
- Clearly separate implemented behavior from scaffolded metadata.
- Do not fake runtime support for a binary format if the runtime still reads the old one.
- Preserve existing export paths until the new format is verified.

Final response:
- Summary
- Files changed
- Implemented
- Scaffolded only
- How to test
- Risks/TODOs
```

---

# 27. Bottom Line

The recommended mesh binary direction is:

```text
chunk-local int16 vertices
uint8 UVs
vertex colors for lighting
compact material IDs
texture page / CLUT references
static render groups
separate dynamic object meshes
separate collision banks
chunk/archive/disc ownership
clear exporter reports
plain-English warnings
```

Start with:

```text
metadata
reporting
vertex color export
static grouping analysis
simple mesh bank layout
collision separation
```

Then optimize.

The most important shift is:

```text
from:
  many editor objects with rich formats

to:
  chunk-owned binary render groups designed for the PS1 runtime
```
