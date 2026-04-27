#pragma once

#include <stdint.h>

namespace psxsplash {

class AudioManager;
struct SPLASHPACKInstrumentRecord;
struct SPLASHPACKRegionRecord;
struct SPLASHPACKDrumKitRecord;
struct SPLASHPACKDrumMappingRecord;

// On-disk format — see docs/sequenced-music-format.md.
//
// Two magic values are accepted:
//   "PS1M" — legacy format. Channel direct-binding: each channel
//            entry's audioClipIndex selects the SPU sample. Pitch
//            shift derived from baseNoteMidi.
//   "PS2M" — bank-driven format (Phase 2.5). Same 16-byte header,
//            same ChannelEntry layout, BUT followed by a parallel
//            u8[channelCount] default-program table (ProgramId per
//            channel at sequence start). Plus event kind 4
//            (ProgramChange) is valid; the runtime swaps the
//            channel's current program mid-sequence and routes
//            NoteOn through the scene-wide instrument bank
//            (SPLASHPACKInstrumentRecord / SPLASHPACKRegionRecord
//            in splashpack.hh) instead of the channel entry's
//            audioClipIndex.
struct MusicSequenceHeader {
    char magic[4];          // "PS1M" or "PS2M"
    uint16_t bpm;
    uint16_t ticksPerBeat;
    uint8_t channelCount;
    uint8_t pad0;
    uint16_t eventCount;
    uint32_t loopStartTick; // 0xFFFFFFFF = no loop
};
static_assert(sizeof(MusicSequenceHeader) == 16, "MusicSequenceHeader must be 16 bytes");

struct MusicChannelEntry {
    uint16_t audioClipIndex; // index into the splashpack's audio clip table
    uint8_t  baseNoteMidi;   // sample's native MIDI note (e.g. 60 = middle C)
    uint8_t  volume;         // 0-127, per-channel multiplier
    uint8_t  pan;            // 0=L, 64=center, 127=R
    uint8_t  flags;          // bit0=loopSample, bit1=percussion (no pitch shift)
    // 0 = no choke; non-zero = a hit on any channel sharing this id
    // silences this channel's currently-held note. Canonical use is
    // open-hat / closed-hat sharing one group so a closed strike cuts
    // a ringing open hi-hat. Repurposes the former 2-byte pad: old
    // splashpack bins zero-fill these bytes, so legacy sequences read
    // chokeGroup = 0 = "no choke" — backwards-compatible without a
    // format bump.
    uint8_t  chokeGroup;
    uint8_t  pad;            // reserved (was upper byte of pad)
};
static_assert(sizeof(MusicChannelEntry) == 8, "MusicChannelEntry must be 8 bytes");

struct MusicEvent {
    uint32_t tick;
    uint8_t  channel;
    uint8_t  kind;   // 0=noteOn, 1=noteOff, 2=channelVolume, 3=channelPan
    uint8_t  data1;
    uint8_t  data2;
};
static_assert(sizeof(MusicEvent) == 8, "MusicEvent must be 8 bytes");

// Runtime sequencer. One per scene; the active sequence is whatever
// Music.Play("name") most recently triggered. Walks events on tick(),
// dispatches note-on/note-off to SPU voices via AudioManager + direct
// SPU register writes for pitch control.
class MusicSequencer {
public:
    void init(AudioManager *audio);

    // v28+: hand the scene-wide instrument bank (loaded by
    // SplashPackLoader into SplashpackSceneSetup) to the sequencer so
    // PS2M sequences can resolve channel→program→instrument→region→clip
    // at NoteOn time. Pass nullptr/0 for scenes without a bank — PS2M
    // sequences then fall back to the legacy ChannelEntry.audioClipIndex
    // path. PS1M sequences ignore the bank regardless.
    void setBank(const SPLASHPACKInstrumentRecord*  instruments,
                 uint16_t                           instrumentCount,
                 const SPLASHPACKRegionRecord*      regions,
                 uint16_t                           regionCount,
                 const SPLASHPACKDrumKitRecord*     drumKits,
                 uint16_t                           drumKitCount,
                 const SPLASHPACKDrumMappingRecord* drumMappings,
                 uint16_t                           drumMappingCount);

    // Bind a sequence loaded from the splashpack. Pointer must remain
    // valid for the sequence's lifetime (lives in splashpack data).
    void registerSequence(int index, const uint8_t *data, uint32_t sizeBytes);

    // Lookup helpers used by the Lua API.
    int  findByName(const char *name) const;          // not yet wired (names live in name table)
    bool playByIndex(int index, uint8_t masterVolume); // returns true if started
    void stop();
    bool isPlaying() const { return m_active != nullptr; }

    void setMasterVolume(uint8_t vol) { m_masterVolume = vol; }
    uint8_t getMasterVolume() const { return m_masterVolume; }

    // Current beat (sequence tick / ticksPerBeat). 0 when idle.
    int getBeat() const;

    // 16-bit FNV-1a hash of the most recent kind=8 marker text fired
    // by the active sequence. 0 if none has fired yet (also reset on
    // playByIndex). Lua polls via Music.GetLastMarkerHash() and
    // compares against Music.MarkerHash("name"). Hash function must
    // match godot-ps1/addons/ps1godot/exporter/PS1MSerializer.cs
    // MarkerHash16 bit-for-bit.
    uint16_t getLastMarkerHash() const { return m_lastMarkerHash; }

    // Called once per scene tick. dt12 is fp12 frame delta (1.0 = 1 frame).
    void tick(int32_t dt12);

    // Look up an SPU sample-rate ratio for a semitone offset relative to the
    // sample's authored pitch. fp12 result (0x1000 = native rate). Public so
    // siblings (SoundMacroSequencer, SoundFamily) can pitch-shift one-shots
    // without duplicating the 84-entry table.
    static uint16_t pitchForOffset(int semitoneOffset);

private:
    static constexpr int MAX_SEQUENCES = 8;
    // Matches SPU voice cap (audiomanager.hh MAX_VOICES=24). Voice
    // reservation is dynamic per-scene — scenes that only declare N
    // music channels still leave (24-N) voices for SFX, so this cap
    // doesn't starve SFX in practice. IMPORTANT: bumping this value
    // changes the layout of SceneManager (which owns MusicSequencer).
    // The psxsplash Makefile doesn't track header deps, so a naked
    // `make all` after editing this constant leaves stale .o files
    // compiled against the old size → ABI mismatch between TUs → GPU
    // state corruption → crash in sendPrimitive<FastFill> on the first
    // frame. Always `make clean && make` after touching this.
    static constexpr int MAX_CHANNELS = 24;

    struct Sequence {
        const MusicSequenceHeader *header;
        const MusicChannelEntry *channels;
        const MusicEvent *events;
        // PS2M-only: parallel u8[channelCount] table sitting between
        // channels and events, holding each channel's default ProgramId
        // at sequence start. Null for PS1M sequences.
        const uint8_t *channelPrograms;
        bool isPS2M;  // magic was "PS2M" (vs "PS1M")
    };

    // Returns true if the event triggered an inline loop-back (kind=10
    // jumping to the prior LoopStart). The caller must NOT advance
    // m_nextEventIdx in that case — the dispatch already rewound it.
    bool dispatchEvent(const MusicEvent &e);
    void noteOn(uint8_t channel, uint8_t note, uint8_t velocity);
    void noteOff(uint8_t channel, uint8_t note);
    void dispatchPitchBend(uint8_t channel, uint8_t lsb, uint8_t msb);
    // Silences any held music-channel notes at a loop seam. Used both
    // by the end-of-stream loop (header loopStartTick) and the inline
    // LoopEnd event-driven loop (kinds 9/10).
    void silenceLoopSeam();
    // 14-bit MIDI pitch-bend value (0..0x3FFF, 0x2000 = center) → fp12
    // ratio. Default ±2-semitone range. Linearly interpolates the
    // existing pitchForOffset table between adjacent integer slots.
    static uint16_t bendRatio12From14(uint16_t value14);

    // PS2M bank dispatch: walk the scene-wide instrument bank to find
    // the region matching (channel.currentProgram, note, velocity).
    // Returns nullptr if no instrument matches the program OR no
    // region matches the (note, velocity) within the picked instrument.
    const SPLASHPACKRegionRecord* resolveBankRegion(uint8_t programId, uint8_t note, uint8_t velocity) const;

    AudioManager *m_audio = nullptr;
    Sequence m_sequences[MAX_SEQUENCES] = {};
    int m_sequenceCount = 0;

    // v28+: scene-wide instrument bank, set by SceneManager via
    // setBank() at scene init. nullptr → no bank for this scene; PS2M
    // sequences fall back to legacy direct-binding.
    const SPLASHPACKInstrumentRecord*  m_bankInstruments = nullptr;
    const SPLASHPACKRegionRecord*      m_bankRegions = nullptr;
    const SPLASHPACKDrumKitRecord*     m_bankDrumKits = nullptr;
    const SPLASHPACKDrumMappingRecord* m_bankDrumMappings = nullptr;
    uint16_t m_bankInstrumentCount = 0;
    uint16_t m_bankRegionCount = 0;
    uint16_t m_bankDrumKitCount = 0;
    uint16_t m_bankDrumMappingCount = 0;

    const Sequence *m_active = nullptr;
    uint32_t m_currentTick = 0;
    uint32_t m_subTick12 = 0;       // fp12 fractional tick accumulator
    uint32_t m_ticksPerFrame12 = 0; // (bpm * ticksPerBeat / 60 / 60fps) << 12
    int m_nextEventIdx = 0;

    // Inline loop bracket state, populated by event kind=9 (LoopStart)
    // and consumed by kind=10 (LoopEnd). 0xFFFFFFFFu = no LoopStart seen
    // yet → kind=10 is a no-op (header loopStartTick still applies as
    // an end-of-stream fallback). Reset on sequence start/stop.
    uint32_t m_inlineLoopStartTick = 0xFFFFFFFFu;
    int      m_inlineLoopStartIdx  = 0;

    // 16-bit hash of the most recent kind=8 marker that fired. 0 = no
    // marker has fired yet on the active sequence. Reset by
    // playByIndex.
    uint16_t m_lastMarkerHash = 0;

    // Shared CC#1 modulation LFO phase. uint8 wraps naturally so the
    // sine table indexes phase>>4. Advances by a fixed step per
    // tick(), giving an LFO rate independent of song tempo. Reset on
    // playByIndex.
    uint8_t m_lfoPhase = 0;

    // 16-entry signed LFO sine table. Each entry is an fp12 pitch-
    // ratio offset (added to 0x1000 = 1.0 to produce the final ratio
    // multiplier). Peak ±0x80 ≈ ±3.1% rate variation ≈ ±55 cents at
    // full depth — typical synth vibrato range. Scaled by depth/127
    // per channel.
    static const int16_t LFO_TABLE[16];

    // Per-channel state. activeVoice == -1 means the channel has no
    // note playing right now.
    //
    // PS2M extras (currentProgram, lastClipIndex) are unused on PS1M
    // sequences but always live in the struct — keeps the runtime
    // path branch-free when switching formats and costs 2 bytes per
    // channel at MAX_CHANNELS=24 → 48 B total. Worth it.
    struct ChannelState {
        int8_t activeVoice;
        uint8_t activeNote;
        uint8_t volume;   // mirrors channel entry, mutable via channelVolume event
        uint8_t pan;
        uint8_t currentProgram;  // PS2M: the program the channel is currently set to
        uint8_t lastClipIndex;   // PS2M: clip resolved at the last NoteOn (for pitch reuse)
        // CC#11 Expression — secondary volume controller (0-127, 127 =
        // no attenuation). Multiplied into the noteOn volume formula
        // alongside CC#7 ChannelVolume. Like CC#7, applies at the
        // next noteOn (no live retune of held notes).
        uint8_t expression;
        // CC#64 Sustain pedal. While sustainHeld, noteOff events on
        // this channel set noteOffPending instead of silencing — the
        // held note rings until the pedal releases. A subsequent
        // noteOn clears noteOffPending (the new note implicitly
        // replaces the held one in mono-per-channel dispatch). Pedal
        // release fires the deferred silence if pending.
        uint8_t sustainHeld;
        uint8_t noteOffPending;
        // CC#1 Modulation wheel. 0 = no vibrato, 127 = full depth.
        // The sequencer owns a single shared LFO phase; per-tick the
        // dispatcher walks held melodic voices with non-zero modDepth
        // and re-pitches the SPU sample rate with the LFO contribution
        // multiplied by depth/127. Percussion ignores modulation.
        uint8_t modDepth;
        // Pre-bend SPU rate of the currently-held note, captured at noteOn
        // after the note→base pitch shift. kind=5 (PitchBend) re-multiplies
        // this by pitchBendRatio12 / 4096 instead of re-running the shift,
        // so live bends don't drift the pitch on re-trigger.
        uint16_t noteBaseRate;
        // fp12 ratio (0x1000 = no bend). Updated by kind=5 events.
        // Persists across notes — a bend from a previous note stays in
        // effect until explicitly reset by another bend event.
        uint16_t pitchBendRatio12;
    };
    ChannelState m_channels[MAX_CHANNELS] = {};

    uint8_t m_masterVolume = 100;
};

} // namespace psxsplash
