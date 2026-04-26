# PS1 audio routing (Phase 3, scaffolded as of 2026-04-26)

PS1 audio splits into three buses with different cost/quality trade-offs. PS1Godot
exposes a single per-clip `Route` knob (`PS1AudioClip.Route` in the editor) and the
build pipeline + runtime dispatch on it. The intent: author with the right bus from
day one even if the streaming backends are not finished, so we don't re-tag every
clip later.

## The three buses

| Bus  | Storage     | Memory cost           | Quality       | Best for                                    |
|------|-------------|-----------------------|---------------|---------------------------------------------|
| SPU  | SPU RAM     | counts against 256 KB | 4-bit ADPCM   | short SFX, UI, footsteps, voice barks       |
| XA   | data CD     | small ring buffer     | mono 18.9 kHz | music, ambient loops, narration, large stingers |
| CDDA | red-book CD | none                  | 44.1 kHz      | title / credits — special cases only        |

## Routes (`PS1AudioRoute`)

```csharp
public enum PS1AudioRoute
{
    Auto = 0,   // build picks SPU vs XA from size+loop heuristics
    SPU  = 1,   // force SPU residency
    XA   = 2,   // force XA streaming (Phase 3 — scaffolded)
    CDDA = 3,   // force CDDA red-book (must be requested explicitly)
}
```

Auto rules (`SceneCollector.ResolveAudioRoute`):

- loop **and** ADPCM > 32 KB ........... XA  (long ambient / music)
- non-loop **and** ADPCM > 24 KB ....... XA  (long stingers / dialog)
- otherwise ............................. SPU

Auto **never** picks CDDA — disc layout impact is too project-shaped to guess.

## What is implemented end-to-end

| Layer                              | Status     | Notes |
|------------------------------------|------------|-------|
| `PS1AudioClip.Route` editor field  | shipped    | v25, defaults to Auto |
| Build-time route resolution        | shipped    | `ResolveAudioRoute` in SceneCollector |
| Splashpack v25 binary field        | shipped    | `routing` byte in audio table entry |
| Runtime route table (SceneManager) | shipped    | `getAudioClipRouting(idx)` |
| `Audio.PlaySfx(name, vol?, pan?)`  | shipped    | warns if clip is non-SPU |
| `Audio.PlayMusic(name)`            | partial    | SPU + CDDA work end-to-end (CDDA auto-dispatches via `PS1AudioClip.CddaTrackNumber`); XA logs "not implemented" |
| `Audio.StopMusic()`                | shipped    | stops sequencer + CDDA; no-op for XA |
| `Audio.PlayCDDA(track)` etc.       | shipped    | unchanged — direct CDDA control |
| psxavenc detection                 | shipped    | `PsxAvEnc.Detect()` — env `PSXAVENC` or PATH |
| psxavenc XA conversion             | **TODO**   | conversion step is not wired; XA clips ship as ADPCM into `.spu` even though runtime won't play them |
| XA streaming runtime               | **TODO**   | `Audio.PlayMusic` for XA-routed clips logs and returns -1 |

> Anything marked **TODO** is the Phase 3 finish line. Today, marking a clip XA
> means the splashpack records the intent and the runtime knows it; playback is
> silent.

## Lua API surface

```lua
-- Routing-aware
Audio.PlaySfx(name, volume?, pan?)   -- routing-aware SFX play; warns on non-SPU
Audio.PlayMusic(name)                -- dispatches by clip routing
Audio.StopMusic()                    -- blanket stop (sequencer + CDDA)

-- Existing low-level (unchanged)
Audio.Play(soundIdOrName, volume?, pan?) -> channel
Audio.Find(name) -> index
Audio.Stop(channel)
Audio.SetVolume(channel, volume)
Audio.StopAll()
Audio.GetClipDuration(nameOrIndex) -> frames

-- Direct CDDA / sequencer (unchanged; explicit by track)
Audio.PlayCDDA(track), Audio.PauseCDDA(), Audio.ResumeCDDA(), Audio.StopCDDA()
Audio.SetCDDAVolume(left, right)
Music.Play(name), Music.Stop(), Music.IsPlaying() -- PS1M sequenced music
```

`Audio.PlayMusic` is the right entry point for "play this song" / "play this
ambient loop" — the runtime decides whether to route through SPU or stream from
disc based on what the exporter wrote.

## Build pipeline TODO (Phase 3 finish)

1. `PsxAvEnc.cs` already detects the binary; the missing piece is invoking it.
   Pseudocode for the conversion step in `SplashpackWriter`:

   ```text
   for clip in audioClips where Routing == XA:
       psxavenc -t xa -f 18900 -b 4 -c 1 in.wav out.xa
       collect into scene_<n>.xa sidecar
       record (clip.name, byteOffset, byteSize) in v26 XA table
   ```

2. Runtime side: `XaAudioBackend` class wrapping `psyqo::CDRomDevice` sector
   reads + XA-ADPCM decode (PSYQo has the sector machinery; the decoder needs
   to be ported from the Sony XA reference).

3. Splashpack v26: per-clip XA file offset + size.

4. mkpsxiso: include the `.xa` sidecar in the data track when building a real
   ISO.

## CDDA strategy

- Authoring path: place WAV music tracks in the disc layout config (mkpsxiso
  Track 02, 03, …). Tag the matching `PS1AudioClip` with `Route = CDDA` and
  set `CddaTrackNumber` to the disc track. `Audio.PlayMusic("name")` then
  dispatches to `playCDDATrack(track)` automatically — Lua doesn't need to
  know the track number.
- Track 1 is the data track on a PSX disc; authored audio tracks start at 2.
- `Audio.PlayCDDA(<track>)` direct call still works for one-off cases where
  you don't want a `PS1AudioClip` resource at all.
- Cost: CDDA monopolizes the CD drive — no concurrent file/level streaming.
  Use only where that is acceptable (title, end credits, attract loops).

## Migration notes

- Splashpack version went **v24 → v25 → v26**. Loader hard-asserts `>= 26`.
  Re-export any pack older than that.
- AudioClipEntry stride is **20 B** (v25 added routing + 3-byte pad; v26
  claims one of the pad bytes for `cddaTrack`, so the layout is now
  `... routing(u8) cddaTrack(u8) _pad(2)`).
- Existing exports without `Route` set on every clip default to **Auto**, which
  resolves to **SPU** for everything below the size threshold — i.e. the same
  behavior as before for typical jam-scale audio.

## SPU budget triage shortlist

Today's offenders ranked by ADPCM size in the monitor scene
(`scene.spu = 307 KB / 256 KB cap`):

| Clip                | Bytes  | Suggested route           |
|---------------------|-------:|---------------------------|
| `shift_end_stinger` | 37 824 | XA (long, one-shot)       |
| `alarm_distant`     | 37 824 | XA (long, one-shot)       |
| `footsteps_slow`    | 18 928 | SPU (latency-sensitive)   |
| `ambient_drone`     | 18 928 | XA (long ambient loop)    |
| `metal_clang`       | 18 672 | SPU (one-shot impact)     |

Marking the three XA candidates above frees ~95 KB of SPU once the streaming
backend lands. Until then, leave them on SPU or trim duration / sample rate.
