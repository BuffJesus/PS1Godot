# PS1 True Sequenced Audio Strategy

**Project target:** PS1Godot / psxsplash / PS1-style chunked RPG  
**Focus:** evolving the current MIDI-to-sample sequencer into a more complete PS1-style sequenced audio system.

The current system is useful, but it is closer to:

```text
MIDI notes → mapped directly to PS1AudioClip samples
```

A more complete sequenced-audio system should become:

```text
Sequence data
  ↓
Channel/program state
  ↓
Instrument bank
  ↓
Sample regions / keymaps
  ↓
SPU voices with pitch, ADSR, volume, pan, priority
```

The goal is to reduce the need for many finished audio files by using:

```text
small reusable samples
+ instrument definitions
+ sequence data
+ pitch shifting
+ envelopes
+ looping
+ voice allocation
```

---

## 1. Current System Summary

The current system is best described as:

```text
custom MIDI-to-SPU sequenced music using existing PS1AudioClip samples
```

Current flow:

```text
Godot PS1MusicSequence
  points at .mid file
  has PS1MusicChannel bindings
        ↓
Exporter parses MIDI
        ↓
Serializer packs custom sequence binary
        ↓
Splashpack embeds sequence blobs
        ↓
Runtime MusicSequencer plays notes using PS1AudioClip samples
        ↓
Lua calls Music.Play("sequence_name")
```

This is already useful for:

- music
- jingles
- menu themes
- fanfares
- rhythmic loops
- simple MIDI-driven playback

But it is not yet a full sequenced audio system because the “instrument” concept is thin.

---

## 2. Target System

The target is a real instrument/sample-bank model.

Instead of:

```text
MIDI Channel 0 → inst_piano_c4
MIDI Channel 1 → inst_bass_c2
```

Use:

```text
MIDI Channel 0 → Program 0 → PianoInstrument
MIDI Channel 1 → Program 1 → BassInstrument
MIDI Channel 9 → DrumKitInstrument
```

Each instrument owns:

```text
sample regions
root notes
key ranges
velocity ranges
ADSR envelope
volume
pan
pitch bend range
loop mode
priority
polyphony limit
```

Runtime path:

```text
NoteOn(channel, note, velocity)
  ↓
channel has program/instrument
  ↓
instrument finds sample region for note/velocity
  ↓
sample is pitch-shifted from root note
  ↓
SPU voice starts with ADSR, pan, volume, priority
```

This is the key shift:

```text
MIDI event → channel program → instrument → sample region → SPU voice
```

---

# 3. Instruments

## 3.1 Why instruments matter

An instrument lets one or a few samples cover a wide musical range.

Without instruments, you tend to create many files:

```text
bass_c1
bass_d1
bass_e1
bass_f1
...
```

With instruments, you use a few root samples:

```text
bass_e2
bass_e3
```

and pitch-shift them over key ranges.

## 3.2 Instrument data

Suggested instrument definition:

```text
Instrument:
  InstrumentId
  Name
  Regions
  DefaultADSR
  Volume
  Pan
  Priority
  PolyphonyLimit
  PitchBendRange
```

Example:

```text
Instrument: SoftBell
  Region 1:
    Sample: bell_c4
    KeyRange: C3-B4
    RootKey: C4
    Loop: false
    ADSR: quick attack, medium decay

  Region 2:
    Sample: bell_c6
    KeyRange: C5-C7
    RootKey: C6
    Loop: false
```

---

# 4. Sample Regions / Keymaps

## 4.1 Concept

A sample region maps notes to samples.

```text
SampleRegion:
  SampleId
  KeyMin
  KeyMax
  VelocityMin
  VelocityMax
  RootKey
  TuneCents
  Volume
  Pan
  LoopStart
  LoopEnd
  LoopEnabled
  ADSROverride
```

## 4.2 Example: bass instrument

```text
BassInstrument:
  Region 1:
    Sample: bass_e2
    KeyRange: C1-B2
    RootKey: E2

  Region 2:
    Sample: bass_e3
    KeyRange: C3-B4
    RootKey: E3
```

## 4.3 Benefits

Sample regions allow:

- fewer source samples
- pitch-shifted playback
- multi-sampled instruments
- velocity layers later
- cleaner MIDI import
- reusable instrument banks

---

# 5. Program Changes

## 5.1 Why program changes matter

True MIDI-style sequencing expects channels to have programs/instruments.

Support event:

```text
ProgramChange(channel, programId)
```

Then sequence setup can be:

```text
channel 0 = flute
channel 1 = bass
channel 2 = strings
channel 9 = drum kit
```

## 5.2 Event type proposal

Add event types:

```text
0 = NoteOn
1 = NoteOff
2 = ChannelVolume
3 = ChannelPan
4 = ProgramChange
5 = PitchBend
6 = Tempo
7 = Controller
8 = Marker
9 = LoopStart
10 = LoopEnd
```

Even if tempo is flattened at export time, program changes and controller state should exist in the sequence model.

---

# 6. Drum Kits

## 6.1 Drum kits are special

Drums should not behave like pitched melodic instruments by default.

A drum kit maps MIDI notes to samples:

```text
DrumKit:
  note 36 → kick
  note 38 → snare
  note 42 → closed_hat
  note 46 → open_hat
  note 49 → crash
```

Each drum mapping can define:

```text
sample
volume
pan
priority
choke group
```

## 6.2 Choke groups

Useful for hi-hats:

```text
open_hat = choke group hats
closed_hat = choke group hats
```

When closed hat plays, it stops open hat.

## 6.3 Drum kit data

```text
DrumKit:
  KitId
  NoteMappings:
    MidiNote
    SampleId
    Volume
    Pan
    ChokeGroup
    Priority
```

---

# 7. ADSR Envelopes

## 7.1 Why ADSR matters

A sequencer is not just “play sample.”

Instrument envelopes matter.

Each instrument or sample region should define:

```text
Attack
Decay
Sustain
Release
```

## 7.2 Note behavior

```text
NoteOn:
  starts voice
  attack/decay/sustain envelope begins

NoteOff:
  release envelope begins
```

## 7.3 Example envelopes

### Short pluck

```text
Attack: fast
Decay: quick
Sustain: low
Release: short
```

### Pad

```text
Attack: slow
Decay: long
Sustain: medium
Release: long
```

### Organ

```text
Attack: fast
Decay: minimal
Sustain: full
Release: controlled by note-off
```

## 7.4 Why this saves space

With ADSR, a short loopable sample can become:

- a short note
- a long note
- a fading pad
- a percussive pluck

without needing separate rendered files.

---

# 8. Looping Sample Support

## 8.1 Why loop points matter

Loop points are one of the biggest wins for sequenced audio.

They allow small samples to sustain long notes.

Example:

```text
flute_attack_loop.vag
  attack portion
  loopStart
  loopEnd
```

Runtime:

```text
NoteOn:
  play attack
  continue looping sustain area

NoteOff:
  release envelope
```

## 8.2 Good looped instrument types

- strings
- pads
- organs
- winds
- synths
- drones
- ambience tones
- sustained basses

## 8.3 Sample metadata

```text
Sample:
  SampleId
  AudioClipId
  RootKey
  TuneCents
  LoopStart
  LoopEnd
  LoopEnabled
  BaseVolume
```

---

# 9. Channel State

## 9.1 Persistent per-channel state

Each sequence channel should track:

```text
ChannelState:
  programId
  volume
  pan
  expression
  pitchBend
  pitchBendRange
  sustainPedal
  priority
```

## 9.2 Final playback calculation

```text
finalVolume =
  noteVelocity
  * channelVolume
  * expression
  * instrumentVolume
  * regionVolume

finalPan =
  channelPan
  + instrumentPan
  + regionPan

finalPitch =
  notePitch
  + pitchBend
  + regionTune
```

This makes MIDI/controller-style data useful.

---

# 10. Voice Allocation

## 10.1 Why voice allocation matters

The PS1 SPU has limited simultaneous voices.

A true sequencer can easily consume all available voices if there is no policy.

## 10.2 Current behavior

The current music sequencer reserves a fixed number of voices for music.

That is safe but rigid.

## 10.3 Target voice allocator

Add a central voice allocator:

```text
VoiceAllocator:
  total voices: 24
  reserved music voices optional
  reserved SFX voices optional
  priority-based stealing
  oldest/released/quietest stealing
  owner tracking
```

## 10.4 Voice metadata

```text
Voice:
  owner: Music | SFX | Macro | Ambience
  priority
  channel
  note
  age
  released
  volume
```

## 10.5 Steal policy

When all voices are used:

```text
1. steal lowest-priority released voice
2. else steal oldest low-priority voice
3. else drop new note
```

## 10.6 RPG-friendly voice profile

```text
MusicVoiceBudget:
  maxMusicVoices = 12
  minSfxVoices = 8
  emergencyVoices = 4
```

This prevents music from blocking critical gameplay sounds.

---

# 11. Sequence Banks

## 11.1 MIDI as import, not runtime

MIDI is a good source/import format.

Runtime should use a compact PS1-friendly sequence format.

Pipeline:

```text
.mid
  ↓ import/convert
.ps1seq / PS1M
  ↓ runtime
sequencer
```

## 11.2 Sequence bank layout

```text
SequenceBank:
  instruments used
  sample bank references
  event streams
  loop points
  markers
  channel defaults
```

## 11.3 Event stream

Use delta times to save space:

```text
SequenceEvent:
  DeltaTicks
  EventType
  Channel
  Param0
  Param1
  Param2
```

## 11.4 Optional pattern system

Later, consider tracker-style patterns:

```text
Pattern 0: drums intro
Pattern 1: drums loop
Pattern 2: bass loop
Pattern 3: melody A

Song order:
  0, 1, 1, 2, 3, 1, 1
```

This can be smaller for looping music, but it is not needed first.

Start with compact event streams.

---

# 12. Game Music Commands

RPG music needs more than play/stop.

Possible Lua-facing API:

```lua
Music.Play("forest_theme")
Music.Stop()
Music.Pause()
Music.Resume()
Music.SetVolume(100)
Music.FadeTo(0, 120)
Music.SetSection("battle")
Music.QueueSection("victory")
Music.SetTempoScale(0.9)
```

## 12.1 Sequence markers

Useful markers:

```text
loop_start
loop_end
intro_end
battle_start
danger_layer
victory_sting
section_a
section_b
```

Markers enable looping, transitions, and future adaptive music.

---

# 13. Layered / Adaptive Sequences

## 13.1 Future feature

A more advanced sequencer can support layers:

```text
Base layer: drums + bass
Layer 1: melody
Layer 2: danger percussion
Layer 3: low drone
```

Lua:

```lua
Music.SetLayer("danger", true)
Music.SetLayer("melody", false)
```

## 13.2 RPG uses

- town calm / suspicious
- field explore / danger
- dungeon low tension / high tension
- battle intro → loop → victory
- boss phases

## 13.3 Warning

Layering needs careful voice budgeting.

Do not add adaptive layers until basic instruments, loops, envelopes, and voice allocation are stable.

---

# 14. Separate Music From Sound Macros

## 14.1 Do not overload Music.Play

The current `Music.Play(...)` system stops the previous sequence.

That is fine for music, but bad for one-shot SFX macros.

Use separate systems:

```lua
Music.Play("forest_theme")
Sound.PlayMacro("chest_open")
Sound.PlayFamily("footstep_grass")
```

## 14.2 Shared lower layer

These systems can share:

```text
sample bank
instrument definitions
voice allocator
pitch helpers
volume/pan helpers
ADSR helpers
SPU playback
```

## 14.3 Separate high-level systems

```text
MusicSequencer
SoundMacroSequencer
SoundFamilySystem
```

---

# 15. Sound Macros

## 15.1 Purpose

A sound macro is a tiny sequence of sample events.

It replaces a baked composite SFX file.

Example:

```text
SoundMacro: chest_open
  frame 0: wood_creak, pitch -2, volume 100
  frame 7: metal_click, pitch +1, volume 90
  frame 12: item_sparkle, pitch +0, volume 80
```

Lua:

```lua
Sound.PlayMacro("chest_open")
```

## 15.2 Metadata

```text
SoundMacro:
  MacroId
  Events
  MaxVoices
  Priority
  CooldownFrames
```

```text
SoundMacroEvent:
  Frame
  SampleId
  Volume
  Pan
  PitchOffset
  Flags
```

## 15.3 Good uses

- chest open
- door mechanism
- UI error pattern
- magic cast layers
- combat impact layers
- machine sounds
- short environmental events

---

# 16. Sound Families

## 16.1 Purpose

A sound family creates variation from a small set of samples.

Example:

```text
SoundFamily: footsteps_grass
  Samples:
    grass_step_01
    grass_step_02
  PitchRange: -3..+3 semitones/cents config
  VolumeRange: 90..115
  PanJitter: 6
  AvoidRepeat: true
  CooldownFrames: 3
```

Lua:

```lua
Sound.PlayFamily("footsteps_grass")
```

## 16.2 Good uses

- footsteps
- impacts
- creature barks
- small UI ticks
- debris
- cloth movement
- ambient one-shots

## 16.3 Animation event use

Animation event:

```text
footstep_left
```

Runtime maps:

```text
current surface = grass
footstep_left → SoundFamily footsteps_grass
```

One walk animation can work on grass, stone, wood, metal, etc.

---

# 17. Proposed Data Model

## 17.1 Sample

```text
Sample:
  SampleId
  AudioClipId
  RootKey
  TuneCents
  LoopStart
  LoopEnd
  LoopEnabled
  BaseVolume
```

## 17.2 Instrument

```text
Instrument:
  InstrumentId
  Name
  Regions
  DefaultADSR
  Volume
  Pan
  Priority
  PolyphonyLimit
  PitchBendRange
```

## 17.3 SampleRegion

```text
SampleRegion:
  SampleId
  KeyMin
  KeyMax
  VelocityMin
  VelocityMax
  RootKey
  ADSR override optional
  Volume
  Pan
```

## 17.4 DrumKit

```text
DrumKit:
  KitId
  NoteMappings:
    MidiNote
    SampleId
    Volume
    Pan
    ChokeGroup
```

## 17.5 Sequence

```text
Sequence:
  SequenceId
  TicksPerBeat
  BPM
  LoopStartTick
  LoopEndTick
  EventCount
  UsedInstrumentIds
```

## 17.6 SequenceEvent

```text
SequenceEvent:
  DeltaTicks
  EventType
  Channel
  Param0
  Param1
  Param2
```

Event types:

```text
NoteOn
NoteOff
ProgramChange
ChannelVolume
ChannelPan
Expression
PitchBend
Tempo
Marker
LoopStart
LoopEnd
Controller
```

---

# 18. Editor Tools

## 18.1 Instrument editor

Example:

```text
Instrument: SoftFlute
  Regions:
    flute_c4, C3-B4, root C4
    flute_c6, C5-C7, root C6
  ADSR:
    Attack 4
    Decay 12
    Sustain 80
    Release 18
  Loop: enabled for regions
```

## 18.2 Drum kit editor

Example:

```text
DrumKit: BasicKit
  36 Kick
  38 Snare
  42 Closed Hat
  46 Open Hat, choke group hats
```

## 18.3 Sequence inspector

Show:

```text
MIDI source
converted PS1 sequence size
channels used
programs used
max simultaneous notes estimate
voice budget warning
loop points
missing instruments
controller events used
unsupported MIDI events
```

## 18.4 Bank report

Example:

```text
MusicBank: forest_music
  Samples: 12
  Instruments: 7
  Sequences: 3
  SPU sample bytes: 92 KB
  Sequence bytes: 8 KB
  Worst-case voices: 10
```

---

# 19. Exporter Warnings

Add warnings like:

```text
Sequence "forest_theme" uses 18 simultaneous notes.
Music voice budget is 12.
```

```text
MIDI program change references missing instrument 41.
```

```text
Instrument "SoftStrings" has no loop points.
Long notes may require large samples or cut off early.
```

```text
Sample "bass_e2" covers 4 octaves.
Pitch shifting may sound unnatural.
```

```text
Drum note 42 has no mapping in DrumKit "BasicKit".
```

```text
Open hat and closed hat share no choke group.
```

```text
Music sequence uses 16 voices, leaving only 8 for SFX.
```

---

# 20. Migration Path From Current System

Do not throw away the current sequencer.

## Phase 1 — Clean current comments and reporting

- Fix stale channel comments.
- Add sequence report:
  - channel count
  - reserved voices
  - clip bindings
  - estimated voice pressure
  - missing clips

## Phase 2 — Add instrument metadata, still using current clips

Change mapping from:

```text
channel binding → clip
```

to:

```text
channel binding → instrument
instrument → one sample region → PS1AudioClip
```

This preserves current behavior while making the data model expandable.

## Phase 3 — Add key ranges and root notes

One instrument can use multiple sample regions.

## Phase 4 — Add program changes

Let MIDI program changes select instruments.

## Phase 5 — Add ADSR and loop metadata

Enable sustained instruments and real note-off behavior.

## Phase 6 — Add better voice allocator

Options:

```text
keep reserved music voices but add priority/polyphony
or
central allocator with music/SFX budgets
```

## Phase 7 — Add sound macros and families

Reuse the same sample/instrument layer for SFX composition.

## Phase 8 — Add layered/adaptive music

Only after the basics are stable.

---

# 21. Recommended Implementation Order

## Step 1 — Reporting and correctness

```text
fix stale comments
add sequence diagnostics
show voice pressure
show missing clip/instrument warnings
```

## Step 2 — Instrument resource scaffold

```text
PS1Instrument
PS1SampleRegion
PS1DrumKit
```

## Step 3 — One-region instruments

```text
instrument points to one PS1AudioClip
same behavior as current channel binding
```

## Step 4 — Multi-region keymaps

```text
key ranges
root notes
pitch shifting
```

## Step 5 — Program changes and channel state

```text
program change events
channel volume
channel pan
pitch bend
expression
```

## Step 6 — ADSR and loop points

```text
instrument envelope
note-off release
looped sustain samples
```

## Step 7 — Voice allocator improvements

```text
music voice cap
SFX reserve
priority stealing
polyphony limit
```

## Step 8 — Sound macros/families

```text
Sound.PlayMacro
Sound.PlayFamily
macro/family editors
```

## Step 9 — Adaptive layers

```text
sections
markers
layers
fade/crossfade
```

---

# 22. IDE-Agent Prompt

Use this prompt to implement the first safe slice.

```text
You are helping me evolve the PS1Godot / psxsplash sequenced audio system from a MIDI-note-to-sample player into a more complete PS1-style sequenced audio engine.

Current behavior:
- PS1MusicSequence points to a MIDI file.
- PS1MusicChannel bindings map MIDI track/channel/note ranges to existing PS1AudioClip samples.
- Exporter serializes a custom sequence blob.
- Runtime MusicSequencer plays notes using PS1AudioClip samples.
- Music.Play starts one active sequence.

Goal:
Add documentation, metadata, reporting, and safe scaffolding for true sequenced audio without breaking the current music system.

Long-term target:
MIDI event → channel program → instrument → sample region → SPU voice

Implement or scaffold:
1. Fix stale comments/documentation around sequence/channel limits.
2. Add sequence diagnostics/reporting:
   - sequence name
   - MIDI file
   - channel/binding count
   - estimated reserved voices
   - max simultaneous notes estimate if feasible
   - referenced clips
   - missing clips
   - unsupported MIDI events if detectable
3. Add metadata/resource proposal or scaffolding for:
   - PS1Instrument
   - PS1SampleRegion
   - PS1DrumKit
   - RootKey
   - KeyMin/KeyMax
   - TuneCents
   - LoopStart/LoopEnd
   - LoopEnabled
   - ADSR
   - Instrument volume/pan/priority
4. Keep one-region instruments compatible with the current clip-binding behavior.
5. Do not rewrite the runtime sequencer yet unless the change is tiny and safe.
6. Do not overload Music.Play for one-shot SFX macros.
7. Document future separate systems:
   - SoundMacroSequencer
   - SoundFamilySystem
8. Add warnings for:
   - too many reserved music voices
   - missing instrument/clip references
   - MIDI program changes with no instrument mapping
   - drum notes with no mapping
   - long sustained notes using non-looped samples
   - sequence leaving too few voices for SFX

Rules:
- Preserve existing Music.Play behavior.
- Preserve existing PS1MusicSequence assets.
- Keep changes small and reviewable.
- Clearly separate implemented behavior from scaffolded metadata.
- Do not fake ADSR/loop/program-change support if runtime does not use it yet.
- Update docs and comments so future agents do not misunderstand the system.

Final response:
- Summary
- Files changed
- Implemented
- Scaffolded only
- How to test
- Risks/TODOs
```

---

# 23. Bottom Line

Your current system is already a useful MIDI-to-sample music sequencer.

To turn it into true sequenced audio, add:

```text
instrument banks
sample keymaps
root notes and pitch shifting
ADSR envelopes
loop points
program changes
drum kits
channel state
voice allocation / voice stealing
sequence markers and loop points
optional layers
separate sound macros/families for SFX
```

The most important architectural change is:

```text
from:
  MIDI channel/note → PS1AudioClip

to:
  MIDI channel/program → Instrument → SampleRegion → PS1AudioClip/SPU voice
```

That shift gives PS1Godot the foundation for real PS1-style sequenced music, reusable instrument banks, smaller audio footprints, and eventually SFX macros/families that reduce the need for piles of finished WAV files.
