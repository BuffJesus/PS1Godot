# PS1 scene templates

Starting-point `.tscn` scenes for recurring full-scene shapes — the
ones you'd otherwise rebuild from scratch every time. These are
**scene templates**, not child templates (for the latter see
`addons/ps1godot/ui_templates/`).

Each is a hand-authored scene using only shipped `PS1*` nodes — no
hidden helpers, no scripts you can't read. Read them, copy them,
modify them.

## Available templates

| File | Purpose |
|------|---------|
| `empty_template.tscn` | Smallest valid scene — just `PS1Scene` + `PS1Player` + `Camera3D`. Boots black; build outward from here. |
| `demo_template.tscn` | Empty + a floor + 2 colored cubes + a HUD label. Smallest scene that's actually visually interesting on F5. |
| `menu_template.tscn` | Title screen / main menu — pure UI, no 3D gameplay. Title text + "PRESS START" prompt; advance to your game scene via `Scene.Load(1)` from Lua. |
| `gameplay_template.tscn` | Level scaffolding — `PS1Player` + floor + an `ExampleTrigger` box. Add walls, props, more triggers as needed. |
| `intro_splash_template.tscn` | Boot-logo splash screen. 3D spinning logo + studio name + "Licensed by …" text + chime + auto-transition to the main game. |

## Workflow

Godot 4.7 doesn't ship a dedicated "scene template" concept, so use
the standard duplicate-and-save flow:

1. In the FileSystem dock, right-click the template `.tscn` →
   **Duplicate…** → pick a path under your project (e.g.
   `res://your_game/intro.tscn`).
2. Open the duplicate and work from there. The original stays untouched.
3. When you're happy, open `Project Settings → Application → Run` and
   set **Main Scene** to your new scene.

Each template has a comment block at the top of the root node
listing the fields you need to fill in. The sections below walk
through them per-template.

## intro_splash_template.tscn

A PlayStation-1-era boot-logo splash: a 3D logo spins on a black
field, grey "Licensed by …" text below it, a chime plays, and after
5 seconds the splash transitions into your game. See the discussion
in `docs/custom-boot-logo.md` for the design rationale (short version:
the real Sony BIOS screens on retail hardware are untouchable, so
anything a game author can do is a scene-0 splash that *runs after*
the BIOS hands off).

### What's pre-wired

- `PS1Scene` root with black fog (so edges of the world don't leak
  in) and `SceneLuaFile =
  res://addons/ps1godot/templates/scripts/intro_splash.lua`
  (duplicate + edit this if you want splash logic beyond "play
  cutscene, then load scene 1").
- `PS1Player` at origin (required by the runtime for camera setup).
- `LogoMesh` — a `PS1MeshInstance` with no mesh assigned and red
  `FlatColor` so an empty preview is obvious.
- `IntroCutscene` — 150-frame (5-second @ 30fps) camera orbit +
  full-rotation spin on `LogoMesh` + a `PS1AudioEvent` firing the
  chime at frame 0.
- `BrandCanvas` — "Your Studio Name" centered near the top of the
  320×240 frame.
- `LicensedCanvas` — three-line "Licensed by / Your Studio Name / (TM)"
  block at the bottom, using `PS1Theme`'s `Neutral` slot for soft grey.
- `SplashSequenceSlot` — an empty `PS1MusicSequence` slot for authors
  who want a MIDI-driven chime instead of a one-shot sample.

### What you fill in

1. **Your logo mesh.** Select `LogoMesh` in the scene tree and drop
   a mesh onto its `mesh` property. Either:
   - A primitive (`BoxMesh`, `SphereMesh`, `CylinderMesh`) with a
     `ShaderMaterial` override, or
   - An `.fbx` / `.glb` scene instanced directly under the root
     (drag the file from FileSystem onto the scene tree). The
     exporter picks up raw `MeshInstance3D` nodes via the auto-detect
     path — no `PS1MeshInstance` wrapper needed for FBX imports.
2. **Your chime audio.** Select `Clip_SplashChime` (under the root
   `AudioClips` array) and set its `Stream` to an imported `.wav`.
   A short (< 2s) one-shot works best — think "jingle", not song.
3. **Your SubScenes[0].** Drag your game's main `.tscn` onto
   `SubScenes[0]` on the root node. That's what `Scene.Load(1)`
   transitions into on cutscene complete.
4. **Your text.** Replace the three PS1UIElement `Text` values
   (`BrandText`, `LicensedLine1`, `LicensedLine2`, `MarkLine`) with
   whatever you want to show. Keep `Text` under ~60 chars per element
   (runtime buffer is 64 B).
5. **Main Scene.** Project Settings → Application → Run → **Main
   Scene** = your duplicated splash.

### Optional: MIDI-driven chime

The default template uses a single-shot `PS1AudioEvent` firing the
`splash_chime` clip at cutscene frame 0. If you want a multi-note
chime driven by a `.mid`:

1. Author a short MIDI (say, 3-5 notes over 1-2 seconds).
2. Drop it into your project under `res://.../chime.mid`.
3. Edit the `SplashSequenceSlot` sub-resource: set `MidiFile` to your
   `.mid`'s path.
4. Edit `SplashChannelMelody.AudioClipName` to reference an instrument
   `PS1AudioClip` in your `AudioClips` array (the default template
   points it at `splash_chime`; you can add more clips and route
   different MIDI channels to different samples if your chime is
   polyphonic).
5. Call `Music.Play("splash_chime_seq")` from `intro_splash.lua` right
   before or alongside `Cutscene.Play(…)`. Optionally remove the
   `ChimeAt0` PS1AudioEvent if you don't want a sample sting overlaid
   with the sequence.

### What the player sees

```
 t=0s    black frame, chime starts, mesh appears at (0, 0.5, 0),
         camera swings in from (4, 2, 5), text fades in (well — "appears";
         no fade yet).
 t=2.5s  camera reaches (-4, 2, 5) (the mid-orbit point).
 t=5.0s  camera pulls back to (0, 3, 6), cutscene ends, Scene.Load(1)
         fires and your game starts.
```

### Customizing the flow

- **Longer or shorter splash:** change `IntroCutscene.TotalFrames`
  (30 = 1 second). Update the last keyframe `Frame` on each track to
  match.
- **Two-phase splash** (brand text → logo + legal text): author a
  second `PS1Cutscene` (say `IntroSplash_Phase2`) and chain it from
  `intro_splash.lua`'s `onComplete` callback. See the chained-cutscene
  comment in that script.
- **Different camera path:** edit the `CamPosTrack` / `CamRotTrack`
  keyframe `Value` vectors in the inspector. Rotation is in degrees
  per axis (XYZ euler); the runtime converts to PSX fp10.
- **No UI text:** delete the `BrandCanvas` and `LicensedCanvas`
  nodes. Pure 3D splash.

### What not to do

- **Do not copy Sony's wording verbatim** (`Licensed by Sony Computer
  Entertainment …` / `SCEE(TM)`) — that's a trademark. Use your own
  studio / publisher / region text.
- **Do not skip the chime** if you're targeting real hardware — the
  BIOS chime plays during disc load and your game then handing off
  to silence feels jarring. Even 0.5s of tone helps.
- **Do not put gameplay scripts** (input handling, etc.) on this
  scene. The template explicitly disables `Controls.SetEnabled(false)`
  so your player doesn't drift off-screen during the splash. Enable
  controls in the target scene's init script.
