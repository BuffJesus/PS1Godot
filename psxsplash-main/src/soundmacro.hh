#pragma once

#include <stdint.h>

namespace psxsplash {

class AudioManager;
struct SPLASHPACKSoundMacroRecord;
struct SPLASHPACKSoundMacroEventRecord;

// Phase 5 Stage B: composite SFX runtime. A "macro" is a frame-keyed
// list of sample-dispatch events — at frame N play sample S with pitch
// P, volume V, pan PN. Replaces hand-baked composite WAVs (chest open
// = wood + metal + sparkle) with three short clean clips + a tiny
// event sequence, saving SPU RAM at the cost of a bit of allocator
// pressure.
//
// Authoring: PS1Scene.SoundMacros (PS1SoundMacro resources). Lua:
// Sound.PlayMacro("name") returns an instance handle (0..MAX_INSTANCES-1)
// or -1 when the macro's MaxVoices cap is full / cooldown active /
// name unknown. Stop one with stopInstance(handle); stop everything
// with stopAll(). MaxVoices and CooldownFrames are per-macro; the
// runtime tracks last-trigger global frame index per macro.
//
// Voicing: each event triggers an AudioManager::play with the macro's
// authored Priority. Macros never reserve voices and never compete
// with the music sequencer — Phase 4's voice allocator handles
// eviction. Pitch offset (semitones) per event is applied via
// MusicSequencer::pitchForOffset on the resulting SPU channel.
class SoundMacroSequencer {
public:
    static constexpr int MAX_INSTANCES = 8;

    void init(AudioManager* audio);

    // Bind the on-disk macro tables loaded from the splashpack. Pointers
    // must remain valid for the scene's lifetime. Pass nullptr/0 when
    // the scene exported no macros — playByName then returns -1 for
    // every call.
    void setBank(const SPLASHPACKSoundMacroRecord*       macros,
                 uint16_t                                macroCount,
                 const SPLASHPACKSoundMacroEventRecord*  events,
                 uint16_t                                eventCount);

    int  playByName(const char* name);
    int  playByIndex(int macroIdx);
    void stopAll();

    // Called once per scene tick. dt12 is fp12 frame delta (1.0 = 4096 = one
    // 30 FPS frame). Active instances accumulate sub-frame and dispatch
    // events whose authored frame <= the new whole frame.
    void tick(int32_t dt12);

    // Total active instance count. Useful for debug overlays.
    int  activeInstanceCount() const;

private:
    struct Instance {
        bool     active        = false;
        uint16_t macroIdx      = 0;
        uint16_t frame         = 0;   // whole frames since instance start
        uint16_t subFrame      = 0;   // fp12 fractional accumulator
        uint16_t nextEventIdx  = 0;   // next event index within the macro's event slice
    };

    int findMacroByName(const char* name) const;
    int countActiveInstances(uint16_t macroIdx) const;
    int allocInstanceSlot();
    void dispatchEvent(uint16_t macroIdx, const SPLASHPACKSoundMacroEventRecord& evt);

    AudioManager* m_audio = nullptr;
    const SPLASHPACKSoundMacroRecord*      m_macros = nullptr;
    const SPLASHPACKSoundMacroEventRecord* m_events = nullptr;
    uint16_t m_macroCount = 0;
    uint16_t m_eventCount = 0;

    Instance m_instances[MAX_INSTANCES];

    // Per-macro cooldown tracker (small fixed cap; macros indexed beyond
    // this size skip the cooldown check). 64 is well above any realistic
    // per-scene macro count for a PS1 game.
    static constexpr int MAX_MACROS_TRACKED = 64;
    uint32_t m_lastTrigger[MAX_MACROS_TRACKED] = {};

    // Monotonic global frame counter — used as the cooldown timestamp.
    // Wraps at 4 billion frames (~2 years at 30 FPS); not worth defending
    // against on a PSX.
    uint32_t m_globalFrame = 0;
};

} // namespace psxsplash
