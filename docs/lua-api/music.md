<!-- gen_lua_api_docs:generated -->
# `Music`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

7 entries.

## Methods

### `Music.Stop()` { #music-stop }

Stops the active music sequence. Voices fade out over their
remaining release time; instruments don't cut hard.

### `Music.IsPlaying() -> boolean` { #music-isplaying }

True if a music sequence is currently active (not stopped, not
ended).

### `Music.SetVolume(v)` { #music-setvolume }

Master sequencer volume (0..127). Independent from per-instrument
volumes set in the music bank. Use for fades, ducking, mixer.

### `Music.GetBeat() -> integer` { #music-getbeat }

Returns the integer beat count since the active sequence started,
or 0 when nothing is playing. Use for rhythmic gameplay (sync a
light flicker, an enemy hop, etc. to the beat).

### `Music.Find(name) -> index or nil` { #music-find }

Look up a sequence by name in the scene's music bank. Returns its
index (faster than passing the name to Music.Play repeatedly), or
nil if not found.

### `Music.GetLastMarkerHash() -> integer (16-bit hash, 0 if none)` { #music-getlastmarkerhash }

*No description.*

### `Music.MarkerHash(text) -> integer (16-bit hash)` { #music-markerhash }

*No description.*
