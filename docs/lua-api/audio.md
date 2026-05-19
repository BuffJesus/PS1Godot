<!-- gen_lua_api_docs:generated -->
# `Audio`

!!! info "Generated"
    This page is auto-generated from `psxsplash-main/src/luaapi.hh` by `scripts/py/gen_lua_api_docs.py`. Edits won't survive the next build — fix the source comments instead.

15 entries.

## Methods

### `Audio.Play(soundId, volume, pan) -> channelId` { #audio-play }

soundId can be a number (clip index) or string (clip name)

### `Audio.Find(name) -> clipIndex or nil` { #audio-find }

Finds audio clip by name, returns its index for use with Play/Stop/etc.

### `Audio.Stop(channelId)` { #audio-stop }

Stops the SFX voice on the given channel id (returned by Audio.Play).
No-op if the channel finished already or was never used.

### `Audio.SetVolume(channelId, volume)` { #audio-setvolume }

Adjusts the live volume of an SFX voice (channel id from Audio.Play).
`volume` is 0..127 — same range Music.SetVolume uses.

### `Audio.StopAll()` { #audio-stopall }

Silences every SFX voice currently in flight. Doesn't stop music
(use Music.Stop) or CDDA (use Audio.StopCDDA). Use for cutscene
entry, pause-menu hush, etc.

### `Audio.GetClipDuration(nameOrIndex) -> frames` { #audio-getclipduration }

Length of a clip in 60 Hz frames. Returns 0 for clips authored as
looped (no defined end) or for unknown names. Useful for sync'ing
gameplay events to a one-shot SFX's tail (e.g. "wait for door
creak to finish before playing dialogue line").

### `Audio.PlaySfx(name, volume?, pan?) -> channelId` { #audio-playsfx }

Plays SPU-routed clips. Logs a warning if `name` was authored
as XA/CDDA — call PlayMusic for those instead.

### `Audio.PlayMusic(name) -> 0 on success, -1 on failure` { #audio-playmusic }

Resolves clip routing and dispatches: SPU plays via the SFX
path, CDDA logs an error (use PlayCDDA(track) directly), XA
logs "not implemented" (Phase 3 streaming work).

### `Audio.StopMusic()` { #audio-stopmusic }

Scaffold: stops the music sequencer + CDDA playback. XA path is
a no-op until streaming lands.

### `Audio.PlayCDDA(trackNo)` { #audio-playcdda }

Starts CDDA playback of the given track number (1-based). CDDA
tracks are Red Book audio at the end of the disc — high quality
but expensive seeks; reserve for title music and major scene
transitions.

### `Audio.ResumeCDDA()` { #audio-resumecdda }

Resume a paused CDDA track at the position it stopped. No-op if
nothing was paused.

### `Audio.PauseCDDA()` { #audio-pausecdda }

Pause CDDA playback at the current position. Audio.ResumeCDDA
continues; Audio.StopCDDA discards position.

### `Audio.StopCDDA()` { #audio-stopcdda }

Stop CDDA playback. Position is lost — next Audio.PlayCDDA starts
from track beginning.

### `Audio.TellCDDA() -> {min, sec, frame}` { #audio-tellcdda }

Current CDDA playback position as MSF (minute, second, frame) —
CD-Audio's native time format. Use for syncing gameplay to track
beats or showing playback time in UI.

### `Audio.SetCDDAVolume(volume)` { #audio-setcddavolume }

CDDA-specific master volume (0..127). Independent from SFX volume
and the music sequencer's own volume. Use for crossfade-on-pause
or "duck the music" gameplay moments.
