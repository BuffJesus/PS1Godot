# PS1Godot Blender Add-on Integration Plan

**Project target:** PS1Godot / psxsplash / Blender authoring pipeline  
**Focus:** a Blender plug-in/add-on that integrates with PS1Godot for easy import, export, validation, and IDE-assisted development.

The goal is to make Blender a first-class companion tool for PS1Godot.

Blender should become the place where artists can:

```text
model
UV unwrap
paint vertex colors
author simple animations
assign PS1-friendly materials
check PS1 budgets
export directly to PS1Godot-ready resources
round-trip assets cleanly
```

PS1Godot remains the main game-authoring and deployment hub.  
Blender becomes the asset-authoring companion.

---

## 1. Core Vision

The Blender add-on should support:

```text
Import from PS1Godot project
Export to PS1Godot project
PS1-friendly mesh validation
PS1-friendly material setup
vertex color lighting workflow
texture/atlas/CLUT metadata
animation export metadata
collision helper authoring
origin/pivot/chunk metadata
asset ID tagging
binary-format preview/reporting
resource-file generation for IDE agents
```

The user should be able to:

```text
Open Blender
Import a PS1Godot asset/chunk
Edit mesh/UV/material/vertex colors
Validate against PS1 budgets
Export back to PS1Godot
Have IDs/metadata preserved
```

The add-on should not be a generic exporter only. It should understand PS1Godot concepts.

---

## 2. Relationship Between Tools

Recommended responsibility split:

| Tool | Responsibility |
|---|---|
| Blender | modeling, UVs, vertex colors, simple animation, collision helpers |
| PS1Godot | scene/chunk authoring, Lua, audio, deployment, runtime validation |
| psxsplash runtime | actual PS1-facing runtime |
| IDE agent | code/docs/resources/reference-assisted implementation |

Blender should not become the game editor.

It should export clean assets and metadata into the PS1Godot pipeline.

---

## 3. Main Add-on Features

## 3.1 Import

Import from PS1Godot project resources:

```text
PS1 mesh source assets
mesh binary debug manifests
texture atlases
materials
collision helpers
animation metadata
chunk metadata
```

Possible import sources:

```text
Godot scene-derived intermediate files
PS1Godot asset manifest
mesh debug manifest
source FBX/OBJ/glTF if still used during authoring
binary mesh bank debug view
```

Import should preserve:

```text
AssetId
MeshId
ChunkId
MaterialId
TexturePageId
CLUTId
PS1 role metadata
origin/pivot
vertex colors
UVs
collision markers
animation names
```

---

## 3.2 Export

Export to PS1Godot-friendly source resources.

Potential outputs:

```text
.meshsrc.json
.ps1meshmeta.json
.obj / glb source asset if desired
texture metadata
animation metadata
collision metadata
vertex color data
debug report
```

The Blender add-on does **not** need to directly write the final PS1 runtime binary first.

Better initial flow:

```text
Blender add-on
  ↓
PS1Godot source/intermediate files
  ↓
PS1Godot exporter
  ↓
final splashpack / mesh bank / runtime data
```

This keeps the final binary format owned by PS1Godot until the pipeline stabilizes.

Later, Blender can optionally export binary mesh banks directly for testing.

---

# 4. Add-on Panels

Recommended Blender UI panels:

```text
PS1Godot Project
PS1 Asset Metadata
PS1 Mesh Validation
PS1 Material / Texture Page
PS1 Vertex Lighting
PS1 Collision Helpers
PS1 Animation Export
PS1 Export
PS1 Reports
```

---

## 4.1 PS1Godot Project Panel

Fields:

```text
Project Root
Asset Output Folder
Texture Output Folder
Manifest Path
Export Profile
Default ChunkId
Default DiscId
```

Buttons:

```text
Locate PS1Godot Project
Load Asset Manifest
Refresh IDs
Open Output Folder
```

Purpose:

```text
connect Blender file to a PS1Godot project
```

---

## 4.2 PS1 Asset Metadata Panel

Per object fields:

```text
AssetId
MeshId
ChunkId
RegionId
DiscId
AreaArchiveId
MeshRole
ExportMode
MaterialId
TexturePageId
CLUTId
DrawPhase
ShadingMode
AlphaMode
CollisionLayer
```

Suggested enums:

```text
MeshRole:
  StaticWorld
  DynamicRigid
  Skinned
  Segmented
  SpriteBillboard
  CollisionOnly
  EditorOnly

ExportMode:
  MergeStatic
  KeepSeparate
  CollisionOnly
  Ignore

DrawPhase:
  OpaqueStatic
  OpaqueDynamic
  Characters
  CutoutDecals
  TransparentEffects
  UI

ShadingMode:
  Unlit
  FlatColor
  VertexColor
  BakedLighting

AlphaMode:
  Opaque
  Cutout
  SemiTransparent
  Additive
  UI
```

These fields should map cleanly to the PS1Godot mesh binary strategy.

---

## 4.3 PS1 Mesh Validation Panel

Show per selected object:

```text
vertex count
triangle count
quad count if available
material count
texture page count
UV bounds
vertex color presence
estimated binary size
index format estimate
warnings
```

Buttons:

```text
Validate Selected
Validate Scene
Generate Report
```

Example warnings:

```text
Object has 312 vertices and would require U16 indices.
Consider splitting if it can stay under 255 vertices.
```

```text
Mesh expects VertexColor shading but has no vertex color layer.
```

```text
Object uses 4 materials / texture pages.
Consider atlas cleanup.
```

```text
Static object is marked KeepSeparate but has no animation/collision/script role.
Consider MergeStatic.
```

---

## 4.4 PS1 Material / Texture Page Panel

For selected material:

```text
TextureFormat: Auto | 4bpp | 8bpp | 16bpp
TexturePageId
CLUTId
PaletteGroup
AtlasGroup
AlphaMode
DitherAllowed
ForceNoFilter
Approved16bpp
```

Buttons:

```text
Create PS1 Material
Assign Texture Page
Preview 4bpp
Preview 8bpp
Validate Material
```

Warnings:

```text
16bpp material not approved.
Large alpha texture.
Material missing texture page ID.
Too many unique CLUTs in scene.
```

---

## 4.5 PS1 Vertex Lighting Panel

This is one of the most important Blender-side features.

Support:

```text
vertex color painting workflow
baked vertex color helpers
fake light tools
ambient tint tools
height gradient tools
radial glow tools
darken-by-normal tools
```

Buttons:

```text
Create Vertex Color Layer
Bake Simple Directional Light
Apply Ambient Tint
Apply Height Gradient
Apply Radial Fake Light
Clear Vertex Lighting
Preview PS1 Vertex Lighting
```

Why this matters:

```text
vertex colors are the cheapest lighting path for PS1Godot
```

The add-on should make vertex-color lighting easy.

---

## 4.6 PS1 Collision Helpers Panel

Create and tag collision helper objects.

Collision types:

```text
PlayerCollision
CameraCollision
InteractionVolume
TriggerVolume
NavRegion
EncounterVolume
SavePoint
TransitionPoint
```

Buttons:

```text
Create Player Collision Box
Create Camera Blocker
Create Trigger Volume
Create Interaction Volume
Create Nav Region
Create Transition Point
```

Export metadata:

```text
CollisionId
CollisionType
Layer
ChunkId
TargetChunkId optional
SpawnPointId optional
```

Warnings:

```text
transition has no target chunk
camera collision missing in third-person chunk
collision object is too detailed
render mesh has collision flag but no simplified collision
```

---

## 4.7 PS1 Animation Export Panel

Support animation metadata and validation.

Fields:

```text
AnimationBankId
SkeletonId
CharacterMode
Residency
ChunkId
DiscId
FPS
Interpolation
ExportRootMotion
HasEvents
```

Character modes:

```text
Skinned
SegmentedRigid
SpriteBillboard
```

Buttons:

```text
Validate Animations
Export Animation Metadata
Create Animation Event
Generate Animation Report
```

Warnings:

```text
clip is 60fps and long
too many bones
translation keys on too many bones
large vertex animation
combat clip missing hitbox events
animation bank marked Always resident
```

---

## 4.8 PS1 Export Panel

Export options:

```text
Export Selected
Export Scene Assets
Export Chunk Assets
Export Collision Only
Export Metadata Only
Export Report Only
```

Outputs:

```text
mesh source/intermediate files
metadata json
texture metadata
collision metadata
animation metadata
validation report
IDE resource summary
```

Buttons:

```text
Export to PS1Godot
Export Selected to PS1Godot
Validate then Export
Open Export Folder
```

---

# 5. Asset ID and Metadata Strategy

## 5.1 Stable IDs

Every exported object should have stable logical IDs.

Examples:

```text
MeshId: town_crate_01
ChunkId: town_square_north
MaterialId: town_wood_dark
TexturePageId: tpage_town_world_01
CLUTId: town_day
CollisionId: blacksmith_shop_door_trigger
```

## 5.2 Blender custom properties

Use Blender custom properties to store PS1Godot metadata.

Example property names:

```text
ps1godot.asset_id
ps1godot.mesh_id
ps1godot.chunk_id
ps1godot.mesh_role
ps1godot.export_mode
ps1godot.material_id
ps1godot.texture_page_id
ps1godot.clut_id
ps1godot.alpha_mode
ps1godot.shading_mode
ps1godot.draw_phase
```

For materials:

```text
ps1godot.texture_format
ps1godot.palette_group
ps1godot.atlas_group
ps1godot.force_no_filter
ps1godot.approved_16bpp
```

For animations:

```text
ps1godot.anim_bank_id
ps1godot.skeleton_id
ps1godot.residency
ps1godot.disc_id
```

## 5.3 Round-trip rule

When importing from PS1Godot:

```text
preserve IDs
preserve metadata
do not generate new IDs unless missing
warn on duplicates
```

When exporting:

```text
write IDs back into metadata files
do not rename existing IDs silently
```

---

# 6. File and Folder Layout

Recommended PS1Godot project-side folder:

```text
res://ps1godot_assets/
  blender_sources/
  meshes/
  mesh_meta/
  textures/
  texture_meta/
  collision/
  animations/
  reports/
```

Example exported files:

```text
res://ps1godot_assets/meshes/town_crate_01.obj
res://ps1godot_assets/mesh_meta/town_crate_01.ps1meshmeta.json
res://ps1godot_assets/reports/town_crate_01_report.md
```

Alternative:

```text
res://assets/ps1/
  source/
  metadata/
  reports/
```

Keep this configurable.

---

# 7. Intermediate Metadata Format

## 7.1 Mesh metadata JSON

Example:

```json
{
  "MeshId": "town_crate_01",
  "SourcePath": "res://ps1godot_assets/meshes/town_crate_01.obj",
  "ChunkId": "town_square_north",
  "RegionId": "home_town",
  "DiscId": 1,
  "AreaArchiveId": "AREA_TOWN_NORTH",
  "MeshRole": "StaticWorld",
  "ExportMode": "MergeStatic",
  "VertexFormat": "S16",
  "UVFormat": "U8",
  "ColorFormat": "RGBA8",
  "IndexFormat": "Auto",
  "ShadingMode": "VertexColor",
  "AlphaMode": "Opaque",
  "DrawPhase": "OpaqueStatic",
  "MaterialId": "town_wood_dark",
  "TexturePageId": "tpage_town_world_01",
  "CLUTId": "town_day",
  "EstimatedBytes": 0
}
```

## 7.2 Collision metadata JSON

```json
{
  "CollisionId": "blacksmith_shop_door_trigger",
  "ChunkId": "town_square_north",
  "Type": "TransitionPoint",
  "Layer": "Trigger",
  "TargetChunkId": "blacksmith_shop",
  "SpawnPointId": "shop_entry"
}
```

## 7.3 Animation metadata JSON

```json
{
  "AnimationBankId": "town_npcs",
  "SkeletonId": "humanoid_basic",
  "CharacterMode": "SegmentedRigid",
  "Residency": "Chunk",
  "ChunkId": "town_square_north",
  "DiscId": 1,
  "Clips": [
    {
      "ClipId": "npc_idle",
      "FPS": 15,
      "Loop": true
    }
  ]
}
```

---

# 8. Binary Export Strategy

Initial Blender add-on should **not** be required to write final runtime binary mesh banks.

Recommended first phase:

```text
Blender exports source/intermediate assets + metadata.
PS1Godot performs final binary export.
```

Reasons:

```text
single source of truth for runtime format
easier schema/version migration
less duplication between Blender and Godot exporters
simpler debugging
```

Later optional feature:

```text
Blender can export binary mesh bank preview for testing
```

But PS1Godot should remain authoritative.

---

# 9. Import Strategy

## 9.1 Import PS1Godot manifest

The add-on should read:

```text
asset_manifest.json
ids.generated.json
mesh_debug_manifest.json
texture_page_manifest.json
```

This enables dropdowns for:

```text
ChunkId
MaterialId
TexturePageId
CLUTId
PaletteGroup
DiscId
AreaArchiveId
```

## 9.2 Import mesh/source assets

Supported imports:

```text
OBJ
glTF/GLB
FBX if needed
PS1Godot intermediate mesh source
mesh debug manifest
```

## 9.3 Import texture page preview

Show atlas/page texture in Blender materials for authoring.

---

# 10. Validation Rules

## 10.1 Mesh validation

Warn on:

```text
object has too many vertices for U8 indices
object has no UVs
object has UVs outside 0-255 page range
object uses too many materials
static object marked dynamic unnecessarily
mesh expects vertex colors but none exist
object has unapplied scale/rotation if exporter requires applied transforms
origin/pivot missing or suspicious
```

## 10.2 Material validation

Warn on:

```text
material missing TexturePageId
material missing CLUTId
16bpp texture not approved
large alpha texture
material alpha mode conflicts with draw phase
texture is not assigned to atlas group
```

## 10.3 Collision validation

Warn on:

```text
collision helper has render material
collision too detailed
transition lacks target chunk
save point lacks stable ID
camera blocker not assigned to CameraCollision
```

## 10.4 Animation validation

Warn on:

```text
too many bones
60fps long clip
translation keys on many bones
missing animation events for attack/combat clips
animation bank residency too broad
```

## 10.5 Project validation

Warn on:

```text
project root not set
manifest missing
duplicate IDs
output folder unwritable
unknown ChunkId
unknown TexturePageId
unknown CLUTId
```

---

# 11. Reports

Generate reports in markdown and/or JSON.

## 11.1 Scene report

```text
Object count
static objects
dynamic objects
collision helpers
vertex count
primitive estimate
texture pages
CLUTs
materials
vertex-color coverage
warnings
```

## 11.2 Object report

```text
MeshId
vertex count
triangle count
UV status
vertex color status
materials
estimated binary size
warnings
```

## 11.3 Texture report

```text
texture pages used
CLUTs used
materials per page
objects per page
alpha modes
16bpp warnings
```

## 11.4 Animation report

```text
clip count
FPS
duration
bone count
event count
estimated size
warnings
```

---

# 12. IDE Resource Export

Since the source will be added to resource files for the IDE to reference, the add-on should generate IDE-friendly summaries.

Output:

```text
ps1godot_blender_resource_summary.md
ps1godot_asset_manifest.json
ps1godot_export_report.md
```

These should include:

```text
asset IDs
mesh roles
chunk ownership
texture page assignments
collision IDs
animation IDs
warnings
exported files
```

Why:

```text
IDE agents can reason against the same IDs and metadata as PS1Godot.
```

Example resource summary section:

```text
## Meshes

- town_crate_01
  - Role: StaticWorld
  - Chunk: town_square_north
  - TexturePage: tpage_town_world_01
  - Shading: VertexColor
  - ExportMode: MergeStatic

- blacksmith_door
  - Role: DynamicRigid
  - Chunk: town_square_north
  - Collision: blacksmith_door_trigger
```

---

# 13. Blender Add-on Structure

Suggested Python package layout:

```text
ps1godot_blender/
  __init__.py
  addon_preferences.py
  properties.py
  panels/
    project_panel.py
    metadata_panel.py
    mesh_validation_panel.py
    material_panel.py
    vertex_lighting_panel.py
    collision_panel.py
    animation_panel.py
    export_panel.py
  operators/
    import_manifest.py
    export_assets.py
    validate_scene.py
    create_material.py
    create_collision_helper.py
    bake_vertex_lighting.py
    generate_report.py
  exporters/
    mesh_exporter.py
    material_exporter.py
    collision_exporter.py
    animation_exporter.py
    report_exporter.py
  importers/
    manifest_importer.py
    mesh_importer.py
    texture_page_importer.py
  validators/
    mesh_validator.py
    material_validator.py
    collision_validator.py
    animation_validator.py
    project_validator.py
  utils/
    ids.py
    paths.py
    math_conversion.py
    json_io.py
    logging.py
    blender_helpers.py
```

Keep modules small and easy for IDE agents to inspect.

---

# 14. Blender Data Model

## 14.1 Scene-level properties

```text
PS1GodotProjectRoot
PS1GodotOutputRoot
PS1GodotManifestPath
DefaultChunkId
DefaultDiscId
ExportProfile
```

## 14.2 Object-level properties

```text
AssetId
MeshId
ChunkId
RegionId
DiscId
AreaArchiveId
MeshRole
ExportMode
DrawPhase
ShadingMode
AlphaMode
CollisionLayer
```

## 14.3 Material-level properties

```text
MaterialId
TexturePageId
CLUTId
TextureFormat
PaletteGroup
AtlasGroup
AlphaMode
ForceNoFilter
Approved16bpp
```

## 14.4 Armature/action-level properties

```text
SkeletonId
AnimationBankId
CharacterMode
Residency
ChunkId
DiscId
```

---

# 15. Coordinate and Axis Conversion

The add-on should clearly define coordinate conversion between Blender, Godot, and PS1 runtime.

Document:

```text
Blender axes
Godot axes
PS1 runtime axes
scale factor
unit conversion
origin handling
pivot handling
Y/Z or handedness conversions
rotation conversion
```

Add validation:

```text
unapplied scale
non-uniform scale
negative scale/mirror
object far from origin
suspicious pivot
```

Recommended:

```text
Apply transforms or bake them explicitly during export.
Store chunk-local coordinates.
Warn before destructive transform application.
```

---

# 16. Vertex Color Workflow

Blender should be the easiest place to create baked PS1 lighting.

Features:

```text
create vertex color layer
paint vertex colors
bake simple directional light
apply ambient tint
apply radial fake light
apply height gradient
darken by normal
preview with PS1 material
```

Export:

```text
vertex color layer → PS1Godot metadata/export
```

Validation:

```text
object marked VertexColor/BakedLighting but no vertex color layer
vertex colors all white
vertex colors overbright
```

---

# 17. Texture and UV Workflow

Blender should help enforce:

```text
UVs inside assigned texture page
atlas padding awareness
4bpp/8bpp/16bpp metadata
CLUT assignment
no-filter material preview
alpha mode preview
```

Do not require Blender to perform final TIM conversion first.

But it can preview:

```text
estimated 4bpp palette
estimated 8bpp palette
dithered preview
alpha/cutout preview
```

---

# 18. Collision Helper Workflow

Collision helpers should be simple.

Operators:

```text
Create PS1 Player Collision Box
Create PS1 Camera Blocker
Create PS1 Trigger Volume
Create PS1 Interaction Volume
Create PS1 Transition Point
Create PS1 Save Point
```

Objects should be:

```text
named clearly
displayed as wireframe
excluded from render export
included in collision metadata export
```

---

# 19. Animation Workflow

Start simple.

Support:

```text
rigid transform animation metadata
segmented character part metadata
skeletal clip reporting
animation event markers
bank/residency metadata
```

Do not start by writing a full animation compiler in Blender.

Better flow:

```text
Blender authors action/animation
Blender exports metadata/source
PS1Godot compiles final animation bank
```

Animation event markers can be stored as:

```text
timeline markers
custom properties
JSON sidecar
```

Example events:

```text
footstep_left
footstep_right
attack_hit_start
attack_hit_end
play_sound
spawn_effect
```

---

# 20. Import / Export UX

## 20.1 Import UX

```text
File → Import → PS1Godot Asset / Manifest
```

Options:

```text
Import mesh source
Import material metadata
Import texture page preview
Import collision helpers
Import animation metadata
```

## 20.2 Export UX

```text
File → Export → PS1Godot Asset Package
```

Options:

```text
Export selected
Export visible
Export all PS1Godot-tagged
Validate before export
Generate report
Open output folder
```

## 20.3 Quick buttons

Panel buttons:

```text
Validate Scene
Export Selected to PS1Godot
Export Chunk to PS1Godot
Generate IDE Resource Summary
```

---

# 21. Versioning

Add metadata versioning.

```text
PS1GodotBlenderMetadataVersion
TargetPS1GodotVersion
TargetSplashpackVersion
```

Warn when:

```text
Blender add-on metadata version is newer than PS1Godot importer
PS1Godot manifest version unsupported
runtime does not support exported feature
```

---

# 22. Do Not Do Initially

Avoid first-pass overreach:

```text
direct final PS1 binary export as the only path
full TIM conversion inside Blender
full XA/CDDA handling
full disc image building
runtime deployment from Blender
complex skeletal optimizer
complex mesh compression
automatic destructive mesh conversion
automatic atlas packing that alters source art unexpectedly
```

These belong in PS1Godot or later phases.

---

# 23. Suggested Implementation Phases

## Phase 1 — Documentation and add-on skeleton

```text
create add-on package
project settings
basic panels
metadata properties
validate scene button
report generation
```

## Phase 2 — Metadata export

```text
object metadata
material metadata
collision metadata
simple JSON export
IDE resource summary
```

## Phase 3 — Mesh validation

```text
vertex count
triangle count
UV presence
vertex color presence
material count
texture page assignment
estimated index format
```

## Phase 4 — Vertex lighting tools

```text
create vertex color layer
ambient tint
directional bake
radial fake light
height gradient
```

## Phase 5 — Material/texture page workflow

```text
PS1 material helper
texture page/CLUT metadata
4bpp/8bpp preview notes
alpha mode validation
```

## Phase 6 — Collision helpers

```text
player collision boxes
camera blockers
interaction/trigger volumes
transition points
save points
```

## Phase 7 — Animation metadata

```text
animation bank IDs
clip reporting
event markers
segmented/skinned mode metadata
```

## Phase 8 — Import/round-trip

```text
read PS1Godot manifests
import metadata
preserve IDs
dropdowns for known IDs
```

## Phase 9 — Optional binary preview export

```text
experimental mesh bank export
only after PS1Godot format stabilizes
```

---

# 24. IDE-Agent Prompt

Use this prompt when adding the Blender plug-in source to the resource files and asking an IDE agent to implement it.

```text
You are helping me create a Blender add-on that integrates with PS1Godot / psxsplash.

Goal:
Build a Blender companion add-on for PS1Godot that supports easy asset import/export, PS1-friendly validation, metadata tagging, vertex lighting workflows, collision helpers, animation metadata, and IDE-readable reports.

Important:
PS1Godot remains the authoritative final exporter/runtime pipeline. The Blender add-on should initially export source/intermediate assets and metadata, not require direct final PS1 binary export.

Core features to scaffold:
1. Blender add-on package structure:
   - panels
   - operators
   - exporters
   - importers
   - validators
   - utils

2. Project settings:
   - PS1Godot project root
   - output folder
   - manifest path
   - default ChunkId
   - default DiscId

3. Object metadata:
   - AssetId
   - MeshId
   - ChunkId
   - RegionId
   - DiscId
   - AreaArchiveId
   - MeshRole
   - ExportMode
   - DrawPhase
   - ShadingMode
   - AlphaMode
   - CollisionLayer

4. Material metadata:
   - MaterialId
   - TexturePageId
   - CLUTId
   - TextureFormat
   - PaletteGroup
   - AtlasGroup
   - AlphaMode
   - ForceNoFilter
   - Approved16bpp

5. Validation:
   - vertex count
   - triangle count
   - UV presence
   - vertex color presence
   - material count
   - texture page assignment
   - duplicate IDs
   - missing IDs
   - unapplied transforms
   - collision helper issues

6. Export:
   - selected/all PS1Godot-tagged objects
   - metadata JSON sidecars
   - validation report
   - IDE resource summary markdown
   - preserve stable IDs

7. Vertex lighting tools:
   - create vertex color layer
   - ambient tint
   - simple directional bake
   - radial fake light
   - height gradient

8. Collision helpers:
   - player collision box
   - camera blocker
   - trigger volume
   - interaction volume
   - transition point
   - save point

9. Animation metadata:
   - AnimationBankId
   - SkeletonId
   - CharacterMode
   - Residency
   - ChunkId
   - DiscId
   - event markers

Rules:
- Keep the add-on modular and easy to inspect.
- Do not destructively modify source meshes unless explicitly requested.
- Do not make Blender the final disc/build/deploy tool.
- Do not require direct final binary mesh export in the first phase.
- Preserve existing IDs and warn on duplicates.
- Generate reports that IDE agents can read.
- Clearly separate implemented features from scaffolded future features.

Final response:
- Summary
- Files changed
- Implemented
- Scaffolded only
- How to test in Blender
- Risks/TODOs
```

---

# 25. Bottom Line

The Blender add-on should become the asset authoring bridge for PS1Godot.

Best first target:

```text
metadata tagging
validation
vertex color lighting tools
collision helpers
JSON sidecar export
IDE resource summaries
```

Do **not** start by making Blender responsible for everything.

The clean architecture is:

```text
Blender:
  author assets and metadata

PS1Godot:
  validate game/project state
  compile final runtime formats
  build/deploy/test

IDE:
  reference generated summaries and source files
```

This gives you an all-in-one pipeline without making every tool responsible for every stage.
