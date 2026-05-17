#pragma once

#include <stdint.h>
#include <psyqo/primitives/common.hh>

namespace psxsplash {

// Safe clamping bounds for PS1 GPU rasterizer limits (1023×511 max vertex delta).

static constexpr int16_t SAFE_MIN_X = -351;
static constexpr int16_t SAFE_MAX_X =  672;
static constexpr int16_t SAFE_MIN_Y = -135;
static constexpr int16_t SAFE_MAX_Y =  376;

// Sub-pixel reject: triangle screen-space span fits within `threshold`
// pixels on both axes. Triangle would contribute at most one pixel
// (rasterizer's center-of-pixel rule) for the cost of a full GPU
// setup + OT insert. Cheap to detect (4 min/max + 2 subtractions)
// vs the ~50 GPU cycles a tiny tri otherwise burns. See
// docs/rfc/visibility-culling.md Pass 3 — threshold 1 is the conservative
// default (only reject tris within a single pixel span).
inline bool isSubpixel(const psyqo::Vertex& v0,
                       const psyqo::Vertex& v1,
                       const psyqo::Vertex& v2,
                       int16_t threshold = 1) {
    int16_t x0 = v0.x, x1 = v1.x, x2 = v2.x;
    int16_t y0 = v0.y, y1 = v1.y, y2 = v2.y;
    int16_t minX = x0 < x1 ? (x0 < x2 ? x0 : x2) : (x1 < x2 ? x1 : x2);
    int16_t maxX = x0 > x1 ? (x0 > x2 ? x0 : x2) : (x1 > x2 ? x1 : x2);
    int16_t minY = y0 < y1 ? (y0 < y2 ? y0 : y2) : (y1 < y2 ? y1 : y2);
    int16_t maxY = y0 > y1 ? (y0 > y2 ? y0 : y2) : (y1 > y2 ? y1 : y2);
    return (maxX - minX) <= threshold && (maxY - minY) <= threshold;
}

// Early-reject: all 3 vertices past the same screen edge.
inline bool isCompletelyOutside(const psyqo::Vertex& v0,
                                const psyqo::Vertex& v1,
                                const psyqo::Vertex& v2) {
    int16_t x0 = v0.x, x1 = v1.x, x2 = v2.x;
    int16_t y0 = v0.y, y1 = v1.y, y2 = v2.y;

    if (x0 < SAFE_MIN_X && x1 < SAFE_MIN_X && x2 < SAFE_MIN_X) return true;
    if (x0 > SAFE_MAX_X && x1 > SAFE_MAX_X && x2 > SAFE_MAX_X) return true;
    if (y0 < SAFE_MIN_Y && y1 < SAFE_MIN_Y && y2 < SAFE_MIN_Y) return true;
    if (y0 > SAFE_MAX_Y && y1 > SAFE_MAX_Y && y2 > SAFE_MAX_Y) return true;
    return false;
}

// Clamp a projected vertex to safe rasterizer range.
inline void clampForRasterizer(psyqo::Vertex& v) {
    if (v.x < SAFE_MIN_X) v.x = SAFE_MIN_X;
    else if (v.x > SAFE_MAX_X) v.x = SAFE_MAX_X;
    if (v.y < SAFE_MIN_Y) v.y = SAFE_MIN_Y;
    else if (v.y > SAFE_MAX_Y) v.y = SAFE_MAX_Y;
}

}  // namespace psxsplash
