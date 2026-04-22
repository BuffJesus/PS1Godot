# Bundled fonts

## VT323

Proportional pixel-style face, reads cleanly at 10–16 px on the PSX.
Used by the intro splash demo and the splash template as the brand-text
font. Licensed under the SIL Open Font License 1.1 (see `OFL.txt`);
redistribution is permitted as part of this plugin.

**Upstream:** https://fonts.google.com/specimen/VT323

## Using a font at author-time

Godot + the PS1Godot plugin rasterize the TTF into a 256×N-wide glyph
atlas once, during authoring:

1. Open Godot with the plugin enabled. The C++ GDExtension under
   `addons/ps1godot/scripting/` must be built (`scons` from that
   directory) — otherwise the tool menu entry below is a no-op.
2. FileSystem → select `addons/ps1godot/fonts/vt323_font.tres`.
3. Project menu → Tools → **"PS1Godot: Generate bitmap for selected
   PS1UIFontAsset"**.
4. The resource's `Bitmap`, `GlyphWidth`, `GlyphHeight`, and
   `AdvanceWidths` fields get populated and saved back to the `.tres`.

Any `PS1UIElement` with `Type = Text` and this asset in its `Font`
slot will now render with VT323 on PSX. The exporter packs the atlas
to 4bpp at export time and the runtime uploads it to VRAM at
(960, 0) on first scene load.

## Adding another font

Same pattern: drop a `.ttf` / `.otf` into this directory (or anywhere
under `res://`), create a `PS1UIFontAsset` resource pointing at it,
set `FontSize` to your target PSX pixel height, run the tool menu.
Maximum 2 custom fonts per scene — the runtime has 2 VRAM slots
above the system font. A third raises a clear exporter error.

Recommended pixel-oriented fonts (all OFL):
- **Silkscreen** — designed for 8 px, great for tiny HUD labels.
- **Press Start 2P** — chunky 8×8 arcade style.
- **Pixelify Sans** — cleaner modern pixel sans, close to late-PSX vibe.

Avoid general-purpose TTFs (Arial, Helvetica, etc.) at small sizes:
the alpha-threshold cutoff during rasterization throws out the
anti-aliasing they rely on, leaving mushy glyphs. Fonts authored
*as pixel fonts* always look better.
