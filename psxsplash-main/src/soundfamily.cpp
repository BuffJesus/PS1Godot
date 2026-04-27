#include "soundfamily.hh"

#include "audiomanager.hh"
#include "musicsequencer.hh"
#include "scenemanager.hh"
#include "splashpack.hh"
#include "streq.hh"
#include "common/hardware/spu.h"
#include <psyqo/spu.hh>
#include <psyqo/xprintf.h>

namespace psxsplash {

void SoundFamily::init(AudioManager* audio) {
    m_audio = audio;
    m_families = nullptr;
    m_familyClipIndices = nullptr;
    m_familyCount = 0;
    m_familyClipIndexCount = 0;
    m_globalFrame = 0;
    for (int i = 0; i < MAX_FAMILIES_TRACKED; i++) m_state[i] = {};
}

void SoundFamily::setBank(const SPLASHPACKSoundFamilyRecord* families,
                          uint16_t                           familyCount,
                          const uint16_t*                    familyClipIndices,
                          uint16_t                           familyClipIndexCount) {
    m_families = families;
    m_familyClipIndices = familyClipIndices;
    m_familyCount = familyCount;
    m_familyClipIndexCount = familyClipIndexCount;
    for (int i = 0; i < MAX_FAMILIES_TRACKED; i++) m_state[i] = {};
}

int SoundFamily::findFamilyByName(const char* name) const {
    if (!name || !m_families) return -1;
    for (uint16_t i = 0; i < m_familyCount; i++) {
        if (streq(m_families[i].name, name)) return (int)i;
    }
    return -1;
}

int SoundFamily::playByName(const char* name) {
    // Silent on miss — same rationale as SoundMacroSequencer::playByName.
    int idx = findFamilyByName(name);
    if (idx < 0) return -1;
    return playByIndex(idx);
}

int SoundFamily::playByIndex(int familyIdx) {
    if (!m_audio || !m_families || familyIdx < 0 || familyIdx >= (int)m_familyCount) return -1;
    const SPLASHPACKSoundFamilyRecord& fam = m_families[familyIdx];
    if (fam.clipCount == 0 || !m_familyClipIndices) return -1;

    // Cooldown check.
    State* st = (familyIdx < MAX_FAMILIES_TRACKED) ? &m_state[familyIdx] : nullptr;
    if (st && fam.cooldownFrames > 0 && st->lastTrigger != 0) {
        uint32_t since = m_globalFrame - st->lastTrigger;
        if (since < fam.cooldownFrames) return -1;
    }

    bool avoidRepeat = (fam.flags & 0x01) != 0;

    // Pick variant. Range = [firstClipIndex, firstClipIndex + clipCount).
    uint16_t base = fam.firstClipIndex;
    if (base >= m_familyClipIndexCount) return -1;
    uint16_t cap = (uint16_t)(base + fam.clipCount);
    if (cap > m_familyClipIndexCount) cap = m_familyClipIndexCount;
    uint16_t span = (uint16_t)(cap - base);
    if (span == 0) return -1;

    uint16_t pick = (uint16_t)SceneManager::m_random.number(span);
    if (avoidRepeat && fam.clipCount > 1 && st && st->lastVariantIdx == pick) {
        pick = (uint16_t)((pick + 1) % span);
    }
    if (st) st->lastVariantIdx = pick;

    int clipIdx = (int)m_familyClipIndices[base + pick];

    // Volume jitter.
    int volRange = (int)fam.volumeMax - (int)fam.volumeMin;
    int vol = fam.volumeMin;
    if (volRange > 0) vol += (int)SceneManager::m_random.number((uint32_t)(volRange + 1));

    // Pitch jitter (semitones).
    int pitchRange = (int)fam.pitchSemitonesMax - (int)fam.pitchSemitonesMin;
    int pitch = fam.pitchSemitonesMin;
    if (pitchRange > 0) pitch += (int)SceneManager::m_random.number((uint32_t)(pitchRange + 1));

    // Pan jitter — symmetric around centre 64.
    int pan = 64;
    if (fam.panJitter > 0) {
        int j = (int)SceneManager::m_random.number((uint32_t)(fam.panJitter * 2u + 1u)) - (int)fam.panJitter;
        pan = 64 + j;
        if (pan < 0) pan = 0;
        if (pan > 127) pan = 127;
    }

    int ch = m_audio->play(clipIdx, vol, pan, fam.priority);
    if (ch < 0) return -1;

    if (pitch != 0) {
        uint16_t basePitch = SPU_VOICES[ch].sampleRate;
        uint32_t shifted = ((uint32_t)basePitch * MusicSequencer::pitchForOffset(pitch)) >> 12;
        if (shifted < 1) shifted = 1;
        if (shifted > 0x3FFF) shifted = 0x3FFF;
        SPU_VOICES[ch].sampleRate = (uint16_t)shifted;
    }

    if (st) st->lastTrigger = m_globalFrame ? m_globalFrame : 1;
    return ch;
}

void SoundFamily::tick(int32_t dt12) {
    static uint32_t s_sub = 0;
    uint32_t accum = s_sub + (uint32_t)dt12;
    uint32_t whole = accum >> 12;
    s_sub = accum & 0xFFF;
    m_globalFrame += whole;
}

} // namespace psxsplash
