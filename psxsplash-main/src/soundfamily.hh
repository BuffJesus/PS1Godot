#pragma once

#include <stdint.h>

namespace psxsplash {

class AudioManager;
struct SPLASHPACKSoundFamilyRecord;

// Phase 5 Stage B: variation pool runtime. A "family" is a list of
// alternative clips drawn at random per dispatch, with author-set
// pitch / volume / pan jitter applied via Random. Replaces "I authored
// 8 footstep WAVs and round-robin them" with "I authored 2 footsteps
// and let the runtime randomise."
//
// Authoring: PS1Scene.SoundFamilies (PS1SoundFamily resources). Lua:
// Sound.PlayFamily("name") returns the SPU channel index (0-23) on
// success, -1 when the family name is unknown / cooldown is active /
// the voice allocator dropped the play.
//
// Stateless except for two per-family fields that persist across
// dispatches:
//   * lastVariantIdx — for AvoidRepeat (re-roll if next pick equals).
//   * lastTriggerFrame — for cooldown anti-spam.
// Both are kept in m_state[] indexed by family index.
class SoundFamily {
public:
    void init(AudioManager* audio);

    // Bind the on-disk family table + family clip index slice. Pointers
    // must remain valid for the scene's lifetime. Pass nullptr/0 when
    // the scene exported no families.
    void setBank(const SPLASHPACKSoundFamilyRecord* families,
                 uint16_t                           familyCount,
                 const uint16_t*                    familyClipIndices,
                 uint16_t                           familyClipIndexCount);

    int  playByName(const char* name);
    int  playByIndex(int familyIdx);

    // Bumped once per scene tick by the SceneManager so per-family
    // cooldowns can advance without each Lua call doing the math.
    void tick(int32_t dt12);

private:
    static constexpr int MAX_FAMILIES_TRACKED = 64;

    int findFamilyByName(const char* name) const;

    AudioManager* m_audio = nullptr;
    const SPLASHPACKSoundFamilyRecord* m_families = nullptr;
    const uint16_t* m_familyClipIndices = nullptr;
    uint16_t m_familyCount = 0;
    uint16_t m_familyClipIndexCount = 0;

    struct State {
        uint16_t lastVariantIdx = 0xFFFF;  // 0xFFFF = none yet, no anti-repeat veto
        uint32_t lastTrigger = 0;           // m_globalFrame at last play; 0 = never
    };
    State m_state[MAX_FAMILIES_TRACKED] = {};

    uint32_t m_globalFrame = 0;
};

} // namespace psxsplash
