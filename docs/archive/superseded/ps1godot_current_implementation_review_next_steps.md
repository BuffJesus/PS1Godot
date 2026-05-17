# PS1Godot Current Implementation Review and Next-Step Plan

**Purpose:**  
This document reviews the current PS1Godot / psxsplash-facing implementation direction and turns the findings into a practical roadmap for building toward a larger, chunk-based PS1-style RPG.

The main focus areas are:

- cheap lighting
- fog / sky / background separation
- right-stick third-person camera support
- audio routing
- chunk metadata
- budget tooling
- static render grouping
- long-term RPG scalability

This is meant to be useful both as a planning document and as an IDE-agent reference.

---

## 1. Big Picture Assessment

The current project is no longer just a small PS1 scene exporter. It is starting to become a real PS1 RPG authoring pipeline.

The strongest foundations so far are:

- PS1-style scene authoring in Godot
- splashpack export path
- PS1 material/shader preview
- scene statistics and budget dock direction
- audio clip metadata
- sky support
- room / portal / navigation concepts
- early RPG optimization documentation
- large-project architecture thinking
- multi-disc planning
- performance and camera strategy notes

The next maturity jump is to move from:

```text
Scene exporter + preview shader + basic budgets + scattered advanced stubs
```

to:

```text
Authoring metadata + budget enforcement + baked lighting + safe cameras + chunk ownership
```

The most important near-term visual feature is:

```text
Vertex color lighting.
```

The most important medium-term RPG production feature is:

```text
PS1Chunk.
```

---

# 2. Current Strengths

## 2.1 Scene authoring foundation

The project already supports the idea of authoring PS1 scenes in Godot and exporting them into a runtime-consumable format.

This is important because the long-term RPG goal needs the editor to act as the content control center:

- scene layout
- camera placement
- mesh setup
- collision setup
- room/portal setup
- audio metadata
- UI canvases
- budgets
- export validation

The author should not need to manually reason about every PS1 constraint every time. The plugin should turn constraints into visible warnings and sensible defaults.

---

## 2.2 Budget tooling already exists

The current scene stats direction is one of the most important pieces.

Existing or implied useful categories include:

- mesh count
- triangle count
- audio clip count
- texture count
- rough VRAM estimate
- rough SPU estimate

This is exactly the right UX direction.

For a PS1-style RPG, the budget panel is not a bonus feature. It is production infrastructure.

---

## 2.3 Sky support exists

Having sky support already is good.

A cheap sky system helps create the illusion of a larger world without rendering expensive distant geometry.

The next step is making sure the sky works with fog instead of being erased by it.

---

## 2.4 Audio metadata exists

Audio residency metadata is already a useful start.

However, residency and routing need to be separated.

There is a difference between:

```text
This clip is resident in SPU RAM.
```

and:

```text
This audio asset is played through SPU, XA, or CDDA.
```

That difference matters a lot once the project supports real streamed music, ambience, voice, and multi-disc layouts.

---

## 2.5 Vertex color modes are conceptually in the right place

If the project already exposes vertex color / baked lighting options, that is the correct direction.

The missing piece is making those modes actually affect exported runtime data.

The cheapest good-looking PS1 lighting path is:

```text
texture color * vertex color
```

That should become the default static lighting strategy.

---

# 3. Highest Priority Gap: Lighting

## 3.1 Problem

The project currently lacks a complete low-cost lighting path.

For a PS1-style RPG, dynamic lighting should not be the default answer. The default answer should be baked, authored, or palette-driven lighting.

The lighting system should prioritize:

- static vertex colors
- baked texture shading
- CLUT/palette mood shifts
- simple character ambient/directional lighting
- fake local lights
- no expensive world-space dynamic point lights

---

## 3.2 Recommended first implementation: Mesh vertex colors

Implement mesh vertex color export before anything else.

### Behavior

For a mesh with vertex colors:

```text
exported vertex color = imported/authored mesh vertex color
```

For a mesh without vertex colors:

```text
warn and fall back to FlatColor
```

### Why this should come first

Vertex colors allow:

- hand-painted lighting
- Blender-baked lighting
- simple ambient occlusion
- fake torch glow
- mood gradients
- cheap PS1-style shading
- no runtime lighting cost

This one feature unlocks a huge visual improvement.

---

## 3.3 Recommended second implementation: simple baked lighting

After mesh vertex colors work, add a simple bake helper.

Do not start with complex Godot light baking.

Start with tools such as:

```text
Apply directional light to vertex colors
Apply height gradient
Apply radial fake light
Apply ambient tint
Apply darken-by-normal
Apply room mood color
```

Useful examples:

### Cave

```text
Base ambient: dark blue-grey
Height gradient: darker near ceiling
Fake torch: radial orange vertex color patch near torch mesh
```

### Town street

```text
Base ambient: warm grey
Directional: slight sun from front-left
Doorways: painted dark vertex colors
Windows: small warm glow patches
```

### Forest

```text
Base ambient: muted green
Canopy shade: vertex darkening
Path: slightly brighter vertex colors
Fog: green/blue distance tint
```

---

## 3.4 Lighting tiers

Use explicit lighting tiers.

```text
Unlit
FlatColor
MeshVertexColors
BakedLighting
CharacterAmbient
CharacterDirectional
FakeLightOnly
```

Recommended defaults:

| Asset type | Lighting mode |
|---|---|
| Static world | MeshVertexColors or BakedLighting |
| Simple props | MeshVertexColors |
| UI | Unlit |
| Sprites | Unlit or FlatColor |
| Particles | Unlit |
| Player | CharacterAmbient or CharacterDirectional |
| NPCs | CharacterAmbient or CharacterDirectional |
| Far background | Unlit / baked texture |

---

## 3.5 Fake local lights

Do not implement real point lights over the world first.

Fake them with:

- vertex color patches
- small glow sprites
- additive/cutout quads
- palette swaps
- material variants
- animated flame sprites
- baked texture highlights

Example torch:

```text
Torch flame:
  animated sprite or tiny mesh

Nearby wall:
  baked orange vertex color patch

Nearby floor:
  faint orange vertex color patch

Runtime:
  only flame flickers
```

This looks good and costs almost nothing.

---

# 4. Fog, Sky, and Background

## 4.1 Problem

Sky support can be undermined by excessive fog.

If fog starts too close or uses the same color as the background, the sky can become difficult to see.

For RPG outdoor areas, the player needs:

```text
readable near space
controlled mid-distance
fogged far distance
visible sky/horizon
```

---

## 4.2 Separate these three concepts

Do not treat these as one setting:

```text
BackgroundColor
FogColor
Sky
```

They should be separate.

### BackgroundColor

The fallback clear color behind everything.

### FogColor

The color geometry fades toward at distance.

### Sky

The visual backdrop: clear color, sky card, skybox, sky dome, or painted background.

---

## 4.3 Add FogNear and FogFar

A single fog density value is too blunt for RPG authoring.

Use:

```text
FogNear = where fog starts
FogFar  = where geometry reaches full fog color
```

Examples:

### Open field

```text
BackgroundColor: pale blue
FogColor: pale grey-blue
FogNear: medium/far
FogFar: far
Sky: visible sky card or dome
```

### Cave

```text
BackgroundColor: black
FogColor: dark blue-grey
FogNear: close
FogFar: medium
Sky: none
```

### Forest

```text
BackgroundColor: desaturated blue
FogColor: green-blue haze
FogNear: medium
FogFar: medium/far
Sky: partially visible through treeline/canopy
```

### Town

```text
BackgroundColor: sky tone
FogColor: warm haze
FogNear: past gameplay street space
FogFar: at district edge
Sky: visible above rooftops
```

---

## 4.4 Recommended sky modes

```text
None
ClearColor
SkyCard
Skybox
SkyDome
PaintedBackdrop
```

### ClearColor

Best for:

- caves
- interiors
- menus
- fog-heavy scenes

### SkyCard

Best for:

- horizon lines
- mountains
- treelines
- town skylines
- clouds

### Skybox / SkyDome

Best for:

- open field chunks
- world map
- title scenes
- scenic areas

### PaintedBackdrop

Best for:

- fixed camera scenes
- interiors
- scenic overlooks
- prerendered-style RPG areas

---

## 4.5 Add warnings

Useful editor warnings:

```text
Sky is enabled but FogNear is very close. The sky may be hard to see.
```

```text
FogColor and BackgroundColor are identical. The scene may look flat.
```

```text
Skybox texture is 16bpp. Consider 4bpp or 8bpp unless this is a title/cutscene scene.
```

```text
FogFar is beyond the chunk visibility boundary. The player may see unloaded space.
```

---

# 5. Third-Person Right-Stick Camera

## 5.1 Goal

The goal should not be a fully modern free camera.

The goal should be:

```text
Constrained third-person camera with right-stick orbit.
```

The camera should give the player control without allowing them to see broken/unloaded/expensive areas.

---

## 5.2 Camera modes

Support multiple camera modes per scene/chunk:

```text
Fixed
FixedPresetBlend
ThirdPersonConstrained
ThirdPersonFreeLimited
Orbit
Battle
Cutscene
```

Recommended usage:

| Area type | Camera |
|---|---|
| Field road | ThirdPersonConstrained |
| Town street | ThirdPersonConstrained or FixedPresetBlend |
| Interior | Fixed or constrained close camera |
| Dungeon corridor | ThirdPersonConstrained with yaw limits |
| Battle arena | Battle camera |
| Cutscene | Cutscene |
| Scenic room | Fixed camera |

---

## 5.3 Required camera-zone concept

Before implementing a free right-stick camera, add:

```text
PS1CameraZone
```

Suggested fields:

```text
Mode
YawMin
YawMax
PitchMin
PitchMax
MinDistance
MaxDistance
PreferredYaw
PreferredPitch
AllowRightStick
CameraCollisionEnabled
DrawDistanceOverride
FogOverride
```

Camera zones prevent the camera from revealing:

- unloaded chunks
- missing backsides of buildings
- empty void
- low-detail set dressing
- portal seams
- too many objects at once

---

## 5.4 Right-stick orbit behavior

State:

```text
target
yaw
pitch
distance
height
desired_position
actual_position
```

Input:

```text
right_stick_x -> yaw change
right_stick_y -> pitch change
```

Clamp:

```text
pitch_min
pitch_max
distance_min
distance_max
optional yaw_min / yaw_max
```

Add smoothing:

```text
actual_position = lerp(actual_position, desired_position, smoothing)
```

Optional feature:

```text
ResetBehindPlayer()
```

---

## 5.5 Camera collision

Keep it simple.

Do not raycast against detailed render geometry.

Use a dedicated camera collision layer:

```text
CameraCollision
```

Basic algorithm:

```text
desired = target + orbit offset
raycast from target head to desired
if hit:
  place camera slightly in front of hit
else:
  use desired
```

If camera collision becomes messy, solve with camera zones first.

---

## 5.6 Camera warnings

Add editor warnings:

```text
Third-person camera allowed, but no camera zones exist in this chunk.
```

```text
Camera max distance may reveal unloaded neighboring chunks.
```

```text
Camera pitch allows high overhead view. This may expose missing set backsides.
```

```text
Camera collision enabled, but no CameraCollision geometry exists.
```

---

# 6. Audio Routing

## 6.1 Current issue

Audio residency is useful but not enough.

The project needs both:

```text
Route
Residency
```

### Route

How the audio is played:

```text
SPU
XA
CDDA
Auto
```

### Residency

When/how it is loaded:

```text
Gameplay
SceneOnly
MenuOnly
LoadOnDemand
Always
```

---

## 6.2 Recommended audio metadata

```text
AudioClip:
  Name
  SourcePath
  Route: SPU | XA | CDDA | Auto
  Residency: Always | Gameplay | SceneOnly | MenuOnly | LoadOnDemand
  Loop
  LatencySensitive
  Priority
  Volume
  PanDefault
  Duration
  EncodedSize
```

---

## 6.3 Auto routing rules

```text
UI sound -> SPU
Footstep -> SPU
Impact -> SPU
Short repeated bark -> SPU
Music -> XA
Ambient loop -> XA
Narration -> XA
Large stinger -> XA unless latency-sensitive
Title / credits -> XA by default, CDDA only if explicit
```

---

## 6.4 Build report

Report:

```text
clip name
route
residency
duration
source size
encoded size
SPU budget impact
disc/XA output path
warnings
```

Warnings:

```text
Large clip is routed to SPU.
```

```text
LoadOnDemand clip still packed into initial SPU blob.
```

```text
CDDA clip referenced but no CDDA track mapping exists.
```

---

# 7. PS1Chunk

## 7.1 Why it matters

The project needs a dedicated chunk primitive to scale from demos into an RPG.

A chunk is not just a scene.

A chunk owns:

- resident textures
- resident audio
- local geometry
- local lighting
- local fog/sky settings
- active actor budgets
- camera policy
- collision/nav
- transition links
- disc ownership

---

## 7.2 Suggested PS1Chunk fields

```text
PS1Chunk:
  ChunkId
  RegionId
  DiscId
  SceneType
  LightingProfile
  FogProfile
  SkyProfile
  AudioProfile
  CameraProfile
  TextureBudget
  ActorBudget
  EffectBudget
  PrimitiveBudget
  VRAMBudget
  SPUBudget
  NeighborChunks
  TransitionPoints
  AreaArchiveId
```

---

## 7.3 Chunk states

Use states:

```text
Active
Nearby
LoadedInactive
Unloaded
SymbolicOnly
```

### Active

Fully simulated and rendered.

### Nearby

Loaded or partially loaded, maybe used for transition buffering.

### LoadedInactive

Data resident, but entities not fully running.

### Unloaded

Not in memory.

### SymbolicOnly

Only global state exists, no actual objects.

---

## 7.4 Chunk budget panel

Per chunk:

```text
Triangles
Primitives
Objects
Texture pages
CLUTs
VRAM
SPU resident
XA streams
Skinned actors
Dynamic props
Alpha primitives
Particles
Collision volumes
Camera zones
Lights/fake lights
```

---

# 8. Budget Tooling Expansion

## 8.1 Current budget tooling is a good start

Keep it.

Expand it.

---

## 8.2 Add these statistics next

```text
Primitive count
Object count
Texture page count
CLUT count
Alpha texture count
Semi-transparent material count
Skinned mesh count
Collider count
Room count
Portal count
Nav region count
UI canvas count
Sprite count
Particle emitter count
Dynamic object count
Static object count
```

---

## 8.3 Add bit-depth breakdown

Example:

```text
Textures:
  4bpp: 18
  8bpp: 7
  16bpp: 2
  Unknown/source: 4
```

Warnings:

```text
16bpp gameplay texture used outside title/cutscene.
```

```text
Texture larger than 256x256.
```

```text
Large alpha texture detected.
```

---

## 8.4 Add render pressure report

```text
Opaque static prims
Opaque dynamic prims
Cutout prims
Semi-transparent prims
Additive prims
UI prims
Texture page switches estimate
CLUT switches estimate
```

This catches expensive scenes that triangle count alone misses.

---

# 9. Static Render Grouping

## 9.1 Goal

Let authors place objects naturally in Godot, but export them in a PS1-friendly layout.

Group static geometry by:

```text
Chunk
TexturePage
CLUT
AlphaMode
RenderPhase
```

---

## 9.2 Static vs dynamic

Static objects:

- walls
- floors
- cliffs
- buildings
- non-moving props
- baked decals
- terrain pieces

Dynamic objects:

- doors
- switches
- NPCs
- enemies
- pickups
- moving platforms
- scripted props

Static objects can be grouped aggressively.

Dynamic objects should stay separate.

---

## 9.3 Suggested render groups

```text
RenderGroup:
  ChunkId
  TexturePage
  CLUT
  AlphaMode
  RenderPhase
  PrimitiveStart
  PrimitiveCount
```

Render phases:

```text
Sky/background
Opaque static
Opaque dynamic
Characters
Cutout decals
Transparent effects
UI
```

---

# 10. Texture / Image / Alpha

## 10.1 Recommended policy

```text
World textures: 4bpp indexed
UI/font/message boxes: 4bpp indexed
Character textures: 4bpp or 8bpp
Important portraits: 8bpp
Cutscene stills: 8bpp, 16bpp only if approved
Decals: 4bpp cutout
Particles: 4bpp or 8bpp
```

---

## 10.2 Alpha rules

Prefer:

```text
Opaque
Cutout
Dithered fake alpha
Tiny semi-transparent quads
```

Avoid:

```text
large transparent planes
many overlapping particles
huge alpha textures
semi-transparent world materials everywhere
```

---

## 10.3 Atlas rules

Group by use:

```text
World
UI
Character
FX
Decal
Cutscene
```

Warn on:

```text
one texture per prop
too many unique palettes
large unatlased textures
16bpp gameplay textures
```

---

# 11. Multi-Disc Readiness

## 11.1 Add metadata early

Even before implementing real multi-disc support, add fields like:

```text
DiscId
DiscCount
AreaArchiveId
RequiresDisc
DiscSwapSafeTransition
```

---

## 11.2 Runtime later

Later runtime needs:

- disc manifest
- disc identity validation
- save file disc requirement
- disc swap UI
- per-disc area archive assignment
- per-disc XA/CDDA layout
- common data duplication

---

# 12. Suggested Next Implementation Order

## Step 1 — Vertex color lighting

Implement export of mesh vertex colors.

Deliver:

```text
MeshVertexColors mode works.
Missing vertex colors warn and fall back safely.
```

---

## Step 2 — Fog/sky metadata

Add authoring fields:

```text
BackgroundColor
FogNear
FogFar
SkyBrightness
SkyMode
```

Keep old export compatibility if runtime does not support them yet.

---

## Step 3 — Camera metadata

Wire existing camera mode fields into collected/exported scene data.

Add `PS1CameraZone` as metadata even before full runtime support.

---

## Step 4 — Budget expansion

Add cheap stats:

```text
object count
skinned mesh count
collider count
room count
portal count
nav region count
UI count
texture bit-depth breakdown
alpha asset count
```

---

## Step 5 — Audio routing metadata

Add:

```text
Route: SPU | XA | CDDA | Auto
Residency: Always | Gameplay | SceneOnly | MenuOnly | LoadOnDemand
```

Keep old behavior if runtime does not support XA/CDDA yet, but warn clearly.

---

## Step 6 — PS1Chunk metadata

Add chunk node without full streaming.

Use it for:

- budgets
- profiles
- region ownership
- future streaming
- disc ownership
- camera/fog/lighting/audio grouping

---

## Step 7 — Static render grouping report

Before changing renderer output, add reports showing how grouping would work.

Report:

```text
objects by texture page
triangles by texture page
alpha by texture page
potential static groups
one-off textures
```

---

# 13. IDE Agent Prompt

Use this prompt to continue implementation:

```text
You are helping me improve my PS1Godot / psxsplash authoring pipeline toward a larger chunk-based PS1-style RPG.

Do not rewrite the whole exporter or runtime. Preserve the current jam/demo build.

Main goals:
1. Implement the lowest-risk lighting path first:
   - MeshVertexColors export.
   - If a mesh lacks vertex colors, warn and fall back to FlatColor.
   - Do not implement expensive dynamic lights yet.

2. Add or scaffold better fog/sky/background metadata:
   - BackgroundColor
   - FogNear
   - FogFar
   - SkyMode
   - SkyBrightness
   Preserve old FogDensity behavior if runtime still requires it.

3. Wire existing camera mode metadata into scene collection/export where safe.
   Add or scaffold PS1CameraZone metadata:
   - Mode
   - YawMin/YawMax
   - PitchMin/PitchMax
   - MinDistance/MaxDistance
   - PreferredYaw/PreferredPitch
   - AllowRightStick
   - CameraCollisionEnabled

4. Expand scene budget/stat reporting:
   - object count
   - primitive estimate if available
   - skinned mesh count
   - collider count
   - room count
   - portal count
   - nav region count
   - UI canvas count
   - texture bit-depth breakdown
   - alpha texture/material count
   - rough VRAM and SPU estimates

5. Add audio routing metadata:
   - Route: SPU/XA/CDDA/Auto
   - Residency: Always/Gameplay/SceneOnly/MenuOnly/LoadOnDemand
   Do not fake XA/CDDA runtime playback if unsupported. Warn clearly.

6. Add PS1Chunk as metadata only if safe:
   - ChunkId
   - RegionId
   - DiscId
   - SceneType
   - LightingProfile
   - FogProfile
   - SkyProfile
   - AudioProfile
   - CameraProfile
   - budgets
   - neighbor chunks
   - area archive ID

Rules:
- Keep changes small and reviewable.
- Preserve source assets.
- Do not silently change visual output for all scenes.
- Prefer warnings and reports before automatic conversion.
- Do not invent runtime APIs that do not exist.
- Clearly separate implemented features from scaffolded metadata.
- Run existing tests/builds if available.

Deliver final notes with:
- Summary
- Files changed
- Implemented features
- Scaffolded-only features
- How to test
- Risks/TODOs
```

---

# 14. Bottom Line

The project is already pointed in the right direction.

The best next move is not combat, quests, or a massive world.

The best next move is:

```text
make the tool enforce PS1 production discipline
```

That means:

1. vertex color lighting
2. fog/sky/background separation
3. constrained camera zones
4. expanded budget reporting
5. honest audio route metadata
6. chunk ownership metadata
7. static grouping reports

Once those exist, the RPG systems can grow on top of a toolchain that keeps the project from becoming too heavy to run.
