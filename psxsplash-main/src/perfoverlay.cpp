#ifdef PSXSPLASH_PERFOVERLAY

#include "perfoverlay.hh"

#include <psyqo/primitives/rectangles.hh>

namespace psxsplash {

uint32_t PerfOverlay::s_tris = 0;
uint32_t PerfOverlay::s_trisAccum = 0;
uint32_t PerfOverlay::s_objects = 0;
uint32_t PerfOverlay::s_vramBytes = 0;
uint32_t PerfOverlay::s_frameTimeRaw = 0;
uint32_t PerfOverlay::s_fps = 0;

void PerfOverlay::init(psyqo::Font<>* font) {
    m_font = font;
}

void PerfOverlay::renderOT(psyqo::OrderingTable<Renderer::ORDERING_TABLE_SIZE>& ot,
                            psyqo::BumpAllocator<Renderer::BUMP_ALLOCATOR_SIZE>& balloc) {
    if (!m_font) return;

    // Plate in top-left, sized to fit 5 lines at 14-pixel stride with
    // compact text. 100×72 leaves ~2px padding around the longest line
    // ("FT: 16.7ms" ≈ 80px wide) plus 4px breathing room between rows.
    static constexpr int16_t PLATE_X = 2;
    static constexpr int16_t PLATE_Y = 2;
    static constexpr int16_t PLATE_W = 100;
    static constexpr int16_t PLATE_H = 72;

    // PSX semi-transparent rectangles use one of four blend modes set on
    // the active TPage. Default (HalfBackAndHalfFront = abr=0) gives
    // 0.5*B + 0.5*F → an untinted black rect blends to ~50% darkening,
    // which is what we want for a readable HUD plate.
    //
    // Two passes layer the dimming to ~75% (1 - 0.5²), enough that white
    // text reads cleanly over bright BG textures (the wood-plank/stone
    // tavern bake we just shipped washes out a single 50% layer).
    //
    // The OT is LIFO; the TPage we insert AFTER each rect runs FIRST,
    // resetting the GPU drawing-mode state to half-blend (which the 3D
    // pass otherwise leaves clobbered to FullBackAndFullFront / additive,
    // turning a 0,0,0 semitrans rect into B+0=B → invisible).
    size_t needed = (sizeof(psyqo::Fragments::SimpleFragment<psyqo::Prim::Rectangle>)
                  +  sizeof(psyqo::Fragments::SimpleFragment<psyqo::Prim::TPage>)) * 2 + 32;
    if (balloc.remaining() < needed) return;

    for (int pass = 0; pass < 2; pass++) {
        auto& rectFrag = balloc.allocateFragment<psyqo::Prim::Rectangle>();
        rectFrag.primitive.setColor(psyqo::Color{.r = 0, .g = 0, .b = 0});
        rectFrag.primitive.position = {.x = PLATE_X, .y = PLATE_Y};
        rectFrag.primitive.size = {.x = PLATE_W, .y = PLATE_H};
        rectFrag.primitive.setSemiTrans();
        ot.insert(rectFrag, 0);

        auto& tpage = balloc.allocateFragment<psyqo::Prim::TPage>();
        tpage.primitive.attr.set(psyqo::Prim::TPageAttr::HalfBackAndHalfFront);
        ot.insert(tpage, 0);
    }
}

void PerfOverlay::renderText(psyqo::GPU& gpu) {
    if (!m_font) return;
    publishTriCounter();

    // White text, system font.
    psyqo::Color white{.r = 0xff, .g = 0xff, .b = 0xff};

    // Frame time in 0.1 ms units. The runtime hands us a "raw" frame
    // delta from the gpu refresh-rate divisor; convert to a ballpark
    // millisecond value the user can read. With NTSC 60 Hz refresh,
    // raw=1 ≈ 16.67 ms = 167 tenths of ms. The previous `(ftRaw * 167)
    // / 10` divided away the very precision the %lu.%lu split below
    // depends on — at raw=1 it printed "1.6ms" instead of "16.7ms",
    // making the overlay look frozen because every frame at 60 Hz
    // showed the same wrong value.
    uint32_t ftRaw = s_frameTimeRaw;
    uint32_t ftTenths = ftRaw * 167;  // tenths of ms

    m_font->chainprintf(gpu, {{.x = 4,  .y = 4 }}, white, "FPS:%lu",        s_fps);
    m_font->chainprintf(gpu, {{.x = 4,  .y = 18}}, white, "FT:%lu.%lums",
                        ftTenths / 10, ftTenths % 10);
    m_font->chainprintf(gpu, {{.x = 4,  .y = 32}}, white, "TRI:%lu",        s_tris);
    m_font->chainprintf(gpu, {{.x = 4,  .y = 46}}, white, "OBJ:%lu",        s_objects);
    m_font->chainprintf(gpu, {{.x = 4,  .y = 60}}, white, "VRAM:%luK",      s_vramBytes / 1024);
}

} // namespace psxsplash

#endif // PSXSPLASH_PERFOVERLAY
