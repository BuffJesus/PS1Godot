# Custom boot logo — how authors add their own intro splash

A common question from authors coming from commercial PS1 games: *"how do
I make my game open with my own 3D logo — a company mark, a spinning
dog, whatever — the way THQ / Crystal Dynamics / Naughty Dog did?"*

Short answer: **author scene 0 as your splash**. psxsplash has no
dedicated "boot logo" subsystem; the game starts rendering at scene 0,
so scene 0 is your splash. This doc documents the convention, maps out
what exists today, and sketches a nicer plugin-side UX for anyone who
wants to build it.

## What already exists (and what doesn't)

### BIOS-level boot screens are not authorable

When a PS1 powers on it runs the **BIOS** (Sony's, or OpenBIOS when
using PCSX-Redux's `openbios.bin`). The BIOS is responsible for the
"Sony Computer Entertainment" chime + logo and the `Licensed by Sony`
text, then hands control to the disc's PS-X EXE (that's us, psxsplash).

- **Official Sony boot logo.** Baked into every PS1 ROM. Untouchable.
  Real-hardware discs ship a LICENSE region in their ISO that the BIOS
  reads to gate the logo — if it's missing the console shows an error
  screen. Irrelevant for PCdrv iteration; a Phase 3 concern for ISO
  builds.
- **OpenBIOS shell cube.** The spinning wireframe cube you see when
  PCSX-Redux boots with `openbios.bin` is the *OpenBIOS shell* (source
  at `psxsplash-main/third_party/nugget/shell/main.c`, see
  `c_modelVertices` / `c_modelQuads`). It's part of the fake "OS" layer
  before any game runs. Replacing it requires rebuilding OpenBIOS and
  shipping a modified `openbios.bin` alongside the game — not something
  a Godot author should have to do, and meaningless on real hardware
  (OpenBIOS isn't the retail BIOS).

Neither of these is psxsplash's to change. When we say "custom logo"
here we mean something the **game** shows — after the BIOS hands off.

### What psxsplash gives us today

Two relevant systems:

1. **`SceneManager::loadScene(0, isFirstScene=true)`** — called on
   boot, runs the normal render loop on scene 0. If scene 0 is a logo
   scene, the logo is what the player sees first.
2. **`LoadingScreen`** (`psxsplash-main/src/loadingscreen.{hh,cpp}`)
   — optional 2D UI shown *while a scene is loading*. Sprites + text +
   a progress bar, loaded from a `.loading` sidecar next to each
   `.splashpack`. **Pure 2D** — no 3D model support. Authored via the
   UI canvas pipeline (`PS1UICanvas` nodes).

There is **no dedicated 3D intro splash hook** in psxsplash. Which
means the entire "custom logo" concept, today, is an authoring
convention inside Godot rather than a runtime feature.

## The convention: scene 0 is your splash

All the machinery you need is already here.

### Minimum viable setup

1. In Godot, create a scene at `demo/intro.tscn` (or wherever). Add:
   - A `PS1Scene` root.
   - A `PS1Player` (even though the player doesn't move — the camera
     rig lives under it).
   - A `PS1MeshInstance` holding your logo mesh.
   - A `PS1Cutscene` with a camera-orbit track (existing Phase 2
     bullet 10 machinery) for a 3–5-second fly-around.
   - A `PS1AudioClip` with your intro sting; fired from the cutscene
     or from a Lua `onCreate` hook.
2. Set the plugin's scene list so `intro.tscn` is **scene 0**. Your
   real game becomes scene 1.
3. In a Lua script attached to the cutscene (or on a timer in
   `onUpdate`), when the intro finishes call
   `Scene.Load(1)` to transition into the real game.

That's the whole thing. psxsplash boots → scene 0 runs your logo
cutscene → Lua calls `Scene.Load(1)` → the game starts. Same machinery
that ships the two-room demo.

### What this costs

- **Disc space:** whatever your logo mesh + textures take. A
  low-poly dog mesh with a 64×64 texture is ~1–2 KB of splashpack;
  negligible.
- **VRAM during intro:** everything in scene 0 occupies VRAM until
  `Scene.Load(1)` swaps scenes. The scene swap fully unloads scene 0
  assets, so nothing leaks into the real game's budget.
- **Boot-to-gameplay time:** +N seconds of logo duration + one extra
  scene load. The extra scene load is fast because scene 0 is small.
- **SPU:** dedicate one of the per-scene audio clip slots to the
  intro sting; it unloads with the scene.

## A nicer UX: `PS1Splash` template

The convention above works but has setup friction — every author has
to assemble the same four nodes (Scene / Player / mesh / cutscene)
before they can drop in their logo. The plugin should ship a
preset that collapses this to one button.

**Proposed plugin addition (Phase 3 candidate):**

- Right-click in the FileSystem dock → `New → PS1 Splash Scene`.
- Generates a pre-wired scene with:
  - `PS1Scene` root with sensible defaults (short scene duration,
    black fog for fade-out).
  - `PS1Player` with its camera child set to an orbit rig pointed at
    origin.
  - An empty `PS1MeshInstance` slot labelled "LogoMesh — drop your
    model here".
  - A `PS1Cutscene` with a 5-second camera track and a Lua trigger at
    the end that calls `Scene.Load(<next_scene_index>)`.
  - A `PS1AudioClip` slot labelled "IntroSting — drop your sound
    here".
  - Inspector hint text on the `PS1Scene` root: "This is a boot
    splash scene. Add it as Scene 0 in the plugin's scene list, and
    put your real game at Scene 1+."

The author's whole workflow becomes: *create scene → drop a mesh →
drop a sound → done.* Zero boilerplate, still uses 100 % of the
existing runtime — no psxsplash changes needed.

Implementation notes for whoever builds this:

- Build via `EditorPlugin.AddCustomType` or a `.tres` scene template
  under `addons/ps1godot/templates/intro_splash.tscn`.
- Wire the camera track with keyframes already set so the rig orbits
  the origin once over 5 seconds (PS1Cutscene track types are already
  defined; this is pure authoring).
- The "transition to next scene" is a single-line Lua file
  (`Scene.Load(1)`) attached to the cutscene's onComplete, committed
  under `addons/ps1godot/templates/scripts/intro_end.lua`.

## Future: runtime-level 3D splash (optional)

Everything above happens inside scene 0's normal render loop. It
doesn't cover the window of time during which scene 0 *itself* is
loading from disc/PCdrv. For small splash scenes the load is
imperceptible; for a full intro cinematic with voice lines it could be
a few seconds of black screen.

If that ever matters (real-hardware builds with slow CD seeks, long
intro audio), a proper solution would extend
`psxsplash-main/src/loadingscreen.{hh,cpp}` to render an optional 3D
mesh alongside the existing 2D progress bar:

- Add `.splashmodel` format (minimal variant of `.splashpack` —
  header + one mesh + one anim track + one CLUT).
- Runtime loads and renders it on a minimal render path while the
  rest of scene 0 streams in.
- Author-side: a new node type `PS1SplashModel` exports to
  `.splashmodel` rather than folding into the scene.

This is a meaningful chunk of psxsplash work and should not block
anyone; the scene-0 convention above is the right answer today and
remains the right answer for almost all projects.

## TL;DR

- There is no psxsplash-side "boot logo" subsystem; scene 0 is it.
- Author scene 0 as a short intro cutscene, call `Scene.Load(1)`
  when it finishes — same nodes you'd use for any other cutscene.
- Ship a `PS1 Splash Scene` template as Phase 3 polish so authors
  don't re-assemble the four boilerplate nodes every time.
- Runtime-level 3D splash during scene 0 load is a future
  psxsplash extension — nice to have, not needed for the current
  pipeline.
