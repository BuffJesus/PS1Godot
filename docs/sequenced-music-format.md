# Sequenced music format

Binary format for the PS1Godot music sequencer. Authored by the editor
(Godot side) from MIDI + WAV samples; consumed by the patched psxsplash
runtime. Designed to be small enough to drop a 2-minute song in under
10 KB of sequence data, with the sample bank reusing the existing
`PS1AudioClip` ADPCM blob in SPU RAM.

## Why our own format (vs SEQ/VAB)

- **Author barrier.** SEQ/VAB needs Sony PsyQ-era tooling or external
  MIDI→SEQ converters the user runs by hand. Our format lets the editor
  parse MIDI directly and emit binary, no command-line in the loop.
- **Runtime simplicity.** A SEQ player has to handle the full SEQ
  command set (modulation, vibrato, pitch bend, looped instruments).
  We start with note-on/note-off + per-channel volume/pan and grow the
  command set only when a real song needs it.
- **Sample bank reuse.** The runtime already DMAs ADPCM blobs into SPU
  RAM via `PS1AudioClip`. The sequencer just references entries in
  that same table — no second sample upload path.

## On-disk layout

The music section is appended to the splashpack as the last section
written. The v22 header carries `musicSequenceCount` (u16) + a u16 pad
+ `musicTableOffset` (u32). The table at that offset is an array of
24-byte `MusicTableEntry` rows (see `docs/splashpack-format.md`); each
row's `dataOffset` points at a PS1M blob laid out like:

```
MusicSequenceHeader  (16 bytes)
ChannelEntry         (8 bytes) × header.channelCount
Event                (8 bytes) × header.eventCount
```

Blobs are aligned to 4 bytes inside the splashpack. Runtime cap is 8
sequences per scene (`MusicSequencer::MAX_SEQUENCES`).

### `MusicSequenceHeader` — 16 bytes

| Offset | Type | Field | Notes |
|-------:|------|-------|-------|
| 0 | char[4] | `magic` | `"PS1M"` |
| 4 | u16 | `bpm` | Tempo in beats per minute. Constant — variable tempo is post-MVP. |
| 6 | u16 | `ticksPerBeat` | MIDI standard division. 96 or 480 typical. |
| 8 | u8 | `channelCount` | 1–24 (runtime cap matches SPU `MAX_VOICES`). Each channel maps to one SPU voice (mono per channel). |
| 9 | u8 | `_pad` | |
| 10 | u16 | `eventCount` | Total events in the sequence. |
| 12 | u32 | `loopStartTick` | Tick the playhead jumps to after the last event. `0xFFFFFFFF` = no loop (one-shot). |

### `ChannelEntry` — 8 bytes

| Offset | Type | Field | Notes |
|-------:|------|-------|-------|
| 0 | u16 | `audioClipIndex` | Index into the splashpack's existing audio clip table. The clip's ADPCM data is the channel's instrument sample. |
| 2 | u8 | `baseNoteMidi` | The MIDI note number this sample plays back at its native pitch (e.g. 60 = middle C). Note events shift pitch relative to this. |
| 3 | u8 | `volume` | 0–127, multiplied with per-event velocity. |
| 4 | u8 | `pan` | 0 = left, 64 = center, 127 = right. |
| 5 | u8 | `flags` | bit 0 = `loopSample` (sample loops while note held), bit 1 = `percussion` (ignore pitch shift, play at native rate). |
| 6 | u16 | `_pad` | |

### `Event` — 8 bytes

| Offset | Type | Field | Notes |
|-------:|------|-------|-------|
| 0 | u32 | `tick` | Absolute tick from sequence start. Events are sorted ascending. |
| 4 | u8 | `channel` | Index into the channel table (0 to `channelCount-1`). |
| 5 | u8 | `kind` | See event-kind table below. |
| 6 | u8 | `data1` | Event-kind specific. |
| 7 | u8 | `data2` | Event-kind specific. |

#### Event kinds

| Kind | Name | data1 | data2 | Effect |
|---:|---|---|---|---|
| 0 | `noteOn` | MIDI note (0–127) | velocity (1–127) | Allocates the channel's voice, sets pitch + volume, key-on. |
| 1 | `noteOff` | MIDI note (0–127) | (unused) | Key-off the channel's voice if the held note matches. |
| 2 | `channelVolume` | volume (0–127) | (unused) | Updates the channel's `volume` for subsequent note-on events. |
| 3 | `channelPan` | pan (0–127) | (unused) | Updates the channel's `pan` for subsequent note-on events. |

The MVP runtime only implements kinds 0–3. Future kinds (pitch bend,
modulation, channel mute) can be added without breaking the format —
unrecognised kinds are skipped at load time.

## Pitch math

Each channel's sample is recorded at `baseNoteMidi`. To play MIDI note
`n` on that channel, the SPU pitch register is set so playback rate is
multiplied by `2^((n - baseNoteMidi) / 12)`. The PSX SPU's pitch
register is fp12 with `0x1000` = native rate, so:

    sampleRate = 0x1000 * 2^((n - baseNoteMidi) / 12)

The runtime ships a **precomputed** 84-entry table (7 octaves × 12
semitones, indexed by `semitone + 36`, capped at the SPU's `0x3FFF`
register max). It's a static const in `musicsequencer.cpp`. We tried
the iterative `rate = (rate * SEMI_NUM) / SEMI_DEN` approach with a
fp14 ratio for the 12th root of 2 and the rounding error compounded
into wildly wrong pitches over a few semitones — precomputed table is
168 bytes of rodata, no libm dependency, exact pitches. Don't be
clever here; just bake the table.

## Tempo / timing

Header carries `bpm` and `ticksPerBeat`. Runtime computes
ticks-per-second once on `playByIndex`:

    ticksPerSec = bpm * ticksPerBeat / 60

`MusicSequencer::tick(dt12)` advances the playhead by
`(ticksPerFrame12 * dt12) >> 12`. **Note the runtime convention:**
psxsplash's `m_dt12` is `(elapsed_us * 4096) / 33333`, where 33333 µs =
**one 30 fps frame** — *not* 60 fps. The sequencer derives
`ticksPerFrame12` from `ticksPerSec / 30` to match. Mis-setting this
to /60 plays the song at half speed.

## Loop behaviour

When the sequencer's tick passes the last event, it checks
`loopStartTick`:
- `0xFFFFFFFF` — sequence ends, all channels key-off, sequencer idle.
- otherwise — playhead jumps to `loopStartTick`, replay continues.

For a tight loop the author typically sets `loopStartTick = 0`. For an
intro+loop pattern they set it to the tick at which the looping section
begins.

## SPU RAM budgeting

A music sequence's footprint is dominated by its sample bank. With
ADPCM at ~3.5× compression, a 1-second 22050 Hz mono sample is
~12.5 KB in SPU RAM. The full SPU has 512 KB (less the existing
sound-effect resident set). The editor's validation panel (Phase 3)
shows the per-channel + total sample-bank cost against the SPU budget.

A 6-channel sequence with 1.5-second average instrument samples is
~110 KB — comfortably within budget alongside SFX. Percussion samples
(typically <0.5 s each) are a much smaller share.

## Lua API surface

```lua
Music.Play(name)               -- start; master volume defaults to 100; returns bool
Music.Play(name, volume)       -- start with explicit volume (0-127)
Music.Play(index, volume)      -- index form, same as name form
Music.Stop()                   -- key-off all music channels, release voice pool
Music.SetVolume(level)         -- 0-127, master multiplier
Music.GetBeat()                -- current beat (sequencer tick / ticksPerBeat)
Music.IsPlaying()              -- boolean
Music.Find(name)               -- returns index or nil
```

`Audio.GetClipDuration(nameOrIndex)` is also exposed — returns clip
length in 60 Hz frames; not music-specific but used by dialog scripts
to coordinate with `Music.SetVolume(...)` ducking.

Future (post-MVP):
```lua
Music.OnEvent(name, callback)   -- author-placed sync events trigger Lua
Music.GetBar()                  -- bar number on top of GetBeat
```

## Voice reservation (runtime detail)

`MusicSequencer::playByIndex(...)` calls
`AudioManager::reserveVoices(channelCount)`. After that:
- `AudioManager::play(clipIdx, ...)` (used by dialog/SFX) only allocates
  voices in `[channelCount, MAX_VOICES)`.
- The sequencer drives notes via `AudioManager::playOnVoice(voice, clip,
  vol, pan)` — voice index is fixed = packed channel index for the
  song's lifetime.
- `Music.Stop()` (or scene transition) calls `reserveVoices(0)` and
  releases the pool.

This keeps dialog from stealing held music notes (which sounded fine
in trivial test cases but produced stuck notes / cut samples in dense
arrangements).

## MIDI authoring conventions (PS1MusicChannel binding)

The `PS1MusicChannel` Godot resource has these fields, all of which
flow through to the routing decision at export time:

- **`MidiChannel`** (0-15) — required filter. Notes on a different
  source channel skip this binding.
- **`MidiTrackIndex`** (-1 = wildcard, 0..N) — optional filter for
  multi-track MIDIs that all share a single channel (common with DAW
  exports like Reaper's default behaviour).
- **`MidiNoteMin` / `MidiNoteMax`** (0-127) — note-range filter. Used
  for drum kits where one MIDI channel triggers multiple sample
  channels (kick channel filtered to note 36, snare to 38/40, hat
  to 42).
- **`AudioClipName`** — name of a `PS1AudioClip` on `PS1Scene.AudioClips`.
- **`BaseNoteMidi`** — the MIDI note the sample plays at native pitch.
- **`Volume` / `Pan`** — per-channel mix (0-127).
- **`LoopSample`** — sample loops while note is held.
- **`Percussion`** — bit 1 in the binary `flags` field; suppresses
  pitch shifting (drums play at native rate regardless of note number).

At export, the routing pass walks bindings in two phases per note:
exact-track-match first, then wildcard (track = -1) on the same
channel. Within each phase, the first binding whose note-range covers
the note wins.

## Editor authoring (current shipped flow)

1. Author drops a `.mid` and a folder of short `.wav` instrument
   samples (mono, 16-bit PCM, ~22050 Hz) into the project.
2. **Add the WAVs to `PS1Scene.AudioClips`** as `PS1AudioClip`
   resources. Each gets a `ClipName` that the music binding will
   reference. (WAV import note: Godot 4.4+ defaults to QOA compression;
   the `[importer_defaults]` block in `project.godot` overrides this
   to raw PCM so the exporter can read the samples.)
3. Create a **`PS1MusicSequence`** resource. Inspector picks the `.mid`
   path, an optional `BpmOverride`, and `LoopStartBeat`.
4. Add **`PS1MusicChannel`** sub-resources to the sequence's `Channels`
   array — one per voice you want to sound. Each binding picks a
   `MidiChannel` filter, an optional `MidiTrackIndex` filter (for
   single-channel multi-track DAW exports), an optional
   `MidiNoteMin` / `MidiNoteMax` (for drum-kit splits), the
   `AudioClipName`, `BaseNoteMidi`, `Volume`, `Pan`, and the
   `Percussion` flag (no pitch shift).
5. Add the sequence to **`PS1Scene.MusicSequences`** array.
6. From Lua: `Music.Play("name", volume)` — typically in `onCreate`
   on a scene-level script or one of the early per-object scripts.
7. Hit **Run on PSX**. The exporter parses the MIDI, walks each note
   through the routing pass, packs PS1M, embeds in the splashpack.

### Authoring tips (learned the hard way)

- **Each instrument on its own MIDI channel** in the source DAW.
  Reaper: right-click the MIDI item → *Source Properties* → *Send as
  channel*. Drum kit on channel 10 (GM convention).
- **Use velocity dynamics**, not flat 127. The runtime multiplies
  `channelVolume * velocity * masterVolume`, so flat-velocity songs
  sound blasted regardless of the per-channel volume number.
- **Sample envelopes matter**. A 1-second sustained pad sample
  retriggered on every chord change creates a wash of overlap; keep
  envelopes tight (~0.4-0.6 s for pad, ~0.2 s for bass pluck) unless
  you actually want bleed.
- **Don't bake harmonics into a single sample**. A pad sample with a
  perfect fifth stacked into it turns every "single note" the chord
  track plays into a parallel-fifth dyad — instant mud. Single sine
  is usually right.
- **Sort order convention** (parser-side): NoteOff fires before NoteOn
  at the same tick. Otherwise `Off(60) + On(60)` at one tick
  silences the freshly-started note via the mono-per-channel cut.

## True sequenced audio: phased migration

The current format is a **MIDI-note → PS1AudioClip** binding system —
useful for short jingles and looping menu themes, but not a full
PS1-style sequenced engine. Strategy doc:
[`ps1_true_sequenced_audio_strategy.md`](ps1_true_sequenced_audio_strategy.md).
Phased plan with concrete file changes:
[`handoff-true-sequenced-audio-plan.md`](handoff-true-sequenced-audio-plan.md).

**Phase 0 (landed)** — scaffold-only. Three new resource types are
authorable in the inspector but the runtime ignores them; no on-disk
format change, no `Music.Play` behaviour change.

  - `PS1Instrument` — bank-style instrument definition.
  - `PS1SampleRegion` — per-instrument note/velocity range mapping
    to a single PS1AudioClip with optional ADSR override and loop
    points.
  - `PS1DrumKit` — MIDI-note-keyed drum mapping with per-drum
    volume / pan / choke group / priority.

  Plus exporter diagnostics in `CollectMusicSequences`: voice-pressure
  warnings (>12 channels = RPG-budget warning, >16 = error), peak
  simultaneous-note estimate vs binding count, and per-sequence
  warnings for MIDI ProgramChange / Controller / PitchBend /
  Aftertouch events the parser silently drops.

**Phase 1+** — instrument data path, format bumps, ADSR wiring, voice
allocator extension, and `Sound.PlayMacro` separation. See the plan
doc for ordering and per-phase format-bump grouping.
