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
| `Audio.PlayMusic(name)`            | partial    | SPU + CDDA work end-to-end (CDDA auto-dispatches via `PS1AudioClip.CddaTrackNumber`); XA reports table state but doesn't play |
| `Audio.StopMusic()`                | shipped    | stops sequencer + CDDA; no-op for XA |
| `Audio.PlayCDDA(track)` etc.       | shipped    | unchanged — direct CDDA control |
| psxavenc detection                 | shipped    | `PsxAvEnc.Detect()` — env `PSXAVENC` or PATH |
| psxavenc XA conversion             | shipped    | `PsxAvEnc.ConvertWavToXa()` shells out per Route=XA clip; output goes to `scene.<n>.xa` sidecar |
| Splashpack v27 XA table            | shipped    | per-clip (offset, size, name) into the sidecar; runtime parses on scene load |
| `scene.<n>.xa` sidecar emission    | shipped    | `WriteXaSidecar()` concatenates psxavenc outputs in audio-clip order |
| Runtime XA table accessor          | shipped    | `SceneManager::getXaClipInfo(name, &offset, &size)` |
| **XA streaming backend**           | **TODO**   | next session: `XaAudioBackend` C++ class — SETMODE + sector reader coroutine + SPU XA voice DMA. PSYQo gives us the building blocks (cdrom-device.hh sector reads, spu.hh DMA) but no XA-specific helpers. Estimate ~150-300 lines wrapping CDRomDevice + SPU. |
| **ISO build pipeline**             | **TODO**   | XA needs a real ISO (mkpsxiso with XA sector flag). PCdrv is XA-blind. Add `mkpsxiso/build-iso.py` with `.xa` files routed as Mode-2 Form-2 sectors. |

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

## What still needs to happen (next session)

The build pipeline + format are now complete. Two pieces remain:

### 1. `XaAudioBackend` runtime class (~150-300 lines)

PSYQo doesn't ship a turn-key XA player. We have the primitives: sector
reads via `CDRomDevice::readSectors()` (callback / Task / coroutine
flavors), generic SPU DMA via `psyqo::SPU::dmaWrite()`. Glue:

- **SETMODE 0x24** via `cdrom.test()` with raw command buffer to put
  the drive in XA-Form 2 mode.
- **Coroutine reader loop** that `co_await`s `readSectors()` for the
  next batch of XA sectors, fills a small ring buffer.
- **SPU feeder task** that DMAs the ring buffer into SPU XA voice
  area at the rate the hardware needs (PSX SPU decodes XA in voice 4
  hardware-natively — no software decode).
- **MusicManager wrapper** so `Audio.PlayMusic` can dispatch to it.

Sketch:

```cpp
class XaAudioBackend {
public:
    bool init(psyqo::CDRomDevice* cdrom, psyqo::SPU* spu);
    bool play(uint32_t lba, uint32_t lengthSectors);  // start XA stream at LBA
    void stop();
    bool isPlaying() const;
private:
    // coroutine reader, ring buffer, DMA feeder...
};
```

### 2. ISO build pipeline (mkpsxiso)

XA only plays from a real CD layout. PCdrv is XA-blind. We need:

- `mkpsxiso/<scene>.xml` config that includes `scene.<n>.xa` as an
  XA file (Mode-2 Form-2 sectors, file/channel = 0/0 default).
- A build script (`scripts/build-iso.py` or similar) that:
  - Builds the splashpack triplet (we already have this).
  - Runs mkpsxiso with the XA sector flag set on each `.xa` file.
  - Outputs `game.bin/game.cue` ready to mount in PCSX-Redux's full CD
    emulator.

Run path becomes: build.exe → splashpack + .vram + .spu + .xa →
mkpsxiso → game.bin → PCSX-Redux (CD mode, not PCdrv).

## Installing psxavenc (Wonderful Toolchain)

```
# Windows: download a release binary from
#   https://github.com/WonderfulToolchain/psxavenc/releases
# extract somewhere, then either:
#   set PSXAVENC=C:\tools\psxavenc\psxavenc.exe
# or put psxavenc.exe on PATH.
```

Verify with `psxavenc -h` from a shell. After install, re-export from
PS1Godot — the export log should report
`psxavenc detected (PATH: ...) — psxavenc <version>` and start
emitting the `scene.<n>.xa` sidecar.

## Authoring an XA clip today

1. Drop the WAV onto a `PS1AudioClip` resource as usual.
2. Set `Route = XA` (Auto also picks XA for clips > 24 KB ADPCM
   non-loop / > 32 KB loop).
3. Re-export. Without psxavenc the clip silences with a clear log;
   with psxavenc the clip lands in `scene.<n>.xa` and the splashpack
   carries the table entry. `Audio.PlayMusic("name")` reports the
   table entry size; the runtime backend wires up next session.

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

- Splashpack version is now **v27**. Loader hard-asserts `>= 27`. Re-export
  any pack older than that.
- AudioClipEntry stride is **20 B** (`... routing(u8) cddaTrack(u8) _pad(2)`).
- Header gained 8 bytes at the end for `xaClipCount + pad + xaTableOffset`;
  total header size went 168 → 176 B.
- New per-scene sidecar `scene.<n>.xa` (Mode-2 Form-2 XA-ADPCM payload).
  Empty when no Route=XA clip OR psxavenc was missing.
- Per-XA-clip table entry is **24 B** in the splashpack:
  `sidecarOffset(u32) sidecarSize(u32) name[16, null-padded]`.
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
