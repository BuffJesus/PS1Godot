#include "musicsequencer.hh"

#include "audiomanager.hh"
#include "splashpack.hh"
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
    m_inlineLoopStartTick = 0xFFFFFFFFu;
    m_inlineLoopStartIdx = 0;
    m_lastMarkerHash = 0;
    m_masterVolume = 100;
    m_bankInstruments = nullptr;
    m_bankRegions = nullptr;
    m_bankDrumKits = nullptr;
    m_bankDrumMappings = nullptr;
    m_bankInstrumentCount = 0;
    m_bankRegionCount = 0;
    m_bankDrumKitCount = 0;
    m_bankDrumMappingCount = 0;
    for (int i = 0; i < MAX_CHANNELS; i++) {
        m_channels[i].activeVoice = -1;
        m_channels[i].activeNote = 0;
        m_channels[i].volume = 100;
        m_channels[i].pan = 64;
        m_channels[i].currentProgram = 0;
        m_channels[i].lastClipIndex = 0;
    }
}

void MusicSequencer::setBank(const SPLASHPACKInstrumentRecord*  instruments,
                             uint16_t                           instrumentCount,
                             const SPLASHPACKRegionRecord*      regions,
                             uint16_t                           regionCount,
                             const SPLASHPACKDrumKitRecord*     drumKits,
                             uint16_t                           drumKitCount,
                             const SPLASHPACKDrumMappingRecord* drumMappings,
                             uint16_t                           drumMappingCount) {
    m_bankInstruments      = instruments;
    m_bankRegions          = regions;
    m_bankDrumKits         = drumKits;
    m_bankDrumMappings     = drumMappings;
    m_bankInstrumentCount  = instrumentCount;
    m_bankRegionCount      = regionCount;
    m_bankDrumKitCount     = drumKitCount;
    m_bankDrumMappingCount = drumMappingCount;
}

const SPLASHPACKRegionRecord* MusicSequencer::resolveBankRegion(uint8_t programId, uint8_t note, uint8_t velocity) const {
    if (!m_bankInstruments || m_bankInstrumentCount == 0) return nullptr;
    if (!m_bankRegions     || m_bankRegionCount == 0)     return nullptr;
    // Linear scan for the matching program. With <=128 instruments per
    // bank in practice (MIDI program range) this is effectively O(N)
    // but N is small.
    for (uint16_t i = 0; i < m_bankInstrumentCount; i++) {
        const SPLASHPACKInstrumentRecord& inst = m_bankInstruments[i];
        if (inst.programId != programId) continue;
        // Walk this instrument's region slice. First region whose
        // (key, velocity) bracket the incoming note wins —
        // declaration-order priority, matches the exporter and the
        // strategy doc.
        uint16_t end = inst.firstRegionIndex + inst.regionCount;
        if (end > m_bankRegionCount) end = m_bankRegionCount;
        for (uint16_t r = inst.firstRegionIndex; r < end; r++) {
            const SPLASHPACKRegionRecord& region = m_bankRegions[r];
            if (note < region.keyMin || note > region.keyMax) continue;
            if (velocity < region.velocityMin || velocity > region.velocityMax) continue;
            return &region;
        }
        return nullptr;  // program matched but no region caught the note
    }
    return nullptr;  // no instrument matches programId
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
    bool isPS2M = false;
    if (__builtin_memcmp(hdr->magic, "PS1M", 4) == 0) {
        isPS2M = false;
    } else if (__builtin_memcmp(hdr->magic, "PS2M", 4) == 0) {
        isPS2M = true;
    } else {
        printf("[music] sequence %d bad magic\n", index);
        return;
    }

    // PS2M packs a u8[channelCount] default-program table between the
    // channel table and the event table, padded to 4-byte alignment so
    // the event stride stays naturally aligned.
    uint32_t channelTableSize = (uint32_t)sizeof(MusicChannelEntry) * hdr->channelCount;
    uint32_t channelProgramTableSize = isPS2M ? hdr->channelCount : 0u;
    uint32_t paddedProgramTable = (channelProgramTableSize + 3u) & ~3u;

    // Bail before reinterpreting past the buffer. Exporter always writes a
    // matching size; this catches splashpack corruption or future format
    // drift with a single printf instead of silently UB-reading.
    uint32_t expected = sizeof(MusicSequenceHeader)
                      + channelTableSize
                      + paddedProgramTable
                      + (uint32_t)sizeof(MusicEvent) * hdr->eventCount;
    if (sizeBytes < expected) {
        printf("[music] sequence %d truncated (have %u, need %u)\n",
               index, (unsigned)sizeBytes, (unsigned)expected);
        return;
    }

    const uint8_t* afterHeader   = data + sizeof(MusicSequenceHeader);
    const uint8_t* afterChannels = afterHeader + channelTableSize;
    const uint8_t* afterPrograms = afterChannels + paddedProgramTable;

    m_sequences[index].header           = hdr;
    m_sequences[index].channels         = reinterpret_cast<const MusicChannelEntry*>(afterHeader);
    m_sequences[index].channelPrograms  = isPS2M ? afterChannels : nullptr;
    m_sequences[index].events           = reinterpret_cast<const MusicEvent*>(afterPrograms);
    m_sequences[index].isPS2M           = isPS2M;
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
    m_inlineLoopStartTick = 0xFFFFFFFFu;
    m_inlineLoopStartIdx = 0;
    m_lastMarkerHash = 0;
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
        // PS2M: seed currentProgram from the per-channel default table.
        // PS1M sequences leave it 0 (unused — bank dispatch off).
        m_channels[i].currentProgram = m_active->isPS2M && m_active->channelPrograms
            ? m_active->channelPrograms[i]
            : (uint8_t)0;
        m_channels[i].lastClipIndex = 0;
        m_channels[i].noteBaseRate = 0;
        m_channels[i].pitchBendRatio12 = 0x1000;  // no bend at sequence start
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
    m_inlineLoopStartTick = 0xFFFFFFFFu;
    m_inlineLoopStartIdx = 0;
    m_lastMarkerHash = 0;
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

    // Dispatch all events whose tick is now <= m_currentTick. dispatchEvent
    // returns true when it performed an inline loop-back (kind=10): the
    // dispatch already updated m_currentTick + m_nextEventIdx, so we
    // skip the post-loop ++ and re-evaluate from the new index.
    int total = m_active->header->eventCount;
    while (m_nextEventIdx < total) {
        const MusicEvent &e = m_active->events[m_nextEventIdx];
        if (e.tick > m_currentTick) break;
        if (dispatchEvent(e)) continue;
        m_nextEventIdx++;
    }

    // End-of-stream loop / stop.
    if (m_nextEventIdx >= total) {
        uint32_t loopStart = m_active->header->loopStartTick;
        if (loopStart != 0xFFFFFFFFu) {
            silenceLoopSeam();
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

uint16_t MusicSequencer::bendRatio12From14(uint16_t value14) {
    // 14-bit unsigned bend value [0..0x3FFF] with 0x2000 = center.
    // Centred range: -8192..+8191. With the default ±2-semitone bend
    // range, fp12 semitones = centred value (4096 fp12 ticks per
    // semitone × 2 semitones = 8192 fp12 ticks across half the range,
    // and centred itself spans ±8192).
    int32_t semi_fp12 = (int32_t)value14 - 0x2000;
    // Floor to integer semitone via arithmetic shift; fractional part
    // is the bottom 12 bits and is always non-negative regardless of
    // sign (bitwise &).
    int semiInt = semi_fp12 >> 12;
    int semiFrac12 = semi_fp12 & 0xFFF;
    uint16_t lo = pitchForOffset(semiInt);
    uint16_t hi = pitchForOffset(semiInt + 1);
    int32_t diff = (int32_t)hi - (int32_t)lo;
    int32_t interp = (int32_t)lo + ((diff * (int32_t)semiFrac12) >> 12);
    if (interp < 1) interp = 1;
    if (interp > 0xFFFF) interp = 0xFFFF;
    return (uint16_t)interp;
}

void MusicSequencer::silenceLoopSeam() {
    // Silence any notes still held at a loop boundary. Without this, a
    // pad/drone on the final chord keeps droning across the loop because
    // its note-off event was at tick N but m_currentTick is about to
    // jump back to a smaller value. Authors noticed stuck notes at the
    // loop point.
    if (!m_active) return;
    int chanCount = m_active->header->channelCount;
    if (chanCount > MAX_CHANNELS) chanCount = MAX_CHANNELS;
    for (int i = 0; i < chanCount; i++) {
        if (m_channels[i].activeVoice >= 0) {
            psyqo::SPU::silenceChannels(1u << m_channels[i].activeVoice);
            m_channels[i].activeNote = 0;
            // Keep activeVoice pinned to the reserved index — the next
            // noteOn on this channel retriggers it cleanly.
        }
    }
}

bool MusicSequencer::dispatchEvent(const MusicEvent &e) {
    switch (e.kind) {
        case 0: noteOn(e.channel, e.data1, e.data2); break;
        case 1: noteOff(e.channel, e.data1); break;
        case 2:
            if (e.channel < MAX_CHANNELS) m_channels[e.channel].volume = e.data1;
            break;
        case 3:
            if (e.channel < MAX_CHANNELS) m_channels[e.channel].pan = e.data1;
            break;
        case 4:
            // ProgramChange (PS2M only). data1 = new ProgramId. PS1M
            // sequences silently ignore it — no bank, no resolution.
            if (e.channel < MAX_CHANNELS) m_channels[e.channel].currentProgram = e.data1;
            break;
        case 5:
            // PitchBend. data1 = LSB, data2 = MSB of 14-bit value.
            // Updates per-channel bend state; if a note is held, the
            // SPU voice's sample rate is re-pitched live.
            dispatchPitchBend(e.channel, e.data1, e.data2);
            break;
        case 7:
            // Controller. data1 = CC#, data2 = value. Whitelist:
            // CC#7 → channel volume, CC#10 → pan. Both apply at the
            // next noteOn (same as kind=2/3 ChannelVolume/Pan events
            // from the static channel config). Unknown CC# values are
            // silently no-ops so adding new handlers later is forward-
            // compatible without a format bump.
            if (e.channel < MAX_CHANNELS) {
                if (e.data1 == 7)       m_channels[e.channel].volume = e.data2;
                else if (e.data1 == 10) m_channels[e.channel].pan    = e.data2;
            }
            break;
        case 8:
            // Generic text marker. data1/data2 = 16-bit FNV-1a hash
            // of the marker text (lo/hi). Lua polls the latest hash
            // via Music.GetLastMarkerHash() and compares against
            // Music.MarkerHash("name"). LoopStart/LoopEnd never
            // arrive here — those are kind=9/10.
            m_lastMarkerHash = (uint16_t)e.data1 | ((uint16_t)e.data2 << 8);
            break;
        case 9:
            // LoopStart marker. Records this point for the next LoopEnd
            // to jump back to. Re-executing the same LoopStart after a
            // jump-back is intentional and idempotent (the body of the
            // loop sits AFTER this index, so re-recording the same
            // position causes no infinite loop on its own).
            m_inlineLoopStartTick = e.tick;
            m_inlineLoopStartIdx  = m_nextEventIdx;
            break;
        case 10:
            // LoopEnd marker. Jump back to the prior LoopStart if one
            // was seen; otherwise no-op (the header loopStartTick still
            // applies as an end-of-stream fallback).
            if (m_inlineLoopStartTick != 0xFFFFFFFFu && m_active) {
                silenceLoopSeam();
                m_currentTick = m_inlineLoopStartTick;
                m_subTick12 = 0;
                m_nextEventIdx = m_inlineLoopStartIdx;
                return true;
            }
            break;
        default:
            // Unknown event kind — skip silently for forward compat.
            break;
    }
    return false;
}

void MusicSequencer::noteOn(uint8_t channel, uint8_t note, uint8_t velocity) {
    if (!m_active) return;
    if (channel >= m_active->header->channelCount || channel >= MAX_CHANNELS) return;
    if (!m_audio) return;

    const MusicChannelEntry &cfg = m_active->channels[channel];

    // Resolve clip + base note. PS2M sequences with a populated bank
    // route through channel→program→instrument→region; everything else
    // falls back to the channel entry's audioClipIndex + baseNoteMidi
    // (the legacy path).
    int     clipIdx       = cfg.audioClipIndex;
    int     baseNoteMidi  = cfg.baseNoteMidi;
    bool    percussion    = (cfg.flags & 0x02) != 0;
    int     regionVolume  = 127;
    int     regionPan     = 64;
    if (m_active->isPS2M && m_bankInstruments) {
        const SPLASHPACKRegionRecord* region =
            resolveBankRegion(m_channels[channel].currentProgram, note, velocity);
        if (region) {
            clipIdx      = region->audioClipIndex;
            baseNoteMidi = region->rootKey;
            regionVolume = region->volume;
            regionPan    = region->pan;
        }
        // No region match → fall back to the channel entry's
        // audioClipIndex (still useful for "default clip when no
        // program is mapped" sequences).
    }

    if (clipIdx < 0 || clipIdx >= MAX_AUDIO_CLIPS) return;

    // Compose final volume: channel × velocity × master × region.
    // Each factor is 0-127 except master (0-128). Normalised to 0..128.
    int combinedVol = ((int)m_channels[channel].volume * (int)velocity)
                      / 127;                                    // 0..127
    combinedVol = (combinedVol * regionVolume) / 127;            // 0..127
    combinedVol = (combinedVol * (int)m_masterVolume) / 127;     // 0..127
    if (combinedVol > 128) combinedVol = 128;
    if (combinedVol < 1) combinedVol = 1;

    // Pan: channel pan + region pan offset from centre, clamped.
    int finalPan = (int)m_channels[channel].pan + (regionPan - 64);
    if (finalPan < 0) finalPan = 0;
    if (finalPan > 127) finalPan = 127;

    // Each music channel owns voice index = channel index (claimed in
    // playByIndex via reserveVoices). playOnVoice retriggers it
    // unconditionally, key-cycling cleanly without the search-and-
    // possibly-steal-dialog risk of plain play().
    int voice = (int)channel;
    if (m_audio->playOnVoice(voice, clipIdx, combinedVol, finalPan) < 0) return;

    // Compute the pre-bend rate (note→base shift on melodic channels;
    // native rate on percussion). Stash it in noteBaseRate so kind=5
    // (PitchBend) events that arrive mid-note can recompute the final
    // SPU rate without re-running the shift.
    uint16_t noteBaseRate = SPU_VOICES[voice].sampleRate;
    if (!percussion) {
        int semitoneOffset = (int)note - baseNoteMidi;
        uint32_t shifted = ((uint32_t)noteBaseRate * pitchForOffset(semitoneOffset)) >> 12;
        if (shifted < 1) shifted = 1;
        if (shifted > 0x3FFF) shifted = 0x3FFF;
        noteBaseRate = (uint16_t)shifted;
    }

    // Apply current pitch bend on top (skip for percussion — clip pitch
    // shouldn't drift on a hi-hat/snare). Bends from prior notes carry
    // forward by design; a sequence that wants to reset clears it via
    // a 0x2000 bend event.
    uint16_t finalRate = noteBaseRate;
    if (!percussion) {
        uint16_t bend12 = m_channels[channel].pitchBendRatio12;
        if (bend12 != 0x1000) {
            uint32_t bent = ((uint32_t)finalRate * bend12) >> 12;
            if (bent < 1) bent = 1;
            if (bent > 0x3FFF) bent = 0x3FFF;
            finalRate = (uint16_t)bent;
        }
    }
    SPU_VOICES[voice].sampleRate = finalRate;

    m_channels[channel].activeVoice   = (int8_t)voice;
    m_channels[channel].activeNote    = note;
    m_channels[channel].lastClipIndex = (uint8_t)clipIdx;
    m_channels[channel].noteBaseRate  = noteBaseRate;
}

void MusicSequencer::dispatchPitchBend(uint8_t channel, uint8_t lsb, uint8_t msb) {
    if (!m_active) return;
    if (channel >= MAX_CHANNELS) return;
    uint16_t value14 = (uint16_t)(((msb & 0x7F) << 7) | (lsb & 0x7F));
    uint16_t bend12 = bendRatio12From14(value14);
    m_channels[channel].pitchBendRatio12 = bend12;

    // Only retune live if a note is currently held on this channel.
    int8_t voice = m_channels[channel].activeVoice;
    if (voice < 0) return;

    // Skip live retune on percussion — bend is meaningless there and
    // touching the SPU rate would drift the sample's natural pitch.
    if (channel < m_active->header->channelCount) {
        const MusicChannelEntry &cfg = m_active->channels[channel];
        if ((cfg.flags & 0x02) != 0) return;
    }

    uint32_t bent = ((uint32_t)m_channels[channel].noteBaseRate * bend12) >> 12;
    if (bent < 1) bent = 1;
    if (bent > 0x3FFF) bent = 0x3FFF;
    SPU_VOICES[voice].sampleRate = (uint16_t)bent;
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
