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
    uint16_t pad;
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

    void dispatchEvent(const MusicEvent &e);
    void noteOn(uint8_t channel, uint8_t note, uint8_t velocity);
    void noteOff(uint8_t channel, uint8_t note);

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
    };
    ChannelState m_channels[MAX_CHANNELS] = {};

    uint8_t m_masterVolume = 100;
};

} // namespace psxsplash
