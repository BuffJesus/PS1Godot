#pragma once

#include <EASTL/array.h>
#include <EASTL/vector.h>

#include <psyqo/bump-allocator.hh>
#include <psyqo/fragments.hh>
#include <psyqo/gpu.hh>
#include <psyqo/kernel.hh>
#include <psyqo/ordering-table.hh>
#include <psyqo/primitives/common.hh>
#include <psyqo/primitives/misc.hh>
#include <psyqo/primitives/triangles.hh>
#include <psyqo/trigonometry.hh>

#include "bvh.hh"
#include "camera.hh"
#include "gameobject.hh"
#include "skinmesh.hh"
#include "triclip.hh"

namespace psxsplash {

// Forward decls — splashpack.hh includes uisystem.hh which includes this
// header, so we can't pull splashpack.hh here without triggering a
// pragma-once cycle that leaves SPLASHPACKUIModel declared only AFTER
// renderer.hh's class body has already parsed. Pointers are fine with
// forward decls; renderer.cpp pulls in splashpack.hh for full definitions.
struct SPLASHPACKUIModel;
struct UIModelRuntimeState;

class UISystem; // Forward declaration
#ifdef PSXSPLASH_MEMOVERLAY
class MemOverlay; // Forward declaration
#endif

struct FogConfig {
    bool enabled = false;
    psyqo::Color color = {.r = 0, .g = 0, .b = 0};
    uint8_t density = 5;
    int32_t fogFarSZ = 0;
    // v32+: explicit near plane in GTE-Z space. 0 means "derive as
    // fogFarSZ/8" (the legacy fixed-ratio behavior). SetFog clamps
    // fogNearSZ to (fogFarSZ - 1) so an inverted authoring still
    // produces a non-degenerate ramp.
    int32_t fogNearSZ = 0;
};

class Renderer final {
  public:
    Renderer(const Renderer&) = delete;
    Renderer& operator=(const Renderer&) = delete;

#ifndef OT_SIZE
#define OT_SIZE (2048 * 8)
#endif
#ifndef BUMP_SIZE
#define BUMP_SIZE (8096 * 24)
#endif
    static constexpr size_t ORDERING_TABLE_SIZE = OT_SIZE;
    static constexpr size_t BUMP_ALLOCATOR_SIZE = BUMP_SIZE;
    static constexpr size_t MAX_VISIBLE_TRIANGLES = 4096;

    static constexpr int32_t PROJ_H = 120;
    static constexpr int32_t SCREEN_CX = 160;
    static constexpr int32_t SCREEN_CY = 120;

    static void Init(psyqo::GPU& gpuInstance);
    void SetCamera(Camera& camera);
    void SetFog(const FogConfig& fog);

    // v32+: scene-level background tone. When enabled, replaces the
    // GPU clear color (defaults to fog color) so authors can pick a
    // backdrop independent of the fog ramp tint. Useful for interiors
    // where you want pitch-black behind dim mood-lit geometry without
    // the fog tone bleeding into the void.
    void SetBackgroundColor(uint8_t r, uint8_t g, uint8_t b, bool enabled);

    void Render(eastl::vector<GameObject*>& objects);
    void RenderWithBVH(eastl::vector<GameObject*>& objects, const BVHManager& bvh);
    void RenderWithRooms(eastl::vector<GameObject*>& objects,
                         const RoomData* rooms, int roomCount,
                         const PortalData* portals, int portalCount,
                         const TriangleRef* roomTriRefs,
                         const RoomCell* cells = nullptr,
                         const RoomPortalRef* roomPortalRefs = nullptr,
                         int cameraRoom = -1);


    void VramUpload(const uint16_t* imageData, int16_t posX, int16_t posY,
                    int16_t width, int16_t height);

    void SetUISystem(UISystem* ui) { m_uiSystem = ui; }
#ifdef PSXSPLASH_MEMOVERLAY
    void SetMemOverlay(MemOverlay* overlay) { m_memOverlay = overlay; }
#endif
    psyqo::GPU& getGPU() { return m_gpu; }

    void SetSkinData(const SkinAnimSet* sets, const SkinAnimState* states, int count) {
        m_skinSets = sets; m_skinStates = states; m_skinCount = count;
    }

    // v23+: hand the renderer the per-scene UI 3D-model arrays (set once at
    // scene init by SceneManager). Renderer reads them each frame in its
    // post-skin HUD pass.
    void SetUIModelData(const SPLASHPACKUIModel* disk, const UIModelRuntimeState* states, int count) {
        m_uiModelsDisk = disk; m_uiModelStates = states; m_uiModelCount = count;
    }

    // v24+: scene skybox. SceneManager calls this once at scene init.
    // Pass enabled=false to disable; texture coords are ignored when off.
    void SetSky(uint8_t texpageX, uint8_t texpageY, uint16_t clutX, uint16_t clutY,
                uint8_t u0, uint8_t v0, uint8_t u1, uint8_t v1, uint8_t bitDepth,
                uint8_t tintR, uint8_t tintG, uint8_t tintB, bool enabled) {
        m_sky.texpageX = texpageX; m_sky.texpageY = texpageY;
        m_sky.clutX = clutX; m_sky.clutY = clutY;
        m_sky.u0 = u0; m_sky.v0 = v0; m_sky.u1 = u1; m_sky.v1 = v1;
        m_sky.bitDepth = bitDepth;
        m_sky.tintR = tintR; m_sky.tintG = tintG; m_sky.tintB = tintB;
        m_sky.enabled = enabled;
    }

    static Renderer& GetInstance() {
        psyqo::Kernel::assert(instance != nullptr,
                              "Access to renderer was tried without prior initialization");
        return *instance;
    }

  private:
    static Renderer* instance;

    Renderer(psyqo::GPU& gpuInstance) : m_gpu(gpuInstance) {}
    ~Renderer() {}

    Camera* m_currentCamera = nullptr;
    psyqo::GPU& m_gpu;
    psyqo::Trig<> m_trig;

    psyqo::OrderingTable<ORDERING_TABLE_SIZE> m_ots[2];
    psyqo::Fragments::SimpleFragment<psyqo::Prim::FastFill> m_clear[2];
    psyqo::BumpAllocator<BUMP_ALLOCATOR_SIZE> m_ballocs[2];

    FogConfig m_fog;
    psyqo::Color m_clearcolor = {.r = 0, .g = 0, .b = 0};
    // v32+: when true, m_clearcolor is the scene-authored bg and stays
    // pinned even if SetFog is called later. When false (default), the
    // clear color tracks fog tone (legacy behavior).
    bool m_bgEnabled = false;

    UISystem* m_uiSystem = nullptr;
#ifdef PSXSPLASH_MEMOVERLAY
    MemOverlay* m_memOverlay = nullptr;
#endif

    const SkinAnimSet* m_skinSets = nullptr;
    const SkinAnimState* m_skinStates = nullptr;
    int m_skinCount = 0;

    // v23+: UI 3D-model widgets — read once per frame in the HUD pass.
    const SPLASHPACKUIModel* m_uiModelsDisk = nullptr;
    const UIModelRuntimeState* m_uiModelStates = nullptr;
    int m_uiModelCount = 0;

    // v24+: scene skybox. Drawn before main scene OT each frame; the
    // exporter writes a UI-Image-shaped 16-byte block in the splashpack
    // header so we reuse the same tpage/clut/UV decode.
    struct SkyState {
        uint8_t  texpageX;
        uint8_t  texpageY;
        uint16_t clutX;
        uint16_t clutY;
        uint8_t  u0, v0, u1, v1;
        uint8_t  bitDepth;
        uint8_t  tintR, tintG, tintB;
        bool     enabled;
    };
    SkyState m_sky = {};
    int m_skyScrollFrame = 0;  // monotonic counter; renderSky uses for U pan drift

    void renderSky(psyqo::OrderingTable<ORDERING_TABLE_SIZE>& ot,
                   psyqo::BumpAllocator<BUMP_ALLOCATOR_SIZE>& balloc);

    TriangleRef m_visibleRefs[MAX_VISIBLE_TRIANGLES];
    int m_frameCount = 0;
    // Set by each top-level render loop right before processTriangle
    // calls so the per-tri primitive emission can pick setSemiTrans
    // vs setOpaque without an extra parameter through every call site.
    bool m_currentObjTranslucent = false;

    psyqo::Vec3 computeCameraViewPos();
    void setupObjectTransform(GameObject* obj, const psyqo::Vec3& cameraPosition);

    void processTriangle(Tri& tri, int32_t fogFarSZ,
                         psyqo::OrderingTable<ORDERING_TABLE_SIZE>& ot,
                         psyqo::BumpAllocator<BUMP_ALLOCATOR_SIZE>& balloc,
                         int depth = 0,
                         bool forceFrontOT = false);

    void renderSkinnedObjects(eastl::vector<GameObject*>& objects,
                              const psyqo::Vec3& cameraPosition,
                              int32_t fogFarSZ,
                              psyqo::OrderingTable<ORDERING_TABLE_SIZE>& ot,
                              psyqo::BumpAllocator<BUMP_ALLOCATOR_SIZE>& balloc,
                              const Frustum* frustum = nullptr);

    // v23+: post-skin HUD model pass. Inserted into the same OT as the
    // main scene polys (so it draws on top in the same frame). Walks
    // m_uiModelsDisk × m_uiModelStates and re-renders each visible
    // model with an alternate camera transform.
    void renderUIModels(eastl::vector<GameObject*>& objects,
                        psyqo::OrderingTable<ORDERING_TABLE_SIZE>& ot,
                        psyqo::BumpAllocator<BUMP_ALLOCATOR_SIZE>& balloc);
};

}  // namespace psxsplash
