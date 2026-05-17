# VRAM viewer dock — design + patch

Closes three REF-GAPs simultaneously in
`docs/ps1_large_rpg_optimization_reference.md`:

> 1. **No per-scene / per-area resident VRAM view.**
>    `PS1TextureAnalyzer` scans the whole project. RPG work needs
>    "what's resident in *this* chunk?"
> 2. **No texture-page / CLUT grouping visualization.** Authors
>    can't see which atlas pages share tpage allocations.
> 3. **No ordering-table / object-count discipline surface.** OT
>    pressure is silent until performance drops.

And expands the Phase 3 roadmap bullet that currently says:

> - [ ] VRAM viewer dock (Phase 3)

The existing PS1Godot dock has scene-budget bars (triangle count,
VRAM total, SPU total). Good first pass, but it's whole-project
totals at the wrong granularity. Authors need to see what's
resident *right now* in *this* chunk, where pages collide, and
whether the OT is going to thrash.

Drop this file at `docs/vram-viewer.md`.

## Goal

Open a scene, switch to the VRAM tab, see a 1024×512 visualization
of where every byte is allocated — atlases by chunk, CLUTs, font
column, framebuffers, free space. Color-code by residency
(gameplay / menu / load-on-demand). Hover for the source asset.

Then a sibling view shows texture-page grouping (which assets
compete for the same tpage and how often the renderer swaps
between them), and OT pressure (estimated entries vs `OT_SIZE`,
broken out by source — environment / characters / particles /
UI).

Non-goal: pixel-accurate VRAM simulation. The viewer reflects the
exporter's intent, not the running PS1's state. For live runtime
state (after texture uploads, after chunk transitions), defer to
the existing `PSXSPLASH_MEMOVERLAY` runtime overlay.

## What's already in place

- **`PS1GodotDock.cs`** — bottom-right dock panel with Run-on-PSX
  button + secondary actions + scene-stats labels + budget bars
  for triangles / VRAM / SPU.
- **`PS1TextureAnalyzer.cs`** — project-wide texture sweep,
  reports 4bpp / 8bpp / 16bpp / too-big classifications. The
  classification logic is the right primitive; just runs at the
  wrong scope.
- **`VRAMPacker.cs`** — knows where every atlas, CLUT, and font
  lands in VRAM, with byte coordinates. This is the data the
  viewer renders.
- **`PSXTextureAnalyzer.cs`** — the analyze button in the dock
  already runs `PSXTextureAnalyzer.AnalyzeProject()`. Refactor
  the same machinery to accept a "scope" parameter (whole
  project, current scene, specific chunk).

So everything needed exists; this is mostly a UI surface that
visualizes data we already compute.

## Design

Four sub-views, each a tab inside the VRAM dock surface. Authors
flip between them as their question changes.

### Tab 1 — VRAM map (1024 × 512 visualization)

The headline view. A scaled rendering of the entire 1 MB VRAM
grid with allocations color-coded:

- **Dark grey** — framebuffers (top-left and below; reserved).
- **Steel blue** — font column (x=960..1023, reserved).
- **Green tints** — atlas allocations, one tint per atlas index.
- **Magenta** — CLUT rows.
- **Light grey** — free space.
- **Red outline** — any allocation overlapping a reserved region
  (export error indicator).

Per-chunk overlay: when a `PS1Chunk` is selected in the scene
tree, the VRAM map dims everything except that chunk's allocations
and the "base scene" allocations marked `SharedTextures`. Authors
see the chunk's exclusive footprint at a glance.

Hover any colored region → tooltip with:

- Asset name + `res://` path
- Bit depth (4 / 8 / 16)
- Pixel size + VRAM cost
- Which chunk it's resident in (or "base scene")
- Texture page index

Click a region → focus the source asset in Godot's filesystem
panel. Same flow as the Tools menu's "find asset" pattern.

Render path: a Godot `Control` node with a `_Draw()` callback
that walks the `VRAMPacker`'s output and emits rectangles.
Backed by a tiny atlas of color swatches per asset type. The
rendering runs in the editor only — no runtime cost.

### Tab 2 — Texture-page grouping

The optimization reference emphasizes "texture-page discipline":
the GPU hits a tiny 2 KB cache and switching texture pages
frequently kills throughput. This view surfaces page churn.

Layout: a grid of all populated texture pages (max 32 per VRAM).
Each cell shows:

- Page coordinates (e.g., `(P3, Y0)` = page 3, top half).
- Total bytes used.
- Asset count in this page.
- A small inline list of asset names.

Color: green if this page is referenced by ≤ 4 distinct GameObjects
(good cache reuse), amber for 5–12, red for 13+ (likely thrash).

Companion view (optional, advanced): a "tpage-switch frequency"
estimate. For each frame's render order, count adjacent triangles
that use different tpages. The exporter has the render order
implicitly (objects in declaration order, sorted by BVH cull).
Walk it, count tpage transitions, report the per-scene total.

Authors see "your typical frame has 47 tpage switches; budget is
~30" and have something concrete to optimize against.

### Tab 3 — Object & OT pressure

The third REF-GAP: ordering-table load. The renderer's OT has
`OT_SIZE` buckets (compile-time constant, default 4096). Each
triangle gets one bucket entry; large scenes can blow past
`OT_SIZE` and silently overflow.

This view shows:

- **OT entries by category.** Stacked bar chart: environment /
  characters / dynamic props / particles / UI. Total at the top
  vs `OT_SIZE` ceiling.
- **Per-object triangle counts.** Sortable table — name, tris,
  AABB volume, owning chunk. Click → focus in scene.
- **Top offenders.** Highlight the 5 highest-tri objects in the
  scene. The "you have a 600-tri prop that's probably meant to be
  background" auditor.

The numbers come from `SceneCollector` already — total triangle
count is in `data.Objects.Sum(o => o.Mesh.Triangles.Count)`.
Break out by category needs a small classifier (a GameObject
with `Tag = TAG_PARTICLE` counts as "particles"; one with
`StartsInactive = true` and `Tag = TAG_BULLET` counts as "dynamic
prop"; etc.). Classifier defaults are reasonable and can be
overridden per-chunk.

Per-chunk: same chart, scoped to the selected chunk's contents.
Authors check "my dungeon chunk fits in 1500 OT entries" with one
glance.

### Tab 4 — Residency breakdown

Closes REF-GAP-8 (UI canvas residency) and REF-GAP-9 (audio
residency) which were filed alongside the VRAM ones.

A simple table:

| Category | Gameplay-resident | MenuOnly | LoadOnDemand |
| --- | --- | --- | --- |
| Environment textures | 84 KB | — | — |
| Character textures | 32 KB | — | — |
| UI font + atlas | 18 KB | 12 KB | — |
| Audio clips (SPU) | 96 KB | — | 48 KB |
| Lua scripts | 14 KB | 2 KB | — |

Per-chunk breakdown shows the per-chunk residency budget.
Highlights overflow rows in red.

This is the "scoreboard" the budget bars in the existing dock
should eventually become — same data, with the residency
dimension surfaced.

## Implementation stages

Five stages, each shippable. Stage 1 is the headline visualization;
later stages add the auditing capabilities.

### Stage 1 — VRAM map tab ✅ shipped 2026-05-16

`PS1VRAMViewerDock` + `PS1VRAMGrid` + `VramSnapshot` ship the
1024 × 512 visualization with reserved-region coloring, atlas /
texture / CLUT placement, scene picker dropdown for multi-scene
projects, hover tooltips with asset name + bit-depth + VRAM
coords, and (as of 2026-05-16) click-to-focus that navigates
the FileSystem dock to the source `res://` path via
`EditorInterface.SelectFile`.

`VramSnapshot.TextureRect` / `ClutRect` now carry both the
short display name and the full `SourcePath` so click-to-focus
has the path to navigate to.

Per-chunk overlay is the remaining Stage 1 sub-bullet —
deferred because chunks aren't a thing yet (waits on
`chunk-streaming.md`).

Verifiable: open the demo scene, see the framebuffers, font
column, atlas, and CLUTs laid out matching the actual VRAM
the runtime uses.

Stages 2–5 (texture-page grouping, OT pressure, residency
breakdown, auditor + snapshot export) are deferred as their
own session — each is a meaningful new tab + classifier and
together they're another major dock-side feature.

### Stage 2 — Texture-page grouping tab

- Compute per-tpage asset references from `VRAMPacker.PlacedAtlasCount`
  + the per-texture `TexpageX/Y` already on `PSXTexture`.
- Aggregate by tpage coordinate, count owning GameObjects.
- Render the page grid with color-coded counts.

Verifiable: deliberately over-pack a scene with 20 distinct
4bpp textures sharing one page; the view shows the red "13+
assets" warning.

### Stage 3 — OT pressure tab

- Sum triangle counts per object via `SceneCollector`'s data.
- Apply a category classifier (default rules + per-chunk
  override).
- Render the stacked bar + sortable top-offenders table.

Verifiable: the demo's combat showcase scene shows particle /
character / environment breakdown matching the demo's actual
content.

### Stage 4 — Residency breakdown tab

- Aggregate the `Residency` properties from `PS1AudioClip`,
  `PS1UICanvas`, `PS1UIFontAsset` (extend the property to
  `PSXTexture` too — already roadmapped as REF-GAP-7 amendment).
- Build the table with per-category / per-residency cells.
- Per-chunk scope from the chunk selector.

Verifiable: a scene with explicit MenuOnly assets shows them
in the MenuOnly column; total VRAM cost adds up.

### Stage 5 — Polish + auditor

- "Generate residency suggestions" button — runs a heuristic
  over the scene's asset references and suggests which assets
  should be marked `MenuOnly` / `LoadOnDemand` based on which
  scripts touch them.
- "Find duplicate CLUTs" auditor — closes REF-GAP-6 (texture
  reuse auditor). Detects near-duplicate 16- or 256-entry CLUTs
  across textures and proposes merging.
- Snapshot export — save the current VRAM map to a PNG for
  documentation / sharing.

## Open questions / tradeoffs

**Live vs export-time.** The viewer reflects the exporter's
intent, not the running PS1's state. After a chunk transition,
the actual VRAM contents diverge from the editor's "all chunks
visible at their authored origins" view. Two options:

1. *Show only the scene root + base scene's resident set by
   default.* User selects a chunk explicitly to overlay its
   slot. Honest but takes a click.
2. *Auto-rotate through chunks like a slideshow.* Author sees
   the VRAM contents in chunks A, B, C cycle. Cute but
   distracting.

Default to option 1. The slideshow is a Stage 6+ flourish.

**Texture-page churn estimate accuracy.** The "47 tpage
switches per frame" number is approximate — the actual frame
runs the BVH cull which may further re-order. Treat as a hint,
not a measurement. For ground truth, the runtime memoverlay
exists.

**OT_SIZE breakage at runtime.** Today's `OT_SIZE` is a
Makefile constant. Authors who blow past it get silent overflow
+ visual artifacts. The viewer should know the current
`OT_SIZE` — either by parsing the Makefile (brittle) or by a
dock-side authored value (simpler, with a sanity check that
the runtime is built with the same).

**Per-asset residency property propagation.** Textures don't
currently have a `Residency` property — only audio clips and UI
canvases do. Adding it requires extending `PSXTexture` and the
splashpack writer. Cheap addition but a format bump; roll it
into the v25 chunk-streaming bump if both ship together.

**Hover discoverability.** Authors don't know tooltips exist on
custom Godot Control nodes by default. Solution: a small "?"
icon in the corner that documents the interaction model, plus
a one-time "Try hovering an atlas" prompt on first scene open.

**Performance of `_Draw()`.** A 1024 × 512 grid rendered as
many tiny rects could lag. Mitigation: scale the visualization
down to ~512 × 256 for the actual draw, since the source data
is in pages (64 × 256 units) and pixel accuracy is not needed.

**OT category classifier defaults.** The "environment vs
characters vs particles vs UI" split needs a default rule
that's right most of the time:

- UI = anything child of `PS1UICanvas`.
- Particles = GameObject `Tag` matches a registered "particle"
  tag, or `StartsInactive = true` and `polyCount < 8`.
- Characters = `PS1SkinnedMesh` instances.
- Dynamic props = `StartsInactive = true` and not a particle.
- Environment = everything else.

Authors override per-chunk for unusual cases. Document the
defaults in the dock.

**Versioning the dock UI.** As tabs land in stages, the dock
grows. Risk: authors update the plugin and see different tabs.
Mitigation: every tab works standalone; missing tabs = "not
yet implemented" placeholder.

## Suggested ROADMAP additions

Replace the current Phase 3 single-line bullet with the
expanded set:

> - [ ] **VRAM viewer dock — Tab 1: VRAM map.** 1024 × 512
>       visualization with color-coded allocations, hover
>       tooltips, per-chunk overlay. Closes part of `REF-GAP-1`.
> - [ ] **VRAM viewer — Tab 2: Texture-page grouping.** Per-tpage
>       asset reference counts, tpage-switch frequency estimate.
>       Closes `REF-GAP-2`.
> - [ ] **VRAM viewer — Tab 3: OT pressure.** Per-category
>       triangle counts, top-offenders table, vs `OT_SIZE`.
>       Closes `REF-GAP-3`.
> - [ ] **VRAM viewer — Tab 4: Residency breakdown.** Per-category
>       gameplay / menu / load-on-demand budget table. Pairs with
>       the existing residency property roll-out on audio + UI.
> - [ ] **VRAM viewer — Stage 5: Auditor + snapshot.** Residency
>       suggestion heuristic, duplicate-CLUT detector, PNG export.
>       Closes `REF-GAP-6`.
>
> Full design: `docs/vram-viewer.md`.

## Changelog

- `2026-05-11` — Document created. Sixth patch doc in the
  series. Closes `REF-GAP-1`, `REF-GAP-2`, `REF-GAP-3`,
  contributes to `REF-GAP-6`. Pairs with `chunk-streaming.md`
  for per-chunk views.
