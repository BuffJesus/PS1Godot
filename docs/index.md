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
    [Install the plugin](getting-started/installation.md) →
    [Run the demo](getting-started/quickstart.md) →
    [Build your first scene](tutorial-hello-cube.md).

- **Getting started** —
  [Install](getting-started/installation.md) ·
  [Quickstart](getting-started/quickstart.md) ·
  [Hello cube tutorial](tutorial-hello-cube.md) ·
  [Basic scene tutorial](tutorial-basic-scene.md)
- **Authoring** — [Fixed cameras](authoring/fixed-cameras.md) ·
  [Audio routing](authoring/audio/routing.md) ·
  [Custom boot logo](authoring/ui/custom-boot-logo.md)
- **PS1 Graphs** — [Dialogue](authoring/graphs/dialogue.md) ·
  [FSM](authoring/graphs/fsm.md) · [Quest](authoring/graphs/quest.md) ·
  [Behavior Tree](authoring/graphs/behavior-tree.md)
- **Reference** — [Splashpack format](reference/splashpack-format.md) ·
  [API showcase](reference/api-showcase.md) ·
  [Lua cheatsheet](reference/lua-cheatsheet.md) ·
  [psxsplash improvements](reference/psxsplash-improvements.md)

## Releases

Cross-platform plugin + runtime zips ship with each tagged release:
[GitHub Releases](https://github.com/BuffJesus/PS1Godot/releases).

The latest is **v0.5.1** — Linux + macOS GDExtension binaries are now
bundled in the plugin zip (Windows already was). Drop into your project's
`addons/` folder and enable in **Project → Plugins**.

---

!!! note "Docs site is still filling in"
    Nav skeleton (Getting started · Authoring · Reference) is in place;
    per-node guides, per-dock pages, and the Lua API reference are
    being written in follow-up passes. Internal planning docs live in
    [`docs/internal/`](https://github.com/BuffJesus/PS1Godot/tree/main/docs/internal){ target="_blank" }
    and aren't published here.
