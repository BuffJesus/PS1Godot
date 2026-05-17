# Pre-rendered meshes — design + patch

**Status (2026-05-17):** No implementation yet. Strict consumer of
two unstarted ROADMAP items — `PS1Sprite` (single-quad billboard
node, line 791) and `ObjectFrameSwap` track type (atlas-region UV
flip, line 720) — so even Stage 1's bake pipeline has nothing to
emit against until those land. Once Stage 0 ships, Stage 1 sizes
to ~4–6 commits (PS1PrerenderedMesh node, PrerenderedMeshBaker
with SubViewport render + cache, SceneCollector wiring, demo coin)
plus the prefab library (Stage 2) and over-budget warnings
(Stage 3). Deferred to its own session — best slotted after the
session that takes on PS1Sprite + bob extension, since both share
the same per-frame update path.

Bake a 3D mesh down to a sprite-sheet billboard, then play it back
as a flip-book on a single quad. Classic PS1 / SNES technique —
Crash Bandicoot's Wumpa fruits, Donkey Kong Country's everything,
Mario 64's coins. For collectibles, projectiles, simple FX, and any
small object that the player will never see closely or from a
specific angle, this trades a chunk of VRAM for a near-total
collapse of CPU + GTE work per instance.

Drop this file at `docs/prerendered-meshes.md`.

## Goal

A scene with 50 visible coins should cost ~50 textured quads — not
50 × (whatever a coin mesh is). The runtime never sees the mesh; it
only ever draws one quad per pickup, sampling a different region of
a shared atlas each frame.

Non-goal: view-angle billboards (Donkey Kong Country sprite system,
where the runtime picks the closest of 8 pre-rendered angles
based on camera direction). Same mechanism with a different frame
selector — defer to v2 once v1 is shipping. Collectibles are a
better fit for time-driven cycling anyway; they spin on their own
axis regardless of where you're looking.

## Why this is mostly free

Two roadmap items in `ROADMAP.md` § Rendering options already do
the heavy lifting:

> **Sprite / billboard objects.** `PS1Sprite` node — a single-quad
> mesh that always faces the active camera. **[runtime]**

> **Atlas-region flip.** `TrackType::ObjectFrameSwap` swaps between
> authored UV rectangles within a single texture page. Each
> keyframe selects a frame index; the runtime rewrites U/V offsets
> on the object's triangles each tick. **[runtime]**

Compose those two and the runtime story is done. What's missing is
the **bake pipeline** — the editor-side tool that turns a Godot
Mesh into a sprite-sheet and emits the frame data. That's where
this doc focuses.

A "pre-rendered mesh" in this design is:

1. An authored 3D mesh (any Godot `Mesh`).
2. A bake configuration (frame count, rotation axis, atlas slot).
3. Editor-time output: a sprite-sheet atlas + a frame-swap animation
   track.
4. Runtime: a `PS1Sprite` instance plus the frame-swap track
   playing on loop.

Authors never touch the runtime side. They just check a box on a
mesh node and the exporter does the bake at scene-export time.

## Design

### Authoring (PS1Godot side)

New node: `PS1PrerenderedMesh`. Wraps a `MeshInstance3D` (or
references a `Mesh` directly) plus bake settings:

```csharp
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_prerendered.svg")]
public partial class PS1PrerenderedMesh : Node3D
{
    [ExportGroup("Source")]
    [Export] public Mesh? SourceMesh { get; set; } = null;
    // Optional: a material override applied during bake only. Useful for
    // tinting (gold coin vs silver coin from the same mesh) without
    // duplicating the source.
    [Export] public Material? BakeMaterial { get; set; } = null;

    [ExportGroup("Bake")]
    // 4 / 8 / 12 / 16 — how many frames around the rotation axis.
    // 8 is plenty for a small rotating pickup; 16 starts to look
    // smooth for slower-spinning objects.
    [Export(PropertyHint.Enum, "4,8,12,16")]
    public int FrameCount { get; set; } = 8;

    // Y is the typical "spin like a coin" axis; X / Z available for
    // bobbing / wobble effects (rarer).
    [Export(PropertyHint.Enum, "X,Y,Z")]
    public int RotationAxis { get; set; } = 1; // Y

    // Sprite pixel size. PS1-cache-friendly sizes are 32×32 or 64×64
    // for 4bpp; 32×32 is the default. Keep small — every pickup
    // instance pays the same VRAM regardless of bake count.
    [Export(PropertyHint.Enum, "16,32,64")]
    public int SpritePixels { get; set; } = 32;

    [Export] public PSXBPP BitDepth { get; set; } = PSXBPP.TEX_4BIT;

    [ExportGroup("Playback")]
    // Spin speed in frames-per-second of the sprite cycle (NOT
    // mesh rotation degrees per second). At 8 frames / 8 fps the
    // cycle takes 1 second.
    [Export(PropertyHint.Range, "1,30,1,suffix:fps")]
    public int CycleFps { get; set; } = 8;

    // Bobbing — optional vertical offset that adds visual life
    // without extra atlas frames. Runtime applies on top of the
    // base position; doesn't change the sprite content.
    [Export(PropertyHint.Range, "0,0.5,0.01,suffix:m")]
    public float BobAmplitudeMeters { get; set; } = 0f;
    [Export(PropertyHint.Range, "0.1,5.0,0.1,suffix:Hz")]
    public float BobHz { get; set; } = 1.5f;

    [ExportGroup("Behavior")]
    // Standard PS1MeshInstance-equivalents — same semantics.
    [Export(PropertyHint.File, "*.lua")]
    public string ScriptFile { get; set; } = "";
    [Export(PropertyHint.Range, "0,65535,1")]
    public int Tag { get; set; } = 0;
    [Export] public bool StartsInactive { get; set; } = false;
    // Collectible-shaped collision: a small AABB the player walks
    // into. Default to "trigger only" — collectibles don't push.
    [Export] public bool TriggerOnContact { get; set; } = true;
    [Export(PropertyHint.Range, "0.1,5.0,0.1,suffix:m")]
    public float TriggerRadiusMeters { get; set; } = 0.5f;
}
```

The authoring node sits in the scene tree like any other PS1 node.
The exporter does the bake at export time and emits the right
runtime primitives — author doesn't manage atlases or animation
tracks directly.

### Bake pipeline (editor-time)

This is the meat of the new work. The flow:

1. **Build a SubViewport** sized to `SpritePixels × SpritePixels`
   with `transparent_bg = true` and the PS1 shader stack applied
   (same one used by the editor preview, so the bake matches what
   the PSX renders).
2. **Place an orthographic camera** framing the source mesh's
   bounding sphere. Ortho avoids perspective foreshortening within
   a single frame — every rotation step is the same on-screen
   size.
3. **For each frame index 0 .. FrameCount - 1:**
   - Rotate the mesh by `frame * (360° / FrameCount)` around the
     chosen axis.
   - Force a SubViewport render (`await
     RenderingServer.frame_post_draw`).
   - Read back the texture as an `Image`.
4. **Composite all frames into one strip** —
   `FrameCount * SpritePixels` wide × `SpritePixels` tall. Single
   atlas entry, one CLUT.
5. **Quantize** through the existing `PSXTexture.FromGodotImage`
   path at the chosen bit depth. The CLUT is shared across all
   frames, which is what makes the strip a single atlas entry.
6. **Emit a `PS1Sprite` GameObject** with the strip as its texture
   and UVs initially pointing at frame 0.
7. **Emit a frame-swap animation track** with `FrameCount`
   keyframes, looping at `CycleFps`. The track's `frameRects` array
   is `[(0,0,32,32), (32,0,32,32), …]` — one entry per frame.

Implementation lives in
`addons/ps1godot/exporter/PrerenderedMeshBaker.cs`. Runs from
`SceneCollector` as it walks `PS1PrerenderedMesh` nodes:

```csharp
public static (PSXTexture atlas, FrameSwapTrack track) Bake(
    PS1PrerenderedMesh node, SceneData data)
{
    var frames = RenderFrames(node);                  // List<Image>
    var strip  = CompositeStrip(frames, node.SpritePixels);
    var atlas  = PSXTexture.FromGodotImage(strip, node.BitDepth, SyntheticPath(node));
    var track  = BuildFrameSwapTrack(node);
    return (atlas, track);
}
```

The strip image is cached per-bake-config-hash in
`res://.import/ps1godot-bakes/` so a clean reopen doesn't re-render
every frame. Cache key: `(mesh resource path, frame count,
rotation axis, sprite pixels, bake material path)` — change any of
those and the bake re-runs. Mesh content changes invalidate
naturally via the path-keyed import system.

The bake is opt-in per node. Authors can hit "Force re-bake" on the
PS1PrerenderedMesh inspector to ignore the cache, which is useful
after editing the source mesh.

### Composing PS1Sprite + frame-swap

This is purely glue once both roadmap items are in. Per
`PS1PrerenderedMesh` node, the exporter emits:

- One `PS1Sprite` entry with:
  - One quad (2 tris) using the baked atlas region for frame 0.
  - `BillboardMode = AxisLocked` (Y-axis lock) so the quad stands
    upright as the camera orbits — collectibles shouldn't tip.
  - The standard `PS1MeshInstance` fields (Tag, StartsInactive,
    ScriptFile, collision).
- One `FrameSwapAnimation` entry referencing the sprite's
  GameObject, with the keyframes set up to loop at `CycleFps`.
- Optionally one **`BobAnimation`** entry — a tiny secondary track
  that animates Y position with a sine wave. This is just a
  pre-baked translation track using the existing `PS1Animation`
  facility (one full cycle of keyframes, marked `loop = true`).
  Authors get bobbing without writing Lua.

None of this requires new splashpack format work beyond what
PS1Sprite and ObjectFrameSwap already require — both roadmap items
will define their own format additions. The bake just feeds
those tables.

### Animation

Pre-rendered meshes inherit the engine's existing animation surface;
nothing new is strictly required for the common cases. Five patterns
matter.

**Spin = flipbook (no runtime rotation math).** The rotation visible
on a coin isn't a runtime transform — it's the bake. Each atlas
frame captures the mesh at a different rotation step, and the
frame-swap track cycles through them at `CycleFps`.
`setupObjectTransform` runs once per instance with the author-placed
yaw (probably zero), the GPU sees one quad with shifting UVs, and
that's the whole spin story. This is the structural win: 50 visible
coins all spinning costs 50 quads' worth of GTE work total — no
matter how fast they spin, no matter how many frames the bake has.

Authors who want faster spin increase `CycleFps`. Authors who want
smoother spin increase `FrameCount` at the VRAM cost spelled out
below. The two knobs are independent: 8 frames at 16 fps looks
choppy but fast, 16 frames at 4 fps looks smooth but slow.

**Bob via PS1Sprite Y-oscillation.** Bobbing IS a runtime transform —
it changes per-instance position, so it can't be baked into a
shared atlas. The cleanest home for it is a tiny extension to the
PS1Sprite roadmap item itself: optional `bobAmpFp12`, `bobRateFp12`,
and a per-instance `bobPhaseFp12` field. PS1Sprite already does
per-frame work to billboard the quad toward the camera; one more
position write is essentially free:

```cpp
// In the PS1Sprite per-frame update, after the billboard basis math:
state.bobPhaseFp12 = (state.bobPhaseFp12 + state.bobRateFp12) & 0x3FFF;
int16_t sinVal = sinTable[state.bobPhaseFp12 >> 4];  // 1024-entry shared table
obj->position.y = state.basePositionY +
                  ((int64_t)sinVal * state.bobAmpFp12 >> 12);
```

The sine table is 2 KB of rodata shared across every bobbing
object. Per-object state is 12 bytes (base Y + amp + rate + phase).
Per-frame cost: one add, one mask, one table lookup, one mul, one
shift, one position write. Negligible at PS1's typical instance
counts.

Bob and spin run at fully independent rates by construction —
they're driven by different runtime systems (frame-swap track vs
the PS1Sprite update). A coin can spin at 8 fps and bob at 1.5 Hz
and the two never interact.

This nudges the PS1Sprite roadmap item's scope slightly — see the
suggested addition at the bottom of this doc.

**Per-instance phase offset.** Ten coins authored from one prefab
spinning and bobbing in lockstep looks artificial. The runtime
auto-seeds each instance's phase from its GameObject index:

```cpp
// At scene init, for every PS1Sprite with bob enabled:
state.bobPhaseFp12 = (objIndex * 1031) & 0x3FFF;
// And for the frame-swap track on the same object:
swapState.currentFrame = objIndex % swapTrack.frameCount;
```

(Prime multiplier on the bob phase avoids visible patterning when
adjacent indices spawn near each other.) Authors get organic-
looking variance with zero authoring effort.

For deliberate sync — a row of coins choreographed to wave — the
`PS1PrerenderedMesh` node gets `BobPhaseManual` and `SpinPhaseManual`
overrides that default to "auto."

**Tint flash for feedback.** The PS1Sprite quad takes a per-vertex
color multiplied into the texture sample. Lua writes it any frame:

```lua
function onUpdate(self, dt)
    if self.flashing then
        local t = (Scene.Time() * 4) % 1.0          -- 4 Hz cycle
        local v = math.floor(128 + 127 * math.sin(t * 6.28))
        Entity.SetTint(self, v, v, 255)             -- pulse blue → white
    end
end
```

Use for "shiny" pickups, magnet-attracted coins, time-limited
power-ups, "low on items" warning blink. Costs one vertex-color
write per quad per frame — same path as the existing FlatColor
mode, no new runtime work.

**Collect feedback (fade-out).** When a coin is picked up, fading
the quad over ~6 frames before deactivating reads better than an
instant pop. The runtime's existing semi-transparent primitive
support handles this end-to-end:

```lua
function onTriggerEnter(self, other)
    if Entity.GetTag(other) ~= TAG_PLAYER then return end
    self.fading = true
    self.fadeFrame = 0
end

function onUpdate(self, dt)
    if not self.fading then return end
    self.fadeFrame = self.fadeFrame + 1
    if self.fadeFrame > 6 then
        Entity.Destroy(self)
        Events.Fire("coin_collected")
    else
        local alpha = 255 - (self.fadeFrame * 42)
        Entity.SetTint(self, alpha, alpha, alpha)
    end
end
```

~15 lines of Lua, no new runtime support. Combined with a small
camera shake and a sound effect, sells the pickup hard.

**Out of scope (deferred to other roadmap items).** Two animations
that would be nice but depend on work outside this doc:

- **Pulse scale** (rhythmic grow / shrink to draw attention) and
  **spawn pop** (scale up on appear) both need per-object scale
  support, which is tracked separately under Phase 2.5's "Object
  scale animation" community ask. Once that lands, both become
  tiny Lua loops driving `Entity.SetScale`. No prerendered-meshes-
  specific work needed.
- **Sparkle particles** trailing a high-value pickup need a
  particle system. Phase 2.5 has billboard-quad-emitter primitives
  on the radar; not blocking and not in scope here.

Until those land, fade-out approximates spawn pop reasonably well
(coin fades in over a few frames on spawn), and tint flash carries
most of the "look at me" weight that pulse scale would otherwise.

### Atlas budgeting

VRAM cost for an 8-frame 32×32 4bpp pre-rendered mesh:

```
8 frames × 32 × 32 pixels × 4 bits = 4 KB strip
       + 16-entry CLUT (32 bytes)
   ≈ 4.1 KB total
```

For comparison, a hand-modeled coin at maybe 16 triangles + a
single 32×32 texture is ~1 KB texture + ~830 bytes of triangle
data in main RAM. Pre-rendered is roughly 4× the VRAM cost — but
that VRAM is shared across every instance of the coin, and the
runtime savings scale with instance count:

- **Triangles submitted per visible coin:** 2 (vs 16) → 8×
  fewer GTE transforms.
- **OT entries per coin:** 2 (vs 16) → 8× lower OT pressure.
- **BVH entries:** none (PS1Sprite objects use whole-object
  cull, same as dynamic-moved) → BVH stays compact.
- **Animation work:** UV writes are ~free vs running a Lua-side
  rotation track that calls `Entity.SetRotationY` every frame.

The break-even is ~3 visible instances of the same prerendered
mesh on screen. Beyond that, every additional instance is pure
savings.

The editor budget bar (`PS1GodotDock.cs`) gains a "Pre-rendered
atlases" row alongside the regular texture row so authors can see
the trade.

### Authoring conventions for collectibles

The `ui_templates/` folder gets new prefab nodes:

- `prefabs/collectible_coin.tscn` — a `PS1PrerenderedMesh` with
  reasonable defaults (8 frames Y-axis, 32×32 4bpp, 8 fps, 0.1 m
  bob at 1.5 Hz) and a Lua script that despawns on player
  trigger and fires a "coin collected" event.
- `prefabs/collectible_gem.tscn` — same shape, 12 frames for
  the smoother spin a gem should have, larger bob amplitude.
- `prefabs/projectile_orb.tscn` — 8 frames but no bob, no
  trigger-on-contact (uses Physics.OverlapBox in Lua instead).

Authors drop a prefab, set the source mesh, hit Run-on-PSX. No
script work for the common case.

## Implementation stages

### Stage 0 (prerequisite) — Land PS1Sprite + ObjectFrameSwap

Both already on the `ROADMAP.md` rendering-options list. They're
the foundation. Frame-flip is also called out in the texture
animation block. Schedule these first; pre-rendered meshes is a
strict consumer of both.

### Stage 1 — Bake pipeline + PS1PrerenderedMesh node

PS1Godot side only. Once Stage 0 lands:

- Add the `PS1PrerenderedMesh` node and its icon.
- Implement `PrerenderedMeshBaker.cs` with SubViewport rendering
  + cache.
- Extend `SceneCollector` to recognize the node and emit the
  paired PS1Sprite + frame-swap-track records.
- Test against a hand-authored single coin in the demo scene.

Verifiable: the demo scene gets a spinning coin that draws as 2
tris per frame, cycles through 8 baked angles, and pops out of
existence when the player walks through it.

### Stage 2 — Prefab library + bake cache

- Ship the three prefab `.tscn`s.
- Move the bake cache out of `.import/` into a project-tracked
  `res://baked/` so contributors can commit reproducible bakes
  without re-running the SubViewport pipeline on every fresh
  checkout. (Optional — bake is deterministic, so re-rendering
  is fine; the cache is purely a speed win.)
- Editor inspector: a "Preview frames" button on
  `PS1PrerenderedMesh` that opens a small modal showing the
  baked strip with frame numbers — authors verify the bake
  looks right before exporting.

### Stage 3 — Polish

- Author-visible warnings when an over-budget bake happens (e.g.
  64×64 16-frame strip = 32 KB, big chunk of VRAM for one
  pickup type).
- Per-instance tint: vertex color on the PS1Sprite quad
  multiplied with the sampled texel. A common "gold / silver /
  diamond" variant story without rebaking the mesh — one bake,
  multiple instance tints.
- Optional drop-shadow: a second quad of a small dark blob
  drawn at the object's ground projection. Cheap (one more
  tri pair per instance) and sells the floating-pickup look.

## Open questions / tradeoffs

**Why bake offline instead of pre-rendering at scene load?** Bake
quality. We get to use Godot's full renderer (with the PS1
shader applied) at edit time — vertex snap, affine UVs, color
quantization, dither all happen in a representative pipeline.
Pre-rendering at scene boot would either need the same pipeline
running on PS1 (impossible — no shader) or a different fidelity
target (wrong look). Offline bake wins clearly.

**Animation other than spin.** The Y-axis loop is the common case.
For "pickup bounces on landing then settles" the design above
doesn't help — that wants a real per-object animation. Recommend
authors model that case with a real mesh + `PS1Animation` track
on the GameObject's position, and use prerendered meshes only for
the idle-spin loop.

**Mesh changes invalidating bakes.** Godot's import system handles
mesh path changes well, but a mesh edited in place doesn't
trigger any callback. The cache key includes the mesh's
`Mesh.get_rid()` hash, which changes on real edits but not on
metadata-only changes — close enough. Authors who edit a mesh
and don't see the bake update hit "Force re-bake."

**Atlas page churn.** Each prerendered mesh's strip is a
separate atlas entry. If a scene has 12 different pickup types,
that's 12 separate atlas entries the VRAM packer has to fit.
Authors building "many distinct pickups" should consider
collapsing visually-similar types into one mesh + per-instance
tint, or rebaking at smaller sprite pixels.

**Skinned source meshes.** The bake doesn't have to be a static
mesh — Godot can render a skeleton'd mesh in a pose, so animated
NPCs could pre-bake too (DKC route). Out of scope for v1; the
skinned-mesh pipeline (`PS1SkinnedMesh`) already covers full
3D characters where it matters.

**Sorting against world geometry.** PS1Sprite objects go in the
OT alongside everything else. Frame-flipped sprites should sort
correctly by quad depth as long as the renderer is given the
right OT bucket. No special case here vs the base PS1Sprite
roadmap item — the same depth-test rules apply.

## Suggested ROADMAP additions

Most of the runtime story is covered by the existing PS1Sprite +
ObjectFrameSwap items. Two small adjustments worth making:

**Nudge PS1Sprite's scope** to include optional bob oscillation
(`bobAmp`, `bobRate`, `bobPhase` fields + per-frame Y update using
a shared sine table). Tiny addition to the per-frame work
PS1Sprite already does. Update the existing roadmap bullet:

> - [ ] **Sprite / billboard objects.** `PS1Sprite` node — a single-quad
>       mesh that always faces the active camera. **Plus optional
>       sine-wave Y oscillation** (`BobAmplitude` + `BobRate`
>       authored on the node, per-instance phase auto-seeded from
>       GameObject index). Cheap (2 tris, one basis update + one
>       table lookup per sprite). Use cases: foliage, pickups,
>       ground shadow blobs, particle stand-ins, **bobbing
>       collectibles**. **[runtime]**

**Add a new bullet** referencing this doc:

> - [ ] **Pre-rendered mesh collectibles (`PS1PrerenderedMesh`).**
>       Bake a 3D mesh to an N-frame sprite-sheet atlas at export
>       time; emit as a `PS1Sprite` + frame-swap animation track.
>       Strict consumer of the PS1Sprite (with bob) and
>       ObjectFrameSwap items above. Editor-side only; no
>       runtime-only work. Demo: spinning coin pickup with
>       bob, fade-out on collect. Full design:
>       `docs/prerendered-meshes.md`.

## Changelog

- `2026-05-11` — Document created. Pairs with `lod-design.md` and
  `linux-support.md` as the third self-contained patch doc.
  Implementation gated on PS1Sprite + ObjectFrameSwap landing.
- `2026-05-11` — Added Animation section covering spin-as-flipbook,
  bob oscillation, per-instance phase offset, tint flash, fade-out
  feedback. Nudged PS1Sprite roadmap scope to include bob support.
