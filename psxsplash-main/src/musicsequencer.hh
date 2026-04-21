#pragma once

#include <stdint.h>

namespace psxsplash {

class AudioManager;

// On-disk format — see docs/sequenced-music-format.md.
struct MusicSequenceHeader {
    char magic[4];          // "PS1M"
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

private:
    static constexpr int MAX_SEQUENCES = 8;
    static constexpr int MAX_CHANNELS = 16;

    struct Sequence {
        const MusicSequenceHeader *header;
        const MusicChannelEntry *channels;
        const MusicEvent *events;
    };

    void dispatchEvent(const MusicEvent &e);
    void noteOn(uint8_t channel, uint8_t note, uint8_t velocity);
    void noteOff(uint8_t channel, uint8_t note);
    static uint16_t pitchForOffset(int semitoneOffset);

    AudioManager *m_audio = nullptr;
    Sequence m_sequences[MAX_SEQUENCES] = {};
    int m_sequenceCount = 0;

    const Sequence *m_active = nullptr;
    uint32_t m_currentTick = 0;
    uint32_t m_subTick12 = 0;       // fp12 fractional tick accumulator
    uint32_t m_ticksPerFrame12 = 0; // (bpm * ticksPerBeat / 60 / 60fps) << 12
    int m_nextEventIdx = 0;

    // Per-channel state. activeVoice == -1 means the channel has no
    // note playing right now.
    struct ChannelState {
        int8_t activeVoice;
        uint8_t activeNote;
        uint8_t volume;   // mirrors channel entry, mutable via channelVolume event
        uint8_t pan;
    };
    ChannelState m_channels[MAX_CHANNELS] = {};

    uint8_t m_masterVolume = 100;
};

} // namespace psxsplash
