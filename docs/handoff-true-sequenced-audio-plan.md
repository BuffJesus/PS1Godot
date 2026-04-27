# Handoff: True sequenced audio — implementation plan

Companion to [`docs/ps1_true_sequenced_audio_strategy.md`](ps1_true_sequenced_audio_strategy.md).
Translates the strategy doc's §20-22 phased migration into concrete PS1Godot
file changes, splashpack format impact, and ordering risks. Cited line numbers
were verified against the codebase as of 2026-04-26.

## Phase 0 — First safe slice (§22 prompt; ship next)

Reporting + correctness + scaffold-only metadata. **No runtime behaviour
change. No format bump.**

- **Fix stale comments.** `psxsplash-main/src/musicsequencer.hh:79` and `:43`
  claim `MAX_CHANNELS=24` matches `MAX_VOICES`; `godot-ps1/addons/ps1godot/nodes/PS1MusicChannel.cs:43-44`
  says "Max 8 per sequence" (wrong — runtime cap is 24, MAX_SEQUENCES is 8);
  `docs/sequenced-music-format.md:46` says "1-16" for channelCount. Reconcile
  to 24 in all three.
- **Sequence diagnostics in the exporter.** Extend `CollectMusicSequences` in
  `godot-ps1/addons/ps1godot/exporter/SceneCollector.cs:1761-1879` to log
  per-sequence: name, MIDI path, parsed note count, channel binding count,
  estimated reserved voices (= bindings.Count), referenced clip names, missing
  clip warnings (already partial at :1836), unsupported MIDI events (program
  changes, controller events) emitted by the parser — the parser at
  `godot-ps1/addons/ps1godot/exporter/MidiParser.cs` already discards these;
  surface them as `GD.PushWarning` with a one-line summary. Add a "max
  simultaneous notes" estimate by walking `parsed.Notes` and counting
  overlapping note-ons after binding allocation.
- **Scaffold-only resource files** (new C# under
  `godot-ps1/addons/ps1godot/nodes/`):
  - `PS1Instrument.cs` — InstrumentId, Name, Regions (array), DefaultADSR
    (struct: A/D/S/R bytes), Volume, Pan, Priority, PolyphonyLimit,
    PitchBendRange.
  - `PS1SampleRegion.cs` — AudioClipName, RootKey, KeyMin, KeyMax, VelocityMin,
    VelocityMax, TuneCents, LoopStart, LoopEnd, LoopEnabled, ADSROverride.
  - `PS1DrumKit.cs` — KitId, NoteMappings (array of {MidiNote, AudioClipName,
    Volume, Pan, ChokeGroup, Priority}).
  - All marked `[GlobalClass]` with `[Tool]`, no exporter wiring yet — strictly
    editable in inspector. Document at the top: "Scaffold only — runtime
    ignores. Step 3+."
- **Exporter warnings** (additive, in `CollectMusicSequences`): reserved voices
  > 12 (RPG budget), MIDI program changes referencing unmapped instruments
  (ignored today, but log), drum notes with no mapping (when DrumKit is
  authored on the sequence), long sustained notes (>2 s) targeting non-loop
  clips, music voice count leaving <8 SFX voices.
- **Doc updates**: append a "True sequenced audio: phased migration" section
  to `docs/sequenced-music-format.md` linking the strategy doc; update
  `docs/ps1-audio-routing.md` with Music vs Sound.PlayMacro intent (no API
  change yet).

**Green-build checkpoint:** existing `PS1MusicSequence` assets load and play
unchanged. New scaffold resources never touch the exporter; deleting them is
safe. No splashpack version bump.

## Phase 1 — Instrument data path, one region (§21 step 3)

Wire `PS1Instrument` (one region) end-to-end so `PS1MusicChannel` *optionally*
references an instrument instead of an audio clip directly. Behaviour-equivalent
to today.

- Exporter (`PS1MusicChannel.cs`): add optional `Instrument` export; when set,
  the binding resolves AudioClipName from the instrument's first region
  (BaseNoteMidi from RootKey, LoopSample from LoopEnabled).
- `PS1MSerializer.cs:32-47` ChannelBinding gets new optional fields (TuneCents,
  region ADSR override) but emits the same v1 PS1M wire format — these are
  exporter-side resolution only.
- Splashpack: **no bump**. Wire format unchanged. Instruments are an authoring
  layer only.

**Green-build checkpoint:** existing sequences load unchanged; new sequences
using instruments produce identical PS1M bytes to the equivalent direct
binding.

## Phase 2 — Multi-region keymaps + drum kits via exporter expansion

**Status (shipped):** simplified slice that delivers multi-region keymaps
+ drum kits without any format change. Original Phase 2's runtime
instrument bank + program changes were deferred to Phase 2.5.

The simplification: `PS1MSerializer` already routes per-note via
`MidiNoteMin/MidiNoteMax + MidiChannel + MidiTrackIndex` filters
(`PS1MSerializer.cs:166-187`). So a multi-region instrument expands at
export time into one `ChannelBinding` per region, with each binding's
`MidiNoteMin/Max` taken from the region's `KeyMin/KeyMax`. The
existing per-note router picks the right region. Pure exporter
expansion — runtime, splashpack format, and PS1M wire format are all
unchanged.

Drum kits expand the same way: one binding per kit mapping with
`MidiNoteMin == MidiNoteMax == kit-mapping note`, `Percussion=true`
to inhibit pitch shift, `MidiChannel = PS1MusicSequence.DrumMidiChannel`
(default 9 = GM convention). Choke groups and per-drum priority stay on
the kit resource but the runtime ignores them today (Phase 2.5+).

Voice budget caveat: a 4-region instrument used on one MIDI channel
reserves 4 SPU voices, not 1. The Phase 0 voice-pressure diagnostics
already count post-expansion bindings, so the warnings still fire
correctly.

## Phase 2.5 — Program changes + scene-wide instrument bank (shipped)

The original Phase 2 plan (instrument bank in the splashpack header,
PS1M2 sibling format with new event kinds, runtime
channel→program→instrument resolution) is still the right move when
program changes start mattering — typically when a scene wants the same
sample bank reused across multiple sequences (one town theme + one
battle theme + one fanfare, all using the same 8-instrument bank
without duplicating samples in the SPU).

When this lands:

- New PS1M2 sequence format (sibling magic `PS2M`) because
  `MusicSequenceHeader` is 16 bytes load-bearing
  (`musicsequencer.hh:19`). Add:
  - Per-sequence channel→program table.
  - Event kinds 4 (ProgramChange), 5 (PitchBend), 7 (Controller),
    8 (Marker), 9 (LoopStart), 10 (LoopEnd) per strategy §5.2.
- Splashpack header: bump version 27→28; add Instrument/Region/DrumKit
  table offsets in `SPLASHPACKFileHeader` (`splashpack.cpp:21-123`).
  Loader (`splashpack.cpp:154`) raises minimum to 28.
- Runtime (`musicsequencer.cpp`): teach `dispatchEvent` the new kinds;
  `noteOn` resolves channel→program→instrument→region→clip; pitch
  shift uses `note - region.RootKey` instead of
  `note - cfg.baseNoteMidi` at `:243`.

**Green-build checkpoint:** `PS1MusicSequence` assets re-export and
play; sequences using only one-region instruments produce
wire-equivalent runtime behaviour.

## Phase 3 — ADSR + loop metadata wired to SPU hardware (§21 step 6)

Depends on Phase 2.5 (format bump) — `MusicChannelEntry` is 8 bytes
load-bearing today with no room to pack per-region ADSR overrides.
Once PS1M2 ships, region records carry their own ADSR fields and
this phase becomes "stop using `DEFAULT_ADSR` at
`audiomanager.cpp:170`; read from the region instead."

Hardware ADSR is already free — it's a 32-bit register pair
(`audiomanager.hh:14` `DEFAULT_ADSR=0x000A000F`, written at
`audiomanager.cpp:170-183`). Cost: pack the per-instrument ADSR into PS1M2
region/instrument records (already in Phase 2 if grouped) and stop using
`DEFAULT_ADSR` at `audiomanager.cpp:170` — read from instrument override
instead. NoteOff currently calls `psyqo::SPU::silenceChannels` (hard cut) at
`musicsequencer.cpp:130, 261`; switch to a key-off that lets the release
envelope finish.

**No format bump if grouped with Phase 2.** Otherwise v29.

## Phase 4 — Voice allocator (§21 step 7) — shipped

`AudioManager::m_reservedForMusic` (`audiomanager.hh:106`,
`audiomanager.cpp:115-145`) already implements a coarse-grained "first N
voices reserved for music" policy. **Do not replace AudioManager; extend it.**
Add to AudioManager:

- `struct VoiceMeta { Owner owner; uint8_t priority; uint8_t channel; uint8_t note; uint32_t age; bool released; }`
- `int allocateVoice(Owner, priority)` — internal scan with steal policy
  (released → oldest-low-priority → drop).
- `play()` (`audiomanager.cpp:105`) becomes `allocateVoice(SFX, normalPri)`;
  `playOnVoice()` (`:131`) used by music stays explicit.
- `MusicSequencer::playByIndex` keeps calling `reserveVoices(n)` but allocator
  now tracks owner=Music for those slots.

This is purely a runtime change; **no splashpack bump.** The PolyphonyLimit
and Priority fields baked into instruments in Phase 2 finally get consulted
here.

## Phase 5 — Sound macros + sound families (§21 step 8) — shipped

`luaapi.cpp:300-318` registers the `Music` table; `Music_Play` at `:2102-2127`
calls `getMusicSequencer().playByIndex` which itself calls
`reserveVoices(channelCount)` — the "stops the previous sequence" semantic
strategy §14.1 calls out. Draw the line:

- **Keep `Music.Play` driving MusicSequencer** (one active sequence, voice
  reservation owned).
- **New `Sound.PlayMacro(name)` and `Sound.PlayFamily(name)`** registered as a
  separate `Sound` global (alongside `Audio`, not replacing it). Lua-facing
  entry points in `luaapi.cpp` near `:2099`. Backed by a new
  `psxsplash-main/src/soundmacro.{hh,cpp}` and `soundfamily.{hh,cpp}` that
  share AudioManager but never call `reserveVoices` — they pull from the SFX
  pool with priority-based allocation from Phase 4.
- New scaffold resources `PS1SoundMacro.cs` + `PS1SoundFamily.cs`. Splashpack
  v29: add `soundMacroTableOffset` and `soundFamilyTableOffset` in the file
  header.
- **psxlua per-script env caveat**: macro/family Lua state must use `_G.Sound`
  access only — the `Sound` global must be registered via
  `lua.setGlobal("Sound")` exactly like `Music` is at `luaapi.cpp:318`. No
  bare globals from scripts.

## Phase 6 — Layered/adaptive sequences (§21 step 9)

Out of scope until Phases 0-5 ship. Format addition (markers, layer flags) on
top of PS1M2; defer.

## Collision flags

- **Voice allocator vs `m_reservedForMusic`**: extend, don't replace. The
  current "first N reserved" model is a working implementation of strategy
  §10.6's `MusicVoiceBudget`. Phase 4 keeps the API and adds owner/priority
  metadata around it.
- **XA-routed clips inside instruments**: the new `PS1SampleRegion` references
  an `AudioClipName`. If that clip's `Route` (`PS1AudioClip.cs:94`) is XA, the
  instrument cannot play it — XA goes through `xaaudio.cpp`, not the SPU voice
  path. **Restriction**: instrument regions must reference SPU-routed clips
  only. Enforce at export time in `CollectMusicSequences` with `GD.PushError`
  when a region references a non-SPU clip; document in `PS1SampleRegion.cs`.
- **DrumKit choke groups**: implementable today on existing voice
  infrastructure — when a kit-mapped note plays, scan active music voices for
  matching choke group, key-off matches before the new note. Lives in
  MusicSequencer, not AudioManager.
- **psxlua per-script env**: nothing in this plan requires cross-script Lua
  globals. `Music`, `Sound`, `Audio` are all C-side `setGlobal` registrations,
  not author-side state. Safe.

## Shipped to date

- **Phase 0** (commit `92a925b`): scaffold resources, exporter
  diagnostics, stale-comment fixes.
- **Phase 1** (commit `11a28eb`): single-region instrument data path
  via optional `PS1MusicChannel.Instrument`, byte-equivalent to
  legacy direct binding for the no-instrument case.
- **Phase 2** (commit `59ef8b8`): multi-region instrument expansion +
  drum kit expansion in the exporter. No format bump — leans on the
  existing per-note `MidiNoteMin/Max` router.
- **Phase 2.5 Stage A** (commit `f8c44fc`): splashpack v28 with
  scene-wide instrument bank tables (`SPLASHPACKInstrumentRecord` etc.).
  Loader-but-unused; existing scenes must re-export.
- **Phase 2.5 Stage B** (commit `34aebf7`): exporter walks
  `PS1Scene.Instruments` / `PS1Scene.DrumKits`, validates SPU-routing,
  packs the bank into the splashpack.
- **Phase 2.5 Stage C** (commit `cfbaab2`): PS2M wire format magic +
  runtime resolution chain. PS2M sequences route NoteOn through
  channel→program→instrument→region→clip; ProgramChange events (kind
  4) swap the channel's current program mid-sequence. PS1M sequences
  unchanged.
- **Phase 2.5 ergonomics** (commit `249a097`): exporter pre-scans bank
  references and force-resolves Auto → SPU for instrument-backed clips.
  Author drops a Steinway WAV into a region without flipping the Route
  field manually.
- **Phase 4** (commit `ece6912`): voice allocator extension on
  AudioManager. New `VoiceMeta`+`allocateVoice` with three-pass steal
  policy (Free → Released → lower-priority steal → drop). Music slots
  pinned at MUSIC_PRIORITY (255) so SFX never evict them. Existing
  `m_reservedForMusic` semantics preserved.
- **Phase 5 Stage A** (commit `af2ed31`): authoring scaffold.
  `PS1SoundMacro` + `PS1SoundMacroEvent` + `PS1SoundFamily` resource
  types. Splashpack v29 with empty macro/family tables. `Sound`
  global registered with `PlayMacro`/`PlayFamily`/`StopAll` stubs.
- **Phase 5 Stage B** (commit `08b9fb9`): runtime + exporter wiring.
  - `psxsplash-main/src/soundmacro.{hh,cpp}` — `SoundMacroSequencer`
    with up to 8 active macro instances, per-macro MaxVoices cap,
    CooldownFrames anti-spam, fp12-accumulator frame ticking.
    Events dispatch via `AudioManager::play` at the macro's
    Priority; pitchOffset post-modifies `SPU_VOICES[ch].sampleRate`
    using the shared `MusicSequencer::pitchForOffset` table.
  - `psxsplash-main/src/soundfamily.{hh,cpp}` — `SoundFamily`
    stateless dispatcher with per-family lastVariantIdx +
    lastTriggerFrame state. Picks variant via `SceneManager::m_random`,
    applies pitch / volume / pan jitter, dispatches one-shot.
  - `SceneManager` owns one of each, init+setBank wired in
    `InitializeScene`, ticked alongside `m_musicSequencer`.
  - `LuaAPI::Sound_PlayMacro/PlayFamily/StopAll` replace stubs
    with real dispatch through SceneManager.
  - Exporter `CollectSoundBank` walks `PS1Scene.SoundMacros` and
    `PS1Scene.SoundFamilies`, validates SPU routing on every
    referenced clip, packs into `SoundMacros` / `SoundMacroEvents`
    / `SoundFamilies` / `FamilyClipIndices` flat tables.
  - `SplashpackWriter.WriteSoundBankSection` emits the four tables
    with the v29 header backfills already reserved in Stage A.
  - `MusicSequencer::pitchForOffset` is now public so siblings can
    pitch-shift one-shots without duplicating the 84-entry table.
  - `psxsplash-main/Makefile` includes `soundmacro.cpp` +
    `soundfamily.cpp` in `SRCS`.
- **Phase 2.6 — inline loop events** (commits `b7467ff` Godot side,
  `3d2deca` runtime side): MIDI marker / cue-point meta-events with
  text "loopStart" / "loopEnd" (case-insensitive — FFXIV PSF +
  RPG-Maker convention) parsed by `MidiParser` and emitted as PS2M
  event kinds 9 / 10. `MusicSequencer::dispatchEvent` records the
  LoopStart tick + event index, and on LoopEnd silences held notes
  and rewinds `m_currentTick` + `m_nextEventIdx` to the prior
  LoopStart. Lets sequences carry an intro that plays once before
  looping a body, instead of the header `loopStartTick` mechanism's
  "replay-from-start every cycle." No splashpack format bump —
  unknown event kinds were already default-skipped, so older
  runtimes load and play marker-tagged sequences unchanged (just
  without the loop-back).

## What to ship next

Phase 2.6 follow-ups: PitchBend (kind=5), Controller (kind=7), and
Marker-as-other-text (kind=8) event kinds. Runtime drum-kit dispatch
(today kits expand to bindings at export, but choke groups + per-drum
priority on `PS1DrumKit` are runtime-driven by design and need a
voice-scanner that watches active music voices for matching choke
groups).
