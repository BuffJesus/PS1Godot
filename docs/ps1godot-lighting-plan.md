# PS1Godot vertex-lighting plan (Godot side)

**Goal:** out-do SplashEdit's lighting baseline meaningfully — not just
parity. SplashEdit ships pure-Lambertian-from-Unity-Lights bakes with an
0.8 PSX-2×-semi-trans cap. That's the floor. PS1 art at its best
(Silent Hill, FF9, Tomb Raider) had AO + faked radial lights + dithered
quantization that SplashEdit doesn't reach for.

This plan is divisible into four phases. Each is independently
shippable; later phases compound on earlier ones.

## Where lighting lives today

- Blender side: `tools/blender-addon/.../operators/vertex_lighting.py`
  ships create-layer / ambient-tint / directional-bake / clear, with
  the 0.8 PSX cap matching SplashEdit. (Phase 4 of the integration
  plan.)
- Godot side: `PS1MeshInstance.ColorMode` enum (FlatColor /
  BakedLighting / MeshVertexColors) + a `FlatColor` field. No bake
  logic exists; the runtime consumes whatever the mesh's COLOR
  channel happens to hold.

## Storage choice

Three options for "where do baked colors live on the Godot side":

| Option | Pros | Cons |
|---|---|---|
| Mutate the Mesh resource directly | Simple | Imported `.glb` meshes get clobbered on re-import; same Mesh shared across instances re-bakes once for everyone |
| Separate `PS1VertexLighting` Resource keyed by surface | Survives re-import | Two-step author flow; resource sync risk |
| **Per-surface override on `PS1MeshInstance`** (`BakedColors: PackedColorArray[]`) | Survives re-import; per-instance lighting; easy revert (clear array) | Slight cost to writer (prefer override when populated) |

Going with the third. SplashpackWriter's existing per-surface walk
takes one extra null-check: prefer `pmi.BakedColors[surface]` when
non-empty, fall back to the mesh's COLOR channel. Authors who want the
mesh's authored colors back just clear the override.

## Phase L1 — Bake from scene lights (small, ~2h)

Mirrors SplashEdit's primary mode but cleaner.

**Surface:**
- Tools menu: `PS1Godot: Bake Vertex Lighting from Scene Lights`.
  Operates on selected `PS1MeshInstance` / `PS1MeshGroup` nodes.

**Mechanics:**
- Walk the active scene for `DirectionalLight3D` / `OmniLight3D` /
  `SpotLight3D`.
- For each vertex of each surface: world-space normal · light-direction
  for directional; falloff for omni / spot. Sum across enabled lights.
- Multiply final by Light's color × energy.
- Clamp final to 0.8 (PSX 2× semi-trans headroom — matches SplashEdit
  `Runtime/PSXLightingBaker.cs:79-83`).
- Write the result into `PS1MeshInstance.BakedColors[surface]`.

**Why this beats SplashEdit:**
- Per-instance lighting (same mesh, different scenes ⇒ different
  bakes) — SplashEdit can't do this because it bakes into the source
  mesh.
- Composes cleanly with later passes (AO, ambient tint, etc.) since
  every op multiplies / adds into `BakedColors` instead of redoing
  the whole bake from scratch.

## Phase L2 — Vertex-AO bake (medium, ~half day)

The biggest visual quality jump. Lambertian-only bakes look plasticky
on flat-shaded prop geometry; AO darkens the recesses and sells the
chunky PS1 aesthetic.

**Mechanics:**
- For each vertex, fire N rays (N≈8–16) into the hemisphere above the
  vertex normal using `PhysicsServer3D.IntersectRay` against the
  scene's collision shapes (or a temporary ConvexPolygonShape3D
  generated from the meshes themselves if no colliders exist).
- AO term = 1 − (hits / N) × strength.
- Multiply into the existing `BakedColors[surface]`.
- Author iterates: bake key → bake AO → tweak ambient → bake AO again.

**Tunables:**
- Ray count (default 12 — visually sufficient at PSX byte-quantization).
- Ray length (default 0.5 m — too long and you AO-from-three-rooms-away).
- Strength (default 0.5 — pure 1.0 looks crushy at 8-bit).
- Bias (push origin off the surface to avoid self-intersection).

## Phase L3 — PSX preview shader (big, ~1–2 days)

The visual showpiece. Authors see the actual shipped look in Godot's
viewport without launching the emulator.

**Materials shipped:**
- `addons/ps1godot/shaders/ps1_lit_preview.tres` — extends existing
  `ps1_default.tres`.

**Shader operations on top of the existing vertex-snap pass:**
1. **5-bit quantization** of vertex color: `floor(c × 31) / 31` per
   channel. PSX hardware stores vertex color as 5 bits per channel
   (15bpp); the 8-bit Tri struct in mesh.hh is the splashpack carry
   format, but on actual hardware after the GPU step it becomes 5-bit.
2. **Ordered dither** based on screen-space pixel position. 4×4
   Bayer matrix, applied before quantization to break up bands.
   Matches what real PSX games look like under PCSX-Redux's "8-bit
   color path" mode.
3. **2× semi-trans simulation** when `AlphaMode == SemiTransparent`:
   `final = clamp(c × 2, 0, 1)` to show authors what the hardware
   blend will do. Pairs with the 0.8 bake cap from L1 — if the bake
   capped right, the 2× output stays in [0, 1.6] which the shader
   then clamps to 1.0, simulating the PSX saturation.

**Per-mesh toggle:**
- New `PS1MeshInstance.PreviewMode` enum: `Editor` (Godot's normal
  shading) / `PSX` (this preview material). Default `Editor` so
  artists can toggle on demand without committing.

## Phase L4 — Bake stack UI (cherry on top, ~2h)

Phases 1-3 give us the underlying ops. Phase 4 makes them obvious.

**Panel:**
- New `PS1MeshInstance` inspector subgroup `PS1 / Lighting` showing:
  - `BakedColors` status: "0 surfaces baked" / "3 surfaces, last
    operation: AO bake".
  - Per-step buttons: Clear, Bake From Scene Lights, Bake Directional
    (+ params), Apply Ambient (+ tint), Bake AO (+ params).
  - "Save as preset" / "Load preset" so artists can share lighting
    setups across props.

## Cross-references

- **Blender mirror:** the same conceptual ops live in
  `tools/blender-addon/.../operators/vertex_lighting.py`. Phase L1
  here is roughly the Blender add-on's `bake_directional`. Authors
  pick the side they want — both write through to the same byte
  range and respect the 0.8 cap.
- **Round-trip:** baked vertex colors travel Blender → GLB → Godot
  via the COLOR channel. The `BakedColors` per-surface override is
  Godot-side-only; Blender doesn't see it. That's deliberate — bakes
  computed in Godot from Godot scene lights have no Blender
  counterpart.
- **Slot C metadata:** `PS1MeshInstance.ShadingMode = VertexColor` is
  the wire-id signal that the renderer should consume vertex colors.
  Bakes here implicitly imply that mode; no extra metadata travels.

## Open questions

- Should the Godot bake support *light maps* in addition to vertex
  colors, for high-poly meshes where 8 KB/vertex is cheaper than 4
  bytes/triangle? Probably not — PS1 didn't ship lightmaps and the
  whole optimization story falls apart. Defer to "Phase L5+ if
  someone asks."
- Should L2's AO bake use raycasts against authored colliders only,
  or against the actual rendered geometry? Latter is more accurate
  but means temporary collision shapes per-bake. Default to authored
  colliders + a fallback that builds a ConvexPolygonShape3D per mesh
  when there's no scene-side collider.
