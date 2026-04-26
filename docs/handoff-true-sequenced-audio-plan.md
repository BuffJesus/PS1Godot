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

## Phase 2 — Multi-region keymaps + program changes + drum kits (§21 steps 4-5)

This is the format change. Group steps 4 and 5 into one **splashpack v28** bump.

- New PS1M2 sequence format (or in-place: bump magic `PS1M` → `PS2M`, add
  `instrumentBankOffset` to header) — recommend a sibling format because
  `MusicSequenceHeader` is 16 bytes load-bearing (`musicsequencer.hh:19`). Add:
  - Instrument bank table per sequence (Instruments[], Regions[], DrumKits[]).
  - Channel entries gain `programId` instead of (or alongside) raw
    `audioClipIndex`.
  - Event kinds 4 (ProgramChange), 5 (PitchBend), 7 (Controller), 8 (Marker),
    9 (LoopStart), 10 (LoopEnd) per strategy §5.2.
- Runtime (`musicsequencer.cpp`): teach `dispatchEvent` the new kinds;
  `noteOn` resolves channel→program→instrument→region→clip; pitch shift uses
  `note - region.RootKey` instead of `note - cfg.baseNoteMidi` at `:243`.
- Splashpack header: bump version 27→28; add `static_assert` for new size; add
  Instrument/Region/DrumKit table offsets in `SPLASHPACKFileHeader`
  (`splashpack.cpp:21-123`). Loader (`splashpack.cpp:154`) raises minimum to
  28; writer (`SplashpackWriter.cs:391` `WriteMusicSection`) emits both old
  and new tables for one transitional version, then drops the old.

**Green-build checkpoint:** `PS1MusicSequence` assets re-export and play;
sequences using only one-region instruments produce wire-equivalent runtime
behaviour.

## Phase 3 — ADSR + loop metadata wired to SPU hardware (§21 step 6)

Hardware ADSR is already free — it's a 32-bit register pair
(`audiomanager.hh:14` `DEFAULT_ADSR=0x000A000F`, written at
`audiomanager.cpp:170-183`). Cost: pack the per-instrument ADSR into PS1M2
region/instrument records (already in Phase 2 if grouped) and stop using
`DEFAULT_ADSR` at `audiomanager.cpp:170` — read from instrument override
instead. NoteOff currently calls `psyqo::SPU::silenceChannels` (hard cut) at
`musicsequencer.cpp:130, 261`; switch to a key-off that lets the release
envelope finish.

**No format bump if grouped with Phase 2.** Otherwise v29.

## Phase 4 — Voice allocator (§21 step 7) — collision risk

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

## Phase 5 — Sound macros + sound families (§21 step 8) — Music.Play vs Sound.PlayMacro split

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

## What to ship first

Land Phase 0 as one commit: stale-comment fixes, exporter diagnostics, three
scaffold-only `[GlobalClass]` resources (`PS1Instrument`, `PS1SampleRegion`,
`PS1DrumKit`), exporter warnings for the six conditions in strategy §19, and
a doc append to `docs/sequenced-music-format.md` linking the strategy. Zero
runtime change, zero format bump, zero risk to existing scenes — but the
scaffold gives Phase 1 something to build on without re-litigating the data
model. Phase 1 (one-region instruments wired through the exporter, still
emitting v27 PS1M) is the second commit.
