# Audio Routing

Under **PS1 Authoring → Audio Routing**. Inventory of every audio
clip in the active scene with its resolved playback route.

<!-- SCREENSHOT: docks/audio-routing.png — clip list with SPU/XA/CDDA badges + sample rate + size -->

## What it shows

A table row per `PS1AudioClip` in the active scene, with columns
for:

- **Name** — the clip name, as referenced from Lua.
- **Route** — SPU / PS2M / CDDA / XA, with a badge color matching
  the route's bus.
- **Sample rate** — Hz, post-resampling.
- **Size** — bytes after ADPCM compression for SPU clips, raw for
  CDDA / XA.
- **Residency** — when the runtime keeps the clip in memory:
  `Always`, `GameplayOnly`, `MenuOnly`, `OnDemand`.

The route column shows what the exporter **will actually pick**,
not what the resource declares. If a clip is marked `Auto`, the
column shows the resolved route with an "(auto)" annotation —
mirrors the `SceneCollector.ResolveAudioRoute` heuristic so you
catch the "I marked it Auto and got XA but expected SPU" surprise
at edit time, not after export.

## Auditioning

Each row has a play button. SPU clips play directly through
Godot's `AudioStreamPlayer` for a real-time preview. The other
three routes (PS2M sequenced music, CDDA, XA) need the PS1
runtime to play — the buttons are present for parity but
disabled, with tooltips explaining why.

## When to use it

- **Before an export** — confirm clips you expect on SPU aren't
  silently routing to CDDA because they crossed the SPU budget.
- **After a clip rename** — quick scan that every clip's name
  matches what your Lua scripts reference (typos here surface
  here too, since renames don't propagate to string literals).
- **During SPU optimization** — spot the largest clips and
  candidates for moving to XA or shortening.

## Related

- [`authoring/audio/routing.md`](../authoring/audio/routing.md)
  — the conceptual model behind the four routes.
- [PS1 Doctor](doctor.md) — SPU-category warnings when the live
  SPU budget would overflow.
