# PS1Godot Missing Pieces and Production Tooling Roadmap

**Project target:** PS1Godot / psxsplash / chunk-based PS1-style RPG authoring pipeline  
**Focus:** what is still missing to turn PS1Godot from a strong exporter/toolkit into a full production environment.

This document assumes the project already has or is planning:

- chunked RPG architecture
- VRAM/SPU/main RAM budgets
- texture/alpha strategy
- audio routing
- animation banks
- Lua state architecture
- multi-disc planning
- all-in-one deployment
- lighting/fog/sky/camera strategy

The remaining gaps are mostly about:

```text
production validation
stable IDs
authoring tools
runtime safety
feature honesty
data editing
debugging
testability
```

The goal is to make PS1Godot feel like a real PS1 game production tool, not just a collection of exporter features.

---

## 1. Big Picture

The project is moving beyond:

```text
export a Godot scene to PS1-like runtime data
```

Toward:

```text
author, validate, package, deploy, and maintain a PS1-style game from one environment
```

The next major milestone is not just another runtime feature.

The next milestone is:

```text
make the tool enforce PS1 production discipline
```

That means:

- catch mistakes early
- show budgets visually
- warn in plain English
- make IDs stable
- make saves/versioning safe
- make disc/chunk ownership visible
- prevent scaffolded features from being mistaken for implemented ones
- provide templates and validation so users can actually ship

---

# 2. Unified Project Validator: “PS1 Doctor”

## 2.1 Why it matters

Validation ideas currently exist across many areas:

- textures
- audio
- meshes
- chunks
- animation
- Lua
- deployment
- multi-disc
- camera
- saves

These should eventually become one first-class tool:

```text
Project → Tools → PS1Godot: Validate Project
```

or a dock button:

```text
[ PS1 Doctor ]
```

## 2.2 What PS1 Doctor should check

### Project structure

```text
missing runtime
missing exporter dependency
missing output folder
missing scene list
missing project profile
missing tool paths
```

### Assets

```text
missing texture file
missing mesh file
missing audio file
missing animation file
missing Lua script
missing UI canvas
missing dialogue file
```

### Textures / VRAM

```text
oversized textures
unapproved 16bpp gameplay textures
large alpha textures
too many texture pages
too many unique CLUTs
missing atlas group
texture larger than pipeline limit
full-screen image marked always resident
```

### Audio / SPU / XA / CDDA

```text
large clip routed to SPU
XA clip missing encoded output
CDDA track referenced but not mapped
LoadOnDemand clip still packed into initial SPU bank
SPU budget exceeded
missing audio route
```

### Meshes / geometry

```text
triangle budget exceeded
too many separate objects
missing collision mesh
missing vertex colors where expected
unimplemented vertex color mode
high skinned mesh count
```

### Animation

```text
clip too long
clip at unnecessary 60fps
too many bones
translation keys on too many bones
large vertex animation clip
combat animation missing events
animation bank residency too broad
```

### Lua

```text
missing script file
unknown API call if detectable
duplicate module name
script load order problem
possible accidental global
too many per-frame Entity.Find calls if instrumented
missing required core module
```

### Chunks / world

```text
chunk missing ID
duplicate chunk ID
chunk has no camera profile
chunk has no fog/lighting profile
neighbor chunk missing
transition target missing
chunk assigned to missing disc
```

### Save / persistence

```text
save point has no valid reload chunk
save references non-stable object handle
save version missing
required disc missing
old save migration missing
```

### Deployment

```text
disc image too large
missing cue file
missing manifest
ODE folder layout mismatch
serial target missing PS-EXE
multi-disc content referenced from wrong disc
disc swap transition not safe
```

## 2.3 Warning style

Warnings should be plain-English and actionable.

Example:

```text
Chunk "town_square_north" uses 11 texture pages.
Target is 1–3 world pages plus character/FX/UI pages.

Suggested fixes:
- Move small prop textures into tpage_town_world_01.
- Reuse an existing CLUT where possible.
- Open the Texture Page View to inspect usage.

[Open Texture Page View] [Ignore]
```

## 2.4 Severity levels

```text
Info
Warning
Error
Blocking Error
```

Blocking errors prevent release builds.

Warnings may be allowed in dev builds.

---

# 3. Asset Registry and Stable ID System

## 3.1 Why it matters

A larger RPG needs stable logical IDs.

Saves, Lua, dialogue, chunks, disc manifests, audio routing, and deployment all depend on IDs that survive:

- file moves
- object reordering
- chunk unload/reload
- multi-disc splits
- rebuilds
- save/load cycles

## 3.2 Registry should track

```text
textures
meshes
materials
animations
animation banks
audio clips
audio banks
Lua scripts
chunks
regions
camera presets
camera zones
NPCs
items
quests
dialogue entries
shops
encounters
save points
disc archives
disc manifests
UI canvases
```

## 3.3 Generated outputs

The registry can generate:

```text
ids.generated.lua
ids.generated.cs
asset_manifest.json
build_manifest.json
disc_manifest.json
dialogue_manifest.json
```

Example Lua output:

```lua
IDs = {
    Chunk = {
        TownSquareNorth = "town_square_north",
        BlacksmithShop = "blacksmith_shop"
    },
    Item = {
        RustyKey = "rusty_key",
        Potion = "potion"
    },
    Dialogue = {
        BlacksmithIntro = "blacksmith_intro"
    }
}
```

## 3.4 Benefits

Prevents silent typos like:

```lua
Dialogue.Start("blackmsith_intro")
```

when the real ID is:

```lua
Dialogue.Start("blacksmith_intro")
```

## 3.5 Registry validation

Warn on:

```text
duplicate ID
missing referenced ID
unused asset
orphaned dialogue
quest references unknown item
save point references missing chunk
disc manifest references unknown archive
```

---

# 4. Dialogue and Localization Pipeline

## 4.1 Why it matters

RPG dialogue grows fast.

Raw strings inside Lua are fine for demos but bad for a larger RPG.

## 4.2 Dialogue definition shape

```text
DialogueEntry:
  Id
  Speaker
  Text
  PortraitId
  VoiceClipId optional
  Choices optional
  RequiredFlags optional
  SetFlags optional
  NextEntry optional
```

## 4.3 Choice shape

```text
DialogueChoice:
  Text
  TargetEntry
  RequiredFlag optional
  SetFlag optional
```

## 4.4 Validation

Warn on:

```text
missing dialogue ID
missing portrait
missing voice clip
line too long for text box
choice target missing
dialogue references unknown quest flag
dialogue references unknown item
duplicate dialogue ID
```

## 4.5 Localization readiness

Even if localization is not immediate, structure text so it can be extracted later.

Suggested fields:

```text
TextKey
SourceText
Speaker
Context
MaxWidth
Language
```

## 4.6 Runtime use

NPC scripts should call:

```lua
Dialogue.Start("blacksmith_intro")
```

not manually set UI text everywhere.

---

# 5. Item, Quest, NPC, Shop, and Encounter Definition Editors

## 5.1 Why it matters

A larger RPG needs data-driven definitions.

Do not force authors to hand-code every RPG data record in Lua forever.

## 5.2 Suggested resources

```text
PS1ItemDef
PS1QuestDef
PS1NPCDef
PS1DialogueDef
PS1EncounterDef
PS1ShopDef
PS1LootTableDef
PS1EnemyDef
PS1SkillDef
```

## 5.3 Item definition

```text
ItemId
DisplayName
Description
Icon
Type
StackLimit
Value
Flags
```

## 5.4 Quest definition

```text
QuestId
Title
Description
Flags
Objectives
Rewards
StartConditions
CompletionConditions
```

## 5.5 NPC definition

```text
NPCId
DisplayName
DefaultDialogue
HomeChunk
Schedule
Portrait
VoiceBank
Faction
```

## 5.6 Shop definition

```text
ShopId
MerchantNPC
InventoryItems
Prices
RequiredFlags
```

## 5.7 Encounter definition

```text
EncounterId
EnemySet
ArenaChunk
Music
Rewards
EscapeAllowed
```

## 5.8 Tooling goal

Eventually, authors should edit common RPG data from inspector/resources, not only Lua.

---

# 6. Save System and Migration

## 6.1 Why it matters

Save/load is core RPG infrastructure.

Multi-disc support also depends on save files knowing which disc/chunk is required.

## 6.2 Save data should store

```text
SaveVersion
GameVersion
CurrentChunkId
LastSafeChunkId
CurrentDiscRequired
PlayerState
PartyState
Inventory
QuestFlags
ChunkPersistentFlags
GlobalVariables
PlayTime
SavePointId
```

## 6.3 Save data should not store

```text
runtime entity handles
raw pointers
temporary audio channel IDs
temporary animation handles
temporary cutscene state unless intentionally serializable
raw disc file paths
```

## 6.4 Module export/import

Each persistent module should support:

```lua
Quest.Export()
Quest.Import(data)

Inventory.Export()
Inventory.Import(data)

Party.Export()
Party.Import(data)

Chunk.ExportPersistent()
Chunk.ImportPersistent(data)
```

## 6.5 Save migration

Add version migration:

```text
SaveVersion 1 → 2
SaveVersion 2 → 3
```

Example:

```lua
function Save.Migrate(data)
    if data.SaveVersion == 1 then
        data = Save.Migrate1To2(data)
    end
    if data.SaveVersion == 2 then
        data = Save.Migrate2To3(data)
    end
    return data
end
```

## 6.6 Save validator

Warn on:

```text
save point has no valid reload chunk
save point target chunk missing from disc
old save version has no migration path
quest flag in save not declared
inventory item in save not declared
```

---

# 7. Disc and Archive Planner UI

## 7.1 Why it matters

Multi-disc output needs a visual planning tool.

Build reports are useful, but an editor view is better.

## 7.2 Planner view

Example:

```text
Disc 1
  common_runtime
  common_ui
  common_sfx
  area_home_town
  area_forest
  xa_forest_music

Disc 2
  common_runtime
  common_ui
  common_sfx
  area_capital
  area_desert
  xa_desert_music
```

## 7.3 Show per disc

```text
disc size
free space
common data size
area archive size
XA/CDDA size
chunk count
warnings
unsafe transitions
missing shared data
```

## 7.4 Show per archive

```text
ArchiveId
DiscId
Chunks
Textures
Meshes
Animations
Audio
Scripts
EstimatedSize
NeighborArchives
```

## 7.5 Validation

Warn on:

```text
chunk references archive on wrong disc
transition crosses disc without swap-safe boundary
common data differs across discs
disc has missing manifest
save point requires missing chunk
```

---

# 8. Texture Page and CLUT Visualizer

## 8.1 Why it matters

PS1 performance and VRAM usage depend heavily on texture page and CLUT discipline.

A visualizer teaches authors what is expensive.

## 8.2 Example view

```text
Texture Pages in Current Chunk

tpage_town_world_01
  Used by: floor, walls, crates, doors
  Triangles: 812
  CLUTs: 1
  Status: Good

tpage_props_unique_07
  Used by: tiny_bottle
  Triangles: 12
  CLUTs: 1
  Warning: one-off texture page
```

## 8.3 Features

```text
show texture page atlas preview
highlight objects using selected page
show CLUT ownership
show one-off textures
show 4bpp/8bpp/16bpp breakdown
show alpha textures
show unassigned textures
```

## 8.4 Warnings

```text
one tiny prop uses unique texture page
too many CLUTs in chunk
16bpp texture in gameplay chunk
large alpha texture
UI texture not in UI atlas
```

---

# 9. Render Packet / Ordering Table Pressure View

## 9.1 Why triangle count is not enough

A scene can have low triangle count but still be expensive because of:

- too many separate objects
- too many texture page switches
- too many CLUT switches
- too many alpha primitives
- too many particles
- too many skinned actors
- too many UI primitives

## 9.2 Render pressure report

Track:

```text
static opaque primitives
dynamic opaque primitives
cutout primitives
semi-transparent primitives
additive primitives
UI primitives
texture page switches estimate
CLUT switches estimate
skinned actor count
particle count
sprite count
ordering table buckets touched
```

## 9.3 Example

```text
Render Pressure: town_square_north

Triangles: 1840 / 2500
Primitives: 970 / 1400
Objects: 62 / 100
Texture pages: 5 / 8
CLUTs: 14 / 24
Alpha prims: 42 / 80
Skinned actors: 3 / 5
Particles: 18 / 48
OT buckets touched: 384 / 1024
```

## 9.4 Warnings

```text
low triangle count but high object count
too many alpha primitives
too many texture page switches
too many skinned actors
UI primitive count high
```

---

# 10. Collision and Navigation Authoring Tools

## 10.1 Needed node types

```text
PS1CollisionMesh
PS1CameraCollision
PS1InteractionVolume
PS1TriggerVolume
PS1NavRegion
PS1NavLink
PS1EncounterVolume
PS1SpawnPoint
PS1SavePoint
PS1TransitionPoint
```

## 10.2 Why these matter

A third-person RPG needs separate collision concepts:

```text
visual geometry
player collision
camera collision
interaction volumes
trigger volumes
navigation regions
encounter volumes
```

Do not collide against detailed render geometry by default.

## 10.3 Validators

Warn on:

```text
player spawn outside nav region
camera collision missing in third-person chunk
door transition has no target spawn
NPC schedule references chunk with no nav region
save point has no stable ID
transition target missing
encounter volume has no encounter definition
```

---

# 11. Camera Preview and Runtime Frustum Visualization

## 11.1 Why it matters

Fixed and constrained cameras are core PS1 tools.

Camera authoring must match runtime behavior.

## 11.2 Needed tools

```text
PS1 runtime frustum gizmo
camera zone gizmo
camera blocker visualization
what-can-this-camera-see preview
camera leak detector
fog/sky preview with FogNear/FogFar
```

## 11.3 Leak detector

Warn when camera can see:

```text
outside chunk bounds
unloaded neighboring chunk
missing building backsides
void space
portal seam
untextured helper geometry
too many objects at once
```

## 11.4 Camera profile validation

Warn on:

```text
third-person camera has no camera zone
camera max distance too large
pitch allows high overhead view
camera collision enabled but no camera blockers exist
fixed camera preset target missing
```

---

# 12. Runtime Screenshot and Capture Comparison

## 12.1 Why it matters

Some bugs only show in emulator/runtime output.

Examples:

```text
black screen
wrong camera pitch
missing floor
sky hidden by fog
missing texture
UI offscreen
bad alpha sort
```

## 12.2 Tool idea

```text
[Run Scene Capture]
  export scene
  launch emulator
  wait N frames
  capture screenshot
  save to captures/
```

## 12.3 Later comparison

```text
compare to previous capture
flag black screen
flag major color difference
flag missing UI
flag missing texture
flag sky/fog mismatch
```

## 12.4 Output

```text
captures/
  town_square_north_2026-04-26_001.png
  town_square_north_2026-04-26_002.png
```

---

# 13. Host-Mode Validator / Test Runner

## 13.1 Why it matters

Booting an emulator for every validation problem is slow.

A host-mode validator could test data without rendering.

## 13.2 Test goals

```text
load splashpack
parse Lua
resolve entity/script references
validate chunks
validate disc manifests
step one fake frame
run event bus cap test
check save roundtrip
validate texture metadata
validate audio routing
validate animation banks
```

## 13.3 Example command

```text
ps1godot-hostcheck build/dev/game.splashpack
```

## 13.4 Output

```text
HostCheck:
  Splashpack loaded
  42 chunks valid
  0 missing scripts
  2 texture warnings
  0 blocking errors
```

---

# 14. Versioned Splashpack / Schema System

## 14.1 Why it matters

Manual binary format bumps become fragile as features grow.

New systems add fields:

```text
chunks
camera zones
audio routing
animation banks
texture metadata
disc manifests
save metadata
deployment metadata
```

## 14.2 Needed features

```text
versioned fields
default values
backward compatibility
migration reports
schema-generated docs
runtime/plugin compatibility checks
```

## 14.3 Schema direction

Possible approaches:

```text
custom schema
FlatBuffers-like schema
Cap'n Proto-like schema
custom TLV
```

## 14.4 Compatibility validator

Warn on:

```text
plugin exports v26 but runtime supports v24
runtime feature missing
field scaffolded but not consumed
old splashpack cannot be loaded
```

---

# 15. Hot Reload / Partial Reload

## 15.1 Why it matters

Large RPG iteration needs fast feedback.

Full rebuild/reboot is too slow for every change.

## 15.2 Priority order

```text
Lua reload
texture reload
UI reload
chunk metadata reload
audio reload
mesh reload
animation reload
```

## 15.3 Example workflow

```text
edit Lua
press Reload Lua
runtime reloads script block
continue scene
```

## 15.4 Safety

Hot reload should be dev-only.

Warn when reload cannot preserve state.

---

# 16. Dependency Manager and First-Run Setup

## 16.1 Why it matters

All-in-one deployment requires many tools.

## 16.2 First-run checks

```text
PCSX-Redux
mipsel toolchain
psxsplash runtime
disc image builder
psxavenc / XA encoder
serial tool optional
burn tool optional
external editor optional
output folder writable
```

## 16.3 UX

```text
Dependency: PCSX-Redux
Status: Missing
[Locate] [Download Info] [Ignore]
```

Optional tools should not block basic use.

## 16.4 Profiles

Different profiles need different dependencies.

Example:

```text
Emulator PCdrv:
  emulator required

Serial deploy:
  serial tool required

CD-R helper:
  disc builder required
  burn tool optional
```

---

# 17. Project Templates

## 17.1 Why templates matter

Users need good starting points.

Blank PS1 scenes are intimidating.

## 17.2 Suggested templates

```text
Hello Cube
Basic Interactive Scene
Fixed Camera Room
Third-Person Field
Interior With Door Transition
Dialogue NPC
Chest and Item Pickup
Battle Arena
Title Screen / Scene 0 Splash
Multi-Disc Skeleton
ODE Export Example
Chunked RPG Mini-Slice
```

## 17.3 Template goals

Each template should demonstrate:

```text
correct node setup
correct scripts
budget-friendly assets
working export
plain comments
small scope
```

---

# 18. Runtime Feature Matrix

## 18.1 Why it matters

The project has many planned/scaffolded features.

Users and IDE agents need to know what actually works.

## 18.2 Matrix format

```text
Feature                 Editor   Exporter   Runtime   Docs   Status
Vertex Colors           Yes      Partial    No/Partial Yes    Scaffolded
XA Routing              Yes      Scaffold   No        Yes    Planned
Camera Zones            Planned  No         No        Yes    Planned
PS1Chunk                Planned  No         No        Yes    Planned
Animation Banks         Planned  No         No        Yes    Planned
Sky                     Yes      Yes        Yes?      Yes    Implemented/Verify
```

## 18.3 Status values

```text
Implemented
Partial
Scaffolded
Planned
Deprecated
Blocked
```

## 18.4 Rule

Never let scaffolded features look fully implemented.

---

# 19. Vertical Slice RPG Sample

## 19.1 Why it matters

A toolchain is proven by a complete small game, not isolated features.

## 19.2 Suggested vertical slice

```text
title screen
one town chunk
one interior
one field chunk
one NPC
one dialogue
one quest
one item
one chest
one enemy
one battle
one save point
one transition
one disc image build
```

## 19.3 What it proves

```text
scene authoring
chunking
Lua modules
dialogue
items
quest flags
save/load
camera
lighting
texture budgets
audio routing
deployment
```

## 19.4 Keep it small

This should be a reference project, not a full game.

---

# 20. Suggested Priority Order

## Tier 1 — Must-have production foundation

1. PS1 Doctor unified validator
2. Asset registry / stable ID system
3. Vertex color lighting actually exported
4. Runtime feature matrix
5. Expanded budget reporting

## Tier 2 — RPG authoring foundation

6. PS1Chunk node
7. Dialogue definition pipeline
8. Item/quest/NPC definition resources
9. Save system + versioning
10. Collision/nav authoring tools

## Tier 3 — Visual and performance tooling

11. Texture page / CLUT visualizer
12. Render packet / OT pressure report
13. Camera preview / frustum gizmos
14. Runtime screenshot capture comparison
15. Static render grouping reports

## Tier 4 — Build/deployment maturity

16. Disc/archive planner UI
17. Host-mode validator
18. Versioned splashpack/schema system
19. Dependency manager / first-run setup
20. Hot reload / partial reload

## Tier 5 — User-facing polish

21. Project templates
22. Vertical slice RPG sample
23. Better tutorial set
24. Release packaging helper
25. Hardware-specific deployment presets

---

# 21. IDE-Agent Prompt

Use this prompt when asking an IDE coding agent to implement the first safe slice.

```text
You are helping me evolve PS1Godot from a scene exporter into a full PS1-style game production tool.

Goal:
Add documentation and safe scaffolding for the missing production tooling roadmap. Do not break the current exporter or jam/demo scenes.

Main missing systems to document/scaffold:
1. PS1 Doctor unified validator
2. Asset registry / stable ID system
3. Runtime feature matrix
4. Dialogue/item/quest/NPC definition pipeline
5. Save system and migration plan
6. Disc/archive planner UI
7. Texture page / CLUT visualizer
8. Render packet / ordering table pressure report
9. Collision/nav authoring tools
10. Camera preview / runtime frustum visualization
11. Runtime screenshot capture flow
12. Host-mode validator
13. Versioned splashpack/schema plan
14. Hot reload plan
15. Dependency manager / first-run setup
16. Project templates
17. Vertical slice RPG sample

Implementation rules:
- Keep the current build/export path working.
- Prefer documentation, data models, and validation scaffolding before runtime rewrites.
- Do not fake implemented runtime features.
- Add clear status labels: Implemented, Partial, Scaffolded, Planned, Blocked.
- Use plain-English warnings with suggested fixes.
- Preserve source assets.
- Keep changes small and reviewable.

Suggested first implementation:
1. Add a PS1Godot production roadmap doc.
2. Add a runtime feature matrix document or data file.
3. Add initial PS1 Doctor validator skeleton if safe:
   - runs basic project checks
   - reports warnings/errors
   - does not block builds yet unless errors are severe
4. Add an AssetId registry proposal or scaffold.

Final response:
- Summary
- Files changed
- Implemented
- Scaffolded only
- How to test
- Risks/TODOs
```

---

# 22. Bottom Line

The missing pieces are not just more PS1 runtime tricks.

The real missing pieces are production infrastructure:

```text
stable IDs
validation
budget visualization
feature status tracking
data authoring
save safety
disc/archive visibility
camera/texture/render debugging
templates
testability
```

That is what turns PS1Godot from:

```text
cool exporter
```

into:

```text
real PS1 game production environment
```

The shortest roadmap:

```text
PS1 Doctor
Asset Registry
Feature Matrix
Texture/Render Visualizers
PS1Chunk
Dialogue/Quest/Item Data
Save System
Disc Planner
Host Validator
Templates
Vertical Slice RPG
```
