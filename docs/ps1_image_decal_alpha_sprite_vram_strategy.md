# PS1 Image, Decal, Alpha, Sprite, and VRAM Strategy

**Purpose:**  
This document defines a PS1-friendly image and texture strategy for the PS1Godot / psxsplash pipeline. It focuses on VRAM discipline, indexed textures, atlases, CLUT reuse, alpha limitations, decals, sprites, UI art, and validation/reporting.

This is intended to be useful as:

- a design reference
- a content pipeline checklist
- an exporter validation plan
- an IDE-agent implementation prompt
- a future splashpack metadata proposal

---

## 1. Core Principle

Treat **VRAM** as one of the main enemies.

Audio fights SPU RAM.  
Meshes, scripts, animation, and entity state fight main RAM.  
Images, textures, sprites, UI, decals, CLUTs, and framebuffers fight VRAM.

The texture pipeline should default to:

```text
small textures
indexed color
shared palettes
texture pages / atlases
controlled alpha
scene-specific residency
clear budget reporting
```

Avoid:

```text
one unique high-color texture per object
large alpha-heavy images
modern PNG-alpha assumptions
unbudgeted 16bpp gameplay textures
too many unique palettes
loose ungrouped textures
```

---

## 2. Core PS1 Texture Strategy

Use these rules by default:

- Prefer small indexed textures.
- Use **4bpp indexed textures** for most world, UI, decal, and simple prop art.
- Use **8bpp indexed textures** only for important assets that need more color detail.
- Avoid **16bpp textures** unless there is a very specific reason.
- Prefer texture atlases / texture pages over many loose textures.
- Reuse palettes / CLUTs wherever possible.
- Avoid unique high-color textures per object.
- Avoid modern PNG-alpha assumptions at runtime.
- Convert source art into a PS1-friendly build representation.

The source images can stay PNG/TGA/etc. in the Godot project, but exported runtime assets should be optimized.

---

## 3. Recommended Texture Format Policy

| Asset type | Recommended format |
|---|---|
| World textures | 4bpp indexed TIM where possible |
| UI / fonts / message boxes | 4bpp indexed TIM |
| Simple props | 4bpp indexed TIM |
| Character textures | 4bpp or 8bpp indexed TIM |
| Important portraits | 8bpp indexed TIM if needed |
| Cutscene stills | 8bpp indexed TIM by default |
| Decals | 4bpp indexed TIM with cutout/color-key style transparency |
| Particles | 4bpp or 8bpp depending on gradient needs |
| Full-screen stills | Budget carefully; load only when needed |
| 16bpp textures | One-off splash/title/cutscene only, explicitly approved |

### Rule of thumb

```text
Gameplay texture wants to be 4bpp or 8bpp.
16bpp gameplay texture should produce a warning unless explicitly approved.
```

---

## 4. Images and Full-Screen Stills

Full-screen and large images can be expensive in VRAM and disc space.

For each full-screen or large image, track:

```text
image name
resolution
bit depth
palette / CLUT count
approximate VRAM size
resident or temporary
load timing
unload timing
scene/chunk ownership
```

Recommended behavior:

- Prefer smaller images with dithered indexed palettes.
- Use 8bpp for important stills if 4bpp is too limited.
- Use 16bpp only for explicit title/cutscene/splash use.
- Load large stills only for that screen or cutscene.
- Evict them immediately after use.
- Do not keep title/splash/cutscene images resident during gameplay unless required.

### Budget table example

```text
Image: title_splash
Resolution: 320x240
Format: 8bpp indexed
CLUTs: 1
Resident: Title scene only
Evict: before loading gameplay scene
Warning: full-screen image, verify VRAM budget
```

---

## 5. Sprites

Use sprite sheets / atlases.

Pack related sprites together:

```text
UI icons
dialogue portraits
pickups
particles
simple enemies/effects
button prompts
status icons
```

Recommended rules:

- Prefer 4bpp indexed sprites.
- Use 8bpp only when palette limits are too visible.
- Keep animation frame counts low.
- Reuse frames through flipping, timing, offsets, or palette swaps.
- Avoid storing many near-identical animation frames.
- Keep sprite sheets grouped by use and scene.

### Good sprite reuse

```text
one sparkle sheet
palette swaps for magic color
scale/lifetime variation in script
short animation frame sequence
```

### Bad sprite use

```text
unique full-color image for every pickup
huge semi-transparent smoke sheet
long animation sheet with near-duplicate frames
```

---

## 6. Decals

Treat decals as small textured quads or baked texture details, not modern projected decals.

Use simple textured quads for:

- stains
- signs
- posters
- cracks
- oil marks
- warning labels
- fake shadows
- grime
- small environmental storytelling details

### Preferred approach

Bake decals into world texture atlases where possible.

Use separate decal quads only when:

- the decal is reused
- layering is valuable
- the decal needs to appear/disappear
- the decal is shared across multiple surfaces
- the decal is gameplay-relevant

### Avoid

- many overlapping alpha decals in one area
- large transparent quads covering much of the screen
- unique high-color decals per prop
- modern deferred/projected decal assumptions

---

## 7. Alpha and Transparency

Be cautious with transparency.

Prefer:

```text
Opaque
Cutout
Color-key transparency
Dithered fake alpha
Small controlled semi-transparent effects
```

Use semi-transparency sparingly for:

- smoke
- ghosts
- glass
- energy effects
- water overlays
- UI fades
- magic effects

Avoid:

- stacking many transparent surfaces
- huge alpha planes close to the camera
- alpha-heavy particle clouds
- large smooth PNG-style alpha masks
- designs that depend on perfect modern alpha sorting

For PS1-style visuals, hard-edged cutout transparency is often more authentic and easier to budget.

---

## 8. Alpha Sorting Rules

Transparent objects may need manual draw ordering.

Prefer simple draw ordering:

```text
1. opaque world
2. opaque dynamic props
3. characters
4. cutout decals / fake shadows
5. semi-transparent effects
6. UI
```

Avoid designs that require perfect sorting between many overlapping transparent objects.

If sorting artifacts appear, solve with:

1. art/layout changes
2. smaller transparent quads
3. cutout instead of semi-transparent alpha
4. controlled draw phase
5. manual ordering metadata

Only add complex renderer logic after cheaper content fixes fail.

---

## 9. Fake Shadows

Use fake blob shadows instead of dynamic shadows.

Recommended:

- small dark cutout or dithered texture
- simple quad under character
- 4bpp indexed texture
- optional scale change based on height
- controlled draw order after opaque world

Avoid:

- expensive dynamic shadow assumptions
- large smooth transparent shadow textures
- many overlapping semi-transparent shadow planes

Example:

```text
CharacterShadow:
  texture = fx_shadow_blob_4bpp
  alpha mode = cutout or dithered semi-transparent
  size = small
  draw phase = fake shadow
```

---

## 10. Particles

Particles should be tiny, short-lived, and atlas-based.

Good PS1-friendly particle examples:

- sparks
- small smoke puffs
- dust
- simple magic pixels
- impact stars
- tiny debris sprites
- short glints
- low-count embers

Rules:

- Use tiny atlas-based sprites.
- Keep particle counts low.
- Use color, palette, scale, and lifetime variation instead of many unique textures.
- Prefer short-lived effects.
- Avoid lots of semi-transparent overlapping particles.
- Prefer cutout/dithered particles where possible.

### Particle budget example

```text
Small interior: 8-16 active particles
Town chunk: 16-32 active particles
Battle arena: 32-64 active particles, tightly controlled
Boss/cutscene: special budget only
```

---

## 11. UI and Dialogue Images

Fonts, UI panels, and dialogue art can quietly eat VRAM.

Recommended:

- Pack fonts into a small atlas.
- Use 4bpp indexed UI textures.
- Build panels from tiled pieces or 9-slice-like pieces.
- Avoid unique large UI panels.
- Budget dialogue portraits separately.
- Reuse portrait palettes where possible.
- Load only the needed portrait set per scene/chunk if possible.
- Keep message boxes made from tiny reusable pieces.

### UI atlas groups

```text
tpage_ui_font
tpage_ui_common
tpage_ui_icons
tpage_ui_dialogue
```

### Dialogue portrait rules

- Use 4bpp if the art style supports it.
- Use 8bpp when facial detail needs it.
- Avoid 16bpp unless explicitly approved.
- Group portraits by scene/chapter/chunk if possible.
- Do not keep all portraits resident for the whole game.

---

## 12. Texture Atlas Rules

Group textures by scene and use.

Suggested atlas/page names:

```text
tpage_world_01
tpage_ui_01
tpage_character_01
tpage_fx_01
tpage_decals_01
tpage_cutscene_01
```

Rules:

- Avoid one texture page per object.
- Try to keep objects that render together on the same page.
- Keep texture coordinates PS1-friendly.
- Add padding around atlas islands to avoid bleeding.
- Verify filtering settings do not blur atlas edges.
- Prefer consistent texel density.
- Group by chunk/area where possible.

### Good

```text
Town district:
  tpage_town_world_01
  tpage_town_props_01
  tpage_common_ui
  tpage_common_fx
```

### Bad

```text
crate.png
barrel.png
door.png
shop_sign.png
lamp.png
bench.png
...
each as separate runtime texture pages
```

---

## 13. Palette / CLUT Strategy

CLUT reuse is a major tool.

Prefer shared palettes for related assets.

Use palette swaps for cheap variation:

- enemy variants
- item variants
- UI states
- different lighting moods
- regional themes
- time-of-day shifts
- poison/fire/ice variants

Document palette ownership:

```text
which texture uses which CLUT
whether CLUT is shared
whether CLUT must stay resident
whether CLUT can be swapped
whether CLUT belongs to a scene/chunk
```

Avoid creating dozens of tiny textures each with unique palettes unless the content truly needs it.

### Palette group metadata

```text
PaletteGroup:
  town_day
  town_night
  forest_green
  cave_cold
  common_ui
  common_fx
```

---

## 14. Import and Build Pipeline Reporting

Add image asset validation/reporting.

Per asset, report:

```text
asset name
source file
output texture format
width
height
bpp
palette / CLUT count
estimated VRAM footprint
atlas / page assignment
alpha mode
residency
scene/chunk ownership
warnings
```

Example:

```text
Texture: shop_sign_large
Source: res://art/town/shop_sign.png
Output: 8bpp indexed
Size: 128x64
CLUTs: 1
AtlasGroup: World
AlphaMode: Opaque
Residency: Scene
VRAM Estimate: 8 KB + CLUT
Warnings: none
```

---

## 15. Validation Warnings

Warn on:

- large 16bpp textures
- large alpha textures
- non-power-of-two or awkward sizes if the pipeline dislikes them
- too many unique palettes
- unassigned atlas/page
- UI/decals accidentally imported as high-color textures
- oversized sprites
- full-screen images marked always-resident
- one-off textures for tiny props
- excessive alpha in gameplay chunks
- unbudgeted cutscene stills
- missing CLUT group
- missing texture format policy
- texture larger than 256x256 where the pipeline cannot safely handle it

### Example warnings

```text
Texture "portrait_mara_full" is 16bpp and marked Gameplay resident.
Recommended: convert to 8bpp indexed or load only in dialogue scenes.
```

```text
Texture "smoke_cloud_big" has large smooth alpha.
Recommended: use smaller dithered 4bpp/8bpp particle sprites.
```

```text
Object "crate_07" uses a unique texture page.
Recommended: move crate texture into tpage_world_01.
```

```text
Decal "oil_stain_large" covers a large screen area with semi-transparency.
Recommended: bake into floor texture or use smaller cutout decal.
```

---

## 16. VRAM Budget Summary

If possible, produce a VRAM budget summary per scene/chunk.

Example:

```text
Chunk: town_square_north

Framebuffers / system reserve: 320 KB
World texture pages:          192 KB
Character textures:            64 KB
UI textures:                   32 KB
FX / particles:                24 KB
Decals:                        16 KB
CLUTs:                          4 KB
Sky / backdrop:                32 KB

Estimated VRAM total:         684 KB / 1024 KB
Warnings: 2
```

This should appear in the editor dock and export report.

---

## 17. Alpha Mode Metadata

Add or prepare alpha mode metadata per image/material.

Suggested enum:

```text
Opaque
Cutout
SemiTransparent
Additive
Subtractive
UI
```

### Meaning

#### Opaque

Default. Fastest and easiest to order.

#### Cutout

Binary transparency / color-key style. Preferred for sprites, decals, foliage, UI pieces.

#### SemiTransparent

Use sparingly. Needs careful sorting and budget.

#### Additive

Useful for magic/glow effects, but budgeted.

#### Subtractive

Optional if supported/needed.

#### UI

Special UI draw phase. Drawn last and budgeted separately.

---

## 18. Future Image Metadata

For future splashpack/image metadata, consider:

```text
TextureFormat: 4bpp | 8bpp | 16bpp | Auto
AlphaMode: Opaque | Cutout | SemiTransparent | Additive | UI
AtlasGroup: World | UI | Character | FX | Decal | Cutscene
Residency: Always | Scene | OnDemand
PaletteGroup: optional shared CLUT group
AllowPaletteSwap: true/false
AllowDither: true/false
ForceNoFilter: true/false
Approved16bpp: true/false
ChunkId: optional owner
DiscId: optional owner for multi-disc builds
```

---

## 19. Suggested Auto Rules for Textures

Use deterministic auto rules.

```text
Small world/detail texture -> 4bpp
UI/font/icon -> 4bpp
Decal -> 4bpp cutout unless semi-transparency is explicitly needed
Particle -> 4bpp or 8bpp depending on gradient needs
Character skin/clothing -> 4bpp first, 8bpp if quality is too poor
Portrait/cutscene still -> 8bpp by default
16bpp -> only if explicitly approved
Large alpha texture -> warning
16bpp gameplay texture -> warning unless explicitly approved
```

Do not silently make destructive conversions.

---

## 20. Implementation Rules

Follow these rules:

- Do not convert everything blindly.
- Do not destroy source art.
- Prefer generating optimized build outputs while preserving original source images.
- Keep visual tests easy to compare.
- If an image conversion changes the look too much, report it instead of silently accepting it.
- Do not let image/texture work break the current jam/demo build.
- Add docs and validation before full automatic conversion.
- Make warnings plain-English and actionable.
- Keep fallback paths safe.
- Clearly mark scaffolded metadata versus implemented runtime support.

---

## 21. Diagnostic Checks

Add checks for:

```text
oversized textures/images
unnecessary 16bpp textures
alpha-heavy assets
loose textures that should be atlased
decals that should be baked
UI/message-box textures that can be 4bpp
too many unique CLUTs
large full-screen images marked resident
texture pages with only one tiny object
particle sheets with too many near-duplicate frames
```

---

## 22. Deliverables

### Documentation deliverables

1. Texture/image/alpha strategy added to roadmap/design docs.
2. Explanation of 4bpp/8bpp/16bpp policy.
3. Explanation of alpha modes and sorting limitations.
4. Explanation of atlas groups and CLUT reuse.
5. Explanation of VRAM budget reporting.

### Metadata deliverables

Optional splashpack metadata proposal for:

```text
TextureFormat
AlphaMode
AtlasGroup
Residency
PaletteGroup
AllowPaletteSwap
AllowDither
ForceNoFilter
Approved16bpp
ChunkId
DiscId
```

### Validation/reporting deliverables

1. Validation/reporting scaffold for image assets if safe.
2. List of risky current assets:
   - oversized textures
   - unnecessary 16bpp textures
   - alpha-heavy decals/sprites
   - loose textures that should be atlased
3. Recommended fixes that preserve the current build.
4. Per-scene or per-chunk VRAM budget summary if feasible.

---

## 23. IDE-Agent Prompt

Use this prompt when asking an IDE agent to implement the first safe slice.

```text
You are helping me improve the PS1Godot / psxsplash image, texture, decal, alpha, sprite, and VRAM pipeline.

Goal:
Add documentation, metadata, and safe validation scaffolding for PS1-friendly image handling. Do not break the current jam/demo build and do not destructively convert source art.

Main strategy:
- Prefer 4bpp indexed textures for most world/UI/decal/simple prop art.
- Use 8bpp indexed textures for important assets that need more color detail.
- Avoid 16bpp unless explicitly approved for title/cutscene/splash/special assets.
- Prefer texture atlases / texture pages over many loose textures.
- Reuse palettes/CLUTs where possible.
- Prefer cutout/color-key transparency over heavy semi-transparent alpha.
- Budget particles, decals, fake shadows, portraits, and UI separately.
- Preserve original source images; generate optimized build outputs.

Implement or scaffold:
1. Texture metadata:
   - TextureFormat: 4bpp / 8bpp / 16bpp / Auto
   - AlphaMode: Opaque / Cutout / SemiTransparent / Additive / UI
   - AtlasGroup: World / UI / Character / FX / Decal / Cutscene
   - Residency: Always / Scene / OnDemand
   - PaletteGroup
   - AllowPaletteSwap
   - AllowDither
   - ForceNoFilter
   - Approved16bpp

2. Validation/reporting:
   - asset name
   - source file
   - output format
   - width/height
   - bpp
   - CLUT count
   - estimated VRAM footprint
   - atlas/page assignment
   - alpha mode
   - residency
   - warnings

3. Warnings:
   - large 16bpp textures
   - large alpha textures
   - oversized sprites
   - unassigned atlas/page
   - too many unique palettes
   - UI/decals imported as high-color textures
   - full-screen images marked always-resident
   - one-off textures for tiny props

4. Documentation:
   - add a PS1 image/texture/alpha strategy doc
   - explain auto rules
   - explain alpha sorting limitations
   - explain atlas/CLUT strategy
   - explain VRAM budget summary

Rules:
- Do not convert everything blindly.
- Do not destroy source art.
- Do not silently accept ugly conversion results.
- Prefer warnings and reports before automatic changes.
- Keep current builds working.
- Clearly separate implemented behavior from scaffolded metadata.

Final response:
- Summary
- Files changed
- What is implemented
- What is scaffolded only
- How to test
- Risks/TODOs
```

---

## 24. Bottom Line

The image pipeline should make PS1-friendly choices the default:

```text
4bpp/8bpp indexed textures
shared CLUTs
atlas groups
cutout alpha
small particles
budgeted portraits
scene/chunk residency
VRAM reporting
plain-English warnings
```

Do not rely on modern texture habits.

A PS1-style RPG can look rich, but only if the content pipeline is strict about what stays resident, what gets indexed, what shares palettes, and what gets drawn with alpha.
