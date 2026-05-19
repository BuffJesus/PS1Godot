# PS1UICanvas

A UI canvas — a screen-space layer that groups UI elements and
toggles visibility as a unit. Children of type `PS1UIElement`
become the canvas's widgets at export time.

<!-- SCREENSHOT: nodes/ps1-ui-canvas-inspector.png -->

## Where it goes

Direct child of [`PS1Scene`](ps1-scene.md). Plain `Node`, not
`Node3D` — UI canvases aren't spatial. Multiple per scene is the
norm: HUD, pause menu, dialog box, interact prompt, toast
notifier all live as separate canvases.

## Key fields

- **CanvasName** — the string Lua passes to look up this canvas
  (`UI.FindCanvas("dialog")`). Must be unique across all canvases
  in the scene.
- **VisibleOnLoad** — start visible or hidden. HUD typically
  `true`, dialog typically `false`.
- **Residency** — when the canvas's bytes live in memory:
  - `Gameplay` — always resident during gameplay. HUD, health bar.
  - `MenuOnly` — not in the gameplay VRAM set. Loaded when shown.
  - `OnDemand` — loaded on first show, never unloaded.

## Children — UI elements

Drop child `Node`s and promote each to `PS1UIElement`:

- **Type = Box** — solid-color rectangle.
- **Type = Text** — text rendered through the UI font atlas.
- **Type = Sprite** — texture from VRAM.

Each element has X, Y, W, H fields in PSX screen coordinates
(320×240). The text renderer word-wraps on W and honors `\n` for
explicit breaks.

## Layout helpers (advanced)

For non-trivial layouts: `PS1UIHBox`, `PS1UIVBox`, `PS1UIAnchor`,
`PS1UISpacer`, `PS1UISizeBox`, `PS1UISlot`. These run a pass at
load time that resolves to plain element positions. Author
hand-positioned layouts first; bring in helpers when you need
adaptive sizing or right-alignment from a screen edge.

## Lua surface

```lua
local canvas = UI.FindCanvas("dialog")
local body = UI.FindElement(canvas, "body")
UI.SetText(body, "Hello!")
UI.SetCanvasVisible(canvas, true)
```

## Workflows

- **Author against 320×240** — element placement is pixel-exact.
  Drop the WYSIWYG UI canvas editor (in PS1 Authoring → UI Canvas)
  for visual layout work.
- **Auto-show prompts** — set
  [`PS1MeshInstance.ShowPrompt = true`](ps1-mesh-instance.md) +
  `PromptCanvasName = "interact_prompt"` and the runtime
  auto-shows the named canvas when the player is in interact range.
  No Lua needed for the prompt itself.

## Related

- [Lua API → UI](../../lua-api/ui.md)
- [`authoring/ui/custom-boot-logo.md`](../ui/custom-boot-logo.md) ·
  [`authoring/ui/splashedit-import.md`](../ui/splashedit-import.md)
  — adjacent UI/asset topics.
- [UI Canvas editor dock](../../docks/overview.md) — under PS1
  Authoring container.
