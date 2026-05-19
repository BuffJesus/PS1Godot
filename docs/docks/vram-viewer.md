# VRAM Viewer

Under **PS1 Authoring → VRAM Viewer**. Renders the PS1's 1024×512
16-bit VRAM grid as it stands at the end of the most recent export.

<!-- SCREENSHOT: docks/vram-viewer.png — post-F5, demo scene's packed atlases + CLUTs + framebuffer reserves -->

## What it shows

Three overlaid layers on the 1024×512 grid:

- **Reserved regions** — the framebuffer pair (top-left), the UI
  font area, anything else that's off-limits to texture packing.
  Drawn with a hatched overlay so it's visually distinct from
  packed content.
- **Atlas footprints** — each TPage your textures got packed into,
  colored by bit depth (4bpp / 8bpp / 16bpp) so you can see at a
  glance which TPages have headroom.
- **Per-texture sub-rects** — outlines within each atlas showing
  where individual textures landed.

CLUT strips render as thin colored bars along their VRAM rows;
typically 16 or 256 colors wide.

## What to look for

- **Sparse atlases** — an atlas you can pack more textures into.
- **Single-use offenders** — a texture that pulled in its own TPage
  because its format / size didn't fit any existing one. Often a
  resolution-by-1 culprit (`257×128` forces a new TPage when
  `256×128` would have fit).
- **Free space** — the unhatched, untextured regions. If you're
  under 60% VRAM utilization, there's room for higher-quality art.

## Refresh behavior

Updates whenever an export runs. There's no separate Refresh
button — same coupling as Doctor. The
[`PS1Godot panel`](ps1godot-panel.md#vram-thumbnail)'s VRAM
thumbnail jumps you here on click.

## Future affordances

The MVP is a static snapshot. Pending follow-ups (see ROADMAP):

- **Multi-scene picker** — flip between sub-scenes' VRAM layouts
  without re-exporting each.
- **Hover tooltips** — per-texture details on cursor hover.
- **Per-TPage zoom** — drill into a single 256×256 atlas at high
  resolution.

## Related

- [PS1 Doctor](doctor.md) — VRAM-category warnings from the same
  export.
- [`reference/splashpack-format.md`](../reference/splashpack-format.md)
  — the VRAM payload's on-disk shape.
