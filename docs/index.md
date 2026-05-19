# PS1Godot

Author PlayStation 1 games in Godot.

**Platforms:** Windows · Linux · macOS

PS1Godot is a Godot 4.x editor plugin (C# / .NET) that lets you design PS1
scenes in Godot and export them to the [psxsplash](https://github.com/psxsplash/psxsplash)
runtime, which runs on real PS1 hardware and in
[PCSX-Redux](https://github.com/grumpycoders/pcsx-redux).

It is a Godot-native rethink of
[SplashEdit](https://github.com/psxsplash/splashedit) (Unity). Same binary
format, different editor, deliberately better UX.

## Where to start

!!! tip "New here?"
    [Install the plugin](https://github.com/BuffJesus/PS1Godot/blob/main/SETUP.md){ target="_blank" } →
    [Run the demo](https://github.com/BuffJesus/PS1Godot/blob/main/QUICKSTART.md){ target="_blank" } →
    [Build your first scene](tutorial-hello-cube.md).

- **Getting started** —
  [Setup](https://github.com/BuffJesus/PS1Godot/blob/main/SETUP.md){ target="_blank" } ·
  [Quickstart](https://github.com/BuffJesus/PS1Godot/blob/main/QUICKSTART.md){ target="_blank" } ·
  [Hello cube tutorial](tutorial-hello-cube.md) ·
  [Basic scene tutorial](tutorial-basic-scene.md)
- **Authoring** — [Fixed cameras](fixed-camera-authoring.md) ·
  [Audio routing](ps1-audio-routing.md) · [UI canvas](ui-ux-plan.md) ·
  [Custom boot logo](custom-boot-logo.md)
- **PS1 Graphs** — [Dialogue](ps1graph-dialogue-authoring.md) ·
  [FSM](ps1graph-fsm-authoring.md) · [Quest](ps1graph-quest-authoring.md) ·
  [Behavior Tree](ps1graph-bt-authoring.md)
- **Reference** — [Splashpack format](splashpack-format.md) ·
  [API showcase](api-showcase.md) · [Lua cheatsheet](lua-ps1-cheatsheet.md) ·
  [psxsplash improvements](psxsplash-improvements.md)

## Releases

Cross-platform plugin + runtime zips ship with each tagged release:
[GitHub Releases](https://github.com/BuffJesus/PS1Godot/releases).

The latest is **v0.5.1** — Linux + macOS GDExtension binaries are now
bundled in the plugin zip (Windows already was). Drop into your project's
`addons/` folder and enable in **Project → Plugins**.

---

!!! note "Docs site under restructure"
    This site is being reorganized into Getting Started · Authoring ·
    Docks · Lua API · Reference · Contributing buckets. Until that
    lands, navigation is auto-generated from the existing `docs/` tree
    and you'll see internal planning docs in the sidebar.
