# Splashpack format v27

Extracted from `psxsplash-main/src/splashpack.{hh,cpp}` and
`godot-ps1/addons/ps1godot/exporter/SplashpackWriter.cs`. Keep this in sync when
either side bumps the version.

> **Authoritative source:** the C++ structs with `static_assert(sizeof(...) == N)`
> in `splashpack.hh`. If this doc disagrees with the code, the code wins.
>
> **Doc currency:** the field tables below detail the v22 baseline. v23-v27
> additions (UI 3D models, scene skybox, per-clip audio routing, CDDA track,
> XA sidecar table) are summarised in the version history at the bottom but
> not yet expanded into per-field tables — read `splashpack.hh` directly
> for the v23+ field layouts. If you bump the format again, please also
> expand the relevant table here.

## File split

v20 produces three sibling files. Authoring tools emit all three; the runtime
loads each into its respective memory region.

| File | Destination | Contents |
|------|-------------|----------|
| `scene.splashpack` | Main RAM | Header + scene structures + offsets into the other two |
| `scene.splashpack.vram` | PS1 VRAM (1024×512×16bpp) | Texture atlas pixels + CLUTs + UI font pixels |
| `scene.splashpack.spu` | SPU RAM (512 KB) | ADPCM audio data |

Offsets in the main file that point into VRAM/SPU blobs are byte offsets into
the respective sidecar file, not absolute addresses.

## Endianness

Little-endian throughout. The PS1 MIPS CPU is little-endian; writer and loader
both assume it.

## File header — `SPLASHPACKFileHeader` (144 bytes)

Source: `splashpack.cpp` ~lines 21–90. `static_assert(sizeof(...) == 144)`.
v21 appended 16 bytes at the end of the v20 header for editor-driven player
rigs (camera offset + avatar attachment). v22 appended a further 8 bytes
for the sequenced-music table (`musicSequenceCount` + `pad_music` +
`musicTableOffset`).

| Offset | Type | Field | Notes |
|-------:|------|-------|-------|
| 0 | `char[2]` | `magic` | `"SP"` |
| 2 | `u16` | `version` | `27`; loader asserts `>= 27` |
| 4 | `u16` | `luaFileCount` | |
| 6 | `u16` | `gameObjectCount` | |
| 8 | `u16` | `textureAtlasCount` | |
| 10 | `u16` | `clutCount` | |
| 12 | `u16` | `colliderCount` | |
| 14 | `u16` | `interactableCount` | |
| 16 | `PackedVec3` | `playerStartPos` | Fixed-point GTE coords |
| 22 | `PackedVec3` | `playerStartRot` | Euler, fp12 radians |
| 28 | `FixedPoint<12,u16>` | `playerHeight` | |
| 30 | `u16` | `sceneLuaFileIndex` | `0xFFFF` = none |
| 32 | `u16` | `bvhNodeCount` | |
| 34 | `u16` | `bvhTriangleRefCount` | |
| 36 | `u16` | `sceneType` | `0` = exterior (BVH), `1` = interior (rooms) |
| 38 | `u16` | `triggerBoxCount` | |
| 40 | `u16` | `worldCollisionMeshCount` | Removed in v20; written as 0 for compat |
| 42 | `u16` | `worldCollisionTriCount` | Removed in v20; written as 0 for compat |
| 44 | `u16` | `navRegionCount` | |
| 46 | `u16` | `navPortalCount` | |
| 48 | `u16` | `moveSpeed` | fp12 per-frame |
| 50 | `u16` | `sprintSpeed` | fp12 per-frame |
| 52 | `u16` | `jumpVelocity` | fp12, derived from `sqrt(2·g·h) / gteScale` |
| 54 | `u16` | `gravity` | fp12 per-frame² |
| 56 | `u16` | `playerRadius` | fp12 |
| 58 | `u16` | `pad1` | 0 |
| 60 | `u32` | `nameTableOffset` | → name table in `.splashpack` |
| 64 | `u16` | `audioClipCount` | |
| 66 | `u16` | `pad2` | 0 |
| 68 | `u32` | `audioTableOffset` | → audio table in `.splashpack` |
| 72 | `u8` | `fogEnabled` | 0/1 |
| 73 | `u8` | `fogR` | |
| 74 | `u8` | `fogG` | |
| 75 | `u8` | `fogB` | |
| 76 | `u8` | `fogDensity` | 1–10 |
| 77 | `u8` | `pad3` | 0 |
| 78 | `u16` | `roomCount` | `N+1` when non-zero (sentinel room 0) |
| 80 | `u16` | `portalCount` | |
| 82 | `u16` | `roomTriRefCount` | |
| 84 | `u16` | `cutsceneCount` | |
| 86 | `u16` | `roomCellCount` | |
| 88 | `u32` | `cutsceneTableOffset` | |
| 92 | `u16` | `uiCanvasCount` | |
| 94 | `u8` | `uiFontCount` | |
| 95 | `u8` | `uiPad5` | 0 |
| 96 | `u32` | `uiTableOffset` | |
| 100 | `u32` | `pixelDataOffset` | → pixel-data table for CLUTs/atlases |
| 104 | `u16` | `animationCount` | |
| 106 | `u16` | `roomPortalRefCount` | |
| 108 | `u32` | `animationTableOffset` | |
| 112 | `u16` | `skinnedMeshCount` | |
| 114 | `u16` | `pad_skin` | 0 |
| 116 | `u32` | `skinTableOffset` | |
| 120 | `PackedVec3` | `cameraRigOffset` | v21+. Player-local PSX units; runtime rotates by yaw. |
| 126 | `PackedVec3` | `playerAvatarOffset` | v21+. Mesh-origin to player-origin offset, player-local. |
| 132 | `u16` | `playerAvatarObjectIndex` | v21+. Which gameObject auto-tracks player; `0xFFFF` = none. |
| 134 | `u16` | `pad_rig` | 0 |
| 136 | `u16` | `musicSequenceCount` | v22+. Number of sequenced-music entries (capped at 8). |
| 138 | `u16` | `pad_music` | 0 |
| 140 | `u32` | `musicTableOffset` | v22+. → `MusicTableEntry[]` in `.splashpack`, 0 if no sequences. |

## Post-header layout (main `.splashpack`)

Written in this order by `PSXSceneWriter.Write()` and read linearly by
`SplashPackLoader::LoadSplashpack()`:

```
Header (144 bytes)
├── LuaFile[]          — luaFileCount × sizeof(LuaFile)
├── GameObject[]       — gameObjectCount × sizeof(GameObject)
├── SPLASHPACKCollider[] — colliderCount × 32 bytes
├── SPLASHPACKTriggerBox[] — triggerBoxCount × 32 bytes
├── BVHNode[]          — bvhNodeCount × sizeof(BVHNode)  (if exterior)
├── TriangleRef[]      — bvhTriangleRefCount × sizeof(TriangleRef)
├── Room data          — if interior scene (rooms, portals, cells, refs)
├── Interactable[]     — interactableCount
├── Nav regions        — navRegionCount + navPortalCount
├── Cutscene table     — pointed to by cutsceneTableOffset
├── Animation table    — pointed to by animationTableOffset
├── Skinned mesh table — pointed to by skinTableOffset
├── UI table           — canvases + fonts, pointed to by uiTableOffset
├── Audio table        — clip headers (data lives in .spu), at audioTableOffset
├── Music table        — MusicTableEntry[] + PS1M blobs, at musicTableOffset (v22+)
├── Name table         — null-terminated strings, at nameTableOffset
├── Pixel data table   — TPage + CLUT offsets into .vram, at pixelDataOffset
└── Per-object triangle streams — referenced by GameObject.polygonsOffset
```

## Fixed-layout sub-structures

### `SPLASHPACKCollider` — 32 bytes

| Offset | Type | Field |
|-------:|------|-------|
| 0 | `i32×3` | `minX, minY, minZ` (fp12 AABB min) |
| 12 | `i32×3` | `maxX, maxY, maxZ` (fp12 AABB max) |
| 24 | `u8` | `collisionType` |
| 25 | `u8` | `layerMask` |
| 26 | `u16` | `gameObjectIndex` |
| 28 | `u32` | `padding` |

### `SPLASHPACKTriggerBox` — 32 bytes

| Offset | Type | Field |
|-------:|------|-------|
| 0 | `i32×3` | `minX, minY, minZ` |
| 12 | `i32×3` | `maxX, maxY, maxZ` |
| 24 | `i16` | `luaFileIndex` (−1 = no script) |
| 26 | `u16` | `padding` |
| 28 | `u32` | `padding2` |

### `SPLASHPACKTextureAtlas`

| Type | Field | Notes |
|------|-------|-------|
| `u32` | `polygonsOffset` | Offset into `.splashpack` (triangle stream for this atlas) |
| `u16` | `width` | Texels |
| `u16` | `height` | Texels |
| `u16` | `x` | VRAM column |
| `u16` | `y` | VRAM row |

### `SPLASHPACKClut`

| Type | Field |
|------|-------|
| `u32` | `clutOffset` |
| `u16` | `clutPackingX` |
| `u16` | `clutPackingY` |
| `u16` | `length` |
| `u16` | `pad` |

### `MusicTableEntry` — 24 bytes (v22+)

| Offset | Type | Field | Notes |
|-------:|------|-------|-------|
| 0 | `u32` | `dataOffset` | Byte offset of the PS1M blob in the `.splashpack`. |
| 4 | `u32` | `dataSize` | Size of the PS1M blob in bytes. |
| 8 | `char[16]` | `name` | Null-padded; truncated by writer to 15 chars + null. Lua resolves via `Music.Find(name)` / `Music.Play(name)`. |

The PS1M blob's internal layout (header + channel table + event stream)
is documented separately in `docs/sequenced-music-format.md`.

## Coordinate conversions (writer side)

SplashEdit runs in Unity coordinates (left-handed, Y-up, meters) and converts
on write:

- **Position:** `psxCoord = round(metricCoord * 4096 / gteScaling)` as i16. Y is
  flipped: `(x, -y, z)`.
- **Rotation:** Euler XYZ in radians → fp12 via `rad * 4096 / (2π)`.
- **Speed / gravity:** meters-per-second → per-frame at 30 fps → fp12.
- **Jump height:** converted to initial vertical velocity with
  `v = sqrt(2·g·h) / gteScaling`.

All fp12 fields are stored as `u16` with range 0–65535; the writer has overflow
guards that log an error but write a clamped value, so corrupt scenes fail at
export-time, not runtime.

## Version history (as of v27)

- v10: audio clips
- v11: fog config
- v12: cutscenes
- v13: UI canvases + fonts
- v16: trigger boxes
- v17: animations
- v18: skinned meshes
- v20: three-file split (main / vram / spu)
- v21: editor-driven player rig — `cameraRigOffset` + `playerAvatarOffset`
  + `playerAvatarObjectIndex` appended to the header (+16 bytes). Runtime
  reads a `Camera3D` child of `PS1Player` as the third-person offset and a
  `MeshInstance3D` child as the auto-tracked avatar. `Camera.SetMode` Lua
  API flips between first- and third-person at runtime.
- v22: sequenced music — `musicSequenceCount` + `pad_music` +
  `musicTableOffset` appended to the header (+8 bytes). Music section
  is an array of 24-byte `MusicTableEntry` rows pointing at PS1M blobs.
  Format documented in `docs/sequenced-music-format.md`. Runtime adds a
  `MusicSequencer` class on top of `AudioManager` (with voice
  reservation so dialog can't steal music notes).
- v23: UI 3D-model widgets — per-canvas `UIModelEntry[]` table with
  per-instance mutable runtime state. Renderer composes a HUD pass for
  these on top of the main scene. See `splashpack.hh:34` for layout.
- v24: scene skybox — full-screen textured quad with optional tint and
  rotation. Header gains `skyEnabled` flag + sky descriptor block. See
  `splashpack.hh:204`.
- v25: per-`AudioClip` `Route` byte (0=SPU, 1=XA, 2=CDDA, 3=Auto).
  Authoring side picks intent; build pipeline resolves to a concrete
  backend. See `PS1AudioClip.cs:94` and `splashpack.hh:95`.
- v26: per-`AudioClip` `cddaTrack` byte for CDDA-routed clips so Lua
  code doesn't have to know which physical disc track a song landed
  on. 0 = unset. See `splashpack.hh:114`.
- v27: XA sidecar table — `xaClipCount` + `xaTableOffset` in the
  header, with per-XA-clip `(name, sidecarOffset, sidecarSize)` rows
  pointing into the per-scene `scene.<n>.xa` file. Runtime
  `XaAudioBackend` (`xaaudio.cpp`) drives Form-2 disc streaming via
  `psyqo::CDRomDevice::Action` for SETMODE 0x24 → SETLOC → READS. See
  `splashpack.hh:120`.

## When porting the writer to Godot

1. Match struct sizes bit-for-bit. Add a test that hashes an empty-scene splashpack
   against a known-good SplashEdit empty-scene splashpack.
2. Preserve writer order — the loader is a linear cursor walk in places; offsets
   only cover the jump tables.
3. Preserve padding fields exactly. Zeroing them is fine; skipping them is not.
4. `collisionMeshCount` / `collisionTriCount` are dead fields kept for binary
   compat. Write zeros.
5. When SplashEdit bumps to vN+1, read its `PSXSceneWriter.cs` diff and mirror
   here. Update `CLAUDE.md` and the version-check tests.
