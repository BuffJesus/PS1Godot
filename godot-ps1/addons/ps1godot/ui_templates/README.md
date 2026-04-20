# PS1 UI templates

Drop-in `PS1UICanvas` scenes for the four recurring UI shapes: a
bottom-of-screen dialog box, a vertical menu, a HUD progress bar, and a
floating toast notification. Each is a small, hand-authored `.tscn`
using only the `PS1UIElement` primitives — no hidden helpers, no scripts.
Read them, copy them, modify them.

## How to use

1. In your scene, right-click the scene root (or whichever parent you want
   the UI to belong to) and pick **Instantiate Child Scene…**
2. Choose one of the files in `addons/ps1godot/ui_templates/`.
3. The instanced sub-tree is linked by default: changing the template
   file updates every copy. If you want to edit *this one* without
   touching the template, right-click the instanced root → **Editable
   Children**, then **Make Local** if you want a fully-independent copy.

## What each one contains

| File | Purpose | Canvas name | Residency |
|------|---------|-------------|-----------|
| `dialog_box.tscn` | Narrator / NPC dialog at the bottom of the screen. Background box, wrapped body text, optional name tag above. | `dialog` | `MenuOnly` |
| `menu_list.tscn` | Title + four selectable items + `>` cursor. Re-point cursor by moving its `Y` from Lua as selection changes. | `menu` | `MenuOnly` |
| `hud_bar.tscn` | Label + filled bar + background. Change `bar_fill.Width` at runtime to show depletion. | `hud_bar` | `Gameplay` |
| `toast.tscn` | Floating mid-screen notification. Hidden by default; show + schedule hide from Lua. | `toast` | `Gameplay` (non-resident: hide when unused) |

## Common adjustments

- **Canvas name clashes.** Lua scripts look up canvases by `CanvasName`
  with `UI.FindCanvas("...")`. If you have two dialog boxes in the same
  scene, rename one's canvas to avoid the clash (e.g., `dialog_top`).
- **Text won't fit.** Text elements are single-line by default. Use
  explicit `\n` in the `Text` property for a forced break, or widen the
  element's `Width`. Runtime auto-wrap against `Width` is on the roadmap
  (Phase 3 UI authoring).
- **Menu item count.** Duplicate the `Item3` node, bump the name + the
  `Y` coordinate by 16. The `Cursor` stays until Lua moves it.
- **Bar color.** `bar_fill.Color` is authored green; change to amber
  (`0.95, 0.75, 0.25, 1`) or red for low-health HUDs.

## Lua patterns (excerpted from the demo's `test_logger.lua`)

```lua
-- Showing a dialog line
local canvas = UI.FindCanvas("dialog")
if canvas >= 0 then
    local body = UI.FindElement(canvas, "body")
    UI.SetText(body, "Hello, traveller.")
    UI.SetCanvasVisible(canvas, true)
end

-- Hiding after a beat
if tick >= hideAtTick then
    UI.SetCanvasVisible(canvas, false)
end
```

## What's next (not shipped yet, tracked in ROADMAP § UI authoring)

- `PS1Theme.tres` — central colors / font / alignment defaults that
  templates would opt into (change once, whole game restyles).
- WYSIWYG canvas editor dock — see exactly what the PSX will render
  without exporting first.
- Dialog tree editor — replace the hand-rolled sequences in scripts
  like `test_logger.lua` with an authored graph.
- Prefab variants — portrait-slot dialog box, horizontal menu, and so on.
