# PS1 Asset Pipeline Plan

Single roadmap covering meshes, textures, sprites/decals/alpha, VRAM, and
animation. Replaces four separate strategy docs that overlapped heavily and
read greenfield. This one is anchored to what PS1Godot already ships, defers
to `ROADMAP.md` for phase ordering, and treats validation as the cheap win
that gates the expensive binary work.

> **Architectural call.** The splashpack is the contract (CLAUDE.md). New
> mesh/animation/image data extends splashpack with new sections + a version
> bump — not separate `MESH_BANK` / `ANIM_BANK` files. The earlier docs
> proposed standalone banks; that contradicts the v21→v27→v29 evolution
> pattern and would force a parallel loader. This plan explicitly rejects
> that fork.

## Today

What the exporter + runtime already do, so we don't re-spec it:

| Domain | Shipped | Where |
|---|---|---|
| Mesh verts | Chunk-local fixed-point (`psyqo::FixedPoint<12,…>`); per-scene `GteScaling` divisor | `SceneCollector.cs`, `splashpack.hh` |
| Mesh UVs | uint8 packed into PSX-friendly tpage offsets at export | `TexturePacker.cs` |
| Mesh atlas | Auto-pack into 256×256 tpages with CLUTs; reuse-counted | `TexturePacker.cs` |
| Mesh validation | UV-out-of-range linter (no PSX wrap/clamp at hardware level) | `exporter/MeshLinter.cs` |
| Texture validation | Per-asset rows + WARN on 16bpp gameplay / oversized / atlas misuse | `exporter/TextureValidationReport.cs` |
| VRAM budget | Live bars in editor dock; per-scene aggregate cost model | `ui/SceneStats.cs` |
| Skin mesh | Full Mixamo humanoid pipeline: auto-detect + orthonormalize + 8-bone stride; vert-snap off via `ps1_skinned.tres` | `SceneCollector.cs`, memory entries |
| Skybox | 4bpp 256×N + tint RGB | `PS1Sky.Texture` + renderer |
| Fonts | Built-in `vt323` 8×12 4bpp at VRAM (960,0); 2 custom font cap per scene | `uisystem.cpp` |
| CLUT[0] transparency | Hardware-free for 4/8bpp — used by bezel/scanlines/sky | memory `project_psx_clut0_transparency.md` |
| Translucent flag | Per-element/per-mesh 2-pass darken (~25% dst) | `Translucent` bool in writer |
| Sound macros / families | Composite SFX + variation pools, exporter + runtime | Phase 5/B (just shipped) |
| Audio routing | SPU / XA / CDDA per-clip with validation gate | splashpack v27+, memory `project_audio_routing_scaffold` |
| Animation export | Skinned per-frame transforms; clip-name dot→underscore rewrite for Mixamo | `SceneCollector.cs` |

What's **not** shipped yet (the gaps the source docs were pointing at):

- Render-group batching by tpage/CLUT/alpha/draw-phase (today: per-mesh draws)
- Static-vs-dynamic classification metadata (today: every PS1MeshInstance is "dynamic")
- Vertex colors as a first-class authoring path (today: flat material color is the default)
- Animation events (footsteps, hitbox open/close, SFX cues)
- Segmented-rigid character method (today: skinned only)
- Sprite/billboard animation as a gameplay path
- LOD tiers (near 3D / mid simplified / far billboard)
- Per-asset VRAM budget rows in the dock (today: aggregate only)
- Alpha-mode enum (today: `Translucent` boolean)
- Decal-stack ceiling enforcement (~6 alpha quads/region)
- Chunk/disc/archive ownership metadata (today: one scene = one splashpack)

## Direction

Four guiding rules, in priority order. Any work that violates them gets
pushed back.

1. **Validation before binary.** Every asset domain gets a warning catalogue
   in `MeshLinter` / `TextureValidationReport` / new `AnimationLinter` /
   `SceneStats` *before* we propose a new on-disk shape. A warning is cheap;
   a v30 splashpack bump is not.
2. **Authoring metadata before optimisation.** Add the field
   (`MeshRole`, `AlphaMode`, `Residency`, `AtlasGroup`) and surface it in the
   inspector + warnings *before* the exporter consumes it. This way the
   metadata is real before any code rewrites depend on it.
3. **Round-trip Blender ↔ Godot is a first-class constraint.** Every
   metadata field must survive both directions: Godot scene → Blender
   custom property → Godot scene. Stable IDs (`MeshId`, `ChunkId`,
   `MaterialId`, `TexturePageId`, `CLUTId`) are the join key — preserve
   them on import, never silently regenerate. The Blender addon
   (`docs/ps1godot_blender_addon_integration_plan.md`) is the spec for
   the Blender side; this plan owns the shared schema.
4. **Splashpack extension, not parallel banks.** New domains land as new
   sections inside the existing 3-file splashpack split (`.splashpack` /
   `.vram` / `.spu`). Same versioning discipline: add at the end, bump.

## Authoring defaults

The single most useful output of the source docs — pin this somewhere
visible (CLAUDE.md or the editor dock).

### Texture format by content

| Content | Default bpp | Escalate to 8bpp when… |
|---|---|---|
| World walls/floors | 4bpp | hero hallway has skin tones / smooth gradients |
| UI / fonts / message boxes | 4bpp | never (small palette is the point) |
| Decals (blood, sigils, graffiti) | 4bpp cutout | never |
| Sprites / icons / particles | 4bpp | gradient-heavy effects |
| Character skin atlases | 4bpp | face/clothing washed out (e.g. Kenney humanoid) |
| Portraits / cutscene stills | 8bpp | budget separately, not gameplay-resident |
| Skybox | 4bpp | never (read once, depth-tested away) |

**16bpp is reserved.** One-off splash/title/cutscene only, evicted before
gameplay. Anything else is a `TextureValidationReport` WARN.

### Animation method by asset class

| Asset / situation | Method |
|---|---|
| Doors, gates, elevators, platforms, traps | Procedural or rigid transform keys |
| Pickups, spinning props, blinking lights | Procedural |
| Torches, flames | Sprite/texture animation |
| Player, hero NPCs | Skeletal |
| Generic NPCs | Segmented-rigid (preferred) or low-frame skeletal |
| Background NPCs, distant crowds | Sprite/billboard or frozen pose |
| Enemies (gameplay) | Skeletal/segmented with strict bone budget |
| Weird monsters, slimes, water | Limited vertex animation |
| Cutscene props | Transform tracks |
| Facial expressions | Texture/palette swaps, not morphs |

**Bone budget targets** (warn above):

```
Tiny enemy:        4–8 bones
Generic NPC:       8–16 bones
Player / hero:    12–24 bones
Important enemy: 12–24 bones
Boss:             special-case budget
```

**Animation FPS guidance:** 10 fps stylized/background, 15 fps NPC/enemy,
30 fps player and important motion. 60 fps stored animation is almost never
needed.

### Alpha mode (target enum, future v30 metadata)

```
Opaque         — standard textured tri
Cutout         — CLUT[0]=0 hardware transparency, free
SemiTransparent — 0.5 src + 0.5 dst (single hardware mode); 2-pass for ~25%
Additive       — 1.0 src + 1.0 dst (mode 1)
Subtractive   — 1.0 dst – 1.0 src (mode 3)
UI            — explicit UI compositor path
```

Today the runtime exposes a `Translucent` boolean that triggers the 2-pass
path. The full enum is metadata-only until a v30 bump consumes it; most
assets only ever need Opaque or Cutout.

### Decal / particle ceilings (per-screen)

- ≤ 6 overlapping alpha quads per screen-space region (PSX polygon-blender
  rate falls off above this).
- 8–16 active particles for small interiors; 16–32 for town chunks; 32–64
  for battle arenas; bosses budgeted separately.
- One-off graffiti on one wall belongs *baked into* the wall texture, not as
  a separate quad. Reuse threshold: < 2 → bake.

## Phased work (slot into ROADMAP)

This plan does not introduce a parallel "asset pipeline phase" — it inserts
work into the existing phase queue per the *finish current phase first* rule.

### Slot A — Phase 3 extension (next, in parallel with WYSIWYG UI editor)

Cheap exporter + dock work. Authoring-side only. No splashpack bump.

- **A1. Per-asset VRAM rows in the dock.** Extend `SceneStats` with a
  per-texture / per-CLUT table. `TextureValidationReport` already builds the
  rows; surface them in the dock instead of stdout-only. Half-day.
- **A2. Animation linter.** New `AnimationLinter.cs` mirroring
  `MeshLinter` / `TextureValidationReport`: warn on >30 fps clips with many
  frames, >24 bones, translation keys on non-root bones, missing combat
  events on attack clips. 1–2 days.
- **A3. Decal stack warning.** Per-region overlap counter in
  `TextureValidationReport`; WARN at >6 alpha quads in any 320×240 screen
  rect. 1 day.
- **A4. Static-vs-dynamic classifier (warning only).** Walk PS1MeshInstance
  tree; warn when a mesh has no animation track, no Lua attachment, no
  collision, and isn't moved by a parent transform → "should probably be
  baked into a static render group." Today this is reporting only; the
  binary change comes in Slot D. 2 days.

### Slot B — Phase 2 closeout dependency

These were already on the Phase 2 punch list; the unified plan calls them
out so they don't get skipped while we're in Slot A.

- **B1. Nav regions** (Phase 2 bullet 7). Non-flat nav surfaces.
- **B2. Rooms / portals** (Phase 2 bullet 12). Visibility culling primitive
  the render-group work depends on.

### Slot C — Phase 2.5 / 2.6 extension (after A + B)

Authoring metadata that makes Slot D unblockable. Adds inspector fields and
defaults; exporter consumes them as warnings, not yet binary. **Every field
here must round-trip Blender ↔ Godot** — design as plain enums or stable
strings (no GUIDs, no Godot-only resource refs). Blender side stores them
as `ps1godot.<field_name>` custom properties (see Blender plan §5.2);
Godot side stores them as `[Export]` properties on PS1* nodes.

- **C1. `MeshRole` enum** — `StaticWorld | DynamicRigid | Skinned | Segmented | CollisionOnly | EditorOnly`. Default inferred from current node shape so existing scenes don't break.
- **C2. `AlphaMode` enum** — full enum on materials (currently `Translucent: bool`). Warn on the implicit-→explicit migration.
- **C3. `AtlasGroup`** — `World | UI | Character | FX | Decal | Cutscene` hint that the atlas packer can already use as a soft constraint.
- **C4. `Residency`** — `Always | Scene | Chunk | OnDemand`. Warn on any 16bpp asset marked anything other than `Cutscene` / `Menu`.
- **C5. Animation events** — `PS1AnimationEvent` resource attached to clips: frame + `eventId` + 2 params. Runtime gets a Lua `onAnimationEvent` callback. Combat timing depends on this (hitbox open/close), so it gates a lot of gameplay work.
- **C6. Vertex-color authoring path.** Surface Godot's vertex paint output through the exporter; warn when a mesh's material implies vertex-color shading but no colors exist. Cheapest "real" lighting upgrade we can ship.
- **C7. Stable IDs.** Auto-generate + persist `MeshId` / `ChunkId` /
  `MaterialId` / `TexturePageId` / `CLUTId` on first export. Never
  regenerate on re-export. The Blender addon reads these from
  `ps1godot.*` custom props on import and writes them back on export —
  this is what makes round-trip safe.

### Slot D — Phase 2.6 / 2.7 (after C metadata is in scenes)

The binary work. Splashpack bump (probably v30) consumes the C metadata.

- **D1. Render-group batching.** New section: per-chunk static groups keyed by `(tpage, CLUT, alphaMode, drawPhase, shadingMode)`. Emit batched primitive lists instead of per-mesh draws for `MeshRole == StaticWorld`. Drops object iteration count, tpage churn, OT pressure. Largest performance win in this plan.
- **D2. Animation event tracks** in splashpack. C5 gives the authoring shape; D2 emits + dispatches.
- **D3. Segmented-rigid character path.** Body parts as separate meshes, parented to bones; one transform-track-per-piece animation. Cheaper than skinned for generic NPCs, more PS1-authentic.
- **D4. Sprite/billboard animation runtime.** Atlas-driven, frame-keyed, palette-swap variants.

### Slot E — Phase 4+ (RPG scope, deferred)

Big-picture stuff the source docs proposed; valid as direction, not safe to
queue until D ships.

- Chunk / disc / archive ownership metadata (`ChunkId`, `RegionId`,
  `AreaArchiveId`, `DiscId`).
- Area archive container (`AREA_TOWN_NORTH.archive` bundling mesh + tpages
  + CLUTs + collision + scripts + audio).
- LOD tiers (near 3D / mid simplified / far billboard).
- Disc-level compression (LZ on the area archive, not on individual records).
- Vertex animation runtime for special creatures.

## Round-trip authoring (Blender ↔ Godot)

**Goal:** an artist edits a mesh in Godot, opens it in Blender to retopo /
vertex-paint / re-rig, sends it back to Godot — and every PS1Godot
metadata field, ID, and validation result survives the round trip.

### Shared schema

These fields are the contract. Same names on both sides, same value sets,
same defaults. PS1Godot nodes own the canonical definitions; the Blender
addon mirrors them. Any new field added here must land in both tools
before it's consumed by the exporter.

| Field | Type / values | Owner |
|---|---|---|
| `mesh_id` | string, stable, auto-on-first-export | both, generated by whichever exports first |
| `chunk_id` | string, stable | both |
| `mesh_role` | `StaticWorld | DynamicRigid | Skinned | Segmented | CollisionOnly | EditorOnly` | both (C1) |
| `export_mode` | `MergeStatic | KeepSeparate | CollisionOnly` | both |
| `material_id` | string, stable | both |
| `texture_page_id` | string, stable | both |
| `clut_id` | string, stable | both |
| `texture_format` | `4bpp | 8bpp | 16bpp | Auto` | both |
| `alpha_mode` | `Opaque | Cutout | SemiTransparent | Additive | Subtractive | UI` | both (C2) |
| `shading_mode` | `Unlit | FlatColor | VertexColor | BakedLighting | CharacterAmbient | CharacterDirectional` | both |
| `draw_phase` | `Sky | OpaqueStatic | OpaqueDynamic | Characters | CutoutDecals | TransparentFX | UI` | both |
| `atlas_group` | `World | UI | Character | FX | Decal | Cutscene` | both (C3) |
| `residency` | `Always | Scene | Chunk | OnDemand` | both (C4) |
| `palette_group` | string | both |
| `disc_id` | int (multi-disc, Slot E) | both |
| `approved_16bpp` | bool | both |
| `force_no_filter` | bool | both |
| `allow_palette_swap` | bool | both |

### Storage

- **Godot:** `[Export]` properties on `PS1MeshInstance`, `PS1Material`,
  `PS1AudioClip`, etc. Saved into the scene file (.tscn / .tres).
- **Blender:** custom properties under the `ps1godot.` namespace
  (`ps1godot.mesh_id`, `ps1godot.alpha_mode`, …). Survives FBX/glTF
  export when "Custom Properties" is enabled.
- **On disk between tools:** sidecar JSON manifest per chunk
  (`<chunk_id>.ps1godot.json`) for fields that don't survive FBX cleanly
  (vertex-color presence flags, texture-page assignments, residency).
  Both tools read + write this file.

### Round-trip rules

1. **Preserve IDs on import.** If `mesh_id` exists in the source
   (Blender custom prop or Godot scene), reuse it. Never silently
   regenerate.
2. **Warn on duplicates.** Two meshes with the same `mesh_id` is a bug;
   surface it before export.
3. **Warn on field divergence.** Importer should diff the incoming
   metadata against the existing scene's metadata and surface
   conflicts — don't silently overwrite.
4. **No tool-specific encodings.** Don't store Godot resource refs or
   Blender's internal pointers in shared fields. Strings and enums
   only.
5. **Validation on both sides.** The warning catalogue below is
   tool-agnostic — both PS1Godot's exporter and the Blender addon's
   "PS1 Mesh Validation" panel emit the same warnings against the
   same rules.

The Blender addon's panel UI, Python plumbing, and FBX/glTF
import/export logic are out of scope for this plan — see
`docs/ps1godot_blender_addon_integration_plan.md` for the Blender-side
implementation spec.

## Warning catalogue (consolidated)

Single catalogue, replacing the per-doc lists. Source-of-truth for what
`MeshLinter`, `TextureValidationReport`, `AnimationLinter`, and
`SceneStats` should emit. Each entry: trigger → recommended fix.

### Mesh

```
WARN: <name> uses 4 texture pages.
  → atlas/group cleanup; pages 2+ usually mean the mesh straddles
    rooms or drew from unrelated source textures.

WARN: <group> has 260 vertices and forces U16 indices.
  → split at 255 to allow U8 — half the index buffer size.

WARN: <name> expects vertex colors but none were found.
  → falling back to FlatColor; bake vertex colors in Godot or accept
    the flat tint.

WARN: <prop> exported as DynamicRigid but never moves, animates,
      collides, or interacts.
  → set MeshRole = StaticWorld; will merge into static render group.

WARN: <chunk> has 37 separate dynamic object meshes.
  → review static/dynamic flags; most chunks should have <10.

WARN: <prim> uses semi-transparent material and covers >30% of screen.
  → consider Cutout or bake into the underlying texture.
```

### Texture

```
WARN: <texture> is 16bpp and Residency=Scene.
  → 8bpp indexed unless the gradient is genuinely needed; if needed,
    move to Cutscene/Menu residency.

WARN: <texture> > 256×256 (page max).
  → exporter auto-downscales; flag the source so authors can fix
    upstream.

WARN: <texture> imported by 2+ atlases.
  → dedup or pick one owning atlas; current state wastes VRAM.

WARN: alpha-heavy decal at 8bpp where 4bpp would do.
  → 4bpp w/ CLUT[0]=0 is hardware-free transparency.

WARN: <font> is the 3rd custom font in scene.
  → uisystem caps at 2 custom + built-in vt323; 3rd font won't fit.

WARN: full-screen image marked Always-resident.
  → load only for that screen; evict before gameplay.
```

### Decal / alpha

```
WARN: 7 alpha quads stacked in screen region (X,Y).
  → SPU polygon-blender rate falls off above 6; thin or bake.

WARN: large near-camera transparent quad detected.
  → each pixel costs opaque + dst read; consider Cutout.
```

### Animation

```
WARN: clip <name> is 60fps with 120 frames.
  → reduce to 15 or 30fps; PS1-style stepped is authentic.

WARN: skin <rig> has 48 bones.
  → target ≤24 for player, ≤16 for generic NPC.

WARN: clip <walk> stores translation keys on 32 bones.
  → only root/hips need translation in most cases.

WARN: clip <attack_heavy> has no animation events.
  → combat timing needs hitbox_open / hitbox_close events.

WARN: vertex animation <slime_idle> is 86 KB.
  → segmented-rigid or limited frame-count; vertex anim is the
    expensive path.

WARN: animation bank <town_npcs> is Residency=Always.
  → prefer Scene/Chunk; anim banks should follow chunks.
```

### VRAM budget

```
WARN: Chunk <name> est. 920 KB (cap ~512 KB usable).
  → drop a tpage, escalate fewer textures to 8bpp, or split chunk.

WARN: Splash/title still resident during gameplay scene.
  → evict in scene-load hook.
```

## Out of scope for this plan

Documented to prevent these from creeping back in via the source-doc
proposals:

- **Separate `MESH_BANK` / `ANIM_BANK` containers.** Splashpack extension only.
- **Generic per-triangle material data.** Render groups (Slot D1) are the
  abstraction; per-tri material defeats the batching win.
- **Custom bit packing / quaternion compression / motion matching / blend
  trees.** Not until D2 + D3 are in production use.
- **Runtime LOD generation.** Authoring-time LOD tiers only (Slot E).
- **Heavy per-frame decompression.** Disc-archive compression is fine; runtime
  records stay ready-to-render.
- **Modern projected decals.** Textured-quad decals + baked-into-atlas only.
- **Dynamic shadows.** Cutout/dithered fake shadow quads only.
- **A separate texture/image strategy doc.** This file owns it.

## Cross-references

- **Editor dock + budget bars:** `godot-ps1/addons/ps1godot/ui/SceneStats.cs`
- **Atlas packer:** `godot-ps1/addons/ps1godot/exporter/TexturePacker.cs`
- **Mesh linter (UV out-of-range):** `godot-ps1/addons/ps1godot/exporter/MeshLinter.cs`
- **Texture validator + WARN rows:** `godot-ps1/addons/ps1godot/exporter/TextureValidationReport.cs`
- **PSX mesh emitter:** `godot-ps1/addons/ps1godot/exporter/PSXMesh.cs`
- **Splashpack writer (v29):** `godot-ps1/addons/ps1godot/exporter/SplashpackWriter.cs`
- **Splashpack header + struct sizes:** `psxsplash-main/src/splashpack.{hh,cpp}`
- **Skinned-mesh runtime:** `psxsplash-main/src/skinmesh.cpp`
- **Texture-page renderer + GTE pipeline:** `psxsplash-main/src/renderer.cpp`
- **Hardware reference (RPG scope, VRAM/OT/disc):** `docs/ps1_large_rpg_optimization_reference.md`
- **Splashpack format extract:** `docs/splashpack-format.md`
- **Blender addon implementation spec:** `docs/ps1godot_blender_addon_integration_plan.md`
- **Memory entries that constrain this plan:**
  - `project_psx_clut0_transparency.md` (free 4/8bpp transparency)
  - `project_ps1_skinned_snap_collapse.md` (skinned-mesh snap rules)
  - `project_skinned_mesh_gotchas.md` (bullet 11)
  - `project_psx_ui_sort_order_inverted.md` (UI draw order is LIFO)

## Bottom line

The four source docs were directionally right and individually verbose.
Compressed: ship validation-first authoring (Slot A), then metadata
(Slot C, with Blender ↔ Godot round-trip baked in from day one), then
the binary win (Slot D). Don't fork splashpack into parallel banks.
Don't queue Slot D before Phase 2's nav-regions + rooms/portals close
out. Expensive items (E) are direction, not queue. Blender authoring
is a peer path, not a replacement — same metadata schema, same
validation rules, separate tooling implementation.
