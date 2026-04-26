#pragma once

#include <EASTL/vector.h>

#include <psyqo/fixed-point.hh>

#include "bvh.hh"
#include "collision.hh"
#include "gameobject.hh"
#include "lua.h"
#include "navregion.hh"
#include "audiomanager.hh"
#include "interactable.hh"
#include "cutscene.hh"
#include "animation.hh"
#include "skinmesh.hh"
#include "uisystem.hh"

namespace psxsplash {

// v23+: per-PS1UIModel mutable runtime state (Lua mutates via SceneManager;
// renderer reads each frame). Static layout authored in the splashpack
// (canvas index, screen rect, projection H, name) lives in
// SPLASHPACKUIModel below — these two structs are read in lockstep.
struct UIModelRuntimeState {
    int16_t  currentYawFp10;
    int16_t  currentPitchFp10;
    int32_t  currentDistFp12;
    uint16_t currentTargetObj;
    uint8_t  visible;
    uint8_t  _pad[3];
};

// v23+: one 3D-model widget on a UI canvas. The renderer pulls in
// targetObjIndex's polygons and re-renders them via an alternate camera
// matrix built from orbit yaw/pitch/distance around the GameObject's
// own position. Layout must match the C# writer in
// SplashpackWriter.WriteUIModelSection.
struct SPLASHPACKUIModel {
    char     name[16];
    uint16_t canvasIndex;
    uint16_t targetObjIndex;
    int16_t  screenX;
    int16_t  screenY;
    uint16_t screenW;
    uint16_t screenH;
    int16_t  orbitYawFp10;       // psyqo::Angle raw, 1024 = π
    int16_t  orbitPitchFp10;
    int32_t  orbitDistFp12;
    uint16_t projectionH;
    uint8_t  visibleOnLoad;
    uint8_t  _pad;
    uint32_t _reserved1;         // reserved for future runtime state
    uint32_t _reserved2;
};
static_assert(sizeof(SPLASHPACKUIModel) == 48, "SPLASHPACKUIModel must be 48 bytes");

/**
 * Collision data as stored in the binary file (fixed layout for serialization)
 */
struct SPLASHPACKCollider {
    // AABB bounds in fixed-point (24 bytes)
    int32_t minX, minY, minZ;
    int32_t maxX, maxY, maxZ;
    // Collision metadata (8 bytes)
    uint8_t collisionType;
    uint8_t layerMask;
    uint16_t gameObjectIndex;
    uint32_t padding;
};
static_assert(sizeof(SPLASHPACKCollider) == 32, "SPLASHPACKCollider must be 32 bytes");

struct SPLASHPACKTriggerBox {
    int32_t minX, minY, minZ;
    int32_t maxX, maxY, maxZ;
    int16_t luaFileIndex;
    uint16_t padding;
    uint32_t padding2;
};
static_assert(sizeof(SPLASHPACKTriggerBox) == 32, "SPLASHPACKTriggerBox must be 32 bytes");

struct SplashpackSceneSetup {
    int sceneLuaFileIndex;
    eastl::vector<LuaFile *> luaFiles;
    eastl::vector<GameObject *> objects;
    eastl::vector<SPLASHPACKCollider *> colliders;
    eastl::vector<SPLASHPACKTriggerBox *> triggerBoxes;

    // New component arrays
    eastl::vector<Interactable *> interactables;

    eastl::vector<const char *> objectNames;

    // Audio clips (v10+): ADPCM data with metadata.
    // v25+: per-clip routing — declares which backend the clip should go
    // through. SPU is the only backend that actually plays today; XA and
    // CDDA are scaffolded for Phase 3 streaming work and currently log a
    // "not implemented" warning when invoked. AutoUnresolved means the
    // build pipeline failed to commit to a route; runtime treats it as
    // SPU so authoring mistakes don't silence audio.
    enum class AudioRouting : uint8_t {
        SPU = 0,
        XA = 1,
        CDDA = 2,
        AutoUnresolved = 3,
    };
    struct AudioClipSetup {
        const uint8_t* adpcmData;
        uint32_t sizeBytes;
        uint16_t sampleRate;
        bool loop;
        const char* name;   // Points into splashpack data (null-terminated)
        AudioRouting routing = AudioRouting::SPU;
        // v26: red-book CD audio track for CDDA-routed clips. 0 = unset;
        // Audio.PlayMusic logs "no track mapping" and returns -1 when a
        // CDDA clip has track 0.
        uint8_t cddaTrack = 0;
    };
    eastl::vector<AudioClipSetup> audioClips;

    eastl::vector<const char*> audioClipNames;

    BVHManager bvh;  // Spatial acceleration structure for culling
    NavRegionSystem navRegions;    
    psyqo::GTE::PackedVec3 playerStartPosition;
    psyqo::GTE::PackedVec3 playerStartRotation;
    psyqo::FixedPoint<12, uint16_t> playerHeight;

    // v21+: editor-driven rig data from PS1Player child nodes.
    // Offsets in PSX units, player-local space (runtime rotates by yaw).
    psyqo::GTE::PackedVec3 cameraRigOffset;
    psyqo::GTE::PackedVec3 playerAvatarOffset;
    uint16_t playerAvatarObjectIndex = 0xFFFF;  // 0xFFFF = no avatar

    // Scene type: 0=exterior (BVH culling), 1=interior (room/portal culling)
    uint16_t sceneType = 0;

    // Fog configuration (v11+)
    bool fogEnabled = false;
    uint8_t fogR = 0, fogG = 0, fogB = 0;
    uint8_t fogDensity = 5;

    const RoomData* rooms = nullptr;
    uint16_t roomCount = 0;
    const PortalData* portals = nullptr;
    uint16_t portalCount = 0;
    const TriangleRef* roomTriRefs = nullptr;
    uint16_t roomTriRefCount = 0;
    const RoomCell* roomCells = nullptr;
    uint16_t roomCellCount = 0;
    const RoomPortalRef* roomPortalRefs = nullptr;
    uint16_t roomPortalRefCount = 0;

    psyqo::FixedPoint<12, uint16_t> moveSpeed;       // Per-frame speed constant (fp12)
    psyqo::FixedPoint<12, uint16_t> sprintSpeed;     // Per-frame sprint constant (fp12)
    psyqo::FixedPoint<12, uint16_t> jumpVelocity;    // Per-second initial velocity (fp12)
    psyqo::FixedPoint<12, uint16_t> gravity;          // Per-second² acceleration (fp12)
    psyqo::FixedPoint<12, uint16_t> playerRadius;    // Collision radius (fp12)

    Cutscene loadedCutscenes[MAX_CUTSCENES];
    int cutsceneCount = 0;

    Animation loadedAnimations[MAX_ANIMATIONS];
    int animationCount = 0;

    SkinAnimSet loadedSkinAnimSets[MAX_SKINNED_MESHES];
    int skinnedMeshCount = 0;

    uint16_t uiCanvasCount = 0;
    uint8_t  uiFontCount = 0;
    uint32_t uiTableOffset = 0;

    // v22+: sequenced music. Up to 8 tracks per scene. Each entry holds
    // a pointer into the splashpack data plus its byte count, ready for
    // MusicSequencer::registerSequence().
    static constexpr int MAX_MUSIC_SEQUENCES = 8;
    struct MusicSequenceSetup {
        const uint8_t *data;
        uint32_t sizeBytes;
        const char *name; // null-terminated, lives inside the splashpack
    };
    MusicSequenceSetup musicSequences[MAX_MUSIC_SEQUENCES];
    uint16_t musicSequenceCount = 0;

    // v23+: UI 3D-model widgets. The loader leaves `uiModels` pointing
    // at the on-disk SPLASHPACKUIModel array so the renderer can walk
    // them in-place (no copy into scene manager).
    const SPLASHPACKUIModel *uiModels = nullptr;
    uint16_t uiModelCount = 0;

    // v24+: scene skybox. Renderer draws a full-screen textured quad
    // at OT depth (ORDERING_TABLE_SIZE - 2) before scene geometry. The
    // 16-byte field layout matches a UI Image typeData union slot:
    // texpage + clut + UVs + bitDepth + tint + enabled flag.
    struct SkySetup {
        uint8_t  texpageX;
        uint8_t  texpageY;
        uint16_t clutX;
        uint16_t clutY;
        uint8_t  u0, v0, u1, v1;
        uint8_t  bitDepth;  // 0 = 4bpp, 1 = 8bpp, 2 = 16bpp
        uint8_t  tintR, tintG, tintB;
        bool     enabled;
    };
    SkySetup sky = {};  // enabled=false by default
};

class SplashPackLoader {
  public:
    void LoadSplashpack(uint8_t *data, SplashpackSceneSetup &setup);
};

}  // namespace psxsplash
