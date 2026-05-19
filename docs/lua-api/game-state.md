<!-- gen_lua_api_docs:generated -->
# `GameState`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

7 entries, 0 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `GameState.Frame() -> number` { #game-state-frame }

Frames since the current scene loaded. Alias for Timer.GetFrameCount;
grouped here for scripts that read all "what's the game doing?"
state from the GameState table.

### `GameState.GetMode() -> string` { #game-state-getmode }

Current game-mode string ("explore" / "battle" / "dialogue" / "menu" /
"cutscene" / "paused" / etc.). Empty string until something sets it.

### `GameState.SetMode(name)` { #game-state-setmode }

Set the current game-mode string. Free-form; convention picks one of
"explore" / "battle" / "dialogue" / "menu" / "cutscene" / "paused".
Resets to "" on scene load.

### `GameState.IsMode(name) -> boolean` { #game-state-ismode }

Convenience: true when GetMode() == name. Saves the explicit compare.

### `GameState.GetChunk() -> string` { #game-state-getchunk }

Current chunk / area id. Authors set this on transitions so other
scripts can branch on location. Empty string until something sets it.

### `GameState.SetChunk(id)` { #game-state-setchunk }

Set the current chunk / area id. Free-form string. Resets to "" on
scene load.

### `GameState.IsChunk(id) -> boolean` { #game-state-ischunk }

Convenience: true when GetChunk() == id.
