# PS1 Chunked RPG Architecture Plan

**Working target:** a somewhat open-world, PS1-style action/RPG built around authored chunks, controlled visibility, strict budgets, and reusable content.

This document is intended to guide future PS1Godot / psxsplash development after the current jam project. It assumes a real PS1-style target where SPU RAM, main RAM, VRAM, GPU ordering, and CD access patterns all matter.

---

## 1. Core Philosophy

Do **not** aim for a modern seamless open world.

Aim for a **large-feeling connected RPG** made from small, controlled areas:

- town districts
- roads and field segments
- interiors
- dungeon rooms
- caves
- battle spaces
- cutscene scenes
- menu/special scenes

The player should feel like the world is connected, but the runtime should only carry a small working set at any moment.

A good target statement:

> A connected chunk-based action RPG that creates the illusion of a larger world through authored transitions, fog, fixed/controlled cameras, reused area kits, streamed music/ambience, and strict per-area budgets.

Avoid:

> A seamless free-camera open world with large vistas, many unique textures, lots of NPCs, persistent simulation everywhere, and dynamic streaming of everything all the time.

---

## 2. The Three Main Memory Battles

Think of the project as three different memory fights.

| Resource | What fights it | Main strategy |
|---|---|---|
| **SPU RAM** | SFX, resident samples, short voice clips | SPU/XA/CDDA routing, small SFX banks |
| **Main RAM** | meshes, scripts, animations, active entities, collision, gameplay state | chunks, compact binary formats, actor budgets |
| **VRAM** | textures, sprites, UI, CLUTs, decals, framebuffers | atlases, 4bpp/8bpp textures, area texture sets |

CD access is the fourth constraint:

| Resource | What fights it | Main strategy |
|---|---|---|
| **CD-ROM** | scattered files, seek-heavy streaming, many tiny assets | packed area archives, sequential reads, transition buffering |

---

## 3. World Structure: Build Around `PS1Chunk`

The most important future editor/runtime primitive should be a `PS1Chunk`.

A chunk is not just geometry. It is an area budget, streaming unit, and content ownership boundary.

Examples:

```text
PS1Chunk_TownSquareNorth
PS1Chunk_BlacksmithInterior
PS1Chunk_NorthRoad01
PS1Chunk_ForestGate
PS1Chunk_CaveRoom03
PS1Chunk_BattleArena_Wolves
```

### Suggested `PS1Chunk` fields

```text
Identity
- ChunkId
- DisplayName
- SceneType: Town | Field | Interior | Dungeon | Battle | Cutscene | Menu

Streaming
- NeighborChunks
- PreloadChunks
- TransitionType: Door | Fade | CameraCut | Tunnel | Elevator | MenuLoad
- AreaArchiveName

Resident visual data
- WorldTexturePages
- CharacterTexturePages
- FXTexturePages
- UITexturePages
- DecalTexturePages
- PaletteGroups

Resident audio data
- SpuBank
- XaMusicTrack
- XaAmbientTrack
- CddaTrack optional, explicit only

Budgets
- MaxTriangles
- MaxObjects
- MaxActiveNPCs
- MaxDynamicProps
- MaxParticles
- MaxAlphaQuads
- MaxResidentSPUBytes
- MaxResidentVRAMBytes

Gameplay
- NPCSet
- EncounterTable
- ScriptSet
- SaveStateScope
- LocalFlags
```

### Why chunks matter

Each chunk answers:

- What must be loaded right now?
- What can be unloaded?
- Which textures must share VRAM?
- Which sounds must be resident in SPU RAM?
- Which music/ambience streams from CD?
- How many actors and particles can exist here?
- Which neighboring areas should be preloaded?

This is the foundation that lets a larger RPG stay sane.

---

## 4. Scene Types

Each chunk should declare a scene type because different types need different budgets.

| Scene type | Typical traits |
|---|---|
| **Town** | more NPCs, lower combat FX, reused building kits, careful UI/dialogue budget |
| **Field** | more fog/distance control, fewer NPCs, reusable terrain kits, simple encounters |
| **Interior** | tight camera, low draw distance, high detail per room, strong texture reuse |
| **Dungeon** | chunked rooms, doors/halls hide loads, repeated kits, strict enemy/effect budget |
| **Battle** | controlled arena, active combat FX, fewer background systems |
| **Cutscene** | temporary assets allowed, then unloaded immediately |
| **Menu** | UI-heavy, low/no world cost, separate texture/page rules |

---

## 5. Streaming Model

Use controlled transitions instead of trying to stream a modern open world continuously.

Good transition covers:

- doors
- gates
- short tunnels
- fades
- stairs
- elevators
- camera cuts
- dialogue pauses
- loading screens
- battle transitions

### Area archive pattern

Prefer larger grouped reads instead of many tiny files.

```text
area_town_square_north.arc
  geometry.bin
  collision.bin
  scripts.luapack
  textures.timpack
  cluts.bin
  spu_bank.vh/vb or equivalent
  xa_profile.json / metadata
  npc_set.bin
  encounter_table.bin
```

Optional neighbor archive:

```text
area_town_square_north_preload.arc
  transition textures
  neighbor door/interior proxy meshes
  common NPC textures
  shared SFX references
```

Avoid:

- one file per prop
- one file per NPC texture
- many tiny runtime reads
- scattered disc placement
- random file lookups during active play

---

## 6. Camera Strategy

A bigger PS1-style RPG benefits from controlled cameras.

Possible modes:

- fixed cinematic cameras
- semi-fixed zone cameras
- follow camera in small field chunks
- battle camera
- interior camera presets
- cutscene camera tracks

### Important future tool: `PS1FixedCamera`

A `PS1FixedCamera` node should be placed visually in Godot and exported as a runtime preset.

Suggested fields:

```text
Identity
- PresetName

Aim
- LookAtTarget optional
- UseNodeRotationIfNoTarget

Projection
- ProjectionH
- PreviewFrustum

Behavior
- LoadShakeIntensity
- LoadShakeFrames
```

Lua should eventually be able to do:

```lua
Camera.LoadPreset("town_gate_01")
```

instead of hand-converting Godot coordinates to PSX coordinates.

### Why this matters

For a larger RPG, authoring dozens or hundreds of cameras by hand is painful if the workflow requires:

- manual GTE scaling conversion
- Y/Z flipping
- custom pitch/yaw math
- no editor preview of runtime framing
- export/run cycles just to check composition

The camera system needs to become an authoring tool, not just a runtime API.

---

## 7. Audio Strategy

Audio should be routed by purpose.

| Route | Use for | Notes |
|---|---|---|
| **SPU** | short SFX, UI sounds, footsteps, impacts, short barks | resident, low latency, fights SPU RAM |
| **XA** | music, ambience, narration, long voice, large stingers | streams from CD, best general RPG choice |
| **CDDA** | title theme, credits, special high-quality tracks | explicit only; high disc usage and less flexible |
| **Auto** | toolchain-selected route | must be deterministic and documented |

### Suggested audio metadata

```text
AudioClip
- Name
- SourcePath
- Duration
- SampleRate
- Channels
- DeclaredRoute: SPU | XA | CDDA | Auto
- ResolvedRoute: SPU | XA | CDDA
- Loop
- LatencySensitive
- AreaProfile
- OutputPath
- IncludedInSPUBudget
```

### Auto routing rules

```text
UI sounds                 -> SPU
Footsteps/impacts         -> SPU
Short gameplay one-shots  -> SPU
Short repeated barks      -> SPU unless too large
Music                     -> XA
Ambient loops             -> XA
Narration/long voice      -> XA
Large stingers            -> XA unless latency-sensitive
Title/credits             -> XA by default; CDDA only if explicit
Unknown short clip        -> SPU
Unknown long clip         -> XA
```

### Lua API target

```lua
Audio.PlaySfx("door_creak")
Audio.PlayMusic("town_day")
Audio.StopMusic()
Audio.PlayAmbient("forest_wind")
Audio.StopAmbient()
Audio.PlayStinger("boss_intro")
```

Important rule:

> Do not fake completed XA/CDDA playback. If a path is scaffolded only, fail loudly or log clearly.

---

## 8. Texture, Image, Decal, Sprite, and Alpha Strategy

VRAM is one of the most important RPG constraints.

### Default texture policy

| Asset type | Preferred format |
|---|---|
| world textures | 4bpp indexed TIM |
| UI/font/message boxes | 4bpp indexed TIM |
| character textures | 4bpp first, 8bpp if needed |
| portraits/cutscene stills | 8bpp, 16bpp only if explicitly budgeted |
| decals | 4bpp cutout where possible |
| particles | 4bpp or 8bpp atlas sprites |
| title/splash art | 8bpp or 16bpp only if isolated and unloaded after use |

### Texture metadata proposal

```text
TextureAsset
- Name
- SourcePath
- OutputFormat: 4bpp | 8bpp | 16bpp | Auto
- AlphaMode: Opaque | Cutout | SemiTransparent | Additive | Subtractive | UI
- AtlasGroup: World | UI | Character | FX | Decal | Cutscene
- Residency: Always | Scene | Chunk | OnDemand
- PaletteGroup
- AllowPaletteSwap
- AllowDither
- ForceNoFilter
- EstimatedVRAMBytes
```

### Alpha rules

Prefer:

- opaque textures
- binary cutout transparency
- baked decals
- small fake-shadow blobs
- low particle counts

Use sparingly:

- semi-transparent smoke
- glass
- water overlays
- ghosts
- energy effects
- UI fades

Avoid:

- many overlapping transparent quads
- giant alpha planes close to camera
- alpha-heavy particle storms
- designs that require perfect modern alpha sorting

### Decal rules

Use simple textured quads for:

- stains
- signs
- cracks
- posters
- fake shadows
- grime
- labels

Bake decals into world texture pages when possible.

Use separate decal quads only when reuse, layering, or dynamic placement matters.

---

## 9. Mesh and Animation Strategy

Meshes should use compact runtime formats, not authoring formats.

Avoid shipping:

- OBJ
- glTF
- JSON mesh data
- verbose text formats

Prefer compact binary data:

```c
struct Vertex {
    int16_t x, y, z;
};

struct UV {
    uint8_t u, v;
};

struct Tri {
    uint16_t a, b, c;
    uint8_t uv0, uv1, uv2;
    uint8_t material;
};
```

For small meshes, use `uint8` indices where possible.

### Mesh rules

- Quantize vertices to local chunk space.
- Store positions as int16 or equivalent fixed-point data.
- Reuse mesh templates.
- Split large areas by visibility and transition logic.
- Merge static geometry where safe.
- Avoid many tiny independent sorted objects.
- Prefer procedural/hardcoded meshes for cubes, planes, simple markers, and debug props.

### Animation rules

Prefer:

- transform animation
- segmented character rigs
- procedural idle/bob/spin effects
- small keyframe sets
- reused animation clips

Be cautious with:

- baked vertex animation
- many unique NPC animation sets
- high frame-count animations
- full skeletal complexity before the budget tools exist

---

## 10. Entity and Simulation Strategy

Do not simulate the whole world.

Only the active chunk and near-neighbor chunks should have meaningful simulation.

### Entity tiers

| Tier | Behavior |
|---|---|
| Active | full update, animation, collision, interaction |
| Nearby inactive | minimal state, maybe idle animation disabled |
| Distant known | state only, no update |
| Unloaded | saved flags/data only |

### NPC strategy

For towns:

- small active NPC count
- route simple paths
- low-frequency AI updates
- dialogue-triggered behavior
- reuse texture/animation sets

For fields/dungeons:

- small encounter groups
- pooled enemies
- strict active enemy cap
- combat arenas or encounter bubbles

---

## 11. UI and Tooling Strategy

The editor should guide authors before they break the PS1 budget.

### Required budget panel

A future PS1Godot dock should show something like:

```text
Current chunk: town_square_north

Triangles        1840 / 2500
Objects            72 / 100
NPCs                5 / 8
VRAM            612 KB / 1024 KB
Texture pages       5 / 8
SPU resident    188 KB / 256 KB
XA streams          1 active
Alpha quads        12 / 24
```

### Warnings should be plain English

Bad:

```text
TextureFormatException
```

Good:

```text
Texture town_wall_01.png is 512x512 and marked 16bpp.
That is expensive for PS1 VRAM.
[Convert to 4bpp] [Open Import Settings] [Ignore]
```

### Budget categories

Per scene/chunk, report:

- triangle count
- object count
- active NPC count
- dynamic prop count
- particle/sprite count
- alpha quad count
- VRAM estimate
- texture page count
- CLUT count
- SPU resident bytes
- XA/CDDA routed assets
- script count
- collision volume count

---

## 12. File and Build Reports

Every build should be able to emit reports.

### Audio report

```text
clip, route, source, output, sample_rate, channels, duration, size, spu_resident
```

### Texture report

```text
texture, format, width, height, bpp, clut, atlas_group, alpha_mode, residency, estimated_vram
```

### Mesh report

```text
mesh, vertices, triangles, materials, chunk, estimated_size, collision_enabled
```

### Chunk report

```text
chunk, scene_type, triangles, objects, npcs, vram, spu, xa_tracks, texture_pages, alpha_quads
```

These reports should be machine-readable enough for IDE agents and human-readable enough for jam debugging.

---

## 13. Suggested Development Roadmap

### Phase A — Finish the jam game

Use the current jam game as a complete-loop stress test:

- scene boot
- Lua lifecycle
- fixed camera switching
- UI overlay
- timed events
- audio playback
- simple scoring/state
- restart
- PCSX-Redux launch loop

### Phase B — Budget visibility

Before adding RPG systems, build budget feedback:

- VRAM estimate
- SPU estimate
- triangle count
- object count
- texture pages
- CLUTs
- alpha quads
- report generation

### Phase C — `PS1Chunk`

Add chunk authoring and metadata.

This is the biggest RPG foundation piece.

### Phase D — `PS1FixedCamera`

Make authored camera work visual and safe.

This is essential for fast RPG scene authoring.

### Phase E — Audio routing

Implement:

- `SPU/XA/CDDA/Auto` metadata
- build reports
- psxavenc detection/conversion
- Lua music API scaffold
- eventual runtime XA playback

### Phase F — Texture/VRAM pipeline

Implement:

- 4bpp/8bpp conversion
- texture atlas groups
- CLUT groups
- alpha mode metadata
- per-chunk VRAM summaries

### Phase G — RPG gameplay systems

Only after budgets/chunks exist, add:

- dialogue
- quests
- inventory
- combat
- NPC routines
- encounter tables
- save/load

---

## 14. Immediate IDE-Agent Prompt

Use this when asking an IDE assistant to advance the project after the jam:

```text
You are helping me evolve my PS1Godot / psxsplash project toward a chunk-based PS1-style action RPG.

The goal is not a modern seamless open world. The goal is a large-feeling RPG built from strict PS1-friendly chunks: towns, interiors, fields, dungeons, battles, cutscenes, and menus.

Priorities:
1. Preserve the current jam build.
2. Add budget visibility before adding more gameplay.
3. Design or scaffold a PS1Chunk node/metadata model.
4. Treat SPU RAM, main RAM, VRAM, and CD access as separate constraints.
5. Do not invent runtime APIs that do not exist.
6. Keep changes small and reviewable.

Architecture targets:
- PS1Chunk owns resident textures, audio profile, actor budget, effect budget, neighbor links, and transition behavior.
- Audio uses SPU/XA/CDDA/Auto routing metadata.
- Textures use 4bpp/8bpp indexed formats, atlas groups, CLUT reuse, and explicit alpha modes.
- Meshes use compact binary runtime data, quantized local coordinates, and chunk-based visibility.
- Fixed cameras should become authored PS1FixedCamera presets instead of manual Lua coordinate math.
- The editor should show live budget warnings for triangles, VRAM, SPU, texture pages, CLUTs, object counts, and alpha quads.

Deliverables:
- A proposed PS1Chunk data model.
- Budget/report scaffolding if safe.
- Roadmap updates tying chunking, audio routing, texture/VRAM policy, mesh/main-RAM policy, and camera authoring together.
- A clear list of what is implemented versus scaffolded.
```

---

## 15. Design Mantra

Use this as the north star:

```text
Small working set.
Large world illusion.
Strict budgets.
Heavy reuse.
Controlled cameras.
Chunked streaming.
VRAM-aware art.
SPU-aware audio.
Disc-aware packaging.
Editor-enforced constraints.
```

---

# Multi-Disc / CD Build Strategy

## Why this matters

If the project eventually supports physical CD play, the architecture needs to support
multi-disc builds much like large PS1 RPGs such as Final Fantasy VII. This is not only
a packaging problem. It affects save data, scene layout, world progression, shared
assets, disc swap UI, audio layout, and how the game avoids referencing missing content.

A large PS1-style RPG should assume that a single-disc build may not always be enough.

## Core rule

Design the game as a collection of **disc-addressable content sets**:

```text
Core runtime data       Always present on every disc
Shared/common assets    Duplicated on every disc if needed
Disc-local areas        Present only on the disc that owns them
Optional late-game data Present only on later discs
```

The runtime should never assume the entire game exists on the current disc.

## Recommended disc structure

Each disc should contain:

```text
Disc N
  PS-X EXE / runtime
  boot scene / load scene
  common UI assets
  common fonts
  common gameplay scripts
  common SFX bank
  common character/system textures
  save/load/disc-swap UI
  disc-local area archives
  disc-local XA/music banks
  disc-local cutscene assets
  disc-local battle arenas/enemies
```

Avoid making Disc 2 depend on random loose files from Disc 1. Each disc should be able
to boot, load saves, show required UI, and enter the content it owns.

## Data that should exist on every disc

Duplicate these across all discs:

- Runtime executable
- Boot/loading scenes
- Main menu / save menu / disc swap UI
- Fonts and base UI textures
- Common Lua/system scripts
- Common player assets
- Common combat UI
- Common SFX bank
- Common inventory icons, if inventory is accessible anywhere
- Save-game compatibility code
- Minimal fallback error scene
- Disc identity metadata

This costs space, but it prevents fragile cross-disc dependencies.

## Disc-local data

Put these only on the disc that needs them:

- Area chunks
- Town/dungeon/field archives
- Area-specific NPCs
- Area-specific textures/CLUTs
- Area-specific music/XA streams
- Area-specific cutscenes
- Area-specific enemy sets
- Area-specific dialogue banks
- Area-specific battle backgrounds
- Late-game unique models and effects

## Disc ownership metadata

Every chunk/area should declare which disc owns it.

Suggested future `PS1Chunk` fields:

```text
ChunkId: "forest_gate_02"
DiscId: 1
RegionId: "north_forest"
ArchiveId: "AREA_FOREST_A"
NeighborChunks:
  - "forest_gate_01"
  - "forest_path_01"
RequiredCommonBanks:
  - "common_ui"
  - "common_player"
  - "common_sfx"
RequiredDiscBanks:
  - "forest_textures"
  - "forest_music"
```

Suggested future area archive metadata:

```text
AreaArchive:
  ArchiveId
  DiscId
  ChunkIds
  TexturePages
  CLUTs
  MeshBanks
  LuaScripts
  AudioBanks
  XAStreams
  Cutscenes
  EstimatedReadSize
  PreferredDiscLBA
```

## Disc manifest

Each disc should include a manifest that answers:

- What disc number is this?
- What build/version is this?
- What content archives are present?
- What chunks are present?
- What XA/music streams are present?
- What save version does it support?
- What other disc IDs are part of the same game?

Example:

```text
DiscManifest:
  GameId: "MY_PS1_RPG"
  BuildId: "2026-04-26-nightly"
  DiscId: 1
  DiscCount: 3
  SaveVersion: 7
  ContainsChunks:
    - "intro_city"
    - "north_road"
    - "forest_gate"
  ContainsArchives:
    - "AREA_INTRO_CITY"
    - "AREA_NORTH_ROAD"
    - "AREA_FOREST_A"
  CommonBanks:
    - "common_runtime"
    - "common_ui"
    - "common_sfx"
```

The game should validate this manifest before loading disc-local content.

## Save data and disc swaps

Save files must be disc-independent.

The save should store:

```text
SaveGame:
  SaveVersion
  GameProgressFlags
  CurrentDiscRequired
  CurrentChunkId
  LastSafeChunkId
  PlayerState
  Inventory
  PartyState
  QuestFlags
  GlobalVariables
```

Do **not** store raw disc file paths in saves.

Store logical IDs instead:

```text
CurrentChunkId = "forest_gate_02"
CurrentMusicId = "forest_ambient"
CurrentQuestId = "missing_cart"
```

At load time, the runtime checks whether the current disc contains the required chunk.
If not, it shows a disc swap screen.

## Disc swap boundaries

Disc swaps should happen at controlled boundaries:

Good swap locations:

- End of major story act
- World map transition
- Boat/airship/train travel
- Leaving one continent/region for another
- Entering a late-game hub
- Cutscene fade-out
- Save/load screen
- Main menu load

Avoid disc swaps:

- Mid-combat
- During active streamed music
- During a cutscene unless explicitly authored
- During free movement with many active entities
- Inside small interiors unless the swap is story-controlled
- While transient temporary state is important

## Disc swap UI

The game needs a simple disc swap screen.

Suggested behavior:

1. Runtime detects requested content is not present on current disc.
2. Runtime fades to black or opens a full-screen UI.
3. UI says something like:
   - `Please insert Disc 2.`
   - `Press Cross when ready.`
4. Player swaps disc or changes mounted image.
5. Runtime re-reads disc manifest.
6. If the correct disc is present, loading continues.
7. If not, show a clear error and keep waiting.

For emulator/dev builds, this can be simulated with disc image paths or PCdrv folders.

## Development build behavior

During development, there should be two modes:

### Single-folder dev mode

All content is available through PCdrv or a host folder. This is best for iteration.

```text
dev_content/
  disc1/
  disc2/
  disc3/
  common/
```

The runtime/build tools can simulate disc ownership without forcing constant disc swaps.

### Disc-accurate test mode

Only the selected disc's content is visible.

This catches bad references:

- Disc 1 trying to load Disc 2 content
- Missing common files
- Save loads that require the wrong disc
- XA streams placed on the wrong disc
- Cutscene assets not duplicated where required

Disc-accurate test mode should be part of release testing.

## Audio implications

Multi-disc builds affect audio strongly.

Per-disc audio layout:

```text
Disc 1:
  common_sfx_spu
  intro_city_music_xa
  forest_music_xa
  early_cutscene_voice_xa

Disc 2:
  common_sfx_spu
  desert_music_xa
  capital_music_xa
  midgame_cutscene_voice_xa

Disc 3:
  common_sfx_spu
  final_dungeon_music_xa
  final_boss_music_xa
  ending_voice_xa
```

Keep common SFX duplicated where needed. Keep long music, ambience, and voice as disc-local XA streams.

CDDA should be treated carefully in multi-disc builds because CDDA tracks are physical disc tracks, not normal file assets. If CDDA is used, each disc needs its own CDDA track plan.

## Texture/VRAM implications

Multi-disc support does not increase VRAM. It only increases total available disc storage.

The same per-chunk VRAM rules still apply:

- 4bpp/8bpp indexed textures
- area texture sets
- CLUT reuse
- texture page discipline
- no giant always-resident texture sets
- chunk-based resident visual banks

Do not use multi-disc support as an excuse to make each active area too heavy.

## World design implications

A multi-disc RPG should be designed around **content eras**.

Example:

```text
Disc 1:
  Home region
  First town cluster
  Early forest
  Mines
  First major boss

Disc 2:
  Larger kingdom
  Desert / coast / capital
  Midgame dungeons
  Expanded enemy roster

Disc 3:
  World changes
  Late-game hub
  Final dungeons
  Endgame cutscenes
```

If returning to old areas is allowed, there are three options:

1. Duplicate old areas on later discs.
2. Require the earlier disc to revisit old areas.
3. Provide simplified later-disc variants of old areas.

Option 1 is most seamless but costs disc space.
Option 2 is authentic but can be annoying.
Option 3 is often the best compromise.

## Return-to-old-area strategy

For a larger RPG, use one of these policies:

### Story-linear policy

Once the player leaves Disc 1 regions, they cannot freely return until a later summary/hub.
Simplest for disc layout.

### Disc-request policy

The player can return, but the game asks for the required disc.
Authentic, but potentially clunky.

### Duplicated-hub policy

Important towns/hubs are duplicated on later discs, but optional old dungeons are not.
Good balance.

### Changed-world policy

Later discs contain modified versions of earlier regions.
This supports story progression and avoids duplicating everything exactly.

Recommended policy:
Use **duplicated-hub + changed-world** for important areas, and disc-request only for optional/archive content.

## Build tool requirements

The build system eventually needs:

- Disc manifest generation
- Per-disc content assignment
- Common/shared bank duplication
- Missing-reference validation
- Cross-disc save validation
- XA/CDDA layout per disc
- Per-disc size report
- Disc-accurate test mode
- Warnings when a chunk references content not present on its disc
- Warnings when common data differs between discs unexpectedly

Suggested build report:

```text
Disc 1 / 3
  Total size: 421 MB
  Common duplicated data: 38 MB
  Area archives: 210 MB
  XA audio: 155 MB
  Free space estimate: 229 MB
  Chunks: 42
  Warnings: 2

Disc 2 / 3
  Total size: 498 MB
  Common duplicated data: 38 MB
  Area archives: 260 MB
  XA audio: 184 MB
  Free space estimate: 152 MB
  Chunks: 51
  Warnings: 0
```

## Validation rules

The build should fail or warn when:

- A chunk references an asset not present on its assigned disc.
- A save/load entry point requires a chunk missing from the current disc.
- A common asset differs between discs without an intentional version bump.
- A CDDA track is referenced by logical ID but missing from that disc.
- An XA stream is assigned to the wrong disc.
- A required boot/menu/save UI asset is not duplicated to every disc.
- A disc has no valid manifest.
- Disc count or build ID does not match the save file expectations.
- Cross-disc transition targets are not marked as disc-swap-safe.

## RPG architecture impact

Multi-disc support reinforces the main rule:

The world is not one giant asset set. It is a set of logical regions, chunks,
banks, and archives that happen to be distributed across one or more discs.

Design every area as if it may eventually need:

- A disc owner
- A content archive
- A resident texture set
- A resident audio profile
- A transition boundary
- A save/load entry point
- A fallback if the current disc is wrong

## Immediate recommendation

Do not implement full multi-disc runtime support immediately.

Do add the metadata shape now:

```text
DiscId
DiscCount
DiscManifest
Chunk.DiscId
Archive.DiscId
RequiresDisc
DiscSwapSafeTransition
```

Then the project can grow naturally into multi-disc builds later without
rewriting every chunk, save file, audio bank, and area archive.

