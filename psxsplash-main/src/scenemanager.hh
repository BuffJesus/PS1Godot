#pragma once

#include <EASTL/vector.h>

#include <psyqo/trigonometry.hh>
#include <psyqo/vector.hh>
#include <psyqo/gpu.hh>

#include "random.hh"
#include "bvh.hh"
#include "camera.hh"
#include "collision.hh"
#include "controls.hh"
#include "gameobject.hh"
#include "lua.h"
#include "splashpack.hh"
#include "navregion.hh"
#include "audiomanager.hh"
#include "musicmanager.hh"
#include "musicsequencer.hh"
#include "soundmacro.hh"
#include "soundfamily.hh"
#include "xaaudio.hh"
#include "interactable.hh"
#include "luaapi.hh"
#include "fileloader.hh"
#include "cutscene.hh"
#include "animation.hh"
#include "skinmesh.hh"
#include "uisystem.hh"
#include "dialogue.hh"
#ifdef PSXSPLASH_MEMOVERLAY
#include "memoverlay.hh"
#endif
#ifdef PSXSPLASH_PERFOVERLAY
#include "perfoverlay.hh"
#endif

namespace psxsplash {

// Forward-declare; full definition in loadingscreen.hh
class LoadingScreen;

// How the camera behaves relative to PS1Player each frame. Mirrors
// PS1CameraMode on the authoring side (PS1Player.cs). Lua can flip
// between modes at runtime via Camera.SetMode(name).
enum class PlayerCameraMode : uint8_t {
    ThirdPerson = 0,  // Trails at m_cameraRigOffset (editor-configurable).
    FirstPerson = 1,  // Lives at m_playerPosition; player avatar hidden.
    // Orbit mode (radius + right-stick rotation) sits in ROADMAP Phase
    // 2.5 still — adding later so the enum stays stable.
    // FixedPreRendered (3) — camera ignores player position entirely.
    // Author calls Camera.SetPosition + SetRotation once and the runtime
    // holds it there. Used for Resident Evil / FFVII pre-rendered BG
    // scenes (ROADMAP Phase 4 stretch). Player avatar still renders.
    FixedPreRendered = 3,
};

class SceneManager {
  public:
    void InitializeScene(uint8_t* splashpackData, LoadingScreen* loading = nullptr);
    void GameTick(psyqo::GPU &gpu);
    
    // Font access (set from main.cpp after uploadSystemFont)
    static void SetFont(psyqo::Font<>* font) { s_font = font; }
    static psyqo::Font<>* GetFont() { return s_font; }
    
    // Trigger event callbacks (called by CollisionSystem for trigger boxes)
    void fireTriggerEnter(int16_t luaFileIndex, uint16_t triggerIndex);
    void fireTriggerExit(int16_t luaFileIndex, uint16_t triggerIndex);
    
    // Get game object by index (for collision callbacks)
    GameObject* getGameObject(uint16_t index) {
        if (index < m_gameObjects.size()) return m_gameObjects[index];
        return nullptr;
    }

    // Reverse lookup: pointer → index. Returns 0xFFFF if not in the
    // scene's object array. Linear scan — call paths (Lua Stats.*,
    // hurtbox dispatch) all run at most once per script call per
    // frame, so the N×scripts cost is negligible for the scene sizes
    // we target. Used by the Lua handle bridge: scripts hold handle
    // tables with __cpp_ptr lightuserdata; APIs that need an index
    // (Stats.*, iframes, etc.) translate via this helper.
    uint16_t findGameObjectIndex(const GameObject* go) const {
        if (!go) return 0xFFFF;
        for (size_t i = 0; i < m_gameObjects.size(); ++i) {
            if (m_gameObjects[i] == go) return static_cast<uint16_t>(i);
        }
        return 0xFFFF;
    }

    // Get total object count
    size_t getGameObjectCount() const { return m_gameObjects.size(); }

    // ---- v33+: per-entity stats (HP / Stamina / Mana). -----------------
    // Lua's Stats.* API reaches in via these getters. For entities with
    // no PS1Stats authored, max* fields stay 0 and the cur* values stay
    // 0 too — callers get well-defined "no stats" sentinels.
    int16_t getStatHP       (uint16_t i) const { return i < m_entityStats.size() ? m_entityStats[i].hp        : 0; }
    int16_t getStatMaxHP    (uint16_t i) const { return i < m_entityStats.size() ? m_entityStats[i].maxHP     : 0; }
    int16_t getStatStamina  (uint16_t i) const { return i < m_entityStats.size() ? m_entityStats[i].stamina   : 0; }
    int16_t getStatMaxStamina(uint16_t i) const { return i < m_entityStats.size() ? m_entityStats[i].maxStamina : 0; }
    int16_t getStatMana     (uint16_t i) const { return i < m_entityStats.size() ? m_entityStats[i].mana      : 0; }
    int16_t getStatMaxMana  (uint16_t i) const { return i < m_entityStats.size() ? m_entityStats[i].maxMana   : 0; }

    // Setters clamp into [0, max]. No-op for entities without stats
    // (max == 0). Returns the clamped value actually stored.
    int16_t setStatHP      (uint16_t i, int v);
    int16_t setStatStamina (uint16_t i, int v);
    int16_t setStatMana    (uint16_t i, int v);

    // ---- v33+: per-entity i-frame counter (dodge / roll). ---------------
    // Decremented once per GameTick frame; while > 0, Stats.DealDamage
    // treats the entity as invulnerable and returns 0 applied damage.
    uint16_t getIFrames(uint16_t i) const { return i < m_iframes.size() ? m_iframes[i] : 0; }
    bool     isInvulnerable(uint16_t i) const { return getIFrames(i) > 0; }
    void     startIFrames(uint16_t i, uint16_t frames) {
        if (i < m_iframes.size()) m_iframes[i] = frames;
    }

    // ---- v33+: central damage entry point (Stats.DealDamage in Lua). ----
    // Returns the damage that actually landed. 0 if target has no
    // stats, has i-frames active, or HP was already 0. Fires the
    // target's onDamage(self, applied, source) Lua callback after the
    // HP debit lands. Source can be a nullptr (environmental damage).
    int dealDamage(uint16_t targetIndex, int amount, GameObject* source);

    // ---- v33+: lock-on camera (Camera.LockOn / LockOff in Lua). ----------
    // When locked, the GameTick computes the yaw from player→target each
    // frame and overrides playerRotationY before AND after HandleControls,
    // so the camera tracks the target and stick input automatically
    // becomes target-relative (forward = toward target, strafe =
    // orthogonal). Right-stick yaw + L1/R1 rotation are visually
    // suppressed (their changes get overwritten by the per-frame snap).
    // 0xFFFF = unlocked sentinel.
    void setLockTarget(uint16_t entityIndex) { m_lockTargetIndex = entityIndex; }
    void clearLockTarget()                   { m_lockTargetIndex = 0xFFFF; }
    bool isLocked() const                    { return m_lockTargetIndex != 0xFFFF; }
    uint16_t getLockTarget() const           { return m_lockTargetIndex; }

    // ---- v34+: hurtbox query. ------------------------------------------
    // Iterates m_hurtBoxTable, tests each entry's world-space AABB
    // (entity position + offset ± size) against the query box. Per
    // entity, returns the HIGHEST multiplier among hits — authoring
    // "head + body" with descending crits gives "best hit wins" without
    // per-call Lua dedup. Capped at MAX_HURTBOX_RESULTS = 16 to bound
    // the Lua table allocation.
    struct HurtBoxHit {
        uint16_t entityIndex;
        int16_t  multiplier;
    };
    static constexpr int MAX_HURTBOX_RESULTS = 16;
    int overlapHurtBoxes(int32_t minXFp12, int32_t minYFp12, int32_t minZFp12,
                         int32_t maxXFp12, int32_t maxYFp12, int32_t maxZFp12,
                         HurtBoxHit* out, int outCap) const;
    
    // Get object name by index (returns nullptr if no name table or out of range)
    const char* getObjectName(uint16_t index) const {
        if (index < m_objectNames.size()) return m_objectNames[index];
        return nullptr;
    }
    
    // Find first object with matching name (linear scan, case-sensitive)
    GameObject* findObjectByName(const char* name) const;
    
    // Find audio clip index by name (returns -1 if not found)
    int findAudioClipByName(const char* name) const;
    
    // Get audio clip name by index (returns nullptr if out of range)
    const char* getAudioClipName(int index) const {
        if (index >= 0 && index < (int)m_audioClipNames.size()) return m_audioClipNames[index];
        return nullptr;
    }

    // v25: routing byte for an audio clip (0=SPU, 1=XA, 2=CDDA, 3=Auto).
    // Returns 0 (SPU) for unknown indices so missing data plays through
    // the existing path rather than going silent.
    uint8_t getAudioClipRouting(int index) const {
        if (index >= 0 && index < (int)m_audioClipRouting.size()) return m_audioClipRouting[index];
        return 0;
    }

    // v26: red-book CD audio track for a CDDA-routed clip. 0 means
    // unset — Audio.PlayMusic logs a "no track mapping" warning and
    // returns -1. Authors should set the track in the editor's
    // PS1AudioClip resource when they tag the clip CDDA.
    uint8_t getAudioClipCddaTrack(int index) const {
        if (index >= 0 && index < (int)m_audioClipCddaTrack.size()) return m_audioClipCddaTrack[index];
        return 0;
    }

    // v27: XA sidecar lookup. Returns true and fills (offset, size) if
    // `name` was packaged with an XA-ADPCM payload. Audio.PlayMusic uses
    // this to decide whether to attempt XA streaming or report "no
    // payload" — the latter happens when psxavenc was missing at export.
    bool getXaClipInfo(const char* name, uint32_t &outOffset, uint32_t &outSize) const;
    int getXaClipCount() const { return m_xaClipNames.size(); }

    // Music sequence lookup by name (returns -1 if not found). Names
    // come from the splashpack music table and are stored at load time.
    int findMusicSequenceByName(const char* name) const;

    // Skinned mesh accessors (for Lua API and renderer)
    int findSkinAnimByObjectName(const char* name) const;
    SkinAnimSet& getSkinAnimSet(int index) { return m_skinAnimSets[index]; }
    SkinAnimState& getSkinAnimState(int index) { return m_skinAnimStates[index]; }
    int getSkinnedMeshCount() const { return m_skinnedMeshCount; }
    
    // Public API for game systems
    // Interaction system - call from Lua or native code
    void triggerInteraction(GameObject* interactable);
    
    // GameObject state control with events
    void setObjectActive(GameObject* go, bool active);
    
    // Public accessors for Lua API
    Controls& getControls() { return m_controls; }
    Camera& getCamera() { return m_currentCamera; }
    Lua& getLua() { return L; }
    AudioManager& getAudio() { return m_audio; }
    MusicSequencer& getMusicSequencer() { return m_musicSequencer; }
    MusicManager& getMusic() { return m_music; }
    XaAudioBackend& getXa() { return m_xa; }
    SoundMacroSequencer& getSoundMacros() { return m_soundMacros; }
    SoundFamily& getSoundFamilies() { return m_soundFamilies; }
    CollisionSystem& getCollision() { return m_collisionSystem; }

    // v23+: UI 3D-model accessors — the renderer walks these in its
    // post-main-scene HUD pass; Lua reads/writes per-model state.
    int getUIModelCount() const { return m_uiModelCount; }
    const SPLASHPACKUIModel *getUIModelDisk(int i) const {
        return (i >= 0 && i < m_uiModelCount) ? &m_uiModelsDisk[i] : nullptr;
    }
    UIModelRuntimeState *getUIModelState(int i) {
        return (i >= 0 && i < m_uiModelCount) ? &m_uiModelStates[i] : nullptr;
    }
    // Find by authored name (nullptr on miss). Used by Lua UI.SetModel* APIs.
    int findUIModelByName(const char *name) const;

    // Hit-stop / freeze. Holds gameplay tick (animation / cutscene / skin /
    // collision / Lua onUpdate) for `frames`, while still rendering and still
    // ticking camera shake + controls. Souls/Hades-style impact crunch.
    // Calling again before the previous pause expires extends to max(remain,
    // newFrames) — short impacts don't shorten an in-flight long pause.
    void requestPauseFor(int frames) {
        if (frames > m_pauseFramesRemaining) m_pauseFramesRemaining = frames;
    }
    bool isPaused() const { return m_pauseFramesRemaining > 0; }

    // Controls enable/disable (Lua-driven)
    void setControlsEnabled(bool enabled) { m_controlsEnabled = enabled; }
    bool isControlsEnabled() const { return m_controlsEnabled; }

    // enable/disable (Lua-driven)
    void setCameraFollowPlayer(bool enabled) { m_cameraFollowsPlayer = enabled; }
    void setCameraMode(PlayerCameraMode mode) { m_cameraMode = mode; }
    PlayerCameraMode getCameraMode() const { return m_cameraMode; }

    // Interactable access (for Lua API)
    Interactable* getInteractable(uint16_t index) {
        if (index < m_interactables.size()) return m_interactables[index];
        return nullptr;
    }

    // Player
    psyqo::Vec3& getPlayerPosition();
    void setPlayerPosition(psyqo::FixedPoint<12> x, psyqo::FixedPoint<12> y, psyqo::FixedPoint<12> z);
    psyqo::Vec3 getPlayerRotation();
    void setPlayerRotation(psyqo::FixedPoint<12> x, psyqo::FixedPoint<12> y, psyqo::FixedPoint<12> z);

    // Scene loading (for multi-scene support)
    void requestSceneLoad(int sceneIndex);
    int getCurrentSceneIndex() const { return m_currentSceneIndex; }

    /// Load a scene by index.  This is the ONE canonical load path used by
    /// both the initial boot (main.cpp) and runtime scene transitions.
    /// Blanks the screen, shows a loading screen, tears down the old scene,
    /// loads the new splashpack, and initialises.
    /// @param gpu          GPU reference.
    /// @param sceneIndex    Scene to load.
    /// @param isFirstScene  True when called from boot (skips clearScene / free).
    void loadScene(psyqo::GPU& gpu, int sceneIndex, bool isFirstScene = false);

    // Check and process pending scene load (called from GameTick)
    void processPendingSceneLoad();
    
    static Random m_random;
    static Random m_randomGenerator;

  private:
    psxsplash::Lua L;
    psxsplash::SplashPackLoader m_loader;
    CollisionSystem m_collisionSystem;
    BVHManager m_bvh;  // Spatial acceleration for frustum culling
    NavRegionSystem m_navRegions;      // Convex region navigation (v7+)
    uint16_t m_playerNavRegion = NAV_NO_REGION; // Current nav region for player
    
    // Scene type and render path: 0=exterior (BVH), 1=interior (room/portal)
    uint16_t m_sceneType = 0;

    // Hit-stop frame counter — see requestPauseFor / isPaused.
    int m_pauseFramesRemaining = 0;
    
    // Room/portal data (v11+ interior scenes). Pointers into splashpack data.
    const RoomData* m_rooms = nullptr;
    uint16_t m_roomCount = 0;
    const PortalData* m_portals = nullptr;
    uint16_t m_portalCount = 0;
    const TriangleRef* m_roomTriRefs = nullptr;
    uint16_t m_roomTriRefCount = 0;
    const RoomCell* m_roomCells = nullptr;
    uint16_t m_roomCellCount = 0;
    const RoomPortalRef* m_roomPortalRefs = nullptr;
    uint16_t m_roomPortalRefCount = 0;

    eastl::vector<LuaFile*> m_luaFiles;
    eastl::vector<GameObject*> m_gameObjects;

    // v33+: per-entity stats dense array, parallel to m_gameObjects.
    // Built once at scene init from the sparse SplashpackSceneSetup
    // statsTable. Entities without an authored PS1Stats get the
    // zero-initialized default (max* == 0 → Stats.* queries return 0).
    struct RuntimeStats {
        int16_t hp;
        int16_t maxHP;
        int16_t stamina;
        int16_t maxStamina;
        int16_t mana;
        int16_t maxMana;
    };
    eastl::vector<RuntimeStats> m_entityStats;

    // v33+: parallel per-entity i-frame counters. Decrements each
    // GameTick. Cleared to 0 on scene init.
    eastl::vector<uint16_t> m_iframes;

    // v33+: lock-on target entity index (0xFFFF = unlocked). Per-frame
    // GameTick logic snaps playerRotationY to face this entity when
    // locked, making the camera track + input become target-relative.
    // Auto-clears when the target is destroyed or deactivates.
    uint16_t m_lockTargetIndex = 0xFFFF;

    // v34+: per-entity hurtbox table pointer + count. Stored as-is
    // from the splashpack; Lua's Physics.OverlapBoxDetailed walks the
    // list at query time. nullptr/0 = no entity authored hurtboxes.
    const HurtBoxTableEntry* m_hurtBoxTable = nullptr;
    uint16_t m_hurtBoxCount = 0;

    // v35+: per-entity UV scroll. Sparse — m_uvScrollTable points at
    // the on-disk records (speeds); m_uvScrollAcc carries the
    // fractional accumulator (fp8) advanced each GameTick. Both
    // sized to m_uvScrollCount, parallel. The renderer's lookup
    // callback finds the right slot by entityIndex via a linear scan
    // (table is tiny in practice — fog gate + waterfalls).
    const UVScrollTableEntry* m_uvScrollTable = nullptr;
    uint16_t m_uvScrollCount = 0;
    eastl::vector<int32_t> m_uvScrollAccU;  // fp8 accumulator per entry
    eastl::vector<int32_t> m_uvScrollAccV;

  public:
    // v35+: renderer callback — returns (offsetU, offsetV) for the
    // given GameObject if it has UV scroll authored. Wired by main.cpp
    // at scene init via Renderer::SetUVOffsetLookup.
    bool uvScrollOffsetFor(const GameObject* obj, uint8_t* outU, uint8_t* outV) const;
    
    // Object name table (v9+): parallel to m_gameObjects, points into splashpack data
    eastl::vector<const char*> m_objectNames;
    
    // Audio clip name table (v10+): parallel to audio clips, points into splashpack data
    eastl::vector<const char*> m_audioClipNames;

    // v25: parallel to m_audioClipNames. One byte per clip, value matches
    // SplashpackSceneSetup::AudioRouting (0=SPU, 1=XA, 2=CDDA). Lua's
    // PlayMusic/PlaySfx use this to dispatch to the right backend so the
    // game script doesn't have to know how each clip was authored.
    eastl::vector<uint8_t> m_audioClipRouting;

    // v26: parallel to m_audioClipNames. CD audio track number for
    // CDDA-routed clips. 0 = unset.
    eastl::vector<uint8_t> m_audioClipCddaTrack;

    // v27: XA sidecar table. Names are pointers into the splashpack
    // data (same lifetime as m_audioClipNames). Offsets/sizes index
    // into the per-scene `scene.<n>.xa` file.
    eastl::vector<const char*> m_xaClipNames;
    eastl::vector<uint32_t>    m_xaClipOffsets;
    eastl::vector<uint32_t>    m_xaClipSizes;

    // Music sequence name table (v22+). Parallel to the sequencer's
    // registered sequences. Populated from the splashpack music table
    // in InitializeScene().
    eastl::vector<const char*> m_musicSequenceNames;
    
    // Component arrays
    eastl::vector<Interactable*> m_interactables;
    
    // Audio system
    AudioManager m_audio;
    MusicSequencer m_musicSequencer;
    MusicManager m_music;
    XaAudioBackend m_xa;
    SoundMacroSequencer m_soundMacros;
    SoundFamily m_soundFamilies;

    // PS1Graph dialogue walker (slice D1b). Ticks each frame in
    // GameTick; no-op when no dialogue is active.
    DialogueRunner m_dialogueRunner;
    
    // Cutscene playback
    Cutscene m_cutscenes[MAX_CUTSCENES];
    int m_cutsceneCount = 0;
    CutscenePlayer m_cutscenePlayer;

    Animation m_animations[MAX_ANIMATIONS];
    int m_animationCount = 0;
    AnimationPlayer m_animationPlayer;

    SkinAnimSet  m_skinAnimSets[MAX_SKINNED_MESHES];
    SkinAnimState m_skinAnimStates[MAX_SKINNED_MESHES];
    int m_skinnedMeshCount = 0;

    // v23+: UI 3D-model widgets. m_uiModelsDisk points into the
    // splashpack's on-disk array (read-only); m_uiModelStates carries
    // the mutable per-frame state (current yaw/pitch/dist + visibility
    // + runtime target-object override). Renderer reads both to compose
    // the HUD pass. UIModelRuntimeState is defined in splashpack.hh.
    static constexpr int MAX_UI_MODELS = 16;
    const SPLASHPACKUIModel *m_uiModelsDisk = nullptr;
    UIModelRuntimeState m_uiModelStates[MAX_UI_MODELS];
    int m_uiModelCount = 0;

    UISystem m_uiSystem;
#ifdef PSXSPLASH_MEMOVERLAY
    MemOverlay m_memOverlay;
#endif
#ifdef PSXSPLASH_PERFOVERLAY
    PerfOverlay m_perfOverlay;
    uint32_t m_vramSizeLoaded = 0;  // Bytes uploaded from <scene>.vram
#endif
    
    psxsplash::Controls m_controls;

    psxsplash::Camera m_currentCamera;

    // Sin/cos lookup for computing camera offset direction from player yaw.
    // Matches the table in Controls; duplicated rather than plumbed through
    // a getter because Trig is cheap and scenemanager is the only other
    // consumer that needs it.
    psyqo::Trig<> m_trig;

    psyqo::Vec3 m_playerPosition;
    psyqo::Angle playerRotationX, playerRotationY, playerRotationZ;

    psyqo::FixedPoint<12, uint16_t> m_playerHeight;

    // v21: rig offsets captured at export time from PS1Player's editor
    // children (Camera3D → cameraRigOffset; MeshInstance3D → avatar).
    // Player-local space: +X right, +Y down (PSX), +Z behind facing.
    // Runtime rotates these by playerRotationY each frame.
    psyqo::Vec3 m_cameraRigOffset;
    psyqo::Vec3 m_playerAvatarOffset;
    uint16_t m_playerAvatarObjectIndex = 0xFFFF;
    // Snapshot of the avatar's authored rotation from the splashpack
    // (set once during InitializeScene). Each frame we compose this
    // with playerRotationY so scene-side 180° compensations (e.g. a
    // FBX whose local forward is +Z instead of Godot's -Z) survive
    // the yaw update.
    psyqo::Matrix33 m_playerAvatarBaseRotation;
    PlayerCameraMode m_cameraMode = PlayerCameraMode::ThirdPerson;
    
    int32_t m_playerRadius;          
    int32_t m_velocityY;             
    int32_t m_gravityPerFrame;        
    int32_t m_jumpVelocityRaw;        
    bool m_isGrounded;                
    
    // Frame timing
    uint32_t m_lastFrameTime;         // gpu.now() timestamp of previous frame
    int32_t m_dt12;                   // Frame delta in 4.12 fixed-point (4096 = one 30fps frame)

    bool freecam = false;
    bool m_controlsEnabled = true;    // Lua can disable all player input
    bool m_cameraFollowsPlayer = true; // False when scene has no nav regions (freecam/cutscene mode)
    
    // Static font pointer (set from main.cpp)
    static psyqo::Font<>* s_font;
    
    // Scene transition state
    int m_currentSceneIndex = 0;
    int m_pendingSceneIndex = -1;        // -1 = no pending load
    uint8_t* m_currentSceneData = nullptr; // Owned pointer to loaded splashpack data
    
    // System update methods (called from GameTick)
    void updateInteractionSystem();
    void processEnableDisableEvents();
    void clearScene();  // Deallocate current scene objects

    const uint16_t m_coyoteTimeDistance = 24; // How far you can fall and still jump
    // PS1Godot tune: original 32 fp12/frame ≈ 0.94 m/s terminal velocity made
    // every fall/jump-descent feel floaty because gravity's acceleration phase
    // ended before the first frame. Bumped so jump/fall arcs are dominated by
    // the gravity setting in the splashpack instead of the cap. Expose through
    // the splashpack header later if this causes other scenes to drop too fast.
    const uint16_t m_downwardVelocityCap = 512; // Limit downward velocity

    // VRAM/SPU upload from separate data files (v20+)
    void uploadVramData(uint8_t* vramData, int vramSize);
    void uploadSpuData(uint8_t* spuData, int spuSize);
};
}  // namespace psxsplash