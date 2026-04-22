#include "musicsequencer.hh"

#include "audiomanager.hh"
#include "common/hardware/spu.h"
#include <psyqo/spu.hh>
#include <psyqo/xprintf.h>

namespace psxsplash {

// 84-entry pitch lookup: 7 octaves × 12 semitones, indexed by
// (semitoneOffset + 36). s_pitchTable[36] = 0x1000 (native rate),
// each step = exact 12th root of 2 in fp12. Capped at 0x3FFF (the
// SPU pitch register max).  Precomputed in tools/ — runtime would
// need libm or a high-precision iterative scheme to derive these
// accurately, both of which we avoid by baking the table.
static const uint16_t s_pitchTable[84] = {
    0x0200, 0x021E, 0x023F, 0x0261, 0x0285, 0x02AB, 0x02D4, 0x02FF,
    0x032D, 0x035D, 0x0390, 0x03C7, 0x0400, 0x043D, 0x047D, 0x04C2,
    0x050A, 0x0557, 0x05A8, 0x05FE, 0x0659, 0x06BA, 0x0721, 0x078D,
    0x0800, 0x087A, 0x08FB, 0x0983, 0x0A14, 0x0AAE, 0x0B50, 0x0BFD,
    0x0CB3, 0x0D74, 0x0E41, 0x0F1A, 0x1000, 0x10F4, 0x11F6, 0x1307,
    0x1429, 0x155C, 0x16A1, 0x17F9, 0x1966, 0x1AE9, 0x1C82, 0x1E34,
    0x2000, 0x21E7, 0x23EB, 0x260E, 0x2851, 0x2AB7, 0x2D41, 0x2FF2,
    0x32CC, 0x35D1, 0x3904, 0x3C68, 0x3FFF, 0x3FFF, 0x3FFF, 0x3FFF,
    0x3FFF, 0x3FFF, 0x3FFF, 0x3FFF, 0x3FFF, 0x3FFF, 0x3FFF, 0x3FFF,
    0x3FFF, 0x3FFF, 0x3FFF, 0x3FFF, 0x3FFF, 0x3FFF, 0x3FFF, 0x3FFF,
    0x3FFF, 0x3FFF, 0x3FFF, 0x3FFF,
};

void MusicSequencer::init(AudioManager *audio) {
    m_audio = audio;
    m_active = nullptr;
    m_sequenceCount = 0;
    m_currentTick = 0;
    m_subTick12 = 0;
    m_nextEventIdx = 0;
    m_masterVolume = 100;
    for (int i = 0; i < MAX_CHANNELS; i++) {
        m_channels[i].activeVoice = -1;
        m_channels[i].activeNote = 0;
        m_channels[i].volume = 100;
        m_channels[i].pan = 64;
    }
}

uint16_t MusicSequencer::pitchForOffset(int semitoneOffset) {
    int idx = semitoneOffset + 36;
    if (idx < 0) idx = 0;
    if (idx > 83) idx = 83;
    return s_pitchTable[idx];
}

void MusicSequencer::registerSequence(int index, const uint8_t *data, uint32_t sizeBytes) {
    if (index < 0 || index >= MAX_SEQUENCES) return;
    if (!data || sizeBytes < sizeof(MusicSequenceHeader)) return;

    auto *hdr = reinterpret_cast<const MusicSequenceHeader *>(data);
    if (__builtin_memcmp(hdr->magic, "PS1M", 4) != 0) {
        printf("[music] sequence %d bad magic\n", index);
        return;
    }

    // Bail before reinterpreting past the buffer. Exporter always writes a
    // matching size; this catches splashpack corruption or future format
    // drift with a single printf instead of silently UB-reading.
    uint32_t expected = sizeof(MusicSequenceHeader)
                      + (uint32_t)sizeof(MusicChannelEntry) * hdr->channelCount
                      + (uint32_t)sizeof(MusicEvent) * hdr->eventCount;
    if (sizeBytes < expected) {
        printf("[music] sequence %d truncated (have %u, need %u)\n",
               index, (unsigned)sizeBytes, (unsigned)expected);
        return;
    }

    auto *channels = reinterpret_cast<const MusicChannelEntry *>(data + sizeof(MusicSequenceHeader));
    auto *events = reinterpret_cast<const MusicEvent *>(
        data + sizeof(MusicSequenceHeader)
             + sizeof(MusicChannelEntry) * hdr->channelCount);

    m_sequences[index].header = hdr;
    m_sequences[index].channels = channels;
    m_sequences[index].events = events;
    if (index >= m_sequenceCount) m_sequenceCount = index + 1;
}

bool MusicSequencer::playByIndex(int index, uint8_t masterVolume) {
    if (index < 0 || index >= m_sequenceCount) return false;
    if (m_sequences[index].header == nullptr) return false;

    stop();
    m_active = &m_sequences[index];
    m_currentTick = 0;
    m_subTick12 = 0;
    m_nextEventIdx = 0;
    m_masterVolume = masterVolume;

    // Pre-compute ticks-per-(dt12=4096-unit) in fp12. The runtime's
    // dt12 convention is 4096 = one 30 fps frame (see scenemanager.cpp
    // m_dt12 calc: (elapsed_us * 4096) / 33333). So we compute ticks
    // per 1/30 s and let tick() scale by dt12.
    //   ticks/sec   = bpm/60 * ticksPerBeat
    //   ticks/30fps = ticks/sec / 30
    uint32_t ticksPerSec = (uint32_t)m_active->header->bpm * m_active->header->ticksPerBeat / 60;
    m_ticksPerFrame12 = (ticksPerSec << 12) / 30;

    // Claim our voice pool. Each music channel owns voice index =
    // channel index for the duration of playback. AudioManager::play()
    // now skips these so dialog can't steal them mid-note.
    int n = m_active->header->channelCount;
    if (n > MAX_CHANNELS) n = MAX_CHANNELS;
    if (m_audio) m_audio->reserveVoices(n);

    // Reset per-channel state from the channel table defaults. Note
    // activeVoice is the SPU voice index this channel targets, not -1
    // (we own a voice for the whole sequence — no allocate-on-noteOn).
    for (int i = 0; i < n; i++) {
        m_channels[i].activeVoice = (int8_t)i;
        m_channels[i].activeNote = 0;
        m_channels[i].volume = m_active->channels[i].volume;
        m_channels[i].pan = m_active->channels[i].pan;
        // Pre-silence the voice so the next noteOn starts cleanly.
        psyqo::SPU::silenceChannels(1u << i);
    }
    return true;
}

void MusicSequencer::stop() {
    for (int i = 0; i < MAX_CHANNELS; i++) {
        if (m_channels[i].activeVoice >= 0) {
            psyqo::SPU::silenceChannels(1u << m_channels[i].activeVoice);
            m_channels[i].activeVoice = -1;
        }
    }
    m_active = nullptr;
    m_currentTick = 0;
    m_subTick12 = 0;
    m_nextEventIdx = 0;
    // Release the voice pool so dialog/SFX can fill the whole bank again.
    if (m_audio) m_audio->reserveVoices(0);
}

int MusicSequencer::getBeat() const {
    if (!m_active) return 0;
    return (int)(m_currentTick / m_active->header->ticksPerBeat);
}

void MusicSequencer::tick(int32_t dt12) {
    if (!m_active) return;

    // Advance the playhead by (ticksPerFrame12 * dt12) >> 12.
    // dt12 is fp12 where 4096 == 1/30 s wall-clock (see scenemanager),
    // and ticksPerFrame12 holds ticks-per-(1/30 s) << 12, so at dt12=4096
    // we advance exactly ticksPerFrame12 >> 12 ticks per call.
    uint32_t advance12 = (uint32_t)((((uint64_t)m_ticksPerFrame12) * (uint64_t)(dt12 < 0 ? 0 : dt12)) >> 12);
    m_subTick12 += advance12;
    uint32_t whole = m_subTick12 >> 12;
    m_subTick12 &= 0xFFF;
    m_currentTick += whole;

    // Dispatch all events whose tick is now <= m_currentTick.
    int total = m_active->header->eventCount;
    while (m_nextEventIdx < total) {
        const MusicEvent &e = m_active->events[m_nextEventIdx];
        if (e.tick > m_currentTick) break;
        dispatchEvent(e);
        m_nextEventIdx++;
    }

    // Loop / end handling.
    if (m_nextEventIdx >= total) {
        uint32_t loopStart = m_active->header->loopStartTick;
        if (loopStart != 0xFFFFFFFFu) {
            // Silence any notes still held at the loop seam. Without this,
            // a pad/drone on the final chord keeps droning across the loop
            // because its note-off event was at tick N but m_currentTick
            // is now < N. Authors noticed stuck notes at the loop point.
            int chanCount = m_active->header->channelCount;
            if (chanCount > MAX_CHANNELS) chanCount = MAX_CHANNELS;
            for (int i = 0; i < chanCount; i++) {
                if (m_channels[i].activeVoice >= 0) {
                    psyqo::SPU::silenceChannels(1u << m_channels[i].activeVoice);
                    m_channels[i].activeNote = 0;
                    // Keep activeVoice pinned to the reserved index — the
                    // next noteOn on this channel retriggers it cleanly.
                }
            }

            m_currentTick = loopStart;
            m_subTick12 = 0;
            // Find the first event at or after loopStart.
            m_nextEventIdx = 0;
            while (m_nextEventIdx < total
                   && m_active->events[m_nextEventIdx].tick < loopStart) {
                m_nextEventIdx++;
            }
        } else {
            // One-shot: stop once all key-offs settle.
            stop();
        }
    }
}

void MusicSequencer::dispatchEvent(const MusicEvent &e) {
    switch (e.kind) {
        case 0: noteOn(e.channel, e.data1, e.data2); break;
        case 1: noteOff(e.channel, e.data1); break;
        case 2:
            if (e.channel < MAX_CHANNELS) m_channels[e.channel].volume = e.data1;
            break;
        case 3:
            if (e.channel < MAX_CHANNELS) m_channels[e.channel].pan = e.data1;
            break;
        default:
            // Unknown event kind — skip silently for forward compat.
            break;
    }
}

void MusicSequencer::noteOn(uint8_t channel, uint8_t note, uint8_t velocity) {
    if (!m_active) return;
    if (channel >= m_active->header->channelCount || channel >= MAX_CHANNELS) return;
    if (!m_audio) return;

    // Look up the clip for this channel.
    const MusicChannelEntry &cfg = m_active->channels[channel];
    int clipIdx = cfg.audioClipIndex;
    if (clipIdx < 0 || clipIdx >= MAX_AUDIO_CLIPS) return;

    int combinedVol = ((int)m_channels[channel].volume * (int)velocity * (int)m_masterVolume)
                      / (127 * 127); // 0..128
    if (combinedVol > 128) combinedVol = 128;
    if (combinedVol < 1) combinedVol = 1;

    // Each music channel owns voice index = channel index (claimed in
    // playByIndex via reserveVoices). playOnVoice retriggers it
    // unconditionally, key-cycling cleanly without the search-and-
    // possibly-steal-dialog risk of plain play().
    int voice = (int)channel;
    if (m_audio->playOnVoice(voice, clipIdx, combinedVol, m_channels[channel].pan) < 0) return;

    // Apply pitch shift unless this is a percussion channel.
    if ((cfg.flags & 0x02) == 0) {
        int semitoneOffset = (int)note - (int)cfg.baseNoteMidi;
        uint16_t basePitch = SPU_VOICES[voice].sampleRate;
        uint32_t shifted = ((uint32_t)basePitch * pitchForOffset(semitoneOffset)) >> 12;
        if (shifted < 1) shifted = 1;
        if (shifted > 0x3FFF) shifted = 0x3FFF;
        SPU_VOICES[voice].sampleRate = (uint16_t)shifted;
    }

    m_channels[channel].activeVoice = (int8_t)voice;
    m_channels[channel].activeNote = note;
}

void MusicSequencer::noteOff(uint8_t channel, uint8_t note) {
    if (channel >= MAX_CHANNELS) return;
    if (m_channels[channel].activeVoice < 0) return;
    // Only key-off if the held note matches — note-off events for
    // notes already replaced by a subsequent note-on are no-ops.
    if (m_channels[channel].activeNote != note) return;
    psyqo::SPU::silenceChannels(1u << m_channels[channel].activeVoice);
    m_channels[channel].activeVoice = -1;
}

int MusicSequencer::findByName(const char * /*name*/) const {
    // Name lookup is wired via SceneManager (it owns the music name
    // table). This function is here for future expansion.
    return -1;
}

} // namespace psxsplash
