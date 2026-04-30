#pragma once

// Runtime performance overlay - shows FPS, frame time, triangle count,
// game-object count, and splashpack VRAM usage in the top-left corner
// over a semi-transparent black plate (so it stays readable across
// arbitrary BG colors). Compiled only when PSXSPLASH_PERFOVERLAY is
// defined (PERFOVERLAY=1 in the Makefile invocation).
//
// Two-phase API mirrors MemOverlay so the renderer can drive both the
// same way:
//   1. renderOT()   — insert background plate primitives at OT depth 0
//   2. renderText() — chainprintf the metric lines AFTER gpu.chain(ot)
//
// Counters are pushed via static setters from whatever code knows the
// value (Renderer for tris, SceneManager for VRAM/objects, main for
// frame time). Keeps the overlay decoupled from the rest of the
// runtime — no heavy refactor needed to feed it data.

#ifdef PSXSPLASH_PERFOVERLAY

#include <psyqo/font.hh>
#include <psyqo/gpu.hh>
#include <psyqo/bump-allocator.hh>
#include <psyqo/ordering-table.hh>

#include "renderer.hh"

namespace psxsplash {

class PerfOverlay {
public:
    void init(psyqo::Font<>* font);

    // Phase 1: insert semi-transparent backdrop into OT before gpu.chain().
    void renderOT(psyqo::OrderingTable<Renderer::ORDERING_TABLE_SIZE>& ot,
                  psyqo::BumpAllocator<Renderer::BUMP_ALLOCATOR_SIZE>& balloc);

    // Phase 2: emit text lines via chainprintf after gpu.chain().
    void renderText(psyqo::GPU& gpu);

    // ── Counter setters (call from anywhere each frame) ──
    static void setTrisRendered(uint32_t n)    { s_tris = n; }
    static void setObjectCount(uint32_t n)     { s_objects = n; }
    static void setVRAMBytes(uint32_t bytes)   { s_vramBytes = bytes; }
    static void setFrameTimeRaw(uint32_t raw)  { s_frameTimeRaw = raw; }
    static void setFps(uint32_t fps)           { s_fps = fps; }

    // Renderer convenience: bump tri counter as triangles are submitted.
    // Reset at frame start by Renderer.
    static void resetTriCounter()              { s_trisAccum = 0; }
    static void countTri()                     { s_trisAccum++; }
    static void publishTriCounter()            { s_tris = s_trisAccum; }

private:
    psyqo::Font<>* m_font = nullptr;

    // Live counters, updated externally (atomic-ish on PSX since we're
    // single-threaded; the renderer thread is the only writer).
    static uint32_t s_tris;
    static uint32_t s_trisAccum;
    static uint32_t s_objects;
    static uint32_t s_vramBytes;
    static uint32_t s_frameTimeRaw;
    static uint32_t s_fps;
};

} // namespace psxsplash

#endif // PSXSPLASH_PERFOVERLAY
