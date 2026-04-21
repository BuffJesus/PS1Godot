# PS1Godot 2026-04-20 — Sequenced music + interior scenes

First public snapshot. Phase 2 of the roadmap complete; Phase 3 polish
starting. You can now author a full PS1 scene in Godot — meshes with
CLUT-quantized textures, colliders, flat nav regions, interactive
objects, UI canvases, cutscenes, skinned meshes with baked animation,
**MIDI-driven sequenced music**, multi-scene packing, portal-culled
interior scenes, dialog-driven Lua scripts — and hit one button to see
it running on a real PS1 (or PCSX-Redux). Splashpack format at **v22**.

---

## ⚡ Quickstart

```
1. Grab PS1Godot-plugin-<version>.zip below.
2. Extract into your Godot .NET project's addons/ folder.
3. Grab psxsplash-runtime-<version>.zip, put psxsplash.ps-exe in
   godot-ps1/build/.
4. In Godot, enable the PS1Godot plugin (Project → Project Settings
   → Plugins), then click ▶ Run on PSX in the dock.
```

Full walkthrough: **[QUICKSTART.md](../QUICKSTART.md)**. Build your own
scene from scratch: **[docs/tutorial-basic-scene.md](tutorial-basic-scene.md)**.

---

## 🎵 What's new — sequenced music

The tentpole feature of this release. A complete MIDI → PS1 SPU music
pipeline built on top of the existing psxsplash audio stack.

- **Drop in a `.mid` + short instrument WAV samples** (bass tone, pad,
  lead, kick, snare, hat — as many as you want up to 8 SPU voices).
- **Create a `PS1MusicSequence` resource**, bind each MIDI channel to
  an audio clip with a base note + volume + pan.
- **Per-note routing for drum kits**: one channel per drum, filtered
  to a specific MIDI note (36 = kick, 38/40 = snare, 42 = hat).
  Percussion mode disables pitch shifting.
- **From Lua**: `Music.Play("track_name", volume)`, `Music.Stop()`,
  `Music.SetVolume(v)`, `Music.GetBeat()`, `Music.IsPlaying()`.
- **Voice reservation**: music claims 6 SPU voices for its lifetime;
  dialog/SFX use the other 18. Dialog can never steal a held music
  note mid-sustain.
- **Automatic ducking**: dialog scripts drop music volume on show,
  restore on hide. No more narration fighting the BGM.
- **12th-root-of-2 pitch table** (precomputed, 168 B rodata, no libm)
  so one short sample covers a whole melodic range.
- **Binary format documented** in
  [docs/sequenced-music-format.md](sequenced-music-format.md).

Most retro-game-music authoring flows — SEQ/VAB conversion, external
tooling — go away. Export MIDI from your DAW, pick samples, done.

## 🏠 Phase 2 bullet 12 — interior scenes (rooms + portals)

Authored via `PS1Room` (volume bounds) + `PS1PortalLink` (connect two
rooms through a portal surface). Exporter assigns triangles to rooms
by vertex-majority containment; runtime culls everything you can't see
through the portal you're looking at. Demo ships two rooms connected
by a portal link you can walk through and see the culling in action.

## 🌐 Multi-scene + teleports

`PS1Scene.SubScenes` packs additional scenes into the same splashpack.
`Scene.Load(N)` Lua API swaps between them mid-game. Demo includes a
"checkered realm" sub-scene reachable via a teleport trigger — a
pattern for boss arenas, dream sequences, region transitions,
menus-as-scenes.

## 🎬 Cutscene rotation convention — finally solved

Reproduced + diagnosed + fixed the "camera points wrong direction"
bug in the cutscene timeline track. Root cause was on the exporter
side: `psyqo::Angle` is `FixedPoint<10>` in **fractions of Pi**
(1.0 pi-unit = 180° = 1024 raw fp10), not fp12 degrees. Every
authored angle was being doubled. Documented the matrix convention
so future cutscene authors can rely on "raw Vector3 euler → PSX"
making sense. See [docs/psxsplash-improvements.md](psxsplash-improvements.md)
entry N+1 for the postmortem.

## 💬 Dialog system overhaul

- **Single-line-per-press** model replaces the old multi-line
  auto-advance that could wedge and leave dialogs stuck on screen.
- **Audio-aware auto-hide** via new `Audio.GetClipDuration(name)` Lua
  API: dialog boxes stay up for the full voice-clip duration, not a
  fixed 1.5-second timer. Silent/missing clips fall back cleanly.
- **Music ducking** coordinated via a shared `bgmMasterVol` global
  so all scripts restore the same target volume.

## 🎨 Authoring conventions

- **Godot 4.4+ WAV import fix**: `[importer_defaults]` in
  `project.godot` overrides the default QOA compression to raw PCM.
  New WAV drops work with the exporter out of the box; you no longer
  need to click into Import tab per file.
- **Audio clip residency** flag (`Gameplay` / `MenuOnly` /
  `LoadOnDemand`) carried forward — only gameplay-resident clips count
  against the SPU budget bar.

---

## 🛠️ Bug fixes you don't need to know about but might as well

- **MIDI parse sort order**: NoteOff must fire before NoteOn at the
  same tick. Cost 169 silenced snare hits in the demo song until
  found. Documented in `MidiParser.cs`.
- **Tempo conversion**: runtime's `m_dt12 = 4096` means one 30 fps
  frame, not 60. Music played at half speed until corrected.
- **Pitch table accuracy**: iterative `rate = (rate * NUM) / DEN`
  accumulates 1-2% error per step in fp14 — wildly wrong by the
  third semitone. Precomputed table is the right answer.
- **Portal-culling safety fallback**: upstream psxsplash has an
  `else { render all rooms }` when the camera is outside any room.
  Hid the feature during testing. Local patch removes the fallback;
  tracker entry filed for upstream consideration.

---

## 📦 Splashpack format v22

Bumped from v21 (no compat shim — old splashpacks fail the version
assert). Header tail +8 bytes for music table count + offset. New
section: 24-byte `MusicTableEntry` rows pointing at PS1M blobs.

Full layout: [docs/splashpack-format.md](splashpack-format.md).
PS1M blob layout: [docs/sequenced-music-format.md](sequenced-music-format.md).

**If you were on the previous snapshot**, re-build the runtime
binary (or grab the runtime zip below) so it matches the exporter.
The two drift apart silently otherwise.

---

## 📂 What's in each download

### `PS1Godot-plugin-<version>.zip` (≈1 MB)

Drop into your Godot .NET project's `addons/` folder.

- All custom nodes: `PS1Scene`, `PS1MeshInstance`, `PS1SkinnedMesh`,
  `PS1Camera`, `PS1Player`, `PS1AudioClip`, `PS1MusicSequence`,
  `PS1MusicChannel`, `PS1TriggerBox`, `PS1UICanvas`, `PS1UIElement`,
  `PS1Animation`, `PS1Cutscene`, `PS1Room`, `PS1PortalLink`,
  `PS1NavRegion`, `PS1Theme`
- Splashpack exporter (C#, targets format v22)
- MIDI parser + PS1M serializer
- PS1 spatial shader + compositor effect for the in-editor look
- Dockable panel with scene-budget bars + one-click Run-on-PSX
- PS1Lua GDExtension (prebuilt Windows x86_64 DLL included)
- UI prefab templates (dialog box, menu list, HUD bar, toast)

### `psxsplash-runtime-<version>.zip` (≈2 MB)

Drop `psxsplash.ps-exe` into `godot-ps1/build/`.

Prebuilt MIPS binary of psxsplash with our local patches applied
(music sequencer, voice reservation, v22 format, portal-culling
safety-fallback removed). Saves you from installing the MIPS
toolchain if you just want to try the demo or author content.

The runtime and the exporter must stay in lock-step on splashpack
format. If you upgrade one, upgrade the other.

---

## 🎯 Known limitations

- **Windows-only** launch scripts (`scripts/*.cmd`). Linux/macOS should
  work — all the tooling is cross-platform — but the bootstrap
  scripts are .cmd. PRs welcome.
- **GDExtension binary ships only for Windows x86_64**. Linux / macOS
  / ARM users have to rebuild from `addons/ps1godot/scripting/` with
  SCons + godot-cpp. Tracked for future releases.
- **Non-flat nav regions** (ramps, stairs with variable slope) still
  only partially handled; DotRecast auto-gen is the big gap. Phase 2
  bullet 7.
- **MIDI CC events** (volume / pan / expression / pitch bend) are
  parsed but ignored. Authored mix automation doesn't translate
  through to runtime yet — per-track mix is set on the binding
  resource instead.
- **Tooling has sharp edges.** The MIDI / sample workflow is
  functional but bare; see [ROADMAP.md](../ROADMAP.md) § Music
  authoring experience for the Tier 1 → 3 polish plan.

---

## 📚 Where to go next

- **[QUICKSTART.md](../QUICKSTART.md)** — the 15-minute install + run
  walkthrough.
- **[docs/tutorial-basic-scene.md](tutorial-basic-scene.md)** — build
  a demo-shaped interactive scene from scratch.
- **[ROADMAP.md](../ROADMAP.md)** — full feature checklist, Phase 2.5
  / 2.6 / 3 plans.
- **[docs/splashpack-format.md](splashpack-format.md)** — binary format
  reference (structs, offsets, version history).
- **[docs/sequenced-music-format.md](sequenced-music-format.md)** —
  PS1M format + authoring conventions.
- **[docs/psxsplash-improvements.md](psxsplash-improvements.md)** —
  tracker for upstream-candidate patches (entry N+7 is the music
  sequencer; N+1 is the cutscene-angle postmortem).

---

## 🙏 Credits

Built on **[psxsplash](https://github.com/psxsplash/psxsplash)** (the
PS1-side runtime — consumed mostly unchanged, patches tracked in
`docs/psxsplash-improvements.md`), **[PCSX-Redux](https://github.com/grumpycoders/pcsx-redux)**
(emulator + toolchain host), **[Godot](https://godotengine.org/)**
(editor platform), and a chunk of code directly inspired by
**[SplashEdit](https://github.com/psxsplash/splashedit)**'s Unity
exporter architecture. Tex-pack / VRAM / BVH ports follow the
SplashEdit file structure closely.

Demo song "RetroAdventureSong" composed by the project author. Sample
bank: *Mitch's Music Kit* (used for the test demo only — not
redistributed in the plugin zip).

🤖 **Disclosure**: the sequenced music pipeline, the runtime patches,
most of the new exporter code, and this release write-up were built
with Claude Code over a single extended session. The demo MIDI is
genuinely human-authored. Architecture choices are best-effort, not
gospel — file issues or PRs if something looks off.

---

## 📝 Commit range

```
9d6cdb7 tool: scripts/build-release.py — zip plugin + runtime
0a5b6cc docs: QUICKSTART + basic-scene tutorial + gitignore
7df8976 docs: sequenced music format, workflow plan, handoff update
02edbe7 tool: MIDI inspection + RPP sample extraction + sample conversion
b64e2b8 feat(demo): wire RetroAdventureSong BGM + dialog ducking + audio-aware auto-hide
225e791 feat(exporter): MIDI → PS1M music export, splashpack v22, music resources
7c90a55 feat(runtime): sequenced music — PS1M sequencer + voice reservation + Music Lua API
8673ca2 fix(realm): cube interaction — add Dialog canvas + realm-specific script
c26410b fix(realm): scene scripts use onSceneCreationEnd, not onCreate
```

…plus the prior session's handoff, cutscene rotation fix, multi-scene
support, and interior rooms/portals. Full log:
[main branch on GitHub](https://github.com/BuffJesus/PS1Godot/commits/main).
