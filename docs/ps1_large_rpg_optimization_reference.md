# PS1 Optimization Reference for Larger RPG-Style Projects

A compact, IDE-friendly reference for designing and optimizing a larger original-PlayStation-style RPG project.

This file is written for use with coding assistants such as Claude Code. It focuses on practical constraints, design implications, and optimization strategies rather than hobbyist trivia.

---

## Purpose

Use this document as a reference when:
- planning engine architecture for a PS1-style RPG
- designing streaming and loading systems
- budgeting VRAM, RAM, textures, animation, and audio
- choosing scene, level, and asset strategies that fit PS1-era constraints
- guiding renderer, content pipeline, and gameplay-system decisions

This is **not** a strict emulator or SDK manual. It is a practical synthesis of official-era and respected technical references.

---

## Core Hardware Constraints That Matter Most

## Main memory
The original PlayStation has very limited main RAM, which forces aggressive reuse of world data, animation data, scripts, and decompressed assets. Large RPGs cannot behave like modern open worlds; they must be built from compact room/zone chunks, small runtime working sets, and tightly controlled state.

## VRAM
The PlayStation has about **1 MB of VRAM** for framebuffers, textures, palettes, and other GPU-visible data. In practice, this is one of the most important constraints for an RPG because UI, character textures, environment textures, battle effects, and framebuffers all compete for the same space.

Implication:
- You cannot treat textures as abundant.
- You must think in terms of a **resident texture set** per area.
- Swapping too much visual data between scenes or sub-areas is expensive and disruptive.

## Texture cache
The GPU uses a very small **2 KB texture cache**. This strongly favors:
- repeated use of the same small texture regions
- clustering polygons that reuse the same texture page
- minimizing texture page and CLUT changes
- atlased materials and modular asset reuse

Practical cache-friendly sizes mentioned in available references:
- 4-bit textures: around **64x64**
- 8-bit textures: around **32x64**
- 16-bit textures: around **32x32**

This does **not** mean every texture must be exactly those sizes, but it does mean your renderer and content should be organized to benefit from reuse within those bounds.

## Maximum primitive texture dimensions
A single textured primitive effectively tops out at **256x256** texture resolution. Larger texture images must be split across multiple primitives.

Implication:
- build your world from smaller repeatable textures and material chunks
- do not rely on huge unique surfaces
- keep UI and portraits carefully managed if they need larger assets

## Sound RAM
The SPU has limited sound RAM, so large RPGs must stream or rotate audio intelligently:
- small looping ambient beds
- short compressed voice snippets
- reused effects libraries
- compact instrument/sample sets

## CD-ROM access
Disc access is a major bottleneck. Seek behavior matters a lot. Many small files are inefficient. Bigger grouped reads are better than scattered tiny reads.

Implication:
- streaming design is central to large-project success
- area layout and disc layout are optimization topics, not just packaging topics

---

## The Big Lessons for a Large RPG

If you want something closer in spirit to a larger town-and-field RPG rather than a tiny corridor game, the winning strategies are:

1. **Small working sets**
2. **Heavy asset reuse**
3. **Disc-aware streaming**
4. **VRAM-aware art direction**
5. **Simple simulation outside the active area**
6. **Geometry and texture stability within a scene**
7. **Chunk-based world structure**

The PS1 can present large-feeling games, but only when the game is built around these limits from the start.

---

## What “Large RPG” Should Mean on PS1

A practical PS1-scale “large RPG” should usually mean:
- a world composed of many connected chunks, not one giant seamless map
- controlled transitions between hubs, towns, interiors, and field segments
- small active NPC populations per area
- conservative combat effect counts
- reused architecture kits and texture atlases
- streaming of music, voice, and area data in predictable blocks
- low-cost far-background presentation (sky cards, painted backgrounds, fogging, distant silhouettes)

Think:
- connected illusion of scale
- not persistent full-world simulation
- not many unique high-detail assets visible at once
- not modern open-world density

---

## Rendering Strategy

## Use ordering tables carefully
Standard PS1 rendering flow relies on **ordering tables (OTs)** stored in main memory. Sorting cost rises with object count, especially when scenes are full of props, particles, NPCs, and alpha-like tricks.

Practical advice:
- reduce the number of independently sorted items
- batch by material/texture page where possible
- merge static scene pieces into larger submit units when safe
- limit tiny decorative objects unless they are extremely cheap
- treat particles and billboards as budgeted resources, not free polish

For an RPG:
- static environment should dominate the scene
- dynamic props and effects should be limited and intentional
- towns should be scenic, not densely “interactive-looking” everywhere

## Double buffering is standard
Typical PS1 programs use:
- two ordering tables
- two framebuffers / image buffers

This allows command construction, transfer, drawing, and display to overlap better.

Implication:
- framebuffer strategy is not optional; it affects VRAM planning from day one
- UI and effects should be designed with framebuffer costs in mind

## Texture page discipline
Switching texture pages or CLUTs too frequently hurts throughput.

Recommendations:
- group environment pieces by shared texture pages
- keep characters for one area using a consistent palette strategy
- avoid constantly mixing unrelated texture banks in one frame
- atlas repeated props together
- build “area material sets” rather than authoring unique materials freely

## Prefer stable, modular environment kits
For larger RPGs, modular kits are not just a content convenience. They are a performance strategy.

Good PS1-style kit design:
- walls, trims, doors, ground tiles, windows, stairs, props
- one or two texture pages driving most of a town block
- vertex color variation to fake uniqueness
- decals or tiny alternate meshes instead of large new textures

## Hide distance aggressively
Distance fog, darkness, walls, terrain curves, narrow streets, and doors are useful tools.

They reduce:
- visible geometry
- texture variety on screen
- sorting load
- animation load
- AI update count

For a PS1 large RPG, “beautifully limited visibility” is often better than broad, expensive vistas.

---

## Texture Strategy

## Favor 4-bit and 8-bit indexed textures
Indexed textures with CLUTs are often the most practical choice for PS1-style projects.

Benefits:
- lower memory usage
- more assets fitting in VRAM
- better reuse
- stronger cohesion in art direction

Use 16-bit textures only when they truly justify the cost.

## Build area texture sets
For each area or sub-area, define:
- environment atlas/pages
- character atlas/pages
- UI overlays needed in that context
- effect textures allowed in that context

Ask:
- what must stay resident?
- what can stream on transition?
- what should never coexist?

## Reuse texture regions aggressively
Do not think “one prop, one unique texture.”
Instead think:
- many props from one page
- mirrored UVs
- shared trim sheets
- shared wood/stone/metal patterns
- palette swaps for variety

## Keep texel density consistent
Huge swings in texel density make scenes look messy and waste memory.
A consistent low-resolution texture language helps both performance and aesthetics.

---

## World Structure and Streaming

## Build the world as chunks
For a PS1-scale RPG, use chunks such as:
- town district
- interior building
- field segment
- cave room cluster
- dungeon block
- battle arena variant

Each chunk should have its own:
- geometry set
- resident texture pages
- NPC set
- script set
- audio profile
- effect budget

## Combine data into larger streaming blocks
Historic training material strongly recommends combining data into larger reads rather than many small files.

Practical packaging pattern:
- one packed area archive per chunk or zone
- grouped textures, geometry, scripts, nav data, encounter tables, and audio cues
- optional secondary archive for nearby transition-linked assets

Avoid:
- many tiny disc files for every object or NPC
- frequent file lookups during gameplay
- scattered disc placement

## Avoid excessive CD pauses and seeks
Streaming systems should:
- avoid pausing/stopping the disc repeatedly
- avoid many tiny file reads
- minimize directory lookups and scattered file placement
- prefer predictable sequential access

For design:
- transitions should be intentional and buffered
- neighboring areas should share some data where possible
- cutscene and event assets should be packed with nearby gameplay data when practical

## Disc layout matters
Logical file organization is not enough; physical disc layout also matters.
Related assets should be placed near each other to reduce seek cost.

For a larger RPG:
- organize by area progression and adjacency
- duplicate especially important hot assets where justified
- avoid cross-disc-layout thrashing from systems that constantly pull unrelated resources

---

## Scene Budgeting

A strong PS1 RPG pipeline should define budgets per scene type.

Example categories:
- exploration outdoor
- town square
- interior
- dungeon corridor
- combat scene
- menu / status / map
- cutscene close-up

Each category should budget:
- total visible world triangles / quads
- dynamic characters
- animated props
- particles / billboards
- texture pages
- UI overlays
- audio voices / streams

Even rough budgets are better than none.

---

## NPC and AI Strategy

For larger RPGs, simulation should be tiered.

## High-detail simulation only near the player
Use full update cost only for:
- nearby NPCs
- currently visible enemies
- important interactables
- scripted sequence actors

## Cheap simulation elsewhere
For off-screen or distant actors:
- update at lower frequency
- simplify pathing
- use schedule states rather than live behaviors
- suspend animation or use sparse key updates
- swap to symbolic simulation when not visible

A PS1-scale RPG should feel alive through clever scripting and selective detail, not brute-force AI everywhere.

---

## Animation Strategy

Animation memory can become a silent budget killer.

Recommendations:
- reuse skeletons aggressively
- use smaller animation sets with shared locomotion
- keep enemy families structurally related where possible
- split expressive moments into event-specific assets rather than universal always-loaded animation libraries
- only keep active-area animation data resident

For towns:
- favor idle loops and simple gesture sets
- use camera framing and timing to add personality instead of large animation libraries

---

## Combat and Effects

Battle or action scenes can destroy performance and VRAM discipline if they are treated like a separate unrestricted game.

Rules:
- cap concurrent effects
- reuse effect textures
- design spells around layered simple primitives rather than unique huge assets
- carefully budget transparency-like effects and overdraw-heavy visuals
- keep enemy counts modest when effects intensity rises

For a PS1-inspired project, “memorable effect timing” usually matters more than raw effect density.

---

## UI and Menus

Menus compete for VRAM and fill rate too.

Recommendations:
- keep UI atlases compact
- reuse font pages
- load special full-screen illustrations only when needed
- separate heavy menu contexts from gameplay rendering where possible
- avoid large always-resident UI art sets during exploration

For RPGs especially:
- inventory, shops, equipment, and dialog should share common UI assets
- portraits and icons should follow strict atlas planning

---

## Audio Strategy

Audio contributes heavily to perceived scale, but it must be handled economically.

Guidance:
- use short looping ambience
- stream music intelligently
- keep voice clips short and event-based
- reuse sound libraries across enemies and environments
- maintain area-specific audio packages rather than a giant global audio soup

Design-wise:
- one strong ambient loop plus a few event cues often does more than many simultaneous sounds

---

## Save/Data/System Design

A larger RPG needs careful state management.

Recommendations:
- save compact symbolic state, not whole runtime object graphs
- represent world progress with flags, inventories, quest states, and local chunk states
- rebuild runtime scene state from chunk definitions plus compact save deltas
- avoid making persistence architecture more expensive than rendering architecture

This matters because larger RPG structure often creates pressure to store too much.

---

## Art Direction Choices That Help Performance

A PS1-style larger RPG benefits from art direction that embraces the machine:

- limited but cohesive palettes
- modular architecture
- repeated materials
- visible fog, darkness, or atmospheric haze
- stylized distant scenery
- strong silhouettes over fine texture detail
- vertex color variation and lighting tricks
- painted skyboxes/backdrops
- intentional “soft mystery” in the distance

This makes the world feel bigger while lowering the real cost.

---

## A Practical Content Pipeline for a PS1-Style RPG

A sane production pipeline would look something like this:

1. Define scene types and budgets
2. Define resident texture sets by area
3. Build modular environment kits
4. Package area data into chunk archives
5. Sort assets by likely co-residency
6. Author NPCs and animation families for reuse
7. Restrict effect systems to a clear per-scene budget
8. Validate each area for:
   - VRAM fit
   - RAM fit
   - texture-page churn
   - object count
   - OT pressure
   - stream size
   - load time behavior

---

## Checklist for Claude Code / IDE Use

When using Claude Code or another coding assistant, ask it to evaluate designs against the following:

### Renderer / scene questions
- How many independently submitted objects are visible?
- How often do texture page changes occur?
- Which objects could be merged or grouped?
- Is the scene reusing a small resident texture set?
- Is the ordering-table load reasonable for this scene type?

### Asset questions
- Which textures are unique and can they be replaced with atlas reuse?
- Which props could share UV regions or palettes?
- Are there too many one-off meshes in the area?
- Can far scenery be replaced by billboards, sky cards, or simplified geometry?

### Streaming questions
- Is data packaged into large area archives?
- Does entering an area cause many tiny reads?
- Are adjacent areas disc-local and stream-friendly?
- Can frequently used data be duplicated or reorganized to reduce seeking?

### NPC/system questions
- Which actors truly need full simulation?
- Which can be downgraded when distant or hidden?
- Can schedules or symbolic states replace live updates?
- Can animation sets be shared more aggressively?

### UI/audio questions
- Which UI assets must be resident during gameplay?
- Can menu-only assets load on demand?
- Are audio sets grouped by area and event?
- Is music/voice streaming designed around predictable access?

---

## Suggested Design Rules for a Fable-ish PS1 Project

If the target vibe is “larger whimsical action-RPG,” use rules like these:

- one town district or field chunk loaded at a time
- heavily reused architecture kits
- one compact environment texture set per area
- one compact character texture set per area
- strict cap on simultaneously visible NPCs
- distance fog and composition used to limit visibility
- separate battle/event effect budgets
- chunk archives designed for sequential streaming
- small, expressive world spaces connected to create the illusion of scale

That is much more realistic than attempting a seamless giant world.

---

## Common Mistakes

Avoid these mistakes:

- treating PS1 like a tiny modern 3D platform
- overusing unique textures
- ignoring disc seek behavior
- authoring too many tiny files
- assuming large vistas are worth the cost
- building too many dynamic props and NPCs into every scene
- overspending VRAM on menus and portraits
- making every combat effect unique
- simulating too much outside the active player space

---

## Bottom Line

For a larger PS1-style RPG, the real optimization pillars are:

- **VRAM discipline**
- **texture-page discipline**
- **ordering-table/object-count discipline**
- **chunk-based world structure**
- **disc-aware streaming**
- **aggressive reuse of textures, meshes, animation, and audio**
- **art direction that hides limits instead of fighting them**

The illusion of a large world is achievable.
A truly modern-style large world is not.

Design for:
- compact resident sets
- strong reuse
- predictable transitions
- careful streaming
- controlled scene density

That is the path to a convincing and performant PS1-style RPG.

---

## Integration with PS1Godot

How this reference maps onto the current project. Each concern below points at the
tool that covers it today, the gap that remains, and the roadmap item that will
close it. Use this section as the scoreboard when reviewing designs against the
reference above.

### Already in place

- **Per-texture VRAM verdict** —
  `godot-ps1/addons/ps1godot/tools/PS1TextureAnalyzer.cs` classifies every project
  image as 4bpp / 8bpp / 16bpp-direct / too-big and estimates VRAM cost. Covers
  the "Favor 4-bit and 8-bit indexed textures" and "Maximum primitive texture
  dimensions" concerns.
- **Grouped streaming blocks on disc** — splashpack v20 is already split into
  `scene.splashpack` + `.vram` + `.spu`, letting the runtime DMA each blob into
  the right memory region without parsing. Matches "combine data into larger
  streaming blocks."
- **Hide-distance primitives** — `PS1Scene.FogEnabled / FogColor / FogDensity`,
  plus the shader's vertex jitter and affine warp, give authors the "beautifully
  limited visibility" lever out of the box.
- **Scene subdivision aid** — `PS1MeshSubdivider` splits triangles ×4 on
  demand, letting authors trade off affine-warp budget per mesh rather than
  globally.
- **Two broad scene categories** — `PS1Scene.SceneTypeKind = Exterior | Interior`
  routes culling strategy (BVH vs room/portal).

### Gaps the reference exposes

Numbered so roadmap items can cite them (`REF-GAP-3`).

1. **No per-scene / per-area resident VRAM view.** `PS1TextureAnalyzer` scans
   the whole project. RPG work needs "what's resident in *this* chunk?" with
   environment / character / UI / effects broken out. Closes with the VRAM
   viewer dock (Phase 3) framed around area residency, not project totals.

2. **No texture-page / CLUT grouping visualization.** The reference's
   "texture-page discipline" and "cluster polygons that reuse the same texture
   page" advice is invisible to authors today. Need a view that shows which
   meshes share pages, which materials are one-offs, and estimated page
   switches per frame. Data already available in `SceneCollector` +
   `VRAMPacker`.

3. **No object-count / OT-pressure readout.** The reference flags ordering-table
   cost as rising with independently sorted items; `SceneCollector` knows every
   submitted object but doesn't surface the count per scene. Small editor
   overlay, big authoring payoff.

4. **Scene-type enum is 2-way and unbudgeted.** `SceneTypeKind.Exterior |
   Interior` doesn't match the reference's scene-type categories (exploration /
   town / interior / dungeon / combat / menu / cutscene close-up) and carries
   no budget fields. Needs an expanded enum plus authored caps for target_tris,
   max_actors, max_effects, max_texture_pages.

5. **No chunk / area primitive in the editor.** The reference leans hard on
   chunk-based worlds. Phase 2.5 sketches `Scene.LoadChunk` in the runtime API
   but there's no authoring concept — a `PS1Chunk` node that owns a resident
   texture set, NPC set, script set, and audio profile.

6. **No texture reuse auditor.** The reference repeatedly says "many props from
   one page" and "mirrored UVs" but nothing detects textures used exactly once,
   near-duplicate textures that could share a CLUT, or meshes that each drag
   in a unique atlas.

7. **No animation / skeleton residency tracking.** Phase 2 bullets 10–11 will
   add animations and skinned meshes; the reference warns animation memory is
   a silent budget killer. Need a residency flag and per-area animation budget
   from day one of the port.

8. **No UI residency distinction.** The reference says "avoid large
   always-resident UI art sets during exploration" and "separate heavy menu
   contexts from gameplay rendering where possible." UI canvases (Phase 2
   bullet 8) must be born with a residency property
   (`Gameplay | MenuOnly | LoadOnDemand`), not retrofit after shipping.

9. **No audio residency / per-area SPU budget.** `PS1AudioClip` assets pack
   into `.spu` but there's no per-area SPU-RAM accounting and no authored
   distinction between "ambient loop resident across chunk" vs "event cue
   streamed in on trigger." Relevant when Phase 2.5 audio-from-Lua lands.

10. **Disc layout is undefined.** The reference's "disc layout matters"
    section has no analogue in the writer — we emit one splashpack per scene
    with no adjacency / seek-cost awareness. Relevant when Phase 2.5 chunk
    streaming and Phase 3 ISO build (`mkpsxiso`) land; flag for that point,
    not now.

### How this shapes Phase 2 bullet 8 (UI canvases + fonts)

UI is the first system where VRAM discipline becomes adversarial — fonts, HUD
atlas, menu art, portraits all compete for the same 1 MB with gameplay
textures. Two schema decisions now avoid painful retrofits later:

- **Residency property on UI canvas / font assets**:
  `Gameplay | MenuOnly | LoadOnDemand`. Exporter keeps menu-only art out of
  the gameplay resident set; runtime swaps it in on menu entry. Cheap to add
  on day one of the UI port, expensive to add after the splashpack schema
  ships.
- **UI VRAM accounting shares one budget line with environment / characters**,
  not a separate dock. Reinforces the reference's "menus compete for VRAM and
  fill rate too" stance and stops authors from treating UI as free.

Neither blocks the UI writer port (bullet 8 items 1–6 in `SplashpackWriter`).
They shape the node schema and the eventual budget dock (Phase 3).

### How this shapes Phase 2.5 / Phase 2.6

- Chunk streaming (`Scene.LoadChunk` in Phase 2.5) should be designed around
  the reference's chunk definition: each chunk owns geometry + resident
  texture pages + NPC set + script set + audio profile + effect budget.
  That's one struct, not six; write it once.
- Tiered NPC simulation (Phase 2.5 AI section) directly matches the reference's
  "simulation should be tiered" — high-detail near player, cheap elsewhere.
  Align the `StateMachine` primitive so it can run at variable tick rates
  driven by distance-to-player.
- RPG toolkit (Phase 2.6) `AttributeSet` / `Effect` / `Ability` resources fit
  the reference's "static authoring, dynamic state" rule — authored and
  budgeted at export, runtime only carries state. Already the design.

### Proposed new / promoted roadmap items

These are shims to the existing roadmap, not new phases:

- **Phase 1 add:** `SceneType` enum expansion + authored budget fields on
  `PS1Scene` (`target_tris`, `max_actors`, `max_effects`, `max_texture_pages`).
  No runtime impact — consumed by editor overlays. Closes `REF-GAP-4`.
- **Phase 2 bullet 8 amendment:** UI canvas / font assets born with a
  `Residency` property and counted against a shared VRAM budget line. Closes
  `REF-GAP-8`.
- **Phase 3 amendment:** VRAM viewer dock is framed around **per-scene
  residency**, not project totals. Includes texture-page grouping view and
  OT-pressure readout. Closes `REF-GAP-1`, `REF-GAP-2`, `REF-GAP-3`.
- **Phase 3 add:** texture reuse auditor — "one-off texture" and
  "near-duplicate CLUT" warnings. Closes `REF-GAP-6`.
- **Phase 2.5 add:** `PS1Chunk` authoring node + chunk archive writer.
  Closes `REF-GAP-5`. Pairs with the `Scene.LoadChunk` runtime API already
  on the 2.5 list.
- **Phase 2 bullets 10–11 amendment:** animations and skinned meshes carry
  a residency flag from day one. Closes `REF-GAP-7`.
- **Phase 2 bullet 6 follow-up (Phase 2.5):** per-area SPU budget accounting
  + residency on `PS1AudioClip`. Closes `REF-GAP-9`.
- **Phase 3 or later:** disc-layout-aware ISO build. Closes `REF-GAP-10`.
  Not urgent; surfaces once chunk streaming is real.

### Decision authority

When a design choice in this repo conflicts with a recommendation in the
sections above, this reference wins by default — it's the design-constraint
document. Override only with an explicit note in the PR description pointing
at the specific constraint being traded off and why the tradeoff is
acceptable (e.g., "accepts higher OT pressure because scene is single-digit
objects").

---

## Source Notes

These are the main references behind this summary:

1. **PSX-SPX / PlayStation Specifications**
   - Strong source for VRAM, texture cache, GPU behavior, CD-ROM details, and low-level hardware facts.

2. **Sony PlayStation hardware / SDK-era docs**
   - Useful for texture-cache behavior and practical rendering implications.

3. **Net Yaroze documentation**
   - Good practical explanation of ordering tables, double buffering, and typical rendering flow.

4. **Fall '96 CD-ROM training**
   - Especially useful for streaming and disc-throughput advice.

5. **Copetti PlayStation architecture article**
   - Helpful modern companion for readability and mental models.

---

## Reference URLs

- PSX-SPX: https://problemkaputt.de/psx-spx.htm
- PSn00b / lib reference mirror: https://psx.arthus.net/sdk/PSn00SDK/Docs/libn00bref.pdf
- Net Yaroze documentation (archive text): https://archive.org/stream/NetYarozeDocumentation/usrguid_djvu.txt
- Fall '96 CD-ROM training PDF: https://psx.arthus.net/sdk/Psy-Q/DOCS/TRAINING/FALL96/cdrom.pdf
- Sony-era run-time library overview: https://psx.arthus.net/sdk/Psy-Q/DOCS/LibOver47.pdf
- Copetti PlayStation article: https://www.copetti.org/writings/consoles/playstation/

---

## Suggested prompt to pair with this file

You can give Claude Code a prompt like:

"Use the attached PS1 optimization reference as a design constraint document. Evaluate my renderer, streaming model, scene budgets, texture strategy, and world chunking against PS1-style hardware limits. Prefer recommendations that improve VRAM stability, reduce texture-page churn, reduce ordering-table pressure, improve CD streaming behavior, and increase asset reuse for a larger RPG-style project."

