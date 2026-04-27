#include "splashpack.hh"

#include <EASTL/vector.h>

#include <psyqo/fixed-point.hh>
#include <psyqo/gte-registers.hh>
#include <psyqo/primitives/common.hh>

#include "bvh.hh"
#include "collision.hh"
#include "gameobject.hh"
#include "cutscene.hh"
#include "lua.h"
#include "mesh.hh"
#include "skinmesh.hh"
#include "streq.hh"
#include "navregion.hh"

namespace psxsplash {

struct SPLASHPACKFileHeader {
    char magic[2];
    uint16_t version;
    uint16_t luaFileCount;
    uint16_t gameObjectCount;
    uint16_t textureAtlasCount;
    uint16_t clutCount;
    uint16_t colliderCount;
    uint16_t interactableCount;
    psyqo::GTE::PackedVec3 playerStartPos;
    psyqo::GTE::PackedVec3 playerStartRot;
    psyqo::FixedPoint<12, uint16_t> playerHeight;
    uint16_t sceneLuaFileIndex;
    uint16_t bvhNodeCount;
    uint16_t bvhTriangleRefCount;
    uint16_t sceneType;
    uint16_t triggerBoxCount;
    uint16_t worldCollisionMeshCount;
    uint16_t worldCollisionTriCount;
    uint16_t navRegionCount;
    uint16_t navPortalCount;
    uint16_t moveSpeed;
    uint16_t sprintSpeed;
    uint16_t jumpVelocity;
    uint16_t gravity;
    uint16_t playerRadius;
    uint16_t pad1;
    uint32_t nameTableOffset;
    uint16_t audioClipCount;
    uint16_t pad2;
    uint32_t audioTableOffset;
    uint8_t fogEnabled;
    uint8_t fogR, fogG, fogB;
    uint8_t fogDensity;
    uint8_t pad3;
    uint16_t roomCount;
    uint16_t portalCount;
    uint16_t roomTriRefCount;
    uint16_t cutsceneCount;
    uint16_t roomCellCount;
    uint32_t cutsceneTableOffset;
    uint16_t uiCanvasCount;
    uint8_t  uiFontCount;
    uint8_t  uiPad5;
    uint32_t uiTableOffset;
    uint32_t pixelDataOffset;
    uint16_t animationCount;
    uint16_t roomPortalRefCount;
    uint32_t animationTableOffset;
    uint16_t skinnedMeshCount;
    uint16_t pad_skin;
    uint32_t skinTableOffset;
    // v21+: editor-driven player rig data. cameraRigOffset and
    // playerAvatarOffset are in PSX units (Y already negated at export),
    // player-local: +X = right, +Y = down, +Z = behind the facing
    // direction. Runtime rotates them by playerRotationY each frame.
    // playerAvatarObjectIndex points to the GameObject that should
    // follow the player (0xFFFF = no avatar tracking).
    psyqo::GTE::PackedVec3 cameraRigOffset;
    psyqo::GTE::PackedVec3 playerAvatarOffset;
    uint16_t playerAvatarObjectIndex;
    uint16_t pad_rig;
    // v22+: sequenced music. musicTableOffset points at a flat array of
    // 24-byte MusicTableEntry { u32 dataOffset, u32 dataSize, char[16] name }
    // each pointing at a PS1M-format sequence blob elsewhere in the file.
    uint16_t musicSequenceCount;
    uint16_t pad_music;
    uint32_t musicTableOffset;
    // v23+: UI 3D models. Flat array of 48-byte UIModelEntry records
    // (see SPLASHPACKUIModel below). Rendered in a post-main-scene HUD
    // pass via Renderer::renderUIModels. Zero in scenes with no model
    // widgets — loader still guards the field so v22 splashpacks with
    // extra zero bytes past the end read as "no models" (they won't,
    // since the writer for v22 didn't emit those 8 bytes — but the
    // >= 23 version check keeps the path off for old packs).
    uint16_t uiModelCount;
    uint16_t pad_uimodel;
    uint32_t uiModelTableOffset;
    // v24+: scene-level skybox. Layout matches the UI Image typeData
    // union slot (uisystem.cpp:153) so the same tpage/clut/UV decode
    // applies. skyEnabled = 0 → no sky in this scene; renderer skips
    // the pass and the other 15 bytes are ignored.
    uint8_t  skyTexpageX;
    uint8_t  skyTexpageY;
    uint16_t skyClutX;
    uint16_t skyClutY;
    uint8_t  skyU0;
    uint8_t  skyV0;
    uint8_t  skyU1;
    uint8_t  skyV1;
    uint8_t  skyBitDepth;
    uint8_t  skyTintR;
    uint8_t  skyTintG;
    uint8_t  skyTintB;
    uint8_t  skyEnabled;
    uint8_t  pad_sky;
    // v27+: XA clip table. One entry per Route=XA audio clip; entries
    // point into `scene.<n>.xa` (Form-2 XA-ADPCM payload produced by
    // psxavenc). xaClipCount=0 = no XA clips in this scene.
    uint16_t xaClipCount;
    uint16_t pad_xa;
    uint32_t xaTableOffset;
    // v28+: scene-wide instrument bank. See SPLASHPACK*Record types in
    // splashpack.hh for layouts. instrumentCount=0 means the scene
    // shipped no bank — sequences fall back to legacy
    // direct-channel-to-clip bindings (PS1M format).
    uint16_t instrumentCount;
    uint16_t regionCount;
    uint16_t drumKitCount;
    uint16_t drumMappingCount;
    uint32_t instrumentTableOffset;
    uint32_t regionTableOffset;
    uint32_t drumKitTableOffset;
    uint32_t drumMappingTableOffset;
    // v29+: sound macros + sound families (composite SFX + variation
    // pools, see strategy doc §15-§16). soundMacroCount = 0 means the
    // scene has no macros; same for families. familyClipIndices is a
    // flat u16 array sliced per-family.
    uint16_t soundMacroCount;
    uint16_t soundMacroEventCount;
    uint16_t soundFamilyCount;
    uint16_t familyClipIndexCount;
    uint32_t soundMacroTableOffset;
    uint32_t soundMacroEventTableOffset;
    uint32_t soundFamilyTableOffset;
    uint32_t familyClipIndexTableOffset;
};
static_assert(sizeof(SPLASHPACKFileHeader) == 224, "SPLASHPACKFileHeader must be 224 bytes");

struct MusicTableEntry {
    uint32_t dataOffset;
    uint32_t dataSize;
    char name[16];
};
static_assert(sizeof(MusicTableEntry) == 24, "MusicTableEntry must be 24 bytes");

// SPLASHPACKUIModel is declared in splashpack.hh so the renderer and
// scene manager can walk the on-disk array without including this .cpp.

struct SPLASHPACKTextureAtlas {
    uint32_t polygonsOffset;
    uint16_t width, height;
    uint16_t x, y;
};

struct SPLASHPACKClut {
    uint32_t clutOffset;
    uint16_t clutPackingX;
    uint16_t clutPackingY;
    uint16_t length;
    uint16_t pad;
};

void SplashPackLoader::LoadSplashpack(uint8_t *data, SplashpackSceneSetup &setup) {
    psyqo::Kernel::assert(data != nullptr, "Splashpack loading data pointer is null");
    psxsplash::SPLASHPACKFileHeader *header = reinterpret_cast<psxsplash::SPLASHPACKFileHeader *>(data);
    psyqo::Kernel::assert(__builtin_memcmp(header->magic, "SP", 2) == 0, "Splashpack has incorrect magic");
    psyqo::Kernel::assert(header->version >= 30, "Splashpack version too old (need v30+): re-export from PS1Godot");

    setup.playerStartPosition = header->playerStartPos;
    setup.playerStartRotation = header->playerStartRot;
    setup.playerHeight = header->playerHeight;

    // v21+: editor-configured rig offsets and optional avatar link
    setup.cameraRigOffset = header->cameraRigOffset;
    setup.playerAvatarOffset = header->playerAvatarOffset;
    setup.playerAvatarObjectIndex = header->playerAvatarObjectIndex;
    
    setup.moveSpeed.value = header->moveSpeed;
    setup.sprintSpeed.value = header->sprintSpeed;
    setup.jumpVelocity.value = header->jumpVelocity;
    setup.gravity.value = header->gravity;
    setup.playerRadius.value = header->playerRadius;

    setup.luaFiles.reserve(header->luaFileCount);
    setup.objects.reserve(header->gameObjectCount);
    setup.colliders.reserve(header->colliderCount);
    setup.interactables.reserve(header->interactableCount);

    uint8_t *cursor = data + sizeof(SPLASHPACKFileHeader);

    for (uint16_t i = 0; i < header->luaFileCount; i++) {
        psxsplash::LuaFile *luaHeader = reinterpret_cast<psxsplash::LuaFile *>(cursor);
        luaHeader->luaCode = reinterpret_cast<const char *>(data + luaHeader->luaCodeOffset);
        setup.luaFiles.push_back(luaHeader);
        cursor += sizeof(psxsplash::LuaFile);
    }

    setup.sceneLuaFileIndex = (header->sceneLuaFileIndex == 0xFFFF) ? -1 : (int)header->sceneLuaFileIndex;

    for (uint16_t i = 0; i < header->gameObjectCount; i++) {
        psxsplash::GameObject *go = reinterpret_cast<psxsplash::GameObject *>(cursor);
        go->polygons = reinterpret_cast<psxsplash::Tri *>(data + go->polygonsOffset);
        setup.objects.push_back(go);
        cursor += sizeof(psxsplash::GameObject);
    }

    for (uint16_t i = 0; i < header->colliderCount; i++) {
        psxsplash::SPLASHPACKCollider *collider = reinterpret_cast<psxsplash::SPLASHPACKCollider *>(cursor);
        setup.colliders.push_back(collider);
        cursor += sizeof(psxsplash::SPLASHPACKCollider);
    }

    setup.triggerBoxes.reserve(header->triggerBoxCount);
    for (uint16_t i = 0; i < header->triggerBoxCount; i++) {
        psxsplash::SPLASHPACKTriggerBox *tb = reinterpret_cast<psxsplash::SPLASHPACKTriggerBox *>(cursor);
        setup.triggerBoxes.push_back(tb);
        cursor += sizeof(psxsplash::SPLASHPACKTriggerBox);
    }

    if (header->bvhNodeCount > 0) {
        BVHNode* bvhNodes = reinterpret_cast<BVHNode*>(cursor);
        cursor += header->bvhNodeCount * sizeof(BVHNode);
        
        TriangleRef* triangleRefs = reinterpret_cast<TriangleRef*>(cursor);
        cursor += header->bvhTriangleRefCount * sizeof(TriangleRef);
        
        setup.bvh.initialize(bvhNodes, header->bvhNodeCount, 
                             triangleRefs, header->bvhTriangleRefCount);
    }

    for (uint16_t i = 0; i < header->interactableCount; i++) {
        psxsplash::Interactable *interactable = reinterpret_cast<psxsplash::Interactable *>(cursor);
        setup.interactables.push_back(interactable);
        cursor += sizeof(psxsplash::Interactable);
    }

    // Skip over legacy world collision data if present in older binaries
    if (header->worldCollisionMeshCount > 0) {
        uintptr_t addr = reinterpret_cast<uintptr_t>(cursor);
        cursor = reinterpret_cast<uint8_t*>((addr + 3) & ~3);
        // CollisionDataHeader: 20 bytes
        const uint16_t meshCount = *reinterpret_cast<const uint16_t*>(cursor);
        const uint16_t triCount = *reinterpret_cast<const uint16_t*>(cursor + 2);
        const uint16_t chunkW = *reinterpret_cast<const uint16_t*>(cursor + 4);
        const uint16_t chunkH = *reinterpret_cast<const uint16_t*>(cursor + 6);
        cursor += 20; // CollisionDataHeader
        cursor += meshCount * 32; // CollisionMeshHeader (32 bytes each)
        cursor += triCount * 52;  // CollisionTri (52 bytes each)
        if (chunkW > 0 && chunkH > 0)
            cursor += chunkW * chunkH * 4; // CollisionChunk (4 bytes each)
    }

    if (header->navRegionCount > 0) {
        uintptr_t addr = reinterpret_cast<uintptr_t>(cursor);
        cursor = reinterpret_cast<uint8_t*>((addr + 3) & ~3);
        cursor = const_cast<uint8_t*>(setup.navRegions.initializeFromData(cursor));
    }

    if (header->roomCount > 0) {
        uintptr_t addr = reinterpret_cast<uintptr_t>(cursor);
        cursor = reinterpret_cast<uint8_t*>((addr + 3) & ~3);

        setup.rooms = reinterpret_cast<const RoomData*>(cursor);
        setup.roomCount = header->roomCount;
        cursor += header->roomCount * sizeof(RoomData);

        setup.portals = reinterpret_cast<const PortalData*>(cursor);
        setup.portalCount = header->portalCount;
        cursor += header->portalCount * sizeof(PortalData);

        setup.roomTriRefs = reinterpret_cast<const TriangleRef*>(cursor);
        setup.roomTriRefCount = header->roomTriRefCount;
        cursor += header->roomTriRefCount * sizeof(TriangleRef);

        // Room cells (v17+): per-room spatial subdivision for frustum culling.
        // Cell data follows tri-refs. If roomCellCount is 0, cells == nullptr.
        if (header->roomCellCount > 0) {
            setup.roomCells = reinterpret_cast<const RoomCell*>(cursor);
            setup.roomCellCount = header->roomCellCount;
            cursor += header->roomCellCount * sizeof(RoomCell);
        }

        // Per-room portal reference lists (Phase 5).
        // Each RoomPortalRef is 4 bytes: portalIndex (u16) + otherRoom (u16).
        if (header->roomPortalRefCount > 0) {
            setup.roomPortalRefs = reinterpret_cast<const RoomPortalRef*>(cursor);
            setup.roomPortalRefCount = header->roomPortalRefCount;
            cursor += header->roomPortalRefCount * sizeof(RoomPortalRef);
        }
    }

    // Atlas metadata — v20: pixel data is in a separate .vram file.
    // We still parse the metadata entries (to advance the cursor) since
    // tpage/clut coordinates are baked into the triangle data.
    for (uint16_t i = 0; i < header->textureAtlasCount; i++) {
        cursor += sizeof(psxsplash::SPLASHPACKTextureAtlas);
    }

    // CLUT metadata — v20: CLUT data is in a separate .vram file.
    for (uint16_t i = 0; i < header->clutCount; i++) {
        cursor += sizeof(psxsplash::SPLASHPACKClut);
    }

    if (header->nameTableOffset != 0) {
        uint8_t* nameData = data + header->nameTableOffset;
        setup.objectNames.reserve(header->gameObjectCount);
        for (uint16_t i = 0; i < header->gameObjectCount; i++) {
            uint8_t nameLen = *nameData++;
            const char* nameStr = reinterpret_cast<const char*>(nameData);
            setup.objectNames.push_back(nameStr);
            nameData += nameLen + 1; // +1 for null terminator
        }
    }

    if (header->audioClipCount > 0 && header->audioTableOffset != 0) {
        uint8_t* audioTable = data + header->audioTableOffset;
        setup.audioClips.reserve(header->audioClipCount);
        setup.audioClipNames.reserve(header->audioClipCount);
        // v26 entry layout (20 bytes): dataOff(u32) size(u32) rate(u16)
        // loop(u8) nameLen(u8) nameOff(u32) routing(u8) cddaTrack(u8)
        // _pad(2). The 2-byte tail keeps the next entry on a 4-byte
        // boundary so the u32 reads at offset 0 of each entry stay
        // aligned on MIPS.
        for (uint16_t i = 0; i < header->audioClipCount; i++) {
            uint32_t dataOff   = *reinterpret_cast<uint32_t*>(audioTable); audioTable += 4;
            uint32_t size      = *reinterpret_cast<uint32_t*>(audioTable); audioTable += 4;
            uint16_t rate      = *reinterpret_cast<uint16_t*>(audioTable); audioTable += 2;
            uint8_t  loop      = *audioTable++;
            uint8_t  nameLen   = *audioTable++;
            uint32_t nameOff   = *reinterpret_cast<uint32_t*>(audioTable); audioTable += 4;
            uint8_t  routing   = *audioTable++;
            uint8_t  cddaTrack = *audioTable++;
            audioTable += 2;  // align pad
            SplashpackSceneSetup::AudioClipSetup clip;
            // v20: ADPCM data is in a separate .spu file; dataOff is 0.
            clip.adpcmData = nullptr;
            clip.sizeBytes = size;
            clip.sampleRate = rate;
            clip.loop = (loop != 0);
            clip.name = (nameLen > 0 && nameOff != 0) ? reinterpret_cast<const char*>(data + nameOff) : nullptr;
            clip.routing = (routing <= static_cast<uint8_t>(SplashpackSceneSetup::AudioRouting::AutoUnresolved))
                ? static_cast<SplashpackSceneSetup::AudioRouting>(routing)
                : SplashpackSceneSetup::AudioRouting::SPU;
            clip.cddaTrack = cddaTrack;
            (void)dataOff;  // silence unused warning until we restore inline data path
            setup.audioClips.push_back(clip);
            setup.audioClipNames.push_back(clip.name);
        }
    }

    // v27 XA table. One 24-byte entry per Route=XA clip:
    //   sidecarOffset(u32) sidecarSize(u32) name[16 chars, null-padded]
    // The runtime joins XA clips to audio clips by name string match.
    // xaClipCount can be 0 when the scene has no XA-routed clips OR
    // when psxavenc was unavailable at export time (the Godot side
    // logs that case and falls back to SPU silence for those clips).
    if (header->version >= 27 && header->xaClipCount > 0 && header->xaTableOffset != 0) {
        uint8_t* xaTable = data + header->xaTableOffset;
        setup.xaClips.reserve(header->xaClipCount);
        for (uint16_t i = 0; i < header->xaClipCount; i++) {
            uint32_t sidOff  = *reinterpret_cast<uint32_t*>(xaTable); xaTable += 4;
            uint32_t sidSize = *reinterpret_cast<uint32_t*>(xaTable); xaTable += 4;
            // 16-byte inline name; null-padded so direct char* read is safe.
            const char* inlineName = reinterpret_cast<const char*>(xaTable);
            xaTable += 16;
            SplashpackSceneSetup::XaClipEntry e;
            e.name = inlineName;
            e.sidecarOffset = sidOff;
            e.sidecarSize   = sidSize;
            setup.xaClips.push_back(e);
        }
    }

    // v28+: scene-wide instrument bank. Loader points the setup struct
    // at the on-disk record arrays — runtime parses in-place, no copy.
    // Each table is independent; an instrument with no regions, or a
    // drum kit with no mappings, simply has count 0 in its parent
    // record. instrumentCount=0 → no bank for this scene; sequences
    // stay on the legacy direct-binding path (PS1M format). Phase 2.5
    // Stage A leaves these load-but-unused; Stage C dispatches NoteOn
    // through the bank for PS2M-format sequences.
    setup.instrumentCount  = 0;
    setup.regionCount      = 0;
    setup.drumKitCount     = 0;
    setup.drumMappingCount = 0;
    setup.instruments  = nullptr;
    setup.regions      = nullptr;
    setup.drumKits     = nullptr;
    setup.drumMappings = nullptr;
    if (header->version >= 28) {
        if (header->instrumentCount > 0 && header->instrumentTableOffset != 0) {
            setup.instruments = reinterpret_cast<const SPLASHPACKInstrumentRecord*>(
                data + header->instrumentTableOffset);
            setup.instrumentCount = header->instrumentCount;
        }
        if (header->regionCount > 0 && header->regionTableOffset != 0) {
            setup.regions = reinterpret_cast<const SPLASHPACKRegionRecord*>(
                data + header->regionTableOffset);
            setup.regionCount = header->regionCount;
        }
        if (header->drumKitCount > 0 && header->drumKitTableOffset != 0) {
            setup.drumKits = reinterpret_cast<const SPLASHPACKDrumKitRecord*>(
                data + header->drumKitTableOffset);
            setup.drumKitCount = header->drumKitCount;
        }
        if (header->drumMappingCount > 0 && header->drumMappingTableOffset != 0) {
            setup.drumMappings = reinterpret_cast<const SPLASHPACKDrumMappingRecord*>(
                data + header->drumMappingTableOffset);
            setup.drumMappingCount = header->drumMappingCount;
        }
    }

    // v29+: sound macro + sound family banks. Same in-place pointer
    // pattern as the v28 instrument bank — the runtime indexes the
    // on-disk arrays directly. Stage A leaves these load-but-unused;
    // Stage B will wire SoundMacroSequencer / SoundFamily dispatch.
    setup.soundMacroCount        = 0;
    setup.soundMacroEventCount   = 0;
    setup.soundFamilyCount       = 0;
    setup.familyClipIndexCount   = 0;
    setup.soundMacros            = nullptr;
    setup.soundMacroEvents       = nullptr;
    setup.soundFamilies          = nullptr;
    setup.familyClipIndices      = nullptr;
    if (header->version >= 29) {
        if (header->soundMacroCount > 0 && header->soundMacroTableOffset != 0) {
            setup.soundMacros = reinterpret_cast<const SPLASHPACKSoundMacroRecord*>(
                data + header->soundMacroTableOffset);
            setup.soundMacroCount = header->soundMacroCount;
        }
        if (header->soundMacroEventCount > 0 && header->soundMacroEventTableOffset != 0) {
            setup.soundMacroEvents = reinterpret_cast<const SPLASHPACKSoundMacroEventRecord*>(
                data + header->soundMacroEventTableOffset);
            setup.soundMacroEventCount = header->soundMacroEventCount;
        }
        if (header->soundFamilyCount > 0 && header->soundFamilyTableOffset != 0) {
            setup.soundFamilies = reinterpret_cast<const SPLASHPACKSoundFamilyRecord*>(
                data + header->soundFamilyTableOffset);
            setup.soundFamilyCount = header->soundFamilyCount;
        }
        if (header->familyClipIndexCount > 0 && header->familyClipIndexTableOffset != 0) {
            setup.familyClipIndices = reinterpret_cast<const uint16_t*>(
                data + header->familyClipIndexTableOffset);
            setup.familyClipIndexCount = header->familyClipIndexCount;
        }
    }

    setup.fogEnabled = header->fogEnabled != 0;
    setup.fogR = header->fogR;
    setup.fogG = header->fogG;
    setup.fogB = header->fogB;
    setup.fogDensity = header->fogDensity;
    setup.sceneType = header->sceneType;

    // v22+: sequenced music table. Entries live at
    // header->musicTableOffset; each is MusicTableEntry {u32 dataOff,
    // u32 dataSize, char[16] name}. The referenced blobs are PS1M
    // sequence data — MusicSequencer::registerSequence consumes them.
    setup.musicSequenceCount = 0;
    if (header->musicSequenceCount > 0 && header->musicTableOffset != 0) {
        int count = header->musicSequenceCount;
        if (count > (int)SplashpackSceneSetup::MAX_MUSIC_SEQUENCES) {
            count = SplashpackSceneSetup::MAX_MUSIC_SEQUENCES;
        }
        const MusicTableEntry *table = reinterpret_cast<const MusicTableEntry *>(data + header->musicTableOffset);
        for (int i = 0; i < count; i++) {
            setup.musicSequences[i].data = data + table[i].dataOffset;
            setup.musicSequences[i].sizeBytes = table[i].dataSize;
            setup.musicSequences[i].name = table[i].name[0] ? table[i].name : nullptr;
        }
        setup.musicSequenceCount = (uint16_t)count;
    }

    // v23+: UI 3D-model table. Leave the on-disk array in place and hand
    // the scene manager a typed pointer + count; the renderer walks the
    // array directly without per-frame copying.
    setup.uiModelCount = 0;
    setup.uiModels = nullptr;
    if (header->version >= 23 && header->uiModelCount > 0 && header->uiModelTableOffset != 0) {
        setup.uiModels = reinterpret_cast<const SPLASHPACKUIModel *>(data + header->uiModelTableOffset);
        setup.uiModelCount = header->uiModelCount;
    }

    // v24+: scene skybox. The exporter zeros the whole 16-byte block
    // when no PS1Sky was authored, so checking skyEnabled is enough.
    if (header->version >= 24 && header->skyEnabled) {
        setup.sky.texpageX = header->skyTexpageX;
        setup.sky.texpageY = header->skyTexpageY;
        setup.sky.clutX    = header->skyClutX;
        setup.sky.clutY    = header->skyClutY;
        setup.sky.u0       = header->skyU0;
        setup.sky.v0       = header->skyV0;
        setup.sky.u1       = header->skyU1;
        setup.sky.v1       = header->skyV1;
        setup.sky.bitDepth = header->skyBitDepth;
        setup.sky.tintR    = header->skyTintR;
        setup.sky.tintG    = header->skyTintG;
        setup.sky.tintB    = header->skyTintB;
        setup.sky.enabled  = true;
    }

    if (header->cutsceneCount > 0 && header->cutsceneTableOffset != 0) {
        setup.cutsceneCount = 0;
        uint8_t* tablePtr = data + header->cutsceneTableOffset;
        int csCount = header->cutsceneCount;
        if (csCount > MAX_CUTSCENES) csCount = MAX_CUTSCENES;

        for (int ci = 0; ci < csCount; ci++) {
            // SPLASHPACKCutsceneEntry: 12 bytes
            uint32_t dataOffset  = *reinterpret_cast<uint32_t*>(tablePtr); tablePtr += 4;
            uint8_t  nameLen     = *tablePtr++;                                       
            tablePtr += 3; // pad
            uint32_t nameOffset  = *reinterpret_cast<uint32_t*>(tablePtr); tablePtr += 4;

            Cutscene& cs = setup.loadedCutscenes[ci];
            cs.name = (nameLen > 0 && nameOffset != 0)
                      ? reinterpret_cast<const char*>(data + nameOffset)
                      : nullptr;

            // SPLASHPACKCutscene: 12 bytes at dataOffset
            uint8_t* csPtr = data + dataOffset;
            cs.totalFrames       = *reinterpret_cast<uint16_t*>(csPtr); csPtr += 2;
            cs.trackCount        = *csPtr++;
            cs.audioEventCount   = *csPtr++;
            uint32_t tracksOff   = *reinterpret_cast<uint32_t*>(csPtr); csPtr += 4;
            uint32_t audioOff    = *reinterpret_cast<uint32_t*>(csPtr); csPtr += 4;

            // v19: skin anim events follow (4 bytes: count + pad + offset)
            cs.skinAnimEventCount = 0;
            cs.skinAnimEvents = nullptr;
            if (header->version >= 19) {
                cs.skinAnimEventCount = *csPtr++;
                csPtr += 3; // pad
                uint32_t skinAnimOff = *reinterpret_cast<uint32_t*>(csPtr); csPtr += 4;
                if (cs.skinAnimEventCount > MAX_SKIN_ANIM_EVENTS)
                    cs.skinAnimEventCount = MAX_SKIN_ANIM_EVENTS;
                cs.skinAnimEvents = (cs.skinAnimEventCount > 0 && skinAnimOff != 0)
                    ? reinterpret_cast<CutsceneSkinAnimEvent*>(data + skinAnimOff)
                    : nullptr;
            }

            if (cs.trackCount > MAX_TRACKS) cs.trackCount = MAX_TRACKS;
            if (cs.audioEventCount > MAX_AUDIO_EVENTS) cs.audioEventCount = MAX_AUDIO_EVENTS;

            // Audio events pointer
            cs.audioEvents = (cs.audioEventCount > 0 && audioOff != 0)
                             ? reinterpret_cast<CutsceneAudioEvent*>(data + audioOff)
                             : nullptr;

            // Parse tracks
            uint8_t* trackPtr = data + tracksOff;
            for (uint8_t ti = 0; ti < cs.trackCount; ti++) {
                CutsceneTrack& track = cs.tracks[ti];

                // SPLASHPACKCutsceneTrack: 12 bytes
                track.trackType     = static_cast<TrackType>(*trackPtr++);
                track.keyframeCount = *trackPtr++;
                uint8_t objNameLen  = *trackPtr++;
                trackPtr++; // pad
                uint32_t objNameOff = *reinterpret_cast<uint32_t*>(trackPtr); trackPtr += 4;
                uint32_t kfOff      = *reinterpret_cast<uint32_t*>(trackPtr); trackPtr += 4;

                // Resolve keyframes pointer
                track.keyframes = (track.keyframeCount > 0 && kfOff != 0)
                                  ? reinterpret_cast<CutsceneKeyframe*>(data + kfOff)
                                  : nullptr;

                // Resolve target object by name (or store UI name for later resolution)
                track.target = nullptr;
                track.uiHandle = -1;
                if (objNameLen > 0 && objNameOff != 0) {
                    const char* objName = reinterpret_cast<const char*>(data + objNameOff);
                    bool isUI = isUITrackType(track.trackType);
                    if (isUI) {
                        // Store the raw name pointer temporarily in target
                        // (will be resolved to uiHandle later by scenemanager)
                        track.target = reinterpret_cast<GameObject*>(const_cast<char*>(objName));
                    } else {
                        for (size_t oi = 0; oi < setup.objectNames.size(); oi++) {
                            if (setup.objectNames[oi] &&
                                streq(setup.objectNames[oi], objName)) {
                                track.target = setup.objects[oi];
                                break;
                            }
                        }
                    }
                    // If not found, target stays nullptr — track will be skipped at runtime
                }
            }

            // Zero out unused track slots
            for (uint8_t ti = cs.trackCount; ti < MAX_TRACKS; ti++) {
                cs.tracks[ti].keyframeCount = 0;
                cs.tracks[ti].keyframes = nullptr;
                cs.tracks[ti].target = nullptr;
                cs.tracks[ti].uiHandle = -1;
                cs.tracks[ti].initialValues[0] = 0;
                cs.tracks[ti].initialValues[1] = 0;
                cs.tracks[ti].initialValues[2] = 0;
            }

            setup.cutsceneCount++;
        }
    }

    if (header->version >= 13) {
        setup.uiCanvasCount = header->uiCanvasCount;
        setup.uiFontCount = header->uiFontCount;
        setup.uiTableOffset = header->uiTableOffset;
    }

    // Animation loading (v17+)
    if (header->animationCount > 0 && header->animationTableOffset != 0) {
        setup.animationCount = 0;
        uint8_t* tablePtr = data + header->animationTableOffset;
        int anCount = header->animationCount;
        if (anCount > MAX_ANIMATIONS) anCount = MAX_ANIMATIONS;

        for (int ai = 0; ai < anCount; ai++) {
            // SPLASHPACKAnimationEntry: 12 bytes (same layout as cutscene entry)
            uint32_t dataOffset  = *reinterpret_cast<uint32_t*>(tablePtr); tablePtr += 4;
            uint8_t  nameLen     = *tablePtr++;
            tablePtr += 3; // pad
            uint32_t nameOffset  = *reinterpret_cast<uint32_t*>(tablePtr); tablePtr += 4;

            Animation& an = setup.loadedAnimations[ai];
            an.name = (nameLen > 0 && nameOffset != 0)
                      ? reinterpret_cast<const char*>(data + nameOffset)
                      : nullptr;

            // SPLASHPACKAnimation: 8 bytes (no audio), then optionally skin anim events (v19)
            uint8_t* anPtr = data + dataOffset;
            an.totalFrames = *reinterpret_cast<uint16_t*>(anPtr); anPtr += 2;
            an.trackCount  = *anPtr++;
            an.skinAnimEventCount = 0;
            an.skinAnimEvents = nullptr;
            anPtr++; // pad (was 'pad' field)
            uint32_t tracksOff = *reinterpret_cast<uint32_t*>(anPtr); anPtr += 4;

            // v19: skin anim events for animations
            if (header->version >= 19) {
                an.skinAnimEventCount = *anPtr++;
                anPtr += 3; // pad
                uint32_t skinAnimOff = *reinterpret_cast<uint32_t*>(anPtr); anPtr += 4;
                if (an.skinAnimEventCount > MAX_SKIN_ANIM_EVENTS)
                    an.skinAnimEventCount = MAX_SKIN_ANIM_EVENTS;
                an.skinAnimEvents = (an.skinAnimEventCount > 0 && skinAnimOff != 0)
                    ? reinterpret_cast<CutsceneSkinAnimEvent*>(data + skinAnimOff)
                    : nullptr;
            }

            if (an.trackCount > MAX_ANIM_TRACKS) an.trackCount = MAX_ANIM_TRACKS;

            // Parse tracks (same format as cutscene tracks)
            uint8_t* trackPtr = data + tracksOff;
            for (uint8_t ti = 0; ti < an.trackCount; ti++) {
                CutsceneTrack& track = an.tracks[ti];

                track.trackType     = static_cast<TrackType>(*trackPtr++);
                track.keyframeCount = *trackPtr++;
                uint8_t objNameLen  = *trackPtr++;
                trackPtr++; // pad
                uint32_t objNameOff = *reinterpret_cast<uint32_t*>(trackPtr); trackPtr += 4;
                uint32_t kfOff      = *reinterpret_cast<uint32_t*>(trackPtr); trackPtr += 4;

                track.keyframes = (track.keyframeCount > 0 && kfOff != 0)
                                  ? reinterpret_cast<CutsceneKeyframe*>(data + kfOff)
                                  : nullptr;

                track.target = nullptr;
                track.uiHandle = -1;
                if (objNameLen > 0 && objNameOff != 0) {
                    const char* objName = reinterpret_cast<const char*>(data + objNameOff);
                    bool isUI = isUITrackType(track.trackType);
                    if (isUI) {
                        track.target = reinterpret_cast<GameObject*>(const_cast<char*>(objName));
                    } else {
                        for (size_t oi = 0; oi < setup.objectNames.size(); oi++) {
                            if (setup.objectNames[oi] &&
                                streq(setup.objectNames[oi], objName)) {
                                track.target = setup.objects[oi];
                                break;
                            }
                        }
                    }
                }
            }

            // Zero unused track slots
            for (uint8_t ti = an.trackCount; ti < MAX_ANIM_TRACKS; ti++) {
                an.tracks[ti].keyframeCount = 0;
                an.tracks[ti].keyframes = nullptr;
                an.tracks[ti].target = nullptr;
                an.tracks[ti].uiHandle = -1;
                an.tracks[ti].initialValues[0] = 0;
                an.tracks[ti].initialValues[1] = 0;
                an.tracks[ti].initialValues[2] = 0;
            }

            setup.animationCount++;
        }
    }

    // Skinned mesh loading (v18+)
    if (header->version >= 18 && header->skinnedMeshCount > 0 && header->skinTableOffset != 0) {
        uint8_t* tablePtr = data + header->skinTableOffset;
        int smCount = header->skinnedMeshCount;
        if (smCount > MAX_SKINNED_MESHES) smCount = MAX_SKINNED_MESHES;

        for (int si = 0; si < smCount; si++) {
            uint32_t dataOffset  = *reinterpret_cast<uint32_t*>(tablePtr); tablePtr += 4;
            uint8_t  nameLen     = *tablePtr++;
            tablePtr += 3; // pad
            uint32_t nameOffset  = *reinterpret_cast<uint32_t*>(tablePtr); tablePtr += 4;

            SkinAnimSet& animSet = setup.loadedSkinAnimSets[si];

            // Parse SkinData block
            uint8_t* skinPtr = data + dataOffset;
            animSet.gameObjectIndex = *reinterpret_cast<uint16_t*>(skinPtr); skinPtr += 2;
            animSet.boneCount       = *skinPtr++;
            animSet.clipCount       = *skinPtr++;

            // Bone indices: polyCount × 3 bytes
            uint16_t polyCount = 0;
            if (animSet.gameObjectIndex < setup.objects.size()) {
                polyCount = setup.objects[animSet.gameObjectIndex]->polyCount;
            }
            animSet.boneIndices = skinPtr;
            skinPtr += polyCount * 3;

            // Align to 4-byte boundary
            uintptr_t addr = reinterpret_cast<uintptr_t>(skinPtr);
            skinPtr = reinterpret_cast<uint8_t*>((addr + 3) & ~3);

            // Parse clips
            if (animSet.clipCount > SKINMESH_MAX_CLIPS) animSet.clipCount = SKINMESH_MAX_CLIPS;
            for (uint8_t ci = 0; ci < animSet.clipCount; ci++) {
                SkinAnimClip& clip = animSet.clips[ci];

                uint8_t clipNameLen = *skinPtr++;
                // Null-terminate the name in place
                clip.name = reinterpret_cast<const char*>(skinPtr);
                skinPtr += clipNameLen;
                *skinPtr = '\0';
                skinPtr++;

                clip.flags      = *skinPtr++;
                clip.fps        = *skinPtr++;
                // Align to 2-byte boundary for uint16_t frameCount (MIPS requires aligned reads)
                addr = reinterpret_cast<uintptr_t>(skinPtr);
                skinPtr = reinterpret_cast<uint8_t*>((addr + 1) & ~1);
                clip.frameCount = *reinterpret_cast<uint16_t*>(skinPtr); skinPtr += 2;
                clip.boneCount  = animSet.boneCount;

                // Frame data: frameCount × boneCount × sizeof(BakedBonePose) bytes (v30+)
                clip.frames = reinterpret_cast<const BakedBonePose*>(skinPtr);
                skinPtr += (uint32_t)clip.frameCount * (uint32_t)animSet.boneCount * sizeof(BakedBonePose);
            }

            // Zero unused clip slots
            for (uint8_t ci = animSet.clipCount; ci < SKINMESH_MAX_CLIPS; ci++) {
                animSet.clips[ci].name = nullptr;
                animSet.clips[ci].frames = nullptr;
                animSet.clips[ci].frameCount = 0;
            }

            setup.skinnedMeshCount++;
        }
    }

}

}  // namespace psxsplash