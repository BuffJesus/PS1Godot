#include "soundmacro.hh"

#include "audiomanager.hh"
#include "musicsequencer.hh"
#include "splashpack.hh"
#include "streq.hh"
#include "common/hardware/spu.h"
#include <psyqo/spu.hh>
#include <psyqo/xprintf.h>

namespace psxsplash {

void SoundMacroSequencer::init(AudioManager* audio) {
    m_audio = audio;
    m_macros = nullptr;
    m_events = nullptr;
    m_macroCount = 0;
    m_eventCount = 0;
    m_globalFrame = 0;
    for (int i = 0; i < MAX_INSTANCES; i++) m_instances[i] = {};
    for (int i = 0; i < MAX_MACROS_TRACKED; i++) m_lastTrigger[i] = 0;
}

void SoundMacroSequencer::setBank(const SPLASHPACKSoundMacroRecord*       macros,
                                  uint16_t                                macroCount,
                                  const SPLASHPACKSoundMacroEventRecord*  events,
                                  uint16_t                                eventCount) {
    m_macros = macros;
    m_events = events;
    m_macroCount = macroCount;
    m_eventCount = eventCount;
    // Reset per-macro cooldown tracker on bank swap so a re-load doesn't
    // inherit timestamps from a previous scene.
    for (int i = 0; i < MAX_MACROS_TRACKED; i++) m_lastTrigger[i] = 0;
}

int SoundMacroSequencer::findMacroByName(const char* name) const {
    if (!name || !m_macros) return -1;
    for (uint16_t i = 0; i < m_macroCount; i++) {
        if (streq(m_macros[i].name, name)) return (int)i;
    }
    return -1;
}

int SoundMacroSequencer::countActiveInstances(uint16_t macroIdx) const {
    int n = 0;
    for (int i = 0; i < MAX_INSTANCES; i++) {
        if (m_instances[i].active && m_instances[i].macroIdx == macroIdx) n++;
    }
    return n;
}

int SoundMacroSequencer::allocInstanceSlot() {
    for (int i = 0; i < MAX_INSTANCES; i++) {
        if (!m_instances[i].active) return i;
    }
    return -1;
}

int SoundMacroSequencer::activeInstanceCount() const {
    int n = 0;
    for (int i = 0; i < MAX_INSTANCES; i++) if (m_instances[i].active) n++;
    return n;
}

int SoundMacroSequencer::playByName(const char* name) {
    // Silent on miss — Lua callers commonly use Sound.PlayMacro as a
    // first-attempt path before falling back to Audio.Play, so a printf
    // here would spam the log on every legitimate fallback. Callers
    // get nil from the Lua API; they can Debug.Log themselves if a
    // miss is unexpected.
    int idx = findMacroByName(name);
    if (idx < 0) return -1;
    return playByIndex(idx);
}

int SoundMacroSequencer::playByIndex(int macroIdx) {
    if (!m_macros || macroIdx < 0 || macroIdx >= (int)m_macroCount) return -1;
    const SPLASHPACKSoundMacroRecord& macro = m_macros[macroIdx];

    // Cooldown check (anti-spam). Only enforced for the first
    // MAX_MACROS_TRACKED macros — beyond that, cooldownFrames is a
    // no-op, which is fine for an upper-bound nobody hits.
    if (macroIdx < MAX_MACROS_TRACKED && macro.cooldownFrames > 0) {
        uint32_t since = m_globalFrame - m_lastTrigger[macroIdx];
        if (m_lastTrigger[macroIdx] != 0 && since < macro.cooldownFrames) {
            return -1;
        }
    }

    // Per-macro voice cap.
    if (macro.maxVoices > 0 && countActiveInstances((uint16_t)macroIdx) >= macro.maxVoices) {
        return -1;
    }

    int slot = allocInstanceSlot();
    if (slot < 0) {
        // No instance slots left across the whole sequencer; drop.
        return -1;
    }

    m_instances[slot].active       = true;
    m_instances[slot].macroIdx     = (uint16_t)macroIdx;
    m_instances[slot].frame        = 0;
    m_instances[slot].subFrame     = 0;
    m_instances[slot].nextEventIdx = 0;

    if (macroIdx < MAX_MACROS_TRACKED) {
        // Use 1 as the floor so an early cooldown check doesn't see
        // m_lastTrigger == 0 and treat it as "no prior trigger".
        m_lastTrigger[macroIdx] = m_globalFrame ? m_globalFrame : 1;
    }

    // Fire any events authored at frame 0 immediately.
    uint16_t end = macro.firstEventIndex + macro.eventCount;
    if (end > m_eventCount) end = m_eventCount;
    while (m_instances[slot].nextEventIdx < macro.eventCount) {
        uint16_t evIdx = (uint16_t)(macro.firstEventIndex + m_instances[slot].nextEventIdx);
        if (evIdx >= end) break;
        const SPLASHPACKSoundMacroEventRecord& evt = m_events[evIdx];
        if (evt.frame > 0) break;
        dispatchEvent((uint16_t)macroIdx, evt);
        m_instances[slot].nextEventIdx++;
    }

    return slot;
}

void SoundMacroSequencer::stopAll() {
    for (int i = 0; i < MAX_INSTANCES; i++) m_instances[i].active = false;
    // Note: we don't actively silence SPU channels — events have already
    // dispatched as one-shots through AudioManager::play; their voices
    // fade naturally. If gameplay needs hard-cut, call AudioManager::stopAll.
}

void SoundMacroSequencer::dispatchEvent(uint16_t macroIdx,
                                        const SPLASHPACKSoundMacroEventRecord& evt) {
    if (!m_audio) return;
    if (!m_macros) return;
    const SPLASHPACKSoundMacroRecord& macro = m_macros[macroIdx];

    int ch = m_audio->play((int)evt.audioClipIndex, evt.volume, evt.pan, macro.priority);
    if (ch < 0) return;  // dropped by allocator

    // Pitch offset: post-keyOn override of the SPU sample-rate register.
    if (evt.pitchOffset != 0) {
        uint16_t basePitch = SPU_VOICES[ch].sampleRate;
        uint32_t shifted = ((uint32_t)basePitch * MusicSequencer::pitchForOffset((int)evt.pitchOffset)) >> 12;
        if (shifted < 1) shifted = 1;
        if (shifted > 0x3FFF) shifted = 0x3FFF;
        SPU_VOICES[ch].sampleRate = (uint16_t)shifted;
    }
}

void SoundMacroSequencer::tick(int32_t dt12) {
    // Advance the global frame counter regardless of bank state — keeps
    // cooldowns coherent across scene boundaries. Sub-frame fp12
    // accumulator handles variable frame time.
    static uint32_t s_globalSub = 0;
    uint32_t accum = s_globalSub + (uint32_t)dt12;
    uint32_t whole = accum >> 12;
    s_globalSub = accum & 0xFFF;
    m_globalFrame += whole;

    if (!m_macros || m_macroCount == 0) return;

    for (int i = 0; i < MAX_INSTANCES; i++) {
        Instance& inst = m_instances[i];
        if (!inst.active) continue;

        uint32_t a = (uint32_t)inst.subFrame + (uint32_t)dt12;
        uint16_t w = (uint16_t)(a >> 12);
        inst.subFrame = (uint16_t)(a & 0xFFF);
        inst.frame = (uint16_t)(inst.frame + w);

        const SPLASHPACKSoundMacroRecord& macro = m_macros[inst.macroIdx];
        uint16_t endIdx = macro.firstEventIndex + macro.eventCount;
        if (endIdx > m_eventCount) endIdx = m_eventCount;

        // Dispatch every event whose authored frame is now <= inst.frame.
        while (inst.nextEventIdx < macro.eventCount) {
            uint16_t evIdx = (uint16_t)(macro.firstEventIndex + inst.nextEventIdx);
            if (evIdx >= endIdx) break;
            const SPLASHPACKSoundMacroEventRecord& evt = m_events[evIdx];
            if (evt.frame > inst.frame) break;
            dispatchEvent(inst.macroIdx, evt);
            inst.nextEventIdx++;
        }

        // Instance retires once all events have fired. Voices linger
        // through the natural envelope decay — we don't need to track
        // them.
        if (inst.nextEventIdx >= macro.eventCount) {
            inst.active = false;
        }
    }
}

} // namespace psxsplash
