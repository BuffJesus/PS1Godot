# PS1 memory strategy — three tight budgets

Three separate memories on PS1 each have their own cap and their own asset
diet. Treat them independently; over-budget in one does not buy slack in
another.

| Memory     | Size    | Used by                                              |
|------------|--------:|------------------------------------------------------|
| Main RAM   | 2 MB    | code (~512 KB), heap, scene tree, BVH, animation, gameplay state |
| VRAM       | 1 MB    | front + back framebuffer (~300 KB combined), texture atlas, CLUTs |
| SPU RAM    | 512 KB  | ADPCM samples (~256 KB usable; rest is reverb + reserved) |

The exporter caps usable VRAM at 512 KB and SPU at 256 KB, matching what
the framebuffer + reverb leaves us. The dock budget bars (post-2026-04-26
fix) reflect those caps.

## Audio fights SPU RAM

- Short SFX → SPU. ~256 KB cap, including all gameplay-resident clips at once.
- Music / ambient loops / dialog / large stingers → XA (Phase 3). Streams from
  disc, ~zero memory cost.
- Title / credits → CDDA only when quality matters more than disc bandwidth.
- See [`ps1-audio-routing.md`](ps1-audio-routing.md) for the full bus table and
  per-clip routing API.

## Textures fight VRAM

- Default 4bpp; escalate to 8bpp only when needed. 16bpp is special-case.
- Pack by what renders together, not by what's similar.
- Share CLUTs across enemy/lighting/team variants.
- Bake one-off decals into world atlases instead of separate quads.
- Detailed format policy in [`ps1-texture-strategy.md`](ps1-texture-strategy.md).

## Meshes + level data + gameplay state fight main RAM

### Mesh format

- Compact binary in the splashpack (the existing `GameObject` + `PSXMesh`
  layout) — never ship OBJ / glTF / text into the runtime.
- Vertex positions are `psyqo::FixedPoint<12, int16_t>` — 6 bytes per vertex.
  Don't widen casually; precision is plenty for hand-tuned PS1 levels.
- Triangle indices: `uint8_t` for meshes ≤ 256 vertices, `uint16_t` otherwise.
  The current writer always emits 16-bit; trimming small props to 8-bit is a
  half-K saving per prop and lands well in the v26 pipeline pass.
- Skinned mesh vertex stride is 8 B (4 bone indices + 4 weights, all u8). See
  `psxsplash-main/src/skinmesh.hh`.

### Animation

- Prefer transform-track animations over baked vertex animation. The current
  pipeline already does this — `PS1Animation` tracks Position / Rotation / Active
  per target object, ~12 B per track + 8 B per keyframe.
- Skinned clips: bake at 30 fps, drop to 15 if the motion allows. 8-bone
  vertex weights are the runtime cap; rigging beyond that loses precision
  silently.

### Levels and rooms

- Split the scene into rooms / camera zones. The runtime already has an
  authored "room + portal" pass (Phase 2 bullet 12) — when bullet 12 lands,
  prefer portal-based culling over dumping everything into the BVH catch-all.
- Today's monitor scene has `4 authored rooms + 1 catch-all` per the export
  log; the catch-all carries 4460 of the 7000 tri-refs. Pull more geometry
  into authored rooms over time.

### Gameplay state

- Lua state lives in the same heap as runtime structs. `psxlua_per_script_env`
  isolation keeps script globals from leaking — see
  `MEMORY.md → project_psxlua_per_script_env.md`.
- Avoid per-frame allocations from Lua. Reuse tables; push numbers, not
  strings, where possible.

## Streaming + chunking

PSX has one CD drive — concurrent streaming and seeking is expensive.

- Splashpack = one read at scene boot. Big and resident is fine, slow but
  one-off.
- XA-ADPCM = continuous read at ~75 sectors/sec. Reserves ~half the disc
  bandwidth. Don't run XA + level streaming at the same time without
  budgeting.
- CDDA = entire disc bandwidth. Game can't read files while it plays.
  Title / credits territory only.

When the room/portal pass lands, the next streaming step is per-room
splashpacks (load on transition) — that's the unit of streaming the runtime
should aim for.

## What over-budget looks like

| Symptom                                  | Likely culprit                |
|------------------------------------------|-------------------------------|
| Audio cuts out mid-clip                  | SPU over budget at upload     |
| Texture mosaic on one mesh               | atlas crossed VRAM page edge OR 4bpp quantization too aggressive |
| Crash on splashpack load                 | mesh + collision + animation > main RAM heap |
| GPU prim list overflow / dropped tris    | ordering table too small for current view; reduce per-frame draws |
| CD drive thrashing / dropped audio frames | XA + file I/O conflict        |

## Cross-references

- Dock budget bars + per-bus caps: `godot-ps1/addons/ps1godot/ui/SceneStats.cs`
- Splashpack format + version: `psxsplash-main/src/splashpack.{hh,cpp}` (currently v25)
- Audio routing: [`ps1-audio-routing.md`](ps1-audio-routing.md)
- Texture / decal / alpha policy: [`ps1-texture-strategy.md`](ps1-texture-strategy.md)
- General improvement TODOs: [`psxsplash-improvements.md`](psxsplash-improvements.md)
