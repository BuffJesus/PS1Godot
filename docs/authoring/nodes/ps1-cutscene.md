# PS1Cutscene

A multi-track timeline played by the runtime's `CutscenePlayer`.
Compared to [`PS1Animation`](ps1-animation.md), supports multiple
tracks running in sync — author a camera move + object motion +
audio events as one coordinated cutscene.

<!-- SCREENSHOT: nodes/ps1-cutscene-inspector.png -->

## Where it goes

Anywhere under a [`PS1Scene`](ps1-scene.md). Typically one per
named cutscene (intro, level-end, mid-game scripted moments).

## Child structure

Children of type `PS1AnimationTrack` become the cutscene's tracks
at export time; each track has its own `PS1AnimationKeyframe`
children.

```
IntroCutscene (PS1Cutscene)
├── PS1AnimationTrack — Camera   (kind = Camera)
│   ├── PS1AnimationKeyframe (frame 0)
│   ├── PS1AnimationKeyframe (frame 60)
│   └── PS1AnimationKeyframe (frame 120)
├── PS1AnimationTrack — NarratorAudio (kind = Audio)
│   └── PS1AudioEvent (frame 30: clip = "narrator_line_1")
└── PS1AnimationTrack — CubeBob    (kind = Object, target = "Cube")
    └── PS1AnimationKeyframe (frame 60: position Y +0.5)
```

## Key fields

- **CutsceneName** — string Lua passes to start it
  (`Cutscene.Play("intro")`).
- **DurationFrames** — total length. Per-track frame counts must
  fit within this.
- **AllowInput** — whether the player can interact during the
  cutscene. Default `false` (most cutscenes are non-interactive).
- **OnFinishLua** — optional Lua callback fired when the cutscene
  ends.

## Workflows

- **Skip handling** — `Cutscene.Stop()` from Lua aborts the
  cutscene mid-play. Hook to a button press for "press X to
  skip."
- **Camera handoff** — at the end of a cutscene, the runtime
  smoothly hands control back to the player rig (or the next
  cutscene). The transition is handled by `CutscenePlayer`.
- **Audio sync** — for narrated cutscenes, use
  [`Audio.GetClipDuration`](../../lua-api/audio.md) to align
  the cutscene's FrameCount with the voice clip's length.

## Related

- [Lua API → Cutscene](../../lua-api/cutscene.md)
- [PS1Animation](ps1-animation.md) — single-track equivalent.
