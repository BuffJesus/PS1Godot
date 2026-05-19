<!-- gen_lua_api_docs:generated -->
# `UI`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

22 entries, 4 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `UI.FindCanvas(name) -> integer` { #ui-findcanvas }

Returns the integer handle for a canvas authored in the scene,
or -1 if the name isn't found. Cache the handle in onCreate and
pass it to every other UI.* call — repeated lookups are wasted
string scans. Always guard with `>= 0` before use.

**Example**

```lua
dialogCanvas = UI.FindCanvas("dialog")
```

_Source: `godot-ps1/demo/scripts/checkered_dialog.lua` line 38._

### `UI.SetCanvasVisible(canvas, bool)` { #ui-setcanvasvisible }

Show or hide an entire canvas (header bar, pause menu, HUD layer).
`canvas` accepts either the integer handle from UI.FindCanvas or
the canvas name as a string. Hidden canvases skip layout and
draw entirely — cheap to toggle.

**Example**

```lua
UI.SetCanvasVisible(dialogCanvas, true)
```

_Source: `godot-ps1/demo/scripts/checkered_dialog.lua` line 55._

### `UI.IsCanvasVisible(canvas) -> boolean` { #ui-iscanvasvisible }

True if the canvas is currently rendered. Accepts handle or name.

### `UI.FindElement(canvas, elementName) -> integer` { #ui-findelement }

Returns the integer handle for a named element on a canvas, or
-1 if not found. `canvas` must be the integer handle (NOT the
name — the runtime silently returns -1 on string input). Cache
returned handles in onCreate.

**Example**

```lua
dialogBodyEl = UI.FindElement(dialogCanvas, "body")
```

_Source: `godot-ps1/demo/scripts/checkered_dialog.lua` line 40._

### `UI.SetVisible(element, bool)` { #ui-setvisible }

Show or hide a single element (text label, image, progress bar).
Cheaper than recreating; the slot is preserved.

### `UI.IsVisible(element) -> boolean` { #ui-isvisible }

True if the element will draw this frame.

### `UI.SetText(element, str)` { #ui-settext }

Replaces the text on a Text element. Empty string clears it.
No effect on non-Text elements. Strings are copied — safe to
pass scratch buffers.

**Example**

```lua
UI.SetText(sysVoiceText, text)
```

_Source: `godot-ps1/demo/scripts/test_logger.lua` line 141._

### `UI.GetText(element) -> string` { #ui-gettext }

Returns the current Text element string, or "" for non-Text /
unknown handles.

### `UI.SetProgress(element, percent)` { #ui-setprogress }

Sets a progress-bar element's fill in percent (0..100, clamped).
No effect on non-Progress elements. Use for HP bars, charge
gauges, loading meters.

### `UI.GetProgress(element) -> integer` { #ui-getprogress }

Returns the bar's current 0..100 fill, or 0 for unknown handles.

### `UI.SetColor(element, r, g, b)` { #ui-setcolor }

Sets the element's tint (0..255 per channel). For Text this is
the glyph color; for Image / Box it modulates the texture / fill.
Doesn't affect transparency — that's authored, not Lua-controlled.

### `UI.GetColor(element) -> r, g, b` { #ui-getcolor }

Returns the three 0..255 tint channels (multi-return). For
unknown handles returns 0, 0, 0.

### `UI.SetPosition(element, x, y)` { #ui-setposition }

Moves the element to screen pixel (x, y). Origin is the canvas
top-left. Coordinates are signed 16-bit so off-screen positions
are valid (handy for slide-in animations).

### `UI.GetPosition(element) -> x, y` { #ui-getposition }

Returns the element's current top-left in canvas pixels
(multi-return).

### `UI.SetSize(element, w, h)` { #ui-setsize }

Resizes the element. For Image / Box this is the rect dimensions;
for Text it's the wrap width / max height. Pixel units.

### `UI.GetSize(element) -> w, h` { #ui-getsize }

Returns current width, height in pixels (multi-return).

### `UI.SetImageUV(element, u0, v0, u1, v1)` { #ui-setimageuv }

Sets the source rect inside the texture atlas for an Image
element. UVs are 0..255 (PSX texel coords inside one page),
clamped to that range. Use to scroll a texture, swap atlas
sub-rects (icon variants, animated frames).

### `UI.GetImageUV(element) -> u0, v0, u1, v1` { #ui-getimageuv }

Returns the four 0..255 UV components (multi-return).

### `UI.SetProgressColors(element, bgR, bgG, bgB, fillR, fillG, fillB)` { #ui-setprogresscolors }

Sets both colors of a progress bar in one call: empty-track
background and filled-portion foreground. Each channel 0..255.
No effect on non-Progress elements.

### `UI.GetElementType(element) -> integer` { #ui-getelementtype }

Returns the element type id (0=Text, 1=Image, 2=Box, 3=Progress,
matching ElementType in the runtime). -1 for unknown handles.
Use when iterating with UI.GetElementByIndex to branch on type.

### `UI.GetElementCount(canvas) -> integer` { #ui-getelementcount }

Number of elements authored on the canvas. 0 for unknown
canvases. Drives index-based iteration with UI.GetElementByIndex.

### `UI.GetElementByIndex(canvas, index) -> integer` { #ui-getelementbyindex }

Returns the element handle at the given 0-based index inside the
canvas, or -1 if out of range. Useful for "walk every element"
logic (apply tint, mass-hide, etc.) without naming each one.
