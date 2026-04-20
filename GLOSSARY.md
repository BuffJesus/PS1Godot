# Glossary

PS1 and PSYQo-specific terms that show up constantly in this codebase. If a word
confuses you while reading psxsplash or SplashEdit, it's probably here.

## PlayStation 1 hardware

**GPU** — Not programmable. Draws textured/shaded triangles and rectangles into
a 1024×512 16-bit VRAM framebuffer. No Z-buffer, no perspective-correct texturing,
no mipmaps. Drawing order is managed by the OT (see below).

**GTE (Geometry Transform Engine)** — The PS1's coprocessor-2. Does fixed-point
3D math: matrix × vector, perspective projection, normal clip, lighting. The CPU
feeds it registers and reads results back. In PSYQo it's exposed via
`psyqo::GTE::…` headers. **All transform math on PS1 is GTE, not CPU float.**

**SPU (Sound Processing Unit)** — 24-voice ADPCM sampler with 512 KB of dedicated
sample RAM. Doesn't speak WAV/MP3; everything must be pre-converted to SPU-ADPCM
(4-bit, block-encoded). Looping is expressed by a flag bit in the encoded stream.

**VRAM** — 1 MB of video RAM organized as a 1024×512 grid of 16-bit pixels. It
holds *both* the framebuffers and all texture data simultaneously. Packing
textures into the leftover regions around framebuffers is a core authoring
constraint. See `splashedit-main/Runtime/TexturePacker.cs`.

**CD-ROM** — The game's file backing store. Seek times are brutal; streaming and
up-front loading are designed around that. `pcsx-redux` implements both a real
CD-ROM backend and a PC host passthrough (PCdrv).

## PS1 rendering concepts

**OT (Ordering Table)** — A linked list of draw primitives bucketed by depth.
The CPU builds it each frame; the GPU walks it back-to-front. Substitutes for
a Z-buffer. `OT_SIZE=N` in the psxsplash Makefile picks the bucket count —
bigger = finer depth resolution, more RAM.

**CLUT (Color Look-Up Table)** — A palette stored in VRAM. 4bpp textures use a
16-entry CLUT, 8bpp uses 256 entries. 16bpp textures ("direct color") skip the
CLUT. CLUT packing is a separate VRAM allocation problem from texture packing.

**TPage (Texture Page)** — A 256×256 region of VRAM that the GPU addresses as a
texture source. Each primitive references a TPage + CLUT + UV within the page.
TPage boundaries constrain atlas layout.

**Affine texture mapping** — The PS1 GPU interpolates UVs linearly in screen
space instead of perspective-correctly. Produces the classic warping/wobbling
when large polygons get close to the camera. Phase 1 preview must reproduce
this intentionally.

**Vertex snapping / jitter** — GTE outputs integer screen coordinates. Moving
vertices snap pixel-by-pixel instead of sub-pixel smoothly. The visible effect
is "jittery" geometry, especially on small meshes near the camera.

**Triangle subdivision** — To reduce affine-warp artifacts, large tris are
subdivided CPU-side before being sent to the GPU. `triclip.hh` in psxsplash
handles this.

## Data types you'll see constantly

**`psyqo::FixedPoint<12, T>`** — Q*.12 fixed-point number (12 fractional bits).
Positions, rotations, velocities, physics — everything that needs sub-integer
precision on PS1. Value `1.0` is stored as `4096`. The raw integer lives in
`.value`.

**`psyqo::GTE::PackedVec3`** — A 3-component vector in the format GTE registers
expect (typically three int16s packed into two registers). Used for positions,
normals, rotations.

**`psyqo::Trig::Angle`** — Angle in "PS1 units" where a full circle is `4096`,
not `2π`. Conversion from radians is `rad * 4096 / (2π)` — SplashEdit calls
this `PSXTrig.ConvertToFixed12`.

## PSYQo / toolchain

**PSYQo** — The modern C++ library for PS1 dev, living in
`pcsx-redux-main/src/mips/psyqo/`. Abstraction over GPU, GTE, SPU, CD-ROM,
controllers. psxsplash is built on it. **Not to be confused with PsyQ,** the
original Sony SDK from the 90s (which PSYQo deliberately distances itself
from).

**OpenBIOS** — Open-source replacement for the Sony BIOS, also in the pcsx-redux
tree. Lets you boot/debug without copyrighted BIOS images.

**mipsel-none-elf** — The MIPS little-endian bare-metal cross-compiler triple
used on Windows (Linux uses `mipsel-linux-gnu`). Installed via the `mips.ps1`
helper script.

**PCdrv** — A pcsx-redux extension that lets the emulated PS1 read files from
the host filesystem, bypassing ISO builds during iteration. Enabled via
`PCDRV_SUPPORT=1` in the psxsplash Makefile.

**mkpsxiso** — Builds a valid PS1 ISO from a file tree + `.xml` config. Needed
for real-hardware testing and release builds.

## splashpack format

**Splashpack** — The binary scene-data format SplashEdit produces and psxsplash
consumes. Currently **v20**, three files: `.splashpack` (scene), `.splashpack.vram`
(texture/CLUT/font pixels), `.splashpack.spu` (audio ADPCM). Magic `"SP"`,
header 120 bytes. See `docs/splashpack-format.md`.

**BVH (Bounding Volume Hierarchy)** — Spatial acceleration structure baked at
export time, used for frustum culling on exterior scenes. See
`splashedit-main/Runtime/BVH.cs` and `psxsplash-main/src/bvh.{cpp,hh}`.

**Room / Portal** — Alternative culling scheme for interior scenes: meshes are
tagged with a room ID, portals describe which rooms can see which other rooms.
Cheaper than BVH when geometry is dense and indoor. See `PSXRoom.cs`,
`PSXPortalLink.cs`.

**Nav region** — Walkable-surface data baked for AI pathfinding. SplashEdit
uses [DotRecast](https://github.com/ikpil/DotRecast) (the .NET port of Recast).
Runtime traversal is in `psxsplash-main/src/navregion.{cpp,hh}`.

**Interactable** — A component marking a game object as targetable by a "use"
action. Pure data in the splashpack; behavior lives in Lua.

**Trigger box** — An AABB that fires a Lua callback when the player enters it.

## Things that are NOT what they sound like

- **"Texture atlas"** in SplashEdit means a packed TPage worth of textures, not
  a generic texture atlas. Layout is constrained by 256×256 TPage boundaries.
- **"Dynamic collider"** means a collider baked per-object with a GameObject
  index, not a runtime-mutable collider. PS1 doesn't do full dynamic physics.
- **"Scene"** in splashpack is a level. Multi-scene games load one splashpack
  at a time, managed by `scenemanager.{cpp,hh}`.
