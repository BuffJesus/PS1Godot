<!-- gen_lua_api_docs:generated -->
# `Sound`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

3 entries, 0 with worked examples mined from `godot-ps1/demo/scripts/` and the bundled templates.

## Methods

### `Sound.PlayMacro(name) -> handle or nil` { #sound-playmacro }

Plays a "sound macro" — a composite SFX sequence the bank author
built from multiple clips with delays/volumes/pitches. Returns a
handle for later cancellation, or nil if the macro name isn't in
the scene's sound bank.

### `Sound.PlayFamily(name) -> channel or nil` { #sound-playfamily }

Plays a random clip from a "sound family" — a variation pool used
for footsteps, impacts, etc. so repetition stays varied. Returns
the SFX channel id (same shape as Audio.Play returns), or nil if
the family name isn't found.

### `Sound.StopAll()` { #sound-stopall }

Silences every macro currently in flight. Doesn't affect family
playback (those use the standard SFX channels and respond to
Audio.StopAll instead).
