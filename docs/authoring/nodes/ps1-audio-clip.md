# PS1AudioClip

An audio resource (not a scene-tree node — a `Resource` you
reference from `PS1Scene.AudioClips`). Declares the source `.wav`
plus which PS1 backend the runtime should route it through.

<!-- SCREENSHOT: nodes/ps1-audio-clip-inspector.png -->

## Where it goes

Created as a `.tres` file under `res://`, then referenced from a
[`PS1Scene`](ps1-scene.md)'s **AudioClips** array. Multiple
scenes can share a single `.tres`.

## Key fields

- **ClipName** — the string Lua passes to play this clip
  (`Audio.PlaySfx("door_creak")`).
- **Source** — the source `.wav` resource. Sample rate gets
  resampled at import; bit depth gets ADPCM-encoded for SPU
  clips.
- **Route** — `SPU` / `XA` / `CDDA` / `Auto`. SPU is the in-VRAM
  channel for short clips. XA streams from disc (~minutes-long
  music, voice acting). CDDA is Red Book audio at the end of the
  disc (highest quality, fixed track slots).
- **Residency** — when the runtime keeps the clip's bytes loaded:
  `Always` / `GameplayOnly` / `MenuOnly` / `OnDemand`.

`Auto` route asks the exporter to pick based on size + duration
heuristics — see [Audio Routing dock](../../docks/audio-routing.md)
for what it resolves to.

## Workflows

- **Drop-in import** — drop a `.wav` into the FileSystem dock,
  right-click → **New Resource → PS1AudioClip**, set the wav as
  Source. The auto-importer (`PS1WavDropHelper`) can also create
  these for you on .wav drops.
- **Audition** — use the **Play** button in the
  [Audio Routing dock](../../docks/audio-routing.md) to hear the
  SPU-encoded version before exporting.
- **Volume / pan** — runtime parameters on `Audio.PlaySfx`, not
  baked into the clip. The clip holds source bytes; loudness
  decisions are call-site.

## Route status

| Route | Implementation |
|---|---|
| SPU | Fully implemented. |
| XA | Scaffolded (runtime logs "not implemented" + falls back to silence). Phase 3 streaming. |
| CDDA | Fully implemented; track number assigned at ISO build. |

## Related

- [Audio Routing dock](../../docks/audio-routing.md)
- [Lua API → Audio](../../lua-api/audio.md)
- [`authoring/audio/routing.md`](../audio/routing.md)
