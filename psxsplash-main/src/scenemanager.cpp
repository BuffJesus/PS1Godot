#include "scenemanager.hh"

#include <utility>

#include "collision.hh"
#include "gtemath.hh"
#include "profiler.hh"
#include "renderer.hh"
#include "splashpack.hh"
#include "streq.hh"
#include "luaapi.hh"
#include "loadingscreen.hh"

#include <psyqo/soft-math.hh>

#include <psyqo/primitives/misc.hh>
#include <psyqo/trigonometry.hh>

#if defined(LOADER_CDROM)
#include "cdromhelper.hh"
#include "fileloader_cdrom.hh"
#endif

#include "lua.h"

using namespace psyqo::trig_literals;

using namespace psyqo::fixed_point_literals;

using namespace psxsplash;

// Static member definition
psyqo::Font<>* psxsplash::SceneManager::s_font = nullptr;

Random psxsplash::SceneManager::m_random;
Random psxsplash::SceneManager::m_randomGenerator;

// Default player collision radius: ~0.5 world units at GTE 100 -> 20 in 20.12
static constexpr int32_t PLAYER_RADIUS = 20;

// Interaction system state
static psyqo::Trig<> s_interactTrig;
static int s_activePromptCanvas = -1;  // Currently shown prompt canvas index (-1 = none)

void psxsplash::SceneManager::InitializeScene(uint8_t* splashpackData, LoadingScreen* loading) {
    auto& gpu = Renderer::GetInstance().getGPU();

    L.Reset();

#ifdef LOADER_CDROM
    {
        auto* cdromDev = static_cast<psxsplash::FileLoaderCDRom&>(
            psxsplash::FileLoader::Get()).getCDRomDevice();
        m_music.setCDRomDevice(cdromDev);
        m_xa.init(cdromDev);
    }
#endif

    // Register the Lua API
    LuaAPI::RegisterAll(L.getState(), this, &m_cutscenePlayer, &m_animationPlayer, &m_uiSystem);

#ifdef PSXSPLASH_PROFILER
    debug::Profiler::getInstance().initialize();
#endif

    SplashpackSceneSetup sceneSetup;
    m_loader.LoadSplashpack(splashpackData, sceneSetup);

    if (loading && loading->isActive()) loading->updateProgress(gpu, 40);

    m_luaFiles = std::move(sceneSetup.luaFiles);
    m_gameObjects = std::move(sceneSetup.objects);
    m_objectNames = std::move(sceneSetup.objectNames);
    m_bvh = sceneSetup.bvh;  // Copy BVH for frustum culling
    m_navRegions = sceneSetup.navRegions;          // Nav region system (v7+)
    m_playerNavRegion = m_navRegions.isLoaded() ? m_navRegions.getStartRegion() : NAV_NO_REGION;

    // Enable camera-follow by default. Lua can disable with
    // Camera.FollowPsxPlayer(false) for cutscene-driven scenes.
    // (Historically this was gated on nav regions, but the PS1Godot
    // demos use static colliders for ground, not nav regions, and still
    // want a player-tracking camera.)
    m_cameraFollowsPlayer = true;
    m_controlsEnabled = true;

    // v21: editor-configured rig offsets (from PS1Player's Camera3D / mesh
    // children). Stored player-local; rotated by yaw at runtime.
    // PackedVec3 components are FixedPoint<12, int16> (GTE::Short); our
    // members are FixedPoint<12, int32> (Vec3). Copy the raw .value through.
    m_cameraRigOffset.x.value = sceneSetup.cameraRigOffset.x.value;
    m_cameraRigOffset.y.value = sceneSetup.cameraRigOffset.y.value;
    m_cameraRigOffset.z.value = sceneSetup.cameraRigOffset.z.value;
    m_playerAvatarOffset.x.value = sceneSetup.playerAvatarOffset.x.value;
    m_playerAvatarOffset.y.value = sceneSetup.playerAvatarOffset.y.value;
    m_playerAvatarOffset.z.value = sceneSetup.playerAvatarOffset.z.value;
    m_playerAvatarObjectIndex = sceneSetup.playerAvatarObjectIndex;

    // Snapshot the avatar's authored rotation before the first GameTick
    // so we can compose it with playerRotationY each frame instead of
    // clobbering it. Without this snapshot the mesh's local-space
    // orientation is always what renders (any parent-node rotation the
    // author used to reorient e.g. a Mixamo FBX gets discarded).
    if (m_playerAvatarObjectIndex < m_gameObjects.size()) {
        GameObject* avatar = m_gameObjects[m_playerAvatarObjectIndex];
        if (avatar) {
            m_playerAvatarBaseRotation = avatar->rotation;
        }
    }

    // Scene type and render path
    m_sceneType = sceneSetup.sceneType;

    // Room/portal data for interior scenes (v11+)
    m_rooms = sceneSetup.rooms;
    m_roomCount = sceneSetup.roomCount;
    m_portals = sceneSetup.portals;
    m_portalCount = sceneSetup.portalCount;
    m_roomTriRefs = sceneSetup.roomTriRefs;
    m_roomTriRefCount = sceneSetup.roomTriRefCount;
    m_roomCells = sceneSetup.roomCells;
    m_roomCellCount = sceneSetup.roomCellCount;
    m_roomPortalRefs = sceneSetup.roomPortalRefs;
    m_roomPortalRefCount = sceneSetup.roomPortalRefCount;

    // Configure fog and back color from splashpack data (v11+).
    // v32+: forward fogNearSZ/fogFarSZ if the scene authored explicit
    // values (0 = renderer derives the legacy density-based defaults).
    // SetBackgroundColor must run AFTER SetFog because SetFog seeds
    // m_clearcolor from fog tone — the bg call then overrides it.
    {
        psxsplash::FogConfig fogCfg;
        fogCfg.enabled = sceneSetup.fogEnabled;
        fogCfg.color = {.r = sceneSetup.fogR, .g = sceneSetup.fogG, .b = sceneSetup.fogB};
        fogCfg.density = sceneSetup.fogDensity;
        fogCfg.fogNearSZ = sceneSetup.fogNearSZ;
        fogCfg.fogFarSZ  = sceneSetup.fogFarSZ;
        Renderer::GetInstance().SetFog(fogCfg);
        Renderer::GetInstance().SetBackgroundColor(
            sceneSetup.bgR, sceneSetup.bgG, sceneSetup.bgB, sceneSetup.bgEnabled);
    }
    // Copy component arrays
    m_interactables = std::move(sceneSetup.interactables);

    // Audio clip names are stored in the splashpack. ADPCM data is loaded
    // separately via uploadSpuData() before InitializeScene() is called.
    m_audioClipNames = std::move(sceneSetup.audioClipNames);

    // v25/v26 routing: walk the parallel audioClips array and snapshot
    // the routing byte + cdda track per index. Done before audioClips is
    // consumed/dropped so the runtime keeps the route info even though
    // AudioClipSetup itself isn't retained.
    m_audioClipRouting.clear();
    m_audioClipRouting.reserve(sceneSetup.audioClips.size());
    m_audioClipCddaTrack.clear();
    m_audioClipCddaTrack.reserve(sceneSetup.audioClips.size());
    for (const auto &clip : sceneSetup.audioClips) {
        m_audioClipRouting.push_back(static_cast<uint8_t>(clip.routing));
        m_audioClipCddaTrack.push_back(clip.cddaTrack);
    }

    // v27 XA sidecar — parallel arrays for fast name lookup. Empty when
    // the scene has no XA-routed clips OR psxavenc wasn't available at
    // export time.
    m_xaClipNames.clear();
    m_xaClipOffsets.clear();
    m_xaClipSizes.clear();
    m_xaClipNames.reserve(sceneSetup.xaClips.size());
    m_xaClipOffsets.reserve(sceneSetup.xaClips.size());
    m_xaClipSizes.reserve(sceneSetup.xaClips.size());
    for (const auto &xa : sceneSetup.xaClips) {
        m_xaClipNames.push_back(xa.name);
        m_xaClipOffsets.push_back(xa.sidecarOffset);
        m_xaClipSizes.push_back(xa.sidecarSize);
    }

    if (loading && loading->isActive()) loading->updateProgress(gpu, 55);

    // v22+: sequenced music. Bind each sequence blob to the
    // MusicSequencer. MusicManager keeps ADPCM samples already loaded
    // in SPU RAM; the sequencer references clip indices directly.
    m_musicSequencer.init(&m_audio);
    // v28+: hand the sequencer the scene-wide instrument bank so
    // PS2M sequences can resolve channel→program→instrument→region
    // at NoteOn. Bank pointers are nullptr/0 when the scene has none
    // — PS2M sequences then fall back to the channel entry's clip.
    m_musicSequencer.setBank(
        sceneSetup.instruments,  sceneSetup.instrumentCount,
        sceneSetup.regions,      sceneSetup.regionCount,
        sceneSetup.drumKits,     sceneSetup.drumKitCount,
        sceneSetup.drumMappings, sceneSetup.drumMappingCount);
    m_musicSequenceNames.clear();
    m_musicSequenceNames.reserve(sceneSetup.musicSequenceCount);
    for (int i = 0; i < sceneSetup.musicSequenceCount && i < 8; i++) {
        const auto &ms = sceneSetup.musicSequences[i];
        if (ms.data && ms.sizeBytes > 0) {
            m_musicSequencer.registerSequence(i, ms.data, ms.sizeBytes);
        }
        m_musicSequenceNames.push_back(ms.name);
    }

    // v29+ Phase 5 Stage B: hand the SoundMacro / SoundFamily banks to
    // their runtimes. Pointers are nullptr/0 when the scene exported
    // none, in which case Sound.PlayMacro / PlayFamily resolve to
    // -1/log "not found" rather than crashing.
    m_soundMacros.init(&m_audio);
    m_soundMacros.setBank(sceneSetup.soundMacros, sceneSetup.soundMacroCount,
                          sceneSetup.soundMacroEvents, sceneSetup.soundMacroEventCount);
    m_soundFamilies.init(&m_audio);
    m_soundFamilies.setBank(sceneSetup.soundFamilies, sceneSetup.soundFamilyCount,
                            sceneSetup.familyClipIndices, sceneSetup.familyClipIndexCount);

    // Copy cutscene data into scene manager storage (sceneSetup is stack-local)
    m_cutsceneCount = sceneSetup.cutsceneCount;
    for (int i = 0; i < m_cutsceneCount; i++) {
        m_cutscenes[i] = sceneSetup.loadedCutscenes[i];
    }

    // Initialize cutscene player (v12+)
    m_cutscenePlayer.init(
        m_cutsceneCount > 0 ? m_cutscenes : nullptr,
        m_cutsceneCount,
        &m_currentCamera,
        &m_audio,
        &m_uiSystem,
        this,
        &m_controls
    );

    // Copy animation data into scene manager storage
    m_animationCount = sceneSetup.animationCount;
    for (int i = 0; i < m_animationCount; i++) {
        m_animations[i] = sceneSetup.loadedAnimations[i];
    }

    // Initialize animation player
    m_animationPlayer.init(
        m_animationCount > 0 ? m_animations : nullptr,
        m_animationCount,
        &m_uiSystem,
        this,
        &m_controls
    );

    // Copy skinned mesh data from splashpack into scene manager storage
    m_skinnedMeshCount = sceneSetup.skinnedMeshCount;
    for (int i = 0; i < m_skinnedMeshCount; i++) {
        m_skinAnimSets[i] = sceneSetup.loadedSkinAnimSets[i];
        m_skinAnimStates[i] = SkinAnimState{};
        m_skinAnimStates[i].animSet = &m_skinAnimSets[i];
        // bindPose default-on so characters render in T-pose until a clip
        // starts — otherwise the first frame shows clip[0] frame[0] which is
        // mid-stride for most Mixamo walks. luaCallbackRef must be explicitly
        // LUA_NOREF (= -2); the zero-init from SkinAnimState{} leaves it at 0,
        // which is a valid Lua registry ref and would crash luaL_unref.
        m_skinAnimStates[i].bindPose = true;
        m_skinAnimStates[i].luaCallbackRef = LUA_NOREF;

        uint16_t goIdx = m_skinAnimSets[i].gameObjectIndex;
        if (goIdx < m_gameObjects.size()) {
            GameObject* go = m_gameObjects[goIdx];
            m_skinAnimSets[i].polygons  = go->polygons;
            m_skinAnimSets[i].polyCount = go->polyCount;
            go->polyCount = 0;
            go->flagsAsInt |= 0x10;
        } else {
            m_skinAnimSets[i].polygons  = nullptr;
            m_skinAnimSets[i].polyCount = 0;
        }
    }
    Renderer::GetInstance().SetSkinData(
        m_skinnedMeshCount > 0 ? m_skinAnimSets : nullptr,
        m_skinnedMeshCount > 0 ? m_skinAnimStates : nullptr,
        m_skinnedMeshCount);

    // v23+: UI 3D-model widgets. Copy authored state from the on-disk
    // array into the runtime state array; renderer reads both each
    // frame (disk → static fields like canvas index + screen rect;
    // state → mutable fields like current yaw + visibility).
    m_uiModelCount = (int)sceneSetup.uiModelCount;
    if (m_uiModelCount > MAX_UI_MODELS) m_uiModelCount = MAX_UI_MODELS;
    m_uiModelsDisk = sceneSetup.uiModels;
    for (int i = 0; i < m_uiModelCount; i++) {
        const SPLASHPACKUIModel& disk = m_uiModelsDisk[i];
        UIModelRuntimeState& st = m_uiModelStates[i];
        st.currentYawFp10   = disk.orbitYawFp10;
        st.currentPitchFp10 = disk.orbitPitchFp10;
        st.currentDistFp12  = disk.orbitDistFp12;
        st.currentTargetObj = disk.targetObjIndex;
        st.visible          = disk.visibleOnLoad;
        // Flag the target so the world render pass skips it — the HUD
        // preview becomes the only visible render of this mesh. Authors
        // who want BOTH a world render and a UI preview should duplicate
        // the mesh as two separate PS1MeshInstance / PS1MeshGroup nodes
        // and target the dedicated one.
        if (disk.targetObjIndex < m_gameObjects.size()) {
            m_gameObjects[disk.targetObjIndex]->setUIModelTarget(true);
        }
    }
    Renderer::GetInstance().SetUIModelData(
        m_uiModelCount > 0 ? m_uiModelsDisk : nullptr,
        m_uiModelCount > 0 ? m_uiModelStates : nullptr,
        m_uiModelCount);

    // v24+: scene skybox. Hand the parsed sky struct to the renderer
    // so its renderSky pass picks up the texture coords + tint. When
    // the scene has no PS1Sky, sceneSetup.sky.enabled is false and the
    // renderer skips the pass.
    Renderer::GetInstance().SetSky(
        sceneSetup.sky.texpageX, sceneSetup.sky.texpageY,
        sceneSetup.sky.clutX,    sceneSetup.sky.clutY,
        sceneSetup.sky.u0, sceneSetup.sky.v0,
        sceneSetup.sky.u1, sceneSetup.sky.v1,
        sceneSetup.sky.bitDepth,
        sceneSetup.sky.tintR, sceneSetup.sky.tintG, sceneSetup.sky.tintB,
        sceneSetup.sky.enabled);

    // Initialize UI system (v13+)
    // Custom-font glyph atlases live in the .splashpack (not the .vram
    // file) and must be uploaded to VRAM here, after loadFromSplashpack
    // has populated the font descriptors.
    if (sceneSetup.uiCanvasCount > 0 && sceneSetup.uiTableOffset != 0 && s_font != nullptr) {
        m_uiSystem.init(*s_font);
        m_uiSystem.loadFromSplashpack(splashpackData, sceneSetup.uiCanvasCount,
                                      sceneSetup.uiFontCount, sceneSetup.uiTableOffset);
        m_uiSystem.uploadFonts(gpu);
        Renderer::GetInstance().SetUISystem(&m_uiSystem);

        if (loading && loading->isActive()) loading->updateProgress(gpu, 70);

        // Resolve UI track handles: the splashpack loader stored raw name pointers
        // in CutsceneTrack.target for UI tracks. Now that UISystem is loaded, resolve
        // those names to canvas indices / element handles.
        for (int ci = 0; ci < m_cutsceneCount; ci++) {
            for (uint8_t ti = 0; ti < m_cutscenes[ci].trackCount; ti++) {
                auto& track = m_cutscenes[ci].tracks[ti];
                bool isUI = isUITrackType(track.trackType);
                if (!isUI || track.target == nullptr) continue;

                const char* nameStr = reinterpret_cast<const char*>(track.target);
                track.target = nullptr; // Clear the temporary name pointer

                if (track.trackType == TrackType::UICanvasVisible) {
                    // Name is just the canvas name
                    track.uiHandle = static_cast<int16_t>(m_uiSystem.findCanvas(nameStr));
                } else {
                    // Name is "canvasName/elementName" — find the '/' separator
                    const char* sep = nameStr;
                    while (*sep && *sep != '/') sep++;
                    if (*sep == '/') {
                        // Temporarily null-terminate the canvas portion
                        // (nameStr points into splashpack data, which is mutable)
                        char* mutableSep = const_cast<char*>(sep);
                        *mutableSep = '\0';
                        int canvasIdx = m_uiSystem.findCanvas(nameStr);
                        *mutableSep = '/'; // Restore the separator
                        if (canvasIdx >= 0) {
                            track.uiHandle = static_cast<int16_t>(
                                m_uiSystem.findElement(canvasIdx, sep + 1));
                        }
                    }
                }
            }
        }

        // Resolve UI track handles for animation tracks (same logic)
        for (int ai = 0; ai < m_animationCount; ai++) {
            for (uint8_t ti = 0; ti < m_animations[ai].trackCount; ti++) {
                auto& track = m_animations[ai].tracks[ti];
                bool isUI = isUITrackType(track.trackType);
                if (!isUI || track.target == nullptr) continue;

                const char* nameStr = reinterpret_cast<const char*>(track.target);
                track.target = nullptr;

                if (track.trackType == TrackType::UICanvasVisible) {
                    track.uiHandle = static_cast<int16_t>(m_uiSystem.findCanvas(nameStr));
                } else {
                    const char* sep = nameStr;
                    while (*sep && *sep != '/') sep++;
                    if (*sep == '/') {
                        char* mutableSep = const_cast<char*>(sep);
                        *mutableSep = '\0';
                        int canvasIdx = m_uiSystem.findCanvas(nameStr);
                        *mutableSep = '/';
                        if (canvasIdx >= 0) {
                            track.uiHandle = static_cast<int16_t>(
                                m_uiSystem.findElement(canvasIdx, sep + 1));
                        }
                    }
                }
            }
        }
    } else {
        Renderer::GetInstance().SetUISystem(nullptr);
    }

#ifdef PSXSPLASH_MEMOVERLAY
    if (s_font != nullptr) {
        m_memOverlay.init(s_font);
        Renderer::GetInstance().SetMemOverlay(&m_memOverlay);
    }
#endif

    m_playerPosition = sceneSetup.playerStartPosition;

    playerRotationX = 0.0_pi;
    // Zero yaw — the PS1Godot exporter now reflects both Y and Z on every
    // world-space coordinate, so Godot's -Z (Godot forward) maps directly
    // to PSX +Z (PSX forward) and no yaw compensation is needed here. The
    // prior 180° Y-init was a compensation for exporting Z unflipped, which
    // also side-effect-mirrored the scene on X.
    // TODO: honor splashpack-provided playerStartRotation here once the
    // fp12/fp10 unit mismatch is fixed (see docs/psxsplash-improvements.md).
    playerRotationY = 0.0_pi;
    playerRotationZ = 0.0_pi;

    // Position + rotate the camera up front so authored camera placement is
    // honored even on scenes without nav regions (which is the gate
    // m_cameraFollowsPlayer normally checks below).
    m_currentCamera.SetPosition(
        static_cast<psyqo::FixedPoint<12>>(m_playerPosition.x),
        static_cast<psyqo::FixedPoint<12>>(m_playerPosition.y),
        static_cast<psyqo::FixedPoint<12>>(m_playerPosition.z));
    m_currentCamera.SetRotation(playerRotationX, playerRotationY, playerRotationZ);

    m_playerHeight = sceneSetup.playerHeight;

    m_controls.setMoveSpeed(sceneSetup.moveSpeed);
    m_controls.setSprintSpeed(sceneSetup.sprintSpeed);
    m_playerRadius = (int32_t)sceneSetup.playerRadius.value;
    if (m_playerRadius == 0) m_playerRadius = PLAYER_RADIUS; 
    m_jumpVelocityRaw = (int32_t)sceneSetup.jumpVelocity.value;
    int32_t gravityRaw = (int32_t)sceneSetup.gravity.value;
    m_gravityPerFrame = gravityRaw / 30;  
    if (m_gravityPerFrame == 0 && gravityRaw > 0) m_gravityPerFrame = 1; 
    m_velocityY = 0;
    m_isGrounded = true;
    m_lastFrameTime = 0;
    m_dt12 = 4096;  // Default: 1.0 frame

    m_collisionSystem.init();
    
    for (size_t i = 0; i < sceneSetup.colliders.size(); i++) {
        SPLASHPACKCollider* collider = sceneSetup.colliders[i];
        if (collider == nullptr) continue;
        
        AABB bounds;
        bounds.min.x.value = collider->minX;
        bounds.min.y.value = collider->minY;
        bounds.min.z.value = collider->minZ;
        bounds.max.x.value = collider->maxX;
        bounds.max.y.value = collider->maxY;
        bounds.max.z.value = collider->maxZ;
        
        CollisionType type = static_cast<CollisionType>(collider->collisionType);
        
        m_collisionSystem.registerCollider(
            collider->gameObjectIndex,
            bounds,
            type,
            collider->layerMask
        );
    }

    for (size_t i = 0; i < sceneSetup.triggerBoxes.size(); i++) {
        SPLASHPACKTriggerBox* tb = sceneSetup.triggerBoxes[i];
        if (tb == nullptr) continue;

        AABB bounds;
        bounds.min.x.value = tb->minX;
        bounds.min.y.value = tb->minY;
        bounds.min.z.value = tb->minZ;
        bounds.max.x.value = tb->maxX;
        bounds.max.y.value = tb->maxY;
        bounds.max.z.value = tb->maxZ;

        m_collisionSystem.registerTriggerBox(bounds, tb->luaFileIndex);
    }


    for (int i = 0; i < m_luaFiles.size(); i++) {
        auto luaFile = m_luaFiles[i];
        L.LoadLuaFile(luaFile->luaCode, luaFile->length, i);
    }

    if (loading && loading->isActive()) loading->updateProgress(gpu, 85);

    L.RegisterSceneScripts(sceneSetup.sceneLuaFileIndex);

    L.OnSceneCreationStart();

    for (auto object : m_gameObjects) {
        L.RegisterGameObject(object);
    }

    // Fire all onCreate events AFTER all objects are registered,
    // so Entity.Find works across all objects in onCreate handlers.
    if (!m_gameObjects.empty()) {
        L.FireAllOnCreate(
            reinterpret_cast<GameObject**>(m_gameObjects.data()),
            m_gameObjects.size());
    }

    m_controls.forceAnalogMode();
    m_controls.Init();
    Renderer::GetInstance().SetCamera(m_currentCamera);

    L.OnSceneCreationEnd();

    if (loading && loading->isActive()) loading->updateProgress(gpu, 95);

    // v20: No more shrinkBuffer() — VRAM/SPU data is in separate files,
    // so the splashpack buffer IS the live data. No relocation needed.

    if (loading && loading->isActive()) loading->updateProgress(gpu, 100);
}

void psxsplash::SceneManager::GameTick(psyqo::GPU &gpu) {
    LuaAPI::IncrementFrameCount();
    
    {
        uint32_t now = gpu.now();
        if (m_lastFrameTime != 0) {
            uint32_t elapsed = now - m_lastFrameTime;

            if (elapsed > 200000) elapsed = 200000;  // cap at ~6 frames
            m_dt12 = (int32_t)((elapsed * 4096u) / 33333u);
            if (m_dt12 < 1) m_dt12 = 1;              // minimum: tiny fraction
            if (m_dt12 > 4096 * 4) m_dt12 = 4096 * 4; // cap at 4 frames
        }
        m_lastFrameTime = now;
    }

    // Hit-stop: when paused, freeze gameplay-time systems but keep music
    // and camera shake alive (music shouldn't stutter for a 6-frame freeze;
    // shake provides the impact feedback the pause is selling). Render still
    // fires below regardless.
    bool paused = m_pauseFramesRemaining > 0;
    if (paused) m_pauseFramesRemaining--;

    if (!paused) {
        m_cutscenePlayer.tick(m_dt12);
        m_animationPlayer.tick(m_dt12);

        // Tick skinned mesh animations
        for (int i = 0; i < m_skinnedMeshCount; i++) {
            SkinMesh_Tick(&m_skinAnimStates[i], L.getState().getState(), m_dt12);
        }
    }
    m_musicSequencer.tick(m_dt12);
    // Phase 5 Stage B: tick macro/family runtimes every frame so
    // active macro instances dispatch their next-due events and the
    // family cooldown counter advances. Both run regardless of pause —
    // sound effects shouldn't freeze with the gameplay tick.
    m_soundMacros.tick(m_dt12);
    m_soundFamilies.tick(m_dt12);

    // Controls read/delta every frame (including paused) so button-state
    // tracking stays in sync — otherwise the first post-pause frame would
    // compare current input against pre-pause state and fire ghost edges.
    // Button-event DISPATCH (into Lua onButtonPress / etc.) still lives
    // below the pause short-circuit — we update the state, we just don't
    // wake scripts.
    m_controls.UpdateButtonStates();

    // Camera shake advances every frame (even during pause) so the impact
    // freeze still rattles the screen. Offset is read-only; applied as a
    // render-only wrap around the render block below, so game logic that
    // mutates m_position (camera-follow, Lua Camera.SetPosition) never
    // clobbers the offset.
    m_currentCamera.AdvanceShake();

    uint32_t renderingStart = gpu.now();
    auto& renderer = psxsplash::Renderer::GetInstance();

    // Apply shake offset to the camera position for the render pass only.
    const psyqo::Vec3& shakeOffset = m_currentCamera.GetShakeOffset();
    {
        auto& p = m_currentCamera.GetPosition();
        p.x.value += shakeOffset.x.value;
        p.y.value += shakeOffset.y.value;
        p.z.value += shakeOffset.z.value;
    }

    if (m_roomCount > 0 && m_rooms != nullptr) {

        int camRoom = -1;
        if (m_navRegions.isLoaded()) {
            if (m_cutscenePlayer.isPlaying() && m_cutscenePlayer.hasCameraTracks()) {
                auto& camPos = m_currentCamera.GetPosition();
                uint16_t camRegion = m_navRegions.findRegion(camPos.x.value, camPos.z.value);
                if (camRegion != NAV_NO_REGION) {
                    uint8_t ri = m_navRegions.getRoomIndex(camRegion);
                    if (ri != 0xFF) camRoom = (int)ri;
                }
            } else if (m_playerNavRegion != NAV_NO_REGION) {
                uint8_t ri = m_navRegions.getRoomIndex(m_playerNavRegion);
                if (ri != 0xFF) camRoom = (int)ri;
            }
        }
        renderer.RenderWithRooms(m_gameObjects, m_rooms, m_roomCount,
                                  m_portals, m_portalCount, m_roomTriRefs,
                                  m_roomCells, m_roomPortalRefs, camRoom);
    } else {
        renderer.RenderWithBVH(m_gameObjects, m_bvh);
    }
    gpu.pumpCallbacks();
    uint32_t renderingEnd = gpu.now();
    uint32_t renderingTime = renderingEnd - renderingStart;

    // Revert the shake offset so game logic below (camera-follow, Lua
    // Camera.GetPosition, collision setup) reads the clean position.
    {
        auto& p = m_currentCamera.GetPosition();
        p.x.value -= shakeOffset.x.value;
        p.y.value -= shakeOffset.y.value;
        p.z.value -= shakeOffset.z.value;
    }

#ifdef PSXSPLASH_PROFILER
    psxsplash::debug::Profiler::getInstance().setSectionTime(psxsplash::debug::PROFILER_RENDERING, renderingTime);
#endif

    // Hit-stop short-circuit: skip the rest of the gameplay tick (collision,
    // Lua onUpdate, player movement, button-event dispatch, scene-load
    // processing) for `m_pauseFramesRemaining` frames after Scene.PauseFor.
    // Render + camera shake + music + button-state tracking already ran
    // above. Button-event DISPATCH is frozen — that's part of selling the
    // impact crunch (player can't swing again mid-freeze).
    if (paused) return;

    uint32_t collisionStart = gpu.now();

    AABB playerAABB;
    {
        psyqo::FixedPoint<12> r;
        r.value = m_playerRadius;
        psyqo::FixedPoint<12> px = static_cast<psyqo::FixedPoint<12>>(m_playerPosition.x);
        psyqo::FixedPoint<12> py = static_cast<psyqo::FixedPoint<12>>(m_playerPosition.y);
        psyqo::FixedPoint<12> pz = static_cast<psyqo::FixedPoint<12>>(m_playerPosition.z);
        psyqo::FixedPoint<12> h = static_cast<psyqo::FixedPoint<12>>(m_playerHeight);
        // Y is inverted on PS1: negative = up, positive = down.
        // m_playerPosition.y is the camera (head), feet are at py + h.
        // Leave a small gap at the bottom so the floor geometry doesn't
        // trigger constant collisions (floor contact is handled by nav).
        psyqo::FixedPoint<12> bodyBottom;
        bodyBottom.value = h.value * 3 / 4;  // 75% of height below camera
        playerAABB.min = psyqo::Vec3{px - r, py, pz - r};
        playerAABB.max = psyqo::Vec3{px + r, py + bodyBottom, pz + r};
    }
    
    psyqo::Vec3 pushBack;
    int collisionCount = m_collisionSystem.detectCollisions(playerAABB, pushBack, *this);
    
    {
        psyqo::FixedPoint<12> zero;
        if (pushBack.x != zero || pushBack.z != zero) {
            m_playerPosition.x = m_playerPosition.x + pushBack.x;
            m_playerPosition.z = m_playerPosition.z + pushBack.z;
        }
    }
    
    // Fire onCollideWithPlayer Lua events on collided objects
    const CollisionResult* results = m_collisionSystem.getResults();
    for (int i = 0; i < collisionCount; i++) {
        if (results[i].objectA != 0xFFFF) continue;
        auto* obj = getGameObject(results[i].objectB);
        if (obj) {
            L.OnCollideWithPlayer(obj);
        }
    }
    
    // Process trigger boxes (enter/exit)
    m_collisionSystem.detectTriggers(playerAABB, *this);
    
    gpu.pumpCallbacks();
    uint32_t collisionEnd = gpu.now();
    
    uint32_t luaStart = gpu.now();
    // Lua update tick - call onUpdate for all registered objects with onUpdate handler
    for (auto* go : m_gameObjects) {
        if (go && go->isActive()) {
            L.OnUpdate(go, m_dt12);
        }
    }
    gpu.pumpCallbacks();
    uint32_t luaEnd = gpu.now();
    uint32_t luaTime = luaEnd - luaStart;
#ifdef PSXSPLASH_PROFILER
    psxsplash::debug::Profiler::getInstance().setSectionTime(psxsplash::debug::PROFILER_LUA, luaTime);
#endif
    
    // Update game systems
    processEnableDisableEvents();

    
    uint32_t controlsStart = gpu.now();

    // Button-state tracking already ran above the pause short-circuit so the
    // delta stays in sync across hit-stops. Just dispatch events below.

    // Update interaction system (checks for interact button press)
    updateInteractionSystem();
    
    // Dispatch button events to all objects
    uint16_t pressed = m_controls.getButtonsPressed();
    uint16_t released = m_controls.getButtonsReleased();
    
    if (pressed || released) {
        // Only iterate objects if there are button events
        for (auto* go : m_gameObjects) {
            if (!go || !go->isActive()) continue;
            
            if (pressed) {
                // Dispatch press events for each pressed button
                for (int btn = 0; btn < 16; btn++) {
                    if (pressed & (1 << btn)) {
                        L.OnButtonPress(go, btn);
                    }
                }
            }
            if (released) {
                // Dispatch release events for each released button
                for (int btn = 0; btn < 16; btn++) {
                    if (released & (1 << btn)) {
                        L.OnButtonRelease(go, btn);
                    }
                }
            }
        }
    }
    
    // Save position BEFORE movement for collision detection
    psyqo::Vec3 oldPlayerPosition = m_playerPosition;

    if (m_controlsEnabled) {
        m_controls.HandleControls(m_playerPosition, playerRotationX, playerRotationY, playerRotationZ, freecam, m_dt12);

        // Jump input: Cross button triggers jump when grounded
        if (m_isGrounded && m_controls.wasButtonPressed(psyqo::AdvancedPad::Button::Cross)) {
            m_velocityY = -m_jumpVelocityRaw;  // Negative = upward (PSX Y-down)
            m_isGrounded = false;
        }
    }
    
    gpu.pumpCallbacks();
    uint32_t controlsEnd = gpu.now();
    uint32_t controlsTime = controlsEnd - controlsStart;
#ifdef PSXSPLASH_PROFILER
    psxsplash::debug::Profiler::getInstance().setSectionTime(psxsplash::debug::PROFILER_CONTROLS, controlsTime);
#endif

    uint32_t navmeshStart = gpu.now();

    if (!freecam && m_navRegions.isLoaded()) {
        // Apply gravity scaled by dt12 (4096 = 1 frame)
        int32_t gravityDelta = (int32_t)(((int64_t)m_gravityPerFrame * m_dt12) >> 12);
        m_velocityY += gravityDelta;
        
        // Downward velocity cap
        if(m_velocityY >= m_downwardVelocityCap)
        {
            m_velocityY = m_downwardVelocityCap;
        }

        // Apply vertical velocity to position, scaled by dt12
        int32_t posYDelta = (int32_t)(((int64_t)m_velocityY * m_dt12) >> 12);
        m_playerPosition.y.value += posYDelta;

        int32_t px = m_playerPosition.x.value;
        int32_t py = m_playerPosition.y.value;
        int32_t pz = m_playerPosition.z.value;

        uint16_t newNavRegion = m_navRegions.findRegionClosest(px,py,pz);

        bool isPlatform = m_navRegions.isRegionPlatform(m_playerNavRegion);
        uint8_t walkoffMask = m_navRegions.getWalkoffEdgeMask(m_playerNavRegion);
        bool canWalkOff = isPlatform || (walkoffMask != 0);
        
        // Not in original region
        if(m_playerNavRegion != newNavRegion){

            // Valid Region to No Region
            if(m_playerNavRegion != NAV_NO_REGION && newNavRegion == NAV_NO_REGION){
                if(canWalkOff){
                   m_isGrounded = false; 
                }
            }
            else{
                m_playerNavRegion = newNavRegion;
                isPlatform = m_navRegions.isRegionPlatform(m_playerNavRegion);
                walkoffMask = m_navRegions.getWalkoffEdgeMask(m_playerNavRegion);
                canWalkOff = isPlatform || (walkoffMask != 0);
            } 
        }

        // Is there a valid region?
        if(m_playerNavRegion != NAV_NO_REGION){
            int32_t floorY = m_navRegions.getFloorY(px,pz,m_playerNavRegion);
            int32_t cameraAtFloor = floorY - m_playerHeight.raw();
            
            // Lock down the Y if grounded
            if(m_isGrounded){
                if(m_playerPosition.y.value > cameraAtFloor){
                    m_playerPosition.y.value = cameraAtFloor;
                    m_velocityY = 0;
                } 
            }
            // Handles falling / jumping on to a region
            else if(m_playerPosition.y.value > floorY - m_playerHeight.raw()) {
                
                m_isGrounded = true;
            }

            // Let the player fall for a bit before disabling jump 
            if(m_playerPosition.y.value > floorY + m_playerHeight.raw() + m_coyoteTimeDistance)
            {
                m_isGrounded = false;
                m_playerNavRegion = NAV_NO_REGION;
            }
        }
        else // No Nav Region
        {
            m_isGrounded = false;
        }
        
        // Clamp movement to region boundaries where walls exist
        if (isPlatform) {
            // Platform regions have no boundary clamping
        } else if (walkoffMask != 0) {
            m_navRegions.clampToRegionSelective(m_playerPosition.x.value, m_playerPosition.z.value, m_playerNavRegion);
        } else {
            m_navRegions.clampToRegion(m_playerPosition.x.value, m_playerPosition.z.value, m_playerNavRegion);
        }
    }



    gpu.pumpCallbacks();
    uint32_t navmeshEnd = gpu.now();
    uint32_t navmeshTime = navmeshEnd - navmeshStart;
#ifdef PSXSPLASH_PROFILER
    psxsplash::debug::Profiler::getInstance().setSectionTime(psxsplash::debug::PROFILER_NAVMESH, navmeshTime);
#endif

    // Only snap camera to player when in player-follow mode and no
    // cutscene is actively controlling the camera. In free camera mode
    // (no nav regions / no PSXPlayer), the camera is driven entirely
    // by cutscenes and Lua. After a cutscene ends in free mode, the
    // camera stays at the last cutscene position.
    if (m_cameraFollowsPlayer && !(m_cutscenePlayer.isPlaying() && m_cutscenePlayer.hasCameraTracks())) {
        // Third-person rig: offset is captured at export time from the
        // Camera3D child of PS1Player (editor-tunable), player-local.
        // First-person: camera at player eye, no offset. Runtime rotates
        // the offset by playerRotationY around the Y axis. With yaw=0
        // meaning "facing +Z", a Y-axis rotation is
        //   dx = cosY*offsetX + sinY*offsetZ
        //   dz = -sinY*offsetX + cosY*offsetZ
        //   dy = offsetY (Y is the rotation axis)
        psyqo::Vec3 activeOffset = m_cameraRigOffset;
        if (m_cameraMode == PlayerCameraMode::FirstPerson) {
            activeOffset.x.value = 0;
            activeOffset.y.value = 0;
            activeOffset.z.value = 0;
        }

        auto sinY = m_trig.sin(playerRotationY);
        auto cosY = m_trig.cos(playerRotationY);

        auto camX = static_cast<psyqo::FixedPoint<12>>(m_playerPosition.x)
                  + cosY * activeOffset.x + sinY * activeOffset.z;
        auto camY = static_cast<psyqo::FixedPoint<12>>(m_playerPosition.y)
                  + activeOffset.y;
        auto camZ = static_cast<psyqo::FixedPoint<12>>(m_playerPosition.z)
                  - sinY * activeOffset.x + cosY * activeOffset.z;

        m_currentCamera.SetPosition(camX, camY, camZ);
        m_currentCamera.SetRotation(playerRotationX, playerRotationY, playerRotationZ);
    }

    // v21: auto-track the player avatar mesh (if PS1Player has a child
    // MeshInstance3D). Replaces the Lua tracking that demos used before.
    // The avatar offset is in player-local space (e.g., mesh origin =
    // feet, player pos = eye → offset y = +playerHeight to drop mesh to
    // ground). Rotated by yaw so the mesh faces where the player faces.
    if (m_playerAvatarObjectIndex < m_gameObjects.size()) {
        GameObject* avatar = m_gameObjects[m_playerAvatarObjectIndex];
        if (avatar) {
            // In first-person mode the camera sits at the player's eye,
            // so the avatar would render inside the view frustum and
            // block the screen. Skip tracking + render entirely until
            // mode flips back.
            bool wantVisible = (m_cameraMode != PlayerCameraMode::FirstPerson);
            if (avatar->isActive() != wantVisible) {
                avatar->setActive(wantVisible);
            }

            if (wantVisible) {
                auto sinY = m_trig.sin(playerRotationY);
                auto cosY = m_trig.cos(playerRotationY);

                // Y-axis rotation matches the camera rig formula above.
                auto newX = static_cast<psyqo::FixedPoint<12>>(m_playerPosition.x)
                          + cosY * m_playerAvatarOffset.x + sinY * m_playerAvatarOffset.z;
                auto newY = static_cast<psyqo::FixedPoint<12>>(m_playerPosition.y)
                          + m_playerAvatarOffset.y;
                auto newZ = static_cast<psyqo::FixedPoint<12>>(m_playerPosition.z)
                          - sinY * m_playerAvatarOffset.x + cosY * m_playerAvatarOffset.z;

                int32_t dx = newX.value - avatar->position.x.value;
                int32_t dy = newY.value - avatar->position.y.value;
                int32_t dz = newZ.value - avatar->position.z.value;
                avatar->position.x = newX;
                avatar->position.y = newY;
                avatar->position.z = newZ;
                avatar->aabbMinX += dx; avatar->aabbMaxX += dx;
                avatar->aabbMinY += dy; avatar->aabbMaxY += dy;
                avatar->aabbMinZ += dz; avatar->aabbMaxZ += dz;
                avatar->setDynamicMoved(true);
                // Compose authored base rotation with current yaw. The
                // renderer interprets avatar->rotation as the transpose
                // of the effective vert rotation (v_world = R^T · v_local),
                // so we build base · transpose(R_Y(yaw)) directly.
                psyqo::Matrix33 invYaw = psxsplash::transposeMatrix33(
                    psyqo::SoftMath::generateRotationMatrix33(
                        playerRotationY, psyqo::SoftMath::Axis::Y, m_trig));
                psyqo::Matrix33 composed;
                psyqo::SoftMath::multiplyMatrix33(
                    m_playerAvatarBaseRotation, invYaw, &composed);
                avatar->rotation = composed;
            }
        }
    }

    // Process pending scene transitions (at end of frame)
    processPendingSceneLoad();
}

void psxsplash::SceneManager::fireTriggerEnter(int16_t luaFileIndex, uint16_t triggerIndex) {
    if (luaFileIndex < 0) return;
    L.OnTriggerEnterScript(luaFileIndex, triggerIndex);
}

void psxsplash::SceneManager::fireTriggerExit(int16_t luaFileIndex, uint16_t triggerIndex) {
    if (luaFileIndex < 0) return;
    L.OnTriggerExitScript(luaFileIndex, triggerIndex);
}

// ============================================================================
// INTERACTION SYSTEM
// ============================================================================

void psxsplash::SceneManager::updateInteractionSystem() {
    // Tick cooldowns for all interactables
    for (auto* interactable : m_interactables) {
        if (interactable) interactable->update();
    }

    // Player position for distance checks
    psyqo::FixedPoint<12> playerX = static_cast<psyqo::FixedPoint<12>>(m_playerPosition.x);
    psyqo::FixedPoint<12> playerY = static_cast<psyqo::FixedPoint<12>>(m_playerPosition.y);
    psyqo::FixedPoint<12> playerZ = static_cast<psyqo::FixedPoint<12>>(m_playerPosition.z);

    // Player forward direction from Y rotation (for line-of-sight checks)
    psyqo::FixedPoint<12> forwardX = s_interactTrig.sin(playerRotationY);
    psyqo::FixedPoint<12> forwardZ = s_interactTrig.cos(playerRotationY);

    // First pass: find which interactable is closest and in range (for prompt display)
    Interactable* inRange = nullptr;
    psyqo::FixedPoint<12> closestDistSq;
    closestDistSq.value = 0x7FFFFFFF;

    for (auto* interactable : m_interactables) {
        if (!interactable) continue;
        if (interactable->isDisabled()) continue;

        auto* go = getGameObject(interactable->gameObjectIndex);
        if (!go || !go->isActive()) continue;

        // Distance check
        psyqo::FixedPoint<12> dx = playerX - go->position.x;
        psyqo::FixedPoint<12> dy = playerY - go->position.y;
        psyqo::FixedPoint<12> dz = playerZ - go->position.z;
        psyqo::FixedPoint<12> distSq = dx * dx + dy * dy + dz * dz;

        if (distSq > interactable->radiusSquared) continue;

        // Line-of-sight check: dot product of forward vector and direction to object
        if (interactable->requireLineOfSight()) {
            // dot = forwardX * dx + forwardZ * dz (XZ plane only)
            // Negative dot means object is behind the player
            psyqo::FixedPoint<12> dot = forwardX * dx + forwardZ * dz;
            // Object must be in front of the player (dot < 0 in the coordinate system
            // because dx points FROM player TO object, and forward points where player faces)
            // Actually: dx = playerX - objX, so it points FROM object TO player.
            // We want the object in front, so we need -dx direction to align with forward.
            // dot(forward, objDir) where objDir = obj - player = -dx, -dz
            psyqo::FixedPoint<12> facingDot = -(forwardX * dx + forwardZ * dz);
            if (facingDot.value <= 0) continue;  // Object is behind the player
        }

        if (distSq < closestDistSq) {
            inRange = interactable;
            closestDistSq = distSq;
        }
    }

    // Prompt canvas management: show only when in range AND can interact
    int newPromptCanvas = -1;
    if (inRange && inRange->canInteract() && inRange->showPrompt() && inRange->promptCanvasName[0] != '\0') {
        newPromptCanvas = m_uiSystem.findCanvas(inRange->promptCanvasName);
    }

    if (newPromptCanvas != s_activePromptCanvas) {
        // Hide old prompt
        if (s_activePromptCanvas >= 0) {
            m_uiSystem.setCanvasVisible(s_activePromptCanvas, false);
        }
        // Show new prompt
        if (newPromptCanvas >= 0) {
            m_uiSystem.setCanvasVisible(newPromptCanvas, true);
        }
        s_activePromptCanvas = newPromptCanvas;
    }

    // Check if the closest in-range interactable can be activated
    if (!inRange || !inRange->canInteract()) return;

    // Check if the correct button for this interactable was pressed
    auto button = static_cast<psyqo::AdvancedPad::Button>(
        static_cast<uint16_t>(inRange->interactButton));
    if (!m_controls.wasButtonPressed(button)) return;

    // Trigger the interaction
    triggerInteraction(getGameObject(inRange->gameObjectIndex));
    inRange->triggerCooldown();
}

void psxsplash::SceneManager::triggerInteraction(GameObject* interactable) {
    if (!interactable) return;
    L.OnInteract(interactable);
}

// ============================================================================
// ENABLE/DISABLE SYSTEM
// ============================================================================

void psxsplash::SceneManager::setObjectActive(GameObject* go, bool active) {
    if (!go) return;
    
    bool wasActive = go->isActive();
    if (wasActive == active) return;  // No change
    
    go->setActive(active);
    
    // Fire appropriate event
    if (active) {
        L.OnEnable(go);
    } else {
        L.OnDisable(go);
    }
}

void psxsplash::SceneManager::processEnableDisableEvents() {
    // Process any pending enable/disable flags.
    // Uses raw bit manipulation on flagsAsInt instead of the BitField
    // accessors to avoid a known issue where the BitSpan get/set
    // operations don't behave correctly on the MIPS target.
    for (auto* go : m_gameObjects) {
        if (!go) continue;

        // Bit 1 = pendingEnable
        if (go->flagsAsInt & 0x02) {
            go->flagsAsInt &= ~0x02u;  // clear pending
            if (!(go->flagsAsInt & 0x01)) {  // if not already active
                go->flagsAsInt |= 0x01;  // set active
                L.OnEnable(go);
            }
        }

        // Bit 2 = pendingDisable
        if (go->flagsAsInt & 0x04) {
            go->flagsAsInt &= ~0x04u;  // clear pending
            if (go->flagsAsInt & 0x01) {  // if currently active
                go->flagsAsInt &= ~0x01u;  // clear active
                L.OnDisable(go);
            }
        }
    }
}

// ============================================================================
// PLAYER
// ============================================================================

psyqo::Vec3& psxsplash::SceneManager::getPlayerPosition(){
    return m_playerPosition;
}

void psxsplash::SceneManager::setPlayerPosition(psyqo::FixedPoint<12> x, psyqo::FixedPoint<12> y, psyqo::FixedPoint<12> z){
    m_playerPosition.x = x;
    m_playerPosition.y = y;
    m_playerPosition.z = z;

    if (m_navRegions.isLoaded()) 
    {
        m_playerNavRegion = m_navRegions.findRegionClosest(x.value, y.value, z.value);
    }
}

psyqo::Vec3 psxsplash::SceneManager::getPlayerRotation(){
    psyqo::Vec3 playerRot;

    playerRot.x = (psyqo::FixedPoint<12>)playerRotationX;
    playerRot.y = (psyqo::FixedPoint<12>)playerRotationY;
    playerRot.z = (psyqo::FixedPoint<12>)playerRotationZ;

    return playerRot;
}

void psxsplash::SceneManager::setPlayerRotation(psyqo::FixedPoint<12> x, psyqo::FixedPoint<12> y, psyqo::FixedPoint<12> z){
   playerRotationX = (psyqo::FixedPoint<10>)x;
   playerRotationY = (psyqo::FixedPoint<10>)y;
   playerRotationZ = (psyqo::FixedPoint<10>)z;
}

// ============================================================================
// SCENE LOADING
// ============================================================================

void psxsplash::SceneManager::requestSceneLoad(int sceneIndex) {
    m_pendingSceneIndex = sceneIndex;
}

void psxsplash::SceneManager::processPendingSceneLoad() {
    if (m_pendingSceneIndex < 0) return;

    int targetIndex = m_pendingSceneIndex;
    m_pendingSceneIndex = -1;

    auto& gpu = Renderer::GetInstance().getGPU();
    loadScene(gpu, targetIndex, /*isFirstScene=*/false);
}

void psxsplash::SceneManager::loadScene(psyqo::GPU& gpu, int sceneIndex, bool isFirstScene) {
    // Restore CD-ROM controller and CPU IRQ state for file loading.
#if defined(LOADER_CDROM)
    CDRomHelper::WakeDrive();
#endif

    psyqo::Prim::FastFill ff(psyqo::Color{.r = 0, .g = 0, .b = 0});
    ff.rect = psyqo::Rect{0, 0, 320, 240};
    gpu.sendPrimitive(ff);
    ff.rect = psyqo::Rect{0, 256, 320, 240};
    gpu.sendPrimitive(ff);
    gpu.pumpCallbacks();

    LoadingScreen loading;
    if (s_font) {
        if (loading.load(gpu, *s_font, sceneIndex)) {
            loading.renderInitialAndFree(gpu);
        }
    }

    if (!isFirstScene) {
        // Tear down EVERYTHING in the current scene first —
        // Lua VM, vector backing storage, audio.  This returns as much
        // heap memory as possible before any new allocation.
        clearScene();

        // Free old splashpack data BEFORE loading the new one.
        // This avoids having both scene buffers in the heap simultaneously.
        if (m_currentSceneData) {
            FileLoader::Get().FreeFile(m_currentSceneData);
            m_currentSceneData = nullptr;
        }
    }

    if (loading.isActive()) loading.updateProgress(gpu, 10);

    // ── Step 1: Load VRAM data, upload to GPU, free buffer ──
    {
        char vramFilename[32];
        FileLoader::BuildVramFilename(sceneIndex, vramFilename, sizeof(vramFilename));
        int vramSize = 0;
        uint8_t* vramData = FileLoader::Get().LoadFileSync(vramFilename, vramSize);
        if (vramData) {
            uploadVramData(vramData, vramSize);
            FileLoader::Get().FreeFile(vramData);
        }
    }

    if (loading.isActive()) loading.updateProgress(gpu, 20);

    // ── Step 2: Load SPU data, upload to SPU RAM, free buffer ──
    // Must init audio before uploading ADPCM data so SPU RAM is ready.
    m_audio.init();
    {
        char spuFilename[32];
        FileLoader::BuildSpuFilename(sceneIndex, spuFilename, sizeof(spuFilename));
        int spuSize = 0;
        uint8_t* spuData = FileLoader::Get().LoadFileSync(spuFilename, spuSize);
        if (spuData) {
            uploadSpuData(spuData, spuSize);
            FileLoader::Get().FreeFile(spuData);
        }
    }

    if (loading.isActive()) loading.updateProgress(gpu, 25);

    // ── Step 3: Load splashpack (live data only, stays resident) ──
    char filename[32];
    FileLoader::BuildSceneFilename(sceneIndex, filename, sizeof(filename));

    int fileSize = 0;
    uint8_t* newData = nullptr;

    if (loading.isActive()) {
        struct Ctx { LoadingScreen* ls; psyqo::GPU* gpu; };
        Ctx ctx{&loading, &gpu};
        FileLoader::LoadProgressInfo progress{
            [](uint8_t pct, void* ud) {
                auto* c = static_cast<Ctx*>(ud);
                c->ls->updateProgress(*c->gpu, pct);
            },
            &ctx, 25, 35
        };
        newData = FileLoader::Get().LoadFileSyncWithProgress(
            filename, fileSize, &progress);
    } else {
        newData = FileLoader::Get().LoadFileSync(filename, fileSize);
    }

    if (!newData && isFirstScene) {
        // Fallback: try legacy name for backwards compatibility (PCdrv only)
        newData = FileLoader::Get().LoadFileSync("output.bin", fileSize);
    }

    if (!newData) {
        return;
    }

    if (loading.isActive()) loading.updateProgress(gpu, 35);

    // ── Step 4: Resolve SCENE_<n>.XA's starting LBA while the drive
    // is still awake. XA streaming bypasses the file API and drives
    // SETLOC/READS directly, so all the runtime needs is the LBA.
    // 0 from GetFileLbaSync = no XA file in this scene's layout
    // (i.e. the export wrote no XA-routed clips); play() will
    // short-circuit and report the miss.
#if defined(LOADER_CDROM)
    {
        char xaFilename[32];
        FileLoader::BuildXaFilename(sceneIndex, xaFilename, sizeof(xaFilename));
        uint32_t xaLba = static_cast<psxsplash::FileLoaderCDRom&>(
            FileLoader::Get()).GetFileLbaSync(xaFilename);
        m_xa.setSceneXaLba(xaLba);
    }
#endif

    // Stop the CD-ROM motor and mask all interrupts for gameplay.
#if defined(LOADER_CDROM)
    CDRomHelper::SilenceDrive();
#endif

    m_currentSceneData = newData;
    m_currentSceneIndex = sceneIndex;

    // Initialize with new data (creates fresh Lua VM inside)
    InitializeScene(newData, loading.isActive() ? &loading : nullptr);
}

// ============================================================================
// VRAM DATA UPLOAD (v20+ separate .vram file)
// ============================================================================
// Format: 'V' 'R' atlasCount(u16) clutCount(u16) fontCount(u8) pad(u8)
//         Per-atlas: vramX(u16) vramY(u16) width(u16) height(u16) + pixel data + align4
//         Per-CLUT: clutPackingX(u16) clutPackingY(u16) length(u16) pad(u16) + data + align4
//         Per-font: glyphW(u8) glyphH(u8) vramX(u16) vramY(u16) textureH(u16)
//                   dataSize(u32) + pixel data + align4

void psxsplash::SceneManager::uploadVramData(uint8_t* vramData, int vramSize) {
    if (!vramData || vramSize < 8) return;

    uint8_t* ptr = vramData;

    // Header
    // Skip magic 'V','R'
    ptr += 2;
    uint16_t atlasCount = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;
    uint16_t clutCount  = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;
    uint8_t  fontCount  = *ptr++;
    ptr++; // pad

    auto& renderer = Renderer::GetInstance();

    // Upload texture atlases
    for (uint16_t i = 0; i < atlasCount; i++) {
        uint16_t vramX  = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;
        uint16_t vramY  = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;
        uint16_t width  = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;
        uint16_t height = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;

        uint32_t pixelBytes = (uint32_t)width * height * 2;
        renderer.VramUpload(reinterpret_cast<uint16_t*>(ptr), vramX, vramY, width, height);
        ptr += pixelBytes;

        // Align to 4 bytes
        uintptr_t addr = reinterpret_cast<uintptr_t>(ptr);
        ptr = reinterpret_cast<uint8_t*>((addr + 3) & ~3);
    }

    // Upload CLUTs
    for (uint16_t i = 0; i < clutCount; i++) {
        uint16_t clutPackingX = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;
        uint16_t clutPackingY = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;
        uint16_t length       = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;
        ptr += 2; // pad

        renderer.VramUpload(reinterpret_cast<uint16_t*>(ptr),
                            clutPackingX * 16, clutPackingY, length, 1);
        ptr += (uint32_t)length * 2;

        // Align to 4 bytes
        uintptr_t addr = reinterpret_cast<uintptr_t>(ptr);
        ptr = reinterpret_cast<uint8_t*>((addr + 3) & ~3);
    }

    // Upload font pixel data
    for (uint8_t i = 0; i < fontCount; i++) {
        // uint8_t glyphW = ptr[0]; // not needed for upload
        // uint8_t glyphH = ptr[1]; // not needed for upload
        ptr += 2; // skip glyphW, glyphH
        uint16_t fontVramX  = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;
        uint16_t fontVramY  = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;
        uint16_t textureH   = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;
        uint32_t dataSize   = *reinterpret_cast<uint32_t*>(ptr); ptr += 4;

        if (dataSize > 0) {
            // 4bpp 256px wide = 64 VRAM hwords wide
            renderer.VramUpload(reinterpret_cast<const uint16_t*>(ptr),
                                (int16_t)fontVramX, (int16_t)fontVramY,
                                64, (int16_t)textureH);

            // Upload white CLUT (entry 0=transparent, entry 1=white)
            static const uint16_t whiteCLUT[2] = { 0x0000, 0x7FFF };
            renderer.VramUpload(whiteCLUT,
                                (int16_t)fontVramX, (int16_t)fontVramY,
                                2, 1);
            ptr += dataSize;
        }

        // Align to 4 bytes
        uintptr_t addr = reinterpret_cast<uintptr_t>(ptr);
        ptr = reinterpret_cast<uint8_t*>((addr + 3) & ~3);
    }
}

// ============================================================================
// SPU DATA UPLOAD (v20+ separate .spu file)
// ============================================================================
// Format: 'S' 'A' clipCount(u16)
//         Per-clip: sizeBytes(u32) sampleRate(u16) loop(u8) pad(u8) + ADPCM data + align4

void psxsplash::SceneManager::uploadSpuData(uint8_t* spuData, int spuSize) {
    if (!spuData || spuSize < 4) return;

    uint8_t* ptr = spuData;

    // Header
    // Skip magic 'S','A'
    ptr += 2;
    uint16_t clipCount = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;

    for (uint16_t i = 0; i < clipCount; i++) {
        uint32_t sizeBytes = *reinterpret_cast<uint32_t*>(ptr); ptr += 4;
        uint16_t sampleRate = *reinterpret_cast<uint16_t*>(ptr); ptr += 2;
        uint8_t  loop       = *ptr++;
        ptr++; // pad

        if (sizeBytes > 0) {
            m_audio.loadClip((int)i, ptr, sizeBytes, sampleRate, loop != 0);
            ptr += sizeBytes;
        }

        // Align to 4 bytes
        uintptr_t addr = reinterpret_cast<uintptr_t>(ptr);
        ptr = reinterpret_cast<uint8_t*>((addr + 3) & ~3);
    }
}

void psxsplash::SceneManager::clearScene() {
    // 1. Shut down the Lua VM first — frees ALL Lua-allocated memory
    //    (bytecode, strings, tables, registry) in one shot via lua_close.
    L.Shutdown();

    // 2. Clear all vectors to free their heap storage (game objects, Lua files, names, etc)
    { eastl::vector<GameObject*>    tmp; tmp.swap(m_gameObjects);    }
    { eastl::vector<LuaFile*>       tmp; tmp.swap(m_luaFiles);       }
    { eastl::vector<const char*>    tmp; tmp.swap(m_objectNames);    }
    { eastl::vector<const char*>    tmp; tmp.swap(m_audioClipNames); }
    { eastl::vector<const char*>    tmp; tmp.swap(m_musicSequenceNames); }
    { eastl::vector<Interactable*>  tmp; tmp.swap(m_interactables);  }

    // 3. Reset hardware / subsystems
    m_musicSequencer.stop();   // Key-off any held notes before SPU reset
    m_audio.reset();           // Free SPU RAM and stop all voices
    m_collisionSystem.init();  // Re-init collision system
    m_cutsceneCount = 0;
    s_activePromptCanvas = -1; // Reset prompt tracking
    m_cutscenePlayer.init(nullptr, 0, nullptr, nullptr);  // Reset cutscene player
    m_animationCount = 0;
    m_animationPlayer.init(nullptr, 0);  // Reset animation player
    m_skinnedMeshCount = 0;
    Renderer::GetInstance().SetSkinData(nullptr, nullptr, 0);
    // BVH and NavRegions will be overwritten by next load
    
    // Reset UI system (disconnect from renderer before splashpack data disappears)
    Renderer::GetInstance().SetUISystem(nullptr);

    // Reset room/portal pointers (they point into splashpack data which is being freed)
    m_rooms = nullptr;
    m_roomCount = 0;
    m_portals = nullptr;
    m_portalCount = 0;
    m_roomTriRefs = nullptr;
    m_roomTriRefCount = 0;
    m_roomCells = nullptr;
    m_roomCellCount = 0;
    m_roomPortalRefs = nullptr;
    m_roomPortalRefCount = 0;
    m_sceneType = 0;
}

// ============================================================================
// OBJECT NAME LOOKUP
// ============================================================================


psxsplash::GameObject* psxsplash::SceneManager::findObjectByName(const char* name) const {
    if (!name || m_objectNames.empty()) return nullptr;
    for (size_t i = 0; i < m_objectNames.size() && i < m_gameObjects.size(); i++) {
        if (m_objectNames[i] && streq(m_objectNames[i], name)) {
            return m_gameObjects[i];
        }
    }
    return nullptr;
}

int psxsplash::SceneManager::findAudioClipByName(const char* name) const {
    if (!name || m_audioClipNames.empty()) return -1;
    for (size_t i = 0; i < m_audioClipNames.size(); i++) {
        if (m_audioClipNames[i] && streq(m_audioClipNames[i], name)) {
            return static_cast<int>(i);
        }
    }
    return -1;
}

bool psxsplash::SceneManager::getXaClipInfo(const char* name, uint32_t &outOffset, uint32_t &outSize) const {
    if (!name || m_xaClipNames.empty()) return false;
    for (size_t i = 0; i < m_xaClipNames.size(); i++) {
        if (m_xaClipNames[i] && streq(m_xaClipNames[i], name)) {
            outOffset = m_xaClipOffsets[i];
            outSize   = m_xaClipSizes[i];
            return true;
        }
    }
    return false;
}

int psxsplash::SceneManager::findMusicSequenceByName(const char* name) const {
    if (!name || m_musicSequenceNames.empty()) return -1;
    for (size_t i = 0; i < m_musicSequenceNames.size(); i++) {
        if (m_musicSequenceNames[i] && streq(m_musicSequenceNames[i], name)) {
            return static_cast<int>(i);
        }
    }
    return -1;
}

int psxsplash::SceneManager::findSkinAnimByObjectName(const char* name) const {
    if (!name || m_objectNames.empty()) return -1;
    for (size_t i = 0; i < m_objectNames.size() && i < m_gameObjects.size(); i++) {
        if (m_objectNames[i] && streq(m_objectNames[i], name)) {
            for (int si = 0; si < m_skinnedMeshCount; si++) {
                if (m_skinAnimSets[si].gameObjectIndex == (uint16_t)i) return si;
            }
            return -1;  // Object found but not skinned
        }
    }
    return -1;
}

int psxsplash::SceneManager::findUIModelByName(const char *name) const {
    if (!name || !m_uiModelsDisk || m_uiModelCount == 0) return -1;
    for (int i = 0; i < m_uiModelCount; i++) {
        const char *mn = m_uiModelsDisk[i].name;
        if (mn && streq(mn, name)) return i;
    }
    return -1;
}
