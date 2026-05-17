# Texture authoring tooling — design + patch

Closes the Phase 3 roadmap bullet:

> - [ ] Texture import plugin (CLUT quantization warnings —
>       Phase 3)
> — `ROADMAP.md`

And contributes to `REF-GAP-6`:

> 6. **No texture reuse auditor.** Same image at different bit
>    depths produces duplicate atlas entries. CLUTs that are
>    near-duplicate consume separate slots.
> — `docs/ps1_large_rpg_optimization_reference.md`

Today, authors import a PNG into Godot, set the bit depth on a
`PS1MeshInstance`, hit Run-on-PSX, and discover what their
texture actually looks like *on the PSX*. The export quantizes
to 4bpp/8bpp/16bpp CLUTs, but authors don't see the result
until they're looking at the running game. Iteration cost on
"that texture looks wrong" is ~15 seconds per tweak.

This doc designs a Godot import plugin that does the
quantization at import time, shows a live preview in the
inspector, and warns about common authoring mistakes.

Drop this file at `docs/texture-authoring.md`.

## Goal

Author drops a PNG into Godot. Inspector shows:

- A side-by-side preview: original (left) vs PSX-quantized (right).
- Selected bit depth: 4bpp (16 colors) / 8bpp (256 colors) /
  16bpp (15-bit direct).
- Estimated VRAM cost.
- Warnings: too big for atlas, non-power-of-two, CLUT
  near-duplicate with another texture, low-color image
  forced to high bit depth.
- A "recommend bit depth" button that picks the smallest depth
  with visually-acceptable error.

What the author sees in the inspector is what the PSX renders.
No "preview is just Godot's nearest-filter approximation" gap.

Non-goal: real-time CLUT editing. Some PS1-era tools shipped
interactive palette editors. Worth doing eventually but huge
scope; v1 just quantizes and previews. Authors who need
custom palettes pre-process their PNG in an external tool
(GIMP, Aseprite) and import the already-palettized result.

## What's in place

- **`PSXTexture.FromGodotImage`** does the quantization today.
  Median-cut palette selection, nearest-color mapping, optional
  dithering. The quantization algorithm is correct; it just runs
  at export time, not import time.
- **`PS1TextureAnalyzer`** (`addons/ps1godot/tools/`) scans the
  project's textures and classifies them by bit depth and VRAM
  cost. Whole-project sweep, output as console log. The
  classification logic is the right primitive at the wrong UX
  surface.
- **Godot's `EditorImportPlugin`** is the extension point for
  custom import behavior. Built for exactly this use case
  (gltf, glb, tres, etc. all use it). Documented in Godot's
  GDExtension docs.
- **`PSXBPP` enum** (`TEX_4BIT` / `TEX_8BIT` / `TEX_16BIT`) is
  the bit-depth surface authors see today on `PS1MeshInstance`.

The work is in lifting the quantization to import time,
surfacing the result in Godot's inspector, and wrapping it in
authoring polish.

## Design

Three pieces: a custom import plugin, an inspector preview UI,
and a project-wide auditor.

### Custom import plugin

A `PS1TextureImporter` registered in `PS1GodotPlugin._EnterTree`
via `add_import_plugin`:

```csharp
public partial class PS1TextureImporter : EditorImportPlugin
{
    public override string _GetImporterName() => "ps1godot.texture";
    public override string _GetVisibleName() => "PS1 Texture";
    public override string[] _GetRecognizedExtensions() =>
        new[] { "png", "jpg", "jpeg", "tga", "bmp" };
    public override string _GetSaveExtension() => "ps1tex";
    public override string _GetResourceType() => "Texture2D";
    public override int _GetPresetCount() => 3;
    
    public override string _GetPresetName(int preset) => preset switch
    {
        0 => "4bpp (16 colors)",
        1 => "8bpp (256 colors)",
        2 => "16bpp (32K colors)",
        _ => "Default",
    };
    
    public override Godot.Collections.Array<Godot.Collections.Dictionary>
        _GetImportOptions(string path, int preset)
    {
        return new()
        {
            new() {
                { "name", "ps1/bit_depth" },
                { "default_value", PresetToBpp(preset) },
                { "property_hint", (int)PropertyHint.Enum },
                { "hint_string", "4bpp,8bpp,16bpp" }
            },
            new() {
                { "name", "ps1/dither" },
                { "default_value", true }
            },
            new() {
                { "name", "ps1/clut_strategy" },
                { "default_value", 0 },
                { "property_hint", (int)PropertyHint.Enum },
                { "hint_string", "Auto,Shared,Unique" }
            },
            // ... etc
        };
    }
    
    public override Error _Import(string sourceFile, string savePath,
        Godot.Collections.Dictionary options, ...)
    {
        var img = Image.LoadFromFile(sourceFile);
        var bpp = (PSXBPP)options["ps1/bit_depth"].AsInt32();
        var dither = options["ps1/dither"].AsBool();
        
        // Run the same quantization the exporter does.
        var psxTex = PSXTexture.FromGodotImage(img, bpp, sourceFile);
        
        // Render the quantized result back to an Image for preview.
        var quantizedImg = RenderQuantizedPreview(psxTex);
        
        // Save as a .ps1tex resource that wraps both the
        // quantized data and the original.
        var resource = new PS1TextureResource
        {
            Original = img,
            Quantized = quantizedImg,
            BitDepth = bpp,
            ClutSize = psxTex.ColorPalette?.Count ?? 0,
            VramCostBytes = EstimateVramCost(psxTex),
            Warnings = ValidateForPSX(psxTex, img),
        };
        ResourceSaver.Save(resource, savePath + ".ps1tex");
        return Error.Ok;
    }
}
```

`PS1TextureResource` is a `Resource` subclass that holds the
quantization output. `PS1MeshInstance` references it instead
of a raw `Texture2D`:

```csharp
[Export] public PS1TextureResource? Texture { get; set; }
```

When the user assigns a `PS1TextureResource` to a mesh, the
inspector shows the preview UI (next section). When they assign
a raw `Texture2D` (existing behavior), the importer runs at
export time with default options — backwards-compatible.

### Inspector preview UI

A custom `EditorInspectorPlugin` adds a preview panel when a
`PS1TextureResource` is selected. The panel shows:

**Top row: side-by-side preview.**

```
┌──────────────────┬──────────────────┐
│                  │                  │
│   Original       │   PSX Quantized  │
│   128×128        │   128×128        │
│   RGBA8          │   4bpp + 16 CLUT │
│                  │                  │
└──────────────────┴──────────────────┘
```

Both rendered at integer zoom (1×, 2×, 4×) with nearest-
neighbor scaling. A slider beneath lets the author zoom in to
inspect pixel-level differences.

**Middle row: stats + warnings.**

```
VRAM cost:     8,448 bytes (8 KB)
Atlas page:    1 page (64×256 of 4bpp)
CLUT entries:  16
Status:        ✓ OK

⚠ Color-banding visible in gradient regions; consider 8bpp.
⚠ CLUT entries 5 and 7 are visually similar — palette under-utilized.
```

Warnings are click-to-action where possible. The "consider 8bpp"
suggestion has a "Re-import at 8bpp" button that updates the
import options and re-runs.

**Bottom row: import options.**

The standard Godot import options (`Reimport` button, the
options the import plugin exposed). Authors flip the bit depth
and the preview updates immediately.

**Atlas placement preview.** A small toggle "Show in atlas
context" — when on, the preview replaces the quantized side
with the actual VRAM placement (which atlas page, which
neighbors). Useful for "why is this competing for tpage with
that other texture."

### Quantization preview rendering

The "PSX Quantized" preview needs to accurately show what the
GPU would render. This means:

1. **Quantize colors via the CLUT.** Each pixel's RGB gets
   mapped to the nearest CLUT entry. Already what
   `PSXTexture.FromGodotImage` does at export.
2. **Apply 15-bit truncation.** PSX VRAM is 16-bit but uses
   5 bits per channel + 1 transparency bit. Quantize to that
   resolution.
3. **Apply dithering** (if enabled in import options). The
   actual dithering pattern the PSX GPU applies — checkerboard
   distribution.
4. **Convert back to RGBA8** for Godot to display in the
   inspector.

Result: the preview is bit-accurate to what the PSX displays
(modulo affine-warp / vertex-jitter, which depend on geometry,
not texture).

### CLUT auditor

The `PS1TextureAnalyzer` already exists for project-wide
scans. Extend it to detect:

**Near-duplicate CLUTs.** Two CLUTs where most entries are
within a small RGB distance threshold. A common authoring
mistake: importing the same character at two different bit
depths (one for cutscene HUD, one for in-world rendering)
produces two distinct CLUTs that could be merged.

**Detection:** for each pair of 16-entry or 256-entry CLUTs,
compute the average per-entry RGB distance. If under threshold
(say, ΔE = 8 in CIELAB or simple Euclidean RGB), flag as
near-duplicate.

**Output:**

```
⚠ Near-duplicate CLUT detected:
  textures/hero_4bpp.png  (CLUT @ 0,256)
  textures/hero_hud_4bpp.png  (CLUT @ 16,256)
  Mean color distance: 6.2 (threshold 8.0)
  Action: [Merge to single CLUT]  [Ignore]
```

The "Merge to single CLUT" action edits both textures'
imports to share a CLUT, saving VRAM. (Implementation: a
shared CLUT identifier in the import options; the VRAM packer
places shared CLUTs once.)

**Over-quantization detection.** A 4bpp texture with only
6 distinct CLUT entries used could be 4bpp with a smaller
palette — but PS1 always allocates the full 16 entries per
4bpp CLUT, so this is purely informational. More useful: an
8bpp texture using fewer than 16 distinct colors — that's
3.7× VRAM waste, recommend 4bpp.

**Single-color regions.** A texture that's 90 % one color is
either intentional (a background card) or a mistake. Either
way, the author probably wants to know — auto-flag for
review.

Auditor surface: a "Texture Audit" tab in the dock alongside
VRAM viewer (`vram-viewer.md`). Shows the full list of
findings with click-to-resolve actions.

### Authoring polish

**Drag-and-drop.** Drop a PNG into Godot's filesystem. The
import plugin auto-runs with default options (8bpp). The
inspector shows the result immediately.

**Bit-depth recommendation.** A "Recommend bit depth" button
runs all three quantizations, computes per-pixel error against
the original, picks the smallest depth where mean error < a
configurable threshold.

**Power-of-two enforcement.** PS1 textures don't strictly
require power-of-two sizes, but cache behavior is much better
when they do. Warn on non-power-of-two with a "Pad to nearest
power-of-two" auto-fix.

**Dimensions cap.** A single primitive can sample a 256×256
texture max. Larger textures need splitting across multiple
primitives. The plugin warns at import time on >256×256, with
a suggestion to either resize or split manually.

**Live-update on source edit.** When the underlying PNG is
modified in an external editor (Photoshop / GIMP / Aseprite),
Godot's `EditorFileSystem` detects the change. The import
plugin re-runs with the same options. Preview updates without
author intervention.

## Implementation stages

Five stages. Each ships independently and adds value.

### Stage 1 — Custom import plugin with default options

The headline addition.

- `PS1TextureImporter` registered as an `EditorImportPlugin`.
- `PS1TextureResource` resource type holds quantized output.
- Default options (8bpp, dither on).
- Re-uses existing `PSXTexture.FromGodotImage` quantization.
- `PS1MeshInstance.Texture` property updated to accept
  `PS1TextureResource` (existing `Texture2D` path kept).

Verifiable: drop a PNG into the project, see a `.ps1tex` import
file appear, inspector previews the quantized result.

### Stage 2 — Inspector preview UI 🟡 partially shipped

`PS1TexturePreviewInspector` already attached to `PS1Sky` and
`PS1UIElement(Image)` from an earlier session. As of 2026-05-16
it shows source + PSX-quantized panels side-by-side with a
VRAM-cost stats line that matches the dock's budget bar math
(see `SceneStats.EstimateTextureVramBytes`).

Remaining for Stage 2:
- Atlas-page / CLUT-size context lines.
- Zoom slider.
- Import-option editing surface (waits on Stage 1's
  `EditorImportPlugin`).
- Extend coverage to `PS1MeshInstance` (currently skipped
  because mesh textures flow through material chains, not a
  direct field).

### Stage 3 — Warnings + auto-fixes

- Validate-on-import: dimensions, power-of-two, atlas fit.
- Warning display in inspector.
- One-click auto-fix actions where applicable.
- "Recommend bit depth" button.

### Stage 4 — Project-wide auditor

- Near-duplicate CLUT detection.
- Over-quantization detection.
- Single-color region detection.
- "Texture Audit" tab in dock with results + actions.
- Closes `REF-GAP-6`.

### Stage 5 — Shared CLUT support

- Import option `clut_strategy = shared` groups textures with
  the same source palette.
- Exporter respects the shared-CLUT marker and emits one CLUT
  instead of N.
- VRAM viewer reflects shared-CLUT entries.

This is the optimization-payoff stage — character sets and
texture variants that share a palette stop spending duplicate
VRAM.

## Open questions / tradeoffs

**Import time on large projects.** Re-quantizing every texture
on every Godot project open is expensive. Mitigation: the
quantized result is cached in the `.ps1tex` file; re-runs only
on source-PNG mtime change or option change. Same pattern Godot
uses for its existing import plugins.

**Preview accuracy on dithered output.** The PSX GPU applies
dithering at draw time based on screen position, not at
texture-fetch time. The preview shows pre-dithered texture
content, but the in-game appearance gets additional dithering
from the GPU. The two layers compose in a way that the preview
can't fully replicate. Document this; the preview is
accurate-enough-to-author-against, not bit-exact to the rendered
output.

**Backwards compatibility.** Existing scenes use raw `Texture2D`
references. Don't break them. Strategy: the `PS1MeshInstance.Texture`
property accepts either type; raw `Texture2D` goes through
import-time quantization at export. Authors migrate at their
own pace.

**Shared CLUT identification.** When two textures should share
a CLUT (palette swaps of the same character), how does the
author declare it? Options:

1. Auto-detect via duplicate-detection — fragile, requires
   high similarity threshold.
2. Explicit "shared CLUT id" string property — verbose but
   reliable.
3. Reference to a "CLUT resource" sub-asset — Godot-idiomatic
   but more clicks.

Default to option 2 (explicit string). Authors who write
`clut_id = "hero"` on multiple textures get them merged.
Auto-detect runs as a warning ("these look like they should
share a CLUT") but doesn't force the merge.

**Performance of the auditor.** A whole-project scan with
N² CLUT comparison is N² in texture count. For 500 textures,
that's 125K pairs — still fast. For 5000 textures, a million
pairs starts to lag. Mitigation: build a spatial-hash-by-
dominant-color index, only compare CLUTs in the same hash
bucket. Defer optimization until a project hits the limit.

**Edge case: textures > 256×256.** Splitting across multiple
primitives is non-trivial — the mesh's UVs need adjustment
too. The plugin can detect and warn but can't auto-fix
without modifying the mesh. Document the manual workflow:
split the PNG in an external editor, import each piece
separately, apply to different mesh surfaces.

**16bpp direct-color textures.** PS1 supports 16bpp without
a CLUT for photo-realistic content. Currently the runtime
handles them; the importer should preview them faithfully
(15-bit truncation, no CLUT mapping). Already handled by
`PSXTexture.FromGodotImage`; preview just needs to skip the
CLUT-related UI sections for 16bpp imports.

**Transparency.** PS1 supports binary transparency via a
specific palette entry value (`STP` bit). Authors who want
transparent regions need to mark a CLUT entry as transparent.
The importer should detect alpha-having sources and either
warn ("this PNG has alpha; PS1 supports only binary alpha")
or auto-map alpha < 128 to the STP entry. Default to
auto-map with a configurable threshold.

**Dither toggle defaults.** Default dither ON is the right
choice for general-purpose textures (text, sprites). For
specific use cases (clean cell-shaded art) authors want it
off. Per-import option, default on, easy to flip.

**Reverse: re-quantization round-trip.** Re-importing the
same texture should produce identical output. Critical for
build determinism. Verified by the host-mode tests
(`host-mode-testing.md`).

**Memory cost of preview images.** Holding both original and
quantized Image instances in `PS1TextureResource` doubles
texture memory at edit time. For a 50 MB project's worth of
textures, that's 50 MB extra in the editor — fine. For a
500 MB textures project, that's 500 MB extra — uncomfortable.
Mitigation: the quantized preview is computed on-demand from
the import data, not stored persistently. Display-side caching
holds only the visible preview's pixels.

**Importer ordering.** When a `.png` is also imported by
Godot's default Texture2D importer, both run. Each writes its
own resource. The Inspector then shows two options for the
file. Resolution: register the `PS1TextureImporter` with a
higher priority and a distinct file pattern (e.g.,
`textures/ps1/*.png` only, or detect a sibling `.ps1config`
file). Authors opt textures in by placing them in the right
folder.

## Suggested entries

### For `ROADMAP.md`

Replace the existing Phase 3 single-line bullet with:

> - [ ] **Texture authoring tooling — `PS1TextureImporter`.**
>       Custom Godot `EditorImportPlugin` runs quantization
>       at import time, surfaces preview in inspector. Side-by-
>       side original/quantized view, per-bit-depth comparison,
>       warning auto-fixes for common issues. Full design:
>       `docs/texture-authoring.md`.
> - [ ] **Texture audit tab in dock.** Whole-project scan for
>       near-duplicate CLUTs, over-quantization, single-color
>       regions. Click-to-resolve actions. Closes `REF-GAP-6`.

## Changelog

- `2026-05-11` — Document created. Thirteenth patch doc in
  the series. Closes the Phase 3 "texture import plugin"
  bullet and contributes to `REF-GAP-6`. Builds on the
  existing `PSXTexture.FromGodotImage` quantization logic.
