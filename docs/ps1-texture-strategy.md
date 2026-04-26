# PS1 texture / decal / alpha / VRAM strategy

PS1 VRAM is 1 MB total but only ~512 KB usable for textures (the back half
holds the double-buffered framebuffer + system rects). Treat it like a
fixed-size box you keep packing until something falls out.

## Format policy by content type

| Content                          | Default bpp | When to escalate to 8bpp     |
|----------------------------------|-------------|------------------------------|
| World walls / floors             | 4bpp        | Skin tones / gradients in hero hallway materials |
| UI / fonts / message boxes       | 4bpp        | never (UI palette is small) |
| Decals (blood, sigils, graffiti) | 4bpp cutout | never                        |
| Sprites (icons, particles)       | 4bpp        | gradient-heavy effects       |
| Character skin atlases           | 4bpp first  | If face/clothing is washed out (e.g. Kenney humanoid) |
| Portrait / cutscene stills       | 8bpp        | only for the few that need it; budget separately |
| Skybox                           | 4bpp        | never (read once at depth) |

**16bpp is reserved.** Use it only for one-off splash / title art that lives
in VRAM for one screen and gets evicted before gameplay. Any 16bpp gameplay
texture is a bug; the validator should warn.

## CLUT / palette discipline

- Group related sprites under a shared 16-color (4bpp) or 256-color (8bpp)
  CLUT. Every distinct palette eats a 32-byte (4bpp) or 512-byte (8bpp) slot
  in the VRAM CLUT region.
- Palette swaps are free variation — store one texture, swap CLUTs for
  enemy/team/lighting variants.
- CLUT[0] = `0x0000` is hardware transparent regardless of "alpha mode" —
  the cheapest cutout transparency on the system. Used today by bezel,
  scanlines, sky, every horror decal.

## Atlas grouping

Pack textures by *what renders together*, not by what's *similar*:

```
tpage_world_<scene>      walls + floor + props that share rooms
tpage_ui_<canvas>        fonts + bezel + HUD plates
tpage_character_<rig>    body + face + accessory of one rig
tpage_decals_<scene>     blood/cracks/cobwebs that share a 4bpp CLUT
tpage_fx                 sparks + smoke + impact + particles, palette-swappable
```

Avoid one-texture-per-page. Avoid texture pages that span multiple feeds /
rooms unless a single GameObject spans them.

## Decal pipeline

- Decals are textured quads with 4bpp CLUT[0]=transparent. Don't use modern
  projected decals (no PSX equivalent at reasonable cost).
- Bake into world atlases when reuse < 2 — a one-off graffiti piece on one
  wall belongs in the wall texture, not as a separate quad.
- AI-generated decals from `gen_horror_assets.py` etc. tend to ship content
  flush with the canvas edge. `scripts/py/recenter_decals.py` bbox-crops +
  pads to 12% margin; run before export when a decal looks "cut off".
- Hard cap: ~6 overlapping alpha quads in any one screen-space region.
  More than that and the SPU's polygon-blender rate falls off.

## Alpha modes (proposed metadata for v26+)

```
Opaque         -- standard textured tri
Cutout         -- CLUT[0]=transparent; PSX hardware free
SemiTransparent -- 0.5 src + 0.5 dst (single PSX hardware mode); 2-pass for ~25%
Additive       -- 1.0 src + 1.0 dst (hardware mode 1)
Subtractive    -- 1.0 dst - 1.0 src (hardware mode 3)
UI             -- explicit UI compositor path
```

Today the runtime supports a per-element / per-mesh `Translucent` boolean
that triggers the 2-pass darken (ends at ~25% destination). A future
metadata bump can expose the explicit modes — most assets only ever
need Opaque or Cutout.

## Stacking rules (avoid these patterns)

- Big near-camera transparent quads (UI overlays, screen-space fades).
  Each pixel costs as much as opaque + a destination read.
- > 6 decal layers in one floor patch.
- Particles where every quad is semi-transparent and no two share a CLUT.
- Splash / cutscene stills resident during gameplay.

Any of these will surface as VRAM bandwidth stalls or visible flicker on
real hardware before the budget meter complains.

## Validation scaffold (TODO)

A future build step should emit per-asset rows like:

```
asset                       w×h    bpp  CLUT  est_VRAM  atlas         alpha       residency
walls_hallway.png         128×128  4    A     8.5 KB    world_hall    Opaque      always
crt_bezel.png             256×240  4    B     31.0 KB   ui_bezel      Cutout      always
humanMaleA.png            256×256  8    C     65.5 KB   char_kenney   Opaque      scene
graffiti_run.png          128×64   4    D     4.5 KB    decal_horror  Cutout      scene
title_card.png            320×240  16   -     153.6 KB  cutscene      Opaque      menu
```

…with WARN lines on:

- 16bpp marked anything but `cutscene/menu` residency
- size > 256×256 (page max) — exporter already auto-downscales but flag the source
- alpha-heavy decal at 8bpp where 4bpp would do
- duplicate texture imported by 2+ atlases

`SceneStats.cs` already does the cap-vs-estimate display; extending it with
the per-asset table is a half-day of work and lands well alongside the
audio routing report.

## Skybox

`PS1Sky.Texture` should be 4bpp 256×N. Tint via `Sky.tintR/G/B` for night/day
moods instead of shipping multiple skies. The renderer pans on yaw + slow
drift — make sure the texture seams horizontally.

## Fonts

Built-in `vt323` font pages into VRAM at (960, 0) at 8×12 cells, 4bpp,
4608 B. Custom fonts compete for the same residency slot — max 2 distinct
custom fonts per scene per current runtime (uisystem cap). Use the built-in
unless the look is non-negotiable.

## Cross-references

- VRAM cap + dock budget bars: `godot-ps1/addons/ps1godot/ui/SceneStats.cs`
- Atlas packer: `godot-ps1/addons/ps1godot/exporter/TexturePacker.cs`
- Runtime textured-tri pipeline: `psxsplash-main/src/renderer.cpp`
- Memory note on CLUT[0]=0 hardware transparency: `MEMORY.md` →
  `project_psx_clut0_transparency.md`
