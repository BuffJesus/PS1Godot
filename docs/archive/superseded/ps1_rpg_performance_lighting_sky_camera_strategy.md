# PS1 RPG Performance, Lighting, Sky, and Camera Strategy

**Working target:** a large-feeling, chunk-based PS1-style action/RPG built around strict runtime budgets, controlled visibility, reusable content, and editor-enforced constraints.

This document extends the existing chunked RPG architecture notes with practical strategies for:

- low-cost lighting
- skybox / sky-dome / background handling
- fog that hides limits without destroying visibility
- third-person right-stick camera support
- lower-level performance improvements inspired by PS1 / PSYQo / PSn00bSDK-style thinking
- editor and build-time validation

The goal is not a modern renderer. The goal is a convincing PS1-style renderer that spends CPU, GPU, VRAM, SPU, and CD bandwidth only where the player notices.

---

## 1. Core Performance Philosophy

Do not optimize by chasing one magic trick.

Optimize by controlling the active working set:

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

A PS1-style RPG should usually mean:

- one town district or field chunk loaded at a time
- small active NPC populations
- reused texture pages and architecture kits
- limited dynamic lights
- mostly static / baked lighting
- streamed music and ambience
- symbolic offscreen simulation
- fixed, semi-fixed, or constrained cameras
- fog, walls, turns, cliffs, trees, and darkness used to hide distance

Avoid designing for:

- fully seamless modern open world
- many dynamic lights
- many unique textures per area
- persistent simulation everywhere
- broad vistas with dense detail
- high object count scenes full of separately submitted tiny props

---

## 2. The Three Main Memory Battles

Think of the project as separate budget fights.

| Resource | What fights it | Main strategy |
|---|---|---|
| **SPU RAM** | resident SFX, voice snippets, short samples | SPU/XA/CDDA routing |
| **Main RAM** | meshes, animation, scripts, entities, collision, chunk state | compact formats, chunking, symbolic offscreen state |
| **VRAM** | textures, sprites, UI, CLUTs, decals, framebuffers | 4bpp/8bpp indexed textures, atlases, area texture sets |
| **CD-ROM** | scattered files, streaming, music/cutscene data | area archives, sequential reads, disc layout planning |
| **GPU / OT** | primitives, texture page changes, alpha, object count | batching, static grouping, draw phase discipline |

---

# 3. Lighting Without Huge Performance Cost

## 3.1 Lighting goal

The target should not be physically correct lighting.

The target should be:

```text
Readable forms.
Strong mood.
Cheap runtime.
Baked or precomputed wherever possible.
Tiny runtime math only where it pays off.
```

For a PS1-style RPG, most lighting should come from:

1. **Vertex colors**
2. **Baked texture shading**
3. **Palette / CLUT mood shifts**
4. **Fog and background color**
5. **Small fake light effects**
6. **Very limited runtime directional lighting**
7. **Author-placed highlight/shadow geometry**

Do not start with per-pixel lighting. Do not start with many dynamic lights.

---

## 3.2 Lighting tier system

Use a tiered lighting model.

### Tier 0 — Unlit textured

Best for:

- UI
- sprites
- billboards
- simple props
- intentionally flat PS1 art
- far scenery

Cost:

- cheapest
- no lighting math
- texture + vertex color only, if desired

Use when the texture already has baked lighting or the object does not need form.

---

### Tier 1 — Vertex color baked lighting

Best default for most world geometry.

Author/exporter stores per-vertex color:

```text
final_color = texture_color * vertex_color
```

Use for:

- walls
- floors
- roads
- cliffs
- buildings
- caves
- interiors
- static props

Benefits:

- very cheap at runtime
- authentic PS1 feel
- works well with low-poly geometry
- easy to art-direct
- no dynamic light loops
- no per-pixel cost

Exporter/editor can generate vertex colors from:

- manual vertex painting
- Godot light bake approximation
- simple directional bake
- ambient occlusion approximation
- height-based gradients
- hand-authored color zones
- baked texture brightness

Recommended default:

```text
Static world geometry = textured + baked vertex colors.
```

---

### Tier 2 — Baked texture lighting

Use when vertex density is too low for smooth lighting.

Good uses:

- signs
- interiors
- wall panels
- hero props
- doors
- large setpiece surfaces
- character face/skin texture details
- fake light cones or stains

Bad uses:

- unique lighting baked into every object
- huge unique lightmapped textures
- large high-color textures
- lightmaps that destroy VRAM budget

Use baked texture shading sparingly, usually inside the same atlas.

---

### Tier 3 — Palette / CLUT mood lighting

This is very PS1-friendly.

Instead of recalculating lighting, swap or tint palettes:

```text
forest_day.clut
forest_dusk.clut
forest_night.clut
cave_warm.clut
cave_cold.clut
poison_swamp.clut
```

Use for:

- time-of-day mood changes
- dungeon color shifts
- damage flashes
- magic effects
- enemy variants
- regional themes
- cutscene mood changes

Advantages:

- cheap
- cohesive
- PS1-authentic
- small data compared with new textures

Rules:

- Prefer palette swaps for broad mood.
- Prefer vertex colors for local lighting.
- Avoid unique new textures where a CLUT can do the job.

---

### Tier 4 — Runtime directional light

Optional, limited.

Use one simple directional light for dynamic characters if needed:

```text
character_color = texture * vertex_color * directional_term
```

Possible approximation:

```text
brightness = ambient + max(0, dot(vertex_normal, light_dir)) * strength
```

But on PS1-style content, even this may be overkill for every vertex.

Use on:

- player character
- important NPCs
- enemies
- large animated objects

Avoid on:

- static world geometry
- every prop
- far NPCs
- particles
- UI

Performance rule:

```text
Only dynamic/skinned actors get runtime directional lighting.
Static world gets baked lighting.
```

---

### Tier 5 — Fake local lights

Avoid real dynamic point lights. Fake them.

Examples:

- vertex-colored glow patches near torches
- additive/cutout flame sprite
- orange vertex-color gradient on nearby wall mesh
- small transparent light cone quad
- palette-swapped “lit” material variant
- pre-authored alternate mesh color
- emissive texture region
- flickering sprite, not flickering geometry light

For torches:

```text
Torch object:
  flame sprite / simple animated texture
  baked orange vertex colors around wall/floor
  optional tiny flicker by modulating flame sprite only
```

For magic:

```text
Magic spell:
  small additive/cutout sprite burst
  short-lived particle budget
  optional brief character palette flash
```

Do not add a runtime point light that loops over nearby geometry.

---

## 3.3 Recommended lighting pipeline

### Static world

```text
Author in Godot
  ↓
Exporter samples/uses baked vertex colors
  ↓
Splashpack stores vertex color per vertex/primitive
  ↓
Runtime multiplies texture by vertex color
```

Static lighting metadata:

```text
LightingMode: BakedVertex
AmbientColor
FogColor
BackgroundColor
PaletteGroup
```

### Dynamic characters

```text
Base texture + optional vertex colors
  +
one cheap area directional/ambient term
  +
optional palette/mood tint
```

Dynamic actor metadata:

```text
CharacterLighting:
  AmbientColor
  DirectionalColor
  DirectionalVector
  MinBrightness
  UseChunkLightingProfile
```

### Scene/chunk lighting profile

Each `PS1Chunk` should eventually own lighting data:

```text
PS1ChunkLightingProfile:
  BackgroundColor
  FogColor
  FogNear
  FogFar
  AmbientColor
  KeyLightDirection
  KeyLightColor
  CharacterLightMode: None | AmbientOnly | Directional
  PaletteMood
```

---

## 3.4 Lighting authoring tools

Add editor tools rather than runtime complexity.

Useful tools:

- **Bake simple vertex lighting**
- **Paint vertex color**
- **Apply height gradient**
- **Apply radial fake light**
- **Apply ambient occlusion approximation**
- **Preview chunk lighting profile**
- **Preview character lighting probe**
- **Show overbright/clamped vertices**
- **Show unlit objects**

Plain-English warnings:

```text
Object "TownLamp_04" has dynamic light enabled.
Real dynamic lights are expensive on PS1.
Recommended fix: bake orange vertex colors onto nearby wall/floor.
```

```text
Mesh "CaveWall_02" has no vertex colors and no baked texture shading.
It may look flat in-game.
[Generate simple vertex lighting] [Ignore]
```

---

## 3.5 What not to do

Avoid:

- per-pixel lights
- shadow maps
- real-time point lights over world geometry
- many dynamic lights
- full-scene light recalculation
- unique lightmaps for every object
- high-resolution baked lightmaps
- lighting that requires high vertex density everywhere
- normal maps

Use the PS1 look as an advantage:

```text
Strong art direction beats expensive lighting.
```

---

# 4. Skybox / Sky Dome / Background Strategy

## 4.1 Problem

You already have skybox support, but excessive fog can hide or flatten it.

This happens because fog and clear/background color are often treated as one visual system:

```text
fog color = distance haze
fog color = empty sky / clear color
```

That works for some scenes, but fails when:

- you want blue sky and grey fog
- you want dusk sky and brown dust haze
- you want green forest fog without a green flat sky
- you want a skybox visible above fogged terrain
- you want fog only at distance, not right in front of the camera

---

## 4.2 Separate sky, clear color, and fog

Use three concepts:

```text
Sky visual       What is behind the world
BackgroundColor Framebuffer clear / fallback empty color
FogColor        Distance tint applied to geometry
```

Do not force all three to be the same color.

Suggested chunk fields:

```text
SkyMode: None | ClearColor | Skybox | SkyDome | SkyCard
BackgroundColor
FogColor
FogNear
FogFar
SkyTexturePage
SkyBrightness
SkyScrollSpeed optional
```

---

## 4.3 Fog should have independent near/far

The current improvement notes already identify this as important: fog is one of the cheapest ways to hide PS1 draw distance, but one density value is too blunt.

Preferred model:

```text
FogNear = distance where fog starts
FogFar  = distance where geometry becomes full fog color
```

For open fields:

```text
FogNear: medium/far
FogFar: far
Sky: visible above horizon
```

For caves:

```text
FogNear: close
FogFar: medium
Sky: none / black clear
```

For forests:

```text
FogNear: medium
FogFar: medium/far
Sky: partially visible through canopy / sky cards
```

For towns:

```text
FogNear: far enough that nearby streets stay readable
FogFar: at district edge
Sky: visible between rooftops
```

---

## 4.4 Skybox options ranked

### Option A — Clear color only

Cheapest.

Good for:

- interiors
- caves
- void spaces
- night scenes
- fog-heavy scenes
- menu/special scenes

Cost:

- almost free

Downside:

- no sky detail

---

### Option B — Sky card / horizon card

A few textured quads behind the scene.

Good for:

- mountains
- city skyline
- treeline
- clouds
- painted horizon
- distant castle

Benefits:

- very cheap
- controllable
- strong PS1 vibe
- easy to hide with fog

Rules:

- keep texture small
- use 4bpp/8bpp
- keep it in an area atlas
- draw behind world
- avoid excessive alpha

---

### Option C — Sky dome / skybox

Useful, but budget it.

Good for:

- open field chunks
- world map
- big establishing scenes
- title/splash scenes

Rules:

- low-poly
- low-res texture
- no collision
- no lighting required
- draw before world or as special background pass
- do not overuse unique sky textures
- prefer palette variants for day/dusk/night

---

### Option D — Painted background / prerendered backdrop

Excellent for fixed-camera views.

Good for:

- towns
- interiors
- scenic overlooks
- unreachable distance
- RPG setpieces

Rules:

- budget VRAM carefully
- use 8bpp only if needed
- split large images if necessary
- keep foreground collision/interactive geometry separate
- load only for the current camera/chunk

---

## 4.5 Make fog and sky work together

A good outdoor PS1 chunk often has:

```text
Near:
  readable characters and ground, minimal fog

Mid:
  buildings, trees, cliffs, props

Far:
  fogged silhouettes, sky card/horizon shape

Beyond:
  clear color or skybox
```

Practical setup:

```text
BackgroundColor = sky upper color
FogColor        = horizon haze color
FogNear         = past gameplay space
FogFar          = before draw-distance cutoff
Sky             = low-cost sky card or dome
```

Do not let fog begin at the player’s feet unless it is a cave/night/horror scene.

---

# 5. Third-Person Right-Stick Camera

## 5.1 Goal

You want a third-person camera that moves with the right stick, but stays PS1-friendly.

The camera should feel useful, not modern AAA.

Recommended target:

```text
Constrained third-person camera.
Right stick rotates orbit yaw/pitch.
Soft collision / obstruction handling.
Snap-to-behind button optional.
Chunk/camera-zone constraints.
```

Avoid:

- fully free modern camera in every scene
- camera that reveals unloaded/ugly chunk edges
- camera that forces huge draw distance
- camera that breaks fixed-camera composition
- camera that requires expensive collision traces against render geometry

---

## 5.2 Camera modes

Support multiple camera policies per chunk.

```text
CameraMode:
  Fixed
  FixedPresetBlend
  ThirdPersonConstrained
  ThirdPersonFree-ish
  BattleCamera
  Cutscene
```

Each chunk should decide which camera is allowed.

Examples:

```text
Town district:
  ThirdPersonConstrained

Interior:
  Fixed or semi-fixed

Dungeon corridor:
  ThirdPersonConstrained with yaw limits

Battle arena:
  BattleCamera

Cutscene:
  Cutscene

PS1/FFVII-style scenic room:
  FixedPresetBlend
```

---

## 5.3 Right-stick third-person camera model

Store camera state:

```text
CameraState:
  targetEntity
  yaw
  pitch
  distance
  height
  yawVelocity
  pitchVelocity
  currentPosition
  desiredPosition
  collisionAdjustedPosition
```

Inputs:

```text
right_stick_x → yaw delta
right_stick_y → pitch delta
```

Clamp pitch:

```text
pitch_min = -0.10 pi-units
pitch_max =  0.18 pi-units
```

Clamp or guide yaw depending on chunk:

```text
yaw_min optional
yaw_max optional
allow_full_orbit true/false
```

Typical PS1-friendly camera:

```text
distance = 4 to 7 world units
height   = 1 to 2 world units
pitch    = slight downward angle
```

---

## 5.4 Camera collision

Do not start with complex mesh collision.

Use simple collision layers:

- camera blockers as boxes
- walls as simplified AABB/planes
- optional raycast against solid colliders
- no triangle-perfect camera collision at first

Basic algorithm:

```text
desired = target + orbit_offset
ray = target_head → desired
if ray hits camera blocker:
    camera = hit_point moved slightly toward target
else:
    camera = desired
```

Add smoothing:

```text
camera_pos = lerp(camera_pos, collisionAdjustedPosition, smoothing)
```

If collision gets messy, use authored camera zones instead of smarter runtime code.

---

## 5.5 Camera zones

A PS1 RPG should use authored camera constraints.

Example:

```text
PS1CameraZone:
  Mode: ThirdPersonConstrained
  YawMin
  YawMax
  PitchMin
  PitchMax
  MinDistance
  MaxDistance
  PreferredYaw
  PreferredPitch
  FogOverride optional
  DrawDistanceOverride optional
```

Use zones to prevent the camera from seeing:

- unloaded chunks
- missing backsides of buildings
- horizon gaps
- ugly portal seams
- unlit/unfinished areas
- too many objects at once

This is not a hack. It is PS1-era camera design.

---

## 5.6 Fixed camera still matters

Even if you add right-stick third-person camera, do not abandon fixed cameras.

Fixed cameras are valuable for:

- interiors
- dramatic scenes
- towns with dense art
- cutscenes
- shops
- puzzle rooms
- performance-sensitive views
- hiding unloaded geometry

The existing `PS1FixedCamera` direction is still essential.

Recommended camera policy:

```text
Large fields / roads:
  constrained third-person

Town streets:
  constrained third-person or fixed-preset blends

Interiors:
  fixed camera or constrained low-distance orbit

Dungeons:
  mixed; fixed for scenic rooms, third-person for combat corridors

Cutscenes:
  authored camera tracks
```

---

## 5.7 Lua/API design

Future Lua API could be:

```lua
Camera.SetControllerMode("third_person")
Camera.SetTarget("Player")
Camera.SetOrbit(yaw, pitch, distance)
Camera.SetOrbitLimits(yawMin, yawMax, pitchMin, pitchMax)
Camera.SetCollisionEnabled(true)
Camera.SetZone("town_square_north_camera")
Camera.ResetBehindPlayer()
```

But if current runtime APIs are limited, do not invent fake working APIs in game scripts.

Scaffold editor/runtime architecture first:

```text
PS1CameraController
PS1CameraZone
PS1FixedCamera
CameraPreset
CameraProfile
```

---

## 5.8 Performance warnings for camera

A free camera increases visible scene complexity.

Add editor warnings:

```text
Chunk uses ThirdPersonFree camera but has no camera blockers or fog boundary.
Player may see outside the authored area.
```

```text
Camera max distance is 9.0 in a dense town chunk.
This may reveal too many objects at once.
Recommended: lower distance or add camera zones.
```

```text
Camera pitch allows high overhead view.
This may expose unloaded neighboring chunks.
```

---

# 6. Renderer / GPU / Ordering Table Improvements

## 6.1 Track render packets, not just triangles

Triangle count is useful but incomplete.

Track:

- primitive count
- object count
- texture page switches
- CLUT switches
- alpha primitive count
- particle/sprite count
- ordering-table bucket usage
- UI primitive count
- dynamic/skinned primitive count

Budget panel example:

```text
Tris:          1840 / 2500
Prims:          970 / 1400
Objects:         62 / 100
TPages:           5 / 8
CLUTs:           14 / 24
Alpha prims:     42 / 80
Sprites:         18 / 48
Skinned chars:    3 / 5
OT buckets:     384 / 1024 touched
```

---

## 6.2 Merge static submit units

Exporter should group static geometry by:

```text
chunk
texture page
CLUT
alpha mode
render phase
```

Instead of many small objects:

```text
crate_01
crate_02
wall_01
floor_01
trim_01
```

Build larger render groups:

```text
ChunkRenderGroup:
  ChunkId: town_square_north
  TPage: town_world_01
  CLUT: town_day
  AlphaMode: Opaque
  PrimitiveRange: ...
```

Keep separate only when needed:

- moving objects
- toggled objects
- animated objects
- collision-important props
- interactables
- skinned meshes

---

## 6.3 Draw phases

Use explicit draw phases:

```text
Background / sky
Opaque static world
Opaque dynamic props
Characters / enemies
Cutout decals / fake shadows
Semi-transparent effects
UI
```

Benefits:

- predictable ordering
- easier alpha debugging
- easier budget reporting
- fewer accidental expensive cases

---

## 6.4 Texture page discipline

Warnings to add:

```text
WARNING: chunk uses 11 world texture pages; target is 1–3.
WARNING: prop crate_unique_07 uses a one-off texture.
WARNING: 21 near-duplicate CLUTs detected.
WARNING: 16bpp gameplay texture used outside cutscene/title scene.
```

Per chunk, aim for:

```text
World pages: 1–3
Character pages: 1
FX page: 1
UI page: shared/common
Decal page: optional
```

---

## 6.5 Alpha budget

Alpha must be a hard budget.

Track:

```text
Cutout quads
Semi-transparent quads
Additive quads
Particle sprites
Fake shadow quads
UI alpha elements
```

Rules:

- prefer cutout over semi-transparent
- avoid overlapping alpha piles
- avoid huge transparent quads
- keep particles short-lived
- fake shadows should be tiny blob/cutout/dither textures
- UI drawn last and budgeted separately

---

# 7. GTE / Transform Strategy

## 7.1 Static world geometry

Bake static world transforms during export.

Runtime should not repeatedly transform object matrices for static props that never move.

For static world:

```text
author object transforms
  ↓
exporter bakes into chunk-local int16 vertices
  ↓
runtime submits grouped primitives
```

Benefits:

- less per-object matrix setup
- fewer runtime objects
- better cache/layout
- easier chunk streaming
- smaller object lists

---

## 7.2 Dynamic objects

Dynamic objects keep local vertices and one transform.

Use for:

- doors
- moving platforms
- NPCs
- enemies
- pickups
- physics-ish props
- interactables

Keep counts low.

---

## 7.3 Skinned meshes

Skinned meshes are expensive compared with static geometry.

Rules:

- low bone count
- low vertex count
- few active skinned characters
- simple animation clips
- no runtime lighting beyond cheap ambient/directional
- budget active skinned actors per chunk

Suggested budget:

```text
Small interior: 1–3 skinned actors
Town chunk:     3–6 skinned actors
Battle arena:   3–8 depending on enemy complexity
```

Use sprites/frozen far meshes for background crowds.

---

# 8. CD / Streaming / Archive Improvements

## 8.1 Area archives

Avoid many tiny files.

Build area archives:

```text
AREA_TOWN_NORTH.archive
  header
  mesh groups
  texture pages
  CLUTs
  collision
  scripts
  NPC definitions
  animation clips
  audio references
```

Benefits:

- fewer seeks
- predictable loading
- easier validation
- chunk ownership is clear
- multi-disc support becomes easier

---

## 8.2 Disc layout planner

Eventually the build tool should place likely-neighbor content close together.

Example:

```text
COMMON_RUNTIME
COMMON_UI
COMMON_SFX
AREA_HOME_TOWN
AREA_HOME_INTERIORS
AREA_NORTH_ROAD
AREA_FOREST_GATE
AREA_FOREST_01
XA_FOREST_MUSIC
```

This matters for physical CD play.

---

## 8.3 Disc-accurate test mode

For multi-disc builds, support:

```text
Dev mode:
  all content visible

Disc-accurate mode:
  only current disc content visible
```

Disc-accurate mode catches:

- wrong-disc chunk references
- missing common files
- XA assigned to wrong disc
- save file requiring missing chunk
- cutscene assets not duplicated

---

# 9. Runtime / Lua / Gameplay Performance

## 9.1 Update budgets

Do not update every system every frame.

Use tiers:

```text
Every frame:
  player
  camera
  active enemies
  active interactables
  UI

Every 4 frames:
  passive nearby NPC logic
  ambient emitters
  simple proximity checks

Every 16 frames:
  offscreen schedules
  low-priority triggers
  world-state ticks

On load/transition:
  quest reconstruction
  NPC placement
  chunk state restore
```

---

## 9.2 Symbolic offscreen simulation

Do not simulate unloaded NPCs as real objects.

Store logical state:

```text
NPCState:
  schedule phase
  logical location
  quest flags
  mood/state
  last interaction
```

When a chunk loads:

```text
if NPC schedule says blacksmith is here:
    spawn blacksmith from pool
else:
    do not instantiate
```

---

## 9.3 Lua profiler

Add per-hook timing:

```text
onUpdate(PlayerController): 0.18 ms
onUpdate(CameraController): 0.12 ms
onUpdate(NPC_Blacksmith): 0.04 ms
Scene script total: 0.52 ms
Lua total: 1.20 ms
```

Warn on:

```text
Script "town_npc_schedule.lua" used 2.4 ms this frame.
Consider moving schedule checks to every 16 frames.
```

---

## 9.4 Host-mode validator

A host/test build would be extremely useful.

It should validate:

- every splashpack loads
- every chunk fits budget
- every texture fits VRAM rules
- every Lua file parses
- every transition target exists
- every disc manifest is valid
- every save entry point resolves
- every audio route is valid
- every camera preset is valid

This improves performance indirectly by catching bad data early.

---

# 10. Collision and Raycast Strategy

## 10.1 Separate visual and collision geometry

Do not collide against detailed render mesh by default.

Use:

- simple floor meshes
- low-poly wall blockers
- AABB triggers
- camera blockers
- nav regions
- interact boxes

Suggested layers:

```text
Visual
PlayerCollision
CameraCollision
Interaction
Trigger
Nav
```

---

## 10.2 Camera collision

Camera collision should use the simplest layer.

Do not raycast against all render triangles.

Use:

```text
target_head → desired_camera_position
against CameraCollision only
```

---

# 11. LOD / Distance Strategy

Use PS1-style LOD, not modern continuous LOD.

```text
Near:
  real mesh + collision + script

Mid:
  simple mesh, no script, simple/no collision

Far:
  billboard, silhouette, sky card, painted background, or nothing
```

Examples:

- distant NPC = sprite/frozen pose
- distant building = flat facade
- far forest = treeline card
- far castle = sky card
- far crowd = animated texture

---

# 12. Editor Budget and Warning System

## 12.1 Required budget categories

Per scene/chunk:

```text
Triangles
Primitives
Objects
Active NPCs
Dynamic props
Skinned actors
Particles/sprites
Alpha quads
Texture pages
CLUTs
VRAM estimate
SPU resident bytes
XA/CDDA streamed assets
Scripts
Collision volumes
Camera blockers
Disc archive size
```

---

## 12.2 Plain-English warnings

Examples:

```text
Chunk "TownSquareNorth" uses 9 texture pages.
Target for a town chunk is 1–3 world pages plus character/FX/UI pages.
[Open Texture Page View] [Ignore]
```

```text
Fog starts very close to the player, but this chunk uses a skybox.
The sky may be hard to see.
Recommended: increase FogNear or use a horizon card.
```

```text
Third-person camera can rotate behind the market stalls and see unloaded space.
Add a CameraZone yaw limit or camera blocker.
```

```text
Object "Lantern_08" uses dynamic lighting.
Recommended: bake vertex glow into nearby geometry and animate the flame sprite only.
```

---

# 13. Suggested Roadmap

## Phase A — Low-cost lighting foundation

- Add chunk lighting profile
- Support vertex color lighting path
- Add simple ambient/directional character lighting option
- Add vertex color bake/paint tools
- Add warnings for unlit world meshes

## Phase B — Fog / sky separation

- Separate `BackgroundColor` from `FogColor`
- Add `FogNear` and `FogFar`
- Add sky mode metadata
- Add warnings when fog hides sky

## Phase C — Third-person camera controller

- Add `PS1CameraController`
- Add `PS1CameraZone`
- Add right-stick orbit yaw/pitch
- Add pitch/distance limits
- Add simple camera collision layer
- Add warnings for camera visibility leaks

## Phase D — Render packet reporting

- Add primitive count
- Add texture page/CLUT switch estimate
- Add alpha primitive count
- Add ordering-table pressure estimate
- Add static/dynamic/skinned breakdown

## Phase E — Static render grouping

- Group static geometry by chunk / texture page / CLUT / alpha mode
- Keep dynamic/interactable objects separate
- Report draw phases and render groups

## Phase F — Area archive and disc layout

- Create area archive format
- Add chunk-to-archive ownership
- Add disc layout metadata
- Add disc-accurate validation mode

## Phase G — Runtime profiling

- Add Lua per-hook timing
- Add frame budget overlay/log
- Add host-mode splashpack validator if feasible

---

# 14. IDE-Agent Implementation Prompt

Use this prompt when asking an IDE agent to implement the next slice:

```text
You are helping me evolve my PS1Godot / psxsplash project toward a large-feeling chunk-based PS1-style action RPG.

Focus on performance foundations, low-cost lighting, sky/fog control, and a constrained third-person right-stick camera. Do not chase modern renderer features. Preserve the current jam/demo builds.

Main goals:
1. Add or scaffold a chunk lighting profile.
2. Support cheap lighting paths:
   - unlit
   - baked vertex color
   - baked texture shading
   - palette/CLUT mood shifts
   - optional cheap ambient/directional character lighting
3. Do not implement expensive dynamic point lights over world geometry.
4. Separate sky/background/fog concepts:
   - BackgroundColor
   - FogColor
   - FogNear
   - FogFar
   - SkyMode
5. Add warnings for skyboxes hidden by excessive fog.
6. Design/scaffold a constrained third-person right-stick camera:
   - orbit yaw/pitch
   - pitch/distance clamps
   - camera zones
   - simple camera collision layer
   - snap/reset behind player optional
7. Add performance reporting beyond triangle count:
   - primitive count
   - object count
   - texture pages
   - CLUTs
   - alpha primitives
   - skinned actors
   - particles/sprites
   - VRAM estimate
   - SPU estimate
   - script count
   - collision volume count
8. Prefer editor/exporter validation and metadata over risky runtime rewrites.
9. Keep APIs honest. If a runtime feature is only scaffolded, mark it clearly.

Implementation priorities:
1. Preserve current builds.
2. Add documentation and metadata first.
3. Add budget/warning/report scaffolding.
4. Add lighting profile shape.
5. Add fog/sky/background separation proposal.
6. Add camera controller and camera zone proposal.
7. Only implement runtime changes if they are small, safe, and testable.

Deliverables:
- Updated docs
- Proposed data models
- Any safe metadata additions
- Any safe editor warnings/reports
- Clear list of implemented vs scaffolded items
- Clear test instructions
```

---

# 15. Bottom Line

For your project, the best performance wins are:

1. **Bake lighting instead of calculating it.**
2. **Use vertex colors as the default lighting system.**
3. **Use CLUT/palette shifts for mood.**
4. **Fake local lights with sprites, vertex colors, and material variants.**
5. **Separate fog from sky/background color.**
6. **Use fog to hide limits, not to erase the scene.**
7. **Constrain third-person camera with zones so it cannot reveal too much.**
8. **Report primitive/object/texture/alpha pressure, not just triangles.**
9. **Group static render data by chunk and texture page.**
10. **Make the editor warn before the runtime suffers.**

The PS1 look gives permission to cheat. The more intentionally you cheat, the bigger and better the RPG can feel.
