# PS1Sky

Scene-level skybox. The runtime renders the sky texture as a
full-screen quad **before** the main scene OT, so 3D geometry
naturally over-draws it where present (the Crash / Spyro /
MediEvil pattern). Where geometry doesn't cover (open windows,
missing walls, the gap above mountains), the sky shows through.

<!-- SCREENSHOT: nodes/ps1-sky-inspector.png -->

## Where it goes

Direct child of [`PS1Scene`](ps1-scene.md). **Exactly one PS1Sky
per scene** — the exporter expects a single sky struct.

## Key fields

- **SkyTexture** — the source texture. Authored to fit a single
  TPage (256×256 or smaller for 8bpp); the exporter packs into
  VRAM at export time.
- **HorizonY** — the screen Y coordinate that becomes the horizon
  line. Default 120 (mid-screen for 320×240).
- **TintColor** — multiplied into the sky pixels at draw time.
  Lets you re-use a single sky across multiple scenes with
  per-scene atmosphere (e.g. dawn vs dusk tint of the same
  cloudscape).

## How it interacts with fog

The sky draws first; geometry over-draws; fog applies to geometry
but not to the sky pixels themselves. Pick a sky color that
*matches* the fog far color so the transition between geometry-
fading-into-fog and the sky beyond reads naturally. Mismatched
sky-and-fog colors produce a hard visible band at the far plane.

## Without a sky

A scene without a `PS1Sky` clears the background to the scene's
fog far color (or black if fog is disabled). Acceptable for
strictly interior scenes; required for any scene with a visible
horizon.

## Related

- [PS1Scene](ps1-scene.md) — owns fog settings that interact with
  the sky color.
- [`reference/splashpack-format.md`](../../reference/splashpack-format.md)
  — the 16-byte sky struct (v24+).
