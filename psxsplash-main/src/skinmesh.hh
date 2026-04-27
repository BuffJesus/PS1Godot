#pragma once

#include <stdint.h>

#include <psyqo/matrix.hh>
#include <psyqo/vector.hh>
#include <psyqo/fixed-point.hh>
#include <psyqo/gte-registers.hh>

#include "gameobject.hh"
#include "mesh.hh"

// Forward-declare lua_State to avoid pulling in lua headers
struct lua_State;
#ifndef LUA_NOREF
#define LUA_NOREF (-2)
#endif

namespace psxsplash {

static constexpr uint8_t  SKINMESH_MAX_BONES  = 64;
static constexpr uint8_t  SKINMESH_MAX_CLIPS  = 16;
static constexpr int      MAX_SKINNED_MESHES   = 16;

/// Pre-baked bone matrix: 3×3 rotation (4.12 fp) + translation. The
/// renderer's working format — built either at frame swap by decoding
/// the on-disk BakedBonePose, or assembled directly for the bind-pose
/// identity case. Layout matches the GTE rotation register format
/// (9 × int16) plus a 3-component translation (3 × int16).
struct BakedBoneMatrix {
    int16_t r[9];    // row-major: r00,r01,r02, r10,r11,r12, r20,r21,r22
    int16_t t[3];    // translation: tx, ty, tz (model-space scale, 4.12 fp)
};
static_assert(sizeof(BakedBoneMatrix) == 24, "BakedBoneMatrix must be 24 bytes");

/// On-disk + in-RAM bone pose: quaternion (4 × int16 in fp12) plus
/// translation (3 × int16 in fp12). 14 bytes — 42% smaller than the
/// matrix form. The renderer decodes one pose to a BakedBoneMatrix at
/// frame swap via poseToMatrix() before per-bone matrix interpolation;
/// translation passes through unchanged. Splashpack v30+.
struct BakedBonePose {
    int16_t q[4];    // quaternion (x, y, z, w), fp12 (4096 = 1.0)
    int16_t t[3];    // translation: tx, ty, tz (model-space scale, fp12)
};
static_assert(sizeof(BakedBonePose) == 14, "BakedBonePose must be 14 bytes");

/// Decode a quaternion+translation pose into a 3×3 rotation matrix +
/// translation in fp12. Standard quat-to-matrix formulae, applied
/// component-wise in fp12 fixed-point. All terms stay in int16 range
/// (max |2*(qa*qb)| ≤ 8192 in fp12). Inlined for the per-bone hot path.
inline void poseToMatrix(const BakedBonePose& pose, BakedBoneMatrix& out) {
    int32_t qx = pose.q[0], qy = pose.q[1], qz = pose.q[2], qw = pose.q[3];
    int32_t xx = (qx * qx) >> 12;
    int32_t yy = (qy * qy) >> 12;
    int32_t zz = (qz * qz) >> 12;
    int32_t xy = (qx * qy) >> 12;
    int32_t xz = (qx * qz) >> 12;
    int32_t yz = (qy * qz) >> 12;
    int32_t wx = (qw * qx) >> 12;
    int32_t wy = (qw * qy) >> 12;
    int32_t wz = (qw * qz) >> 12;

    out.r[0] = (int16_t)(4096 - 2 * (yy + zz));
    out.r[1] = (int16_t)(2 * (xy - wz));
    out.r[2] = (int16_t)(2 * (xz + wy));
    out.r[3] = (int16_t)(2 * (xy + wz));
    out.r[4] = (int16_t)(4096 - 2 * (xx + zz));
    out.r[5] = (int16_t)(2 * (yz - wx));
    out.r[6] = (int16_t)(2 * (xz - wy));
    out.r[7] = (int16_t)(2 * (yz + wx));
    out.r[8] = (int16_t)(4096 - 2 * (xx + yy));
    out.t[0] = pose.t[0];
    out.t[1] = pose.t[1];
    out.t[2] = pose.t[2];
}

/// One animation clip: name, playback settings, and pointer into the scene data buffer.
/// Binary layout (v30): flags(1), fps(1), frameCount(2, little-endian), then frame data.
struct SkinAnimClip {
    const char* name;              // points into splashpack data (null-terminated by loader)
    const BakedBonePose* frames;   // points into the scene data buffer (14 B/bone/frame)
    uint16_t frameCount;           // number of baked frames (no hard cap — user's responsibility)
    uint8_t  flags;                // bit 0 = loops
    uint8_t  fps;                  // baked sampling rate (1-30)
    uint8_t  boneCount;
    uint8_t  _pad[3];
};

/// All clips for one skinned object.
struct SkinAnimSet {
    Tri*         polygons;          // stolen from the GO at init (regular render sees polyCount=0)
    const uint8_t* boneIndices;    // polyCount×3 bone index bytes, points into splashpack data
    uint16_t     polyCount;        // triangle count (moved from GO)
    uint8_t      clipCount;
    uint8_t      boneCount;        // from the skin data (shared across clips)
    uint16_t     gameObjectIndex;  // index into m_gameObjects (still used for transform)
    // BIGHEAD cheat support: bone index of the character's "head" bone
    // (auto-detected at export time by name match — "head", "mixamorig:head"
    // etc). 0xFFFF = no head bone identified, BIGHEAD becomes a no-op for
    // this mesh. Repurposed from the prior 16-bit _pad slot — same struct
    // size, no splashpack format bump.
    uint16_t     headBoneIndex;
    SkinAnimClip clips[SKINMESH_MAX_CLIPS];
};

/// Per-instance runtime playback state.
struct SkinAnimState {
    SkinAnimSet* animSet;          // points into scene data, never null after load
    uint16_t currentFrame;         // current whole frame index
    uint16_t subFrame;             // 0..4095 (0.12 fixed-point) fraction between currentFrame and next
    uint8_t  currentClip;
    bool     playing;
    bool     loop;                 // runtime loop override (set by Lua Play call)
    bool     bindPose;             // render bind pose (identity bone matrices) instead of a clip frame.
                                   // Default-on so characters render in T-pose until a clip starts.
    int      luaCallbackRef;       // Lua registry reference, LUA_NOREF = none
};

/// Tick the animation state.  dt12 is the frame delta in 0.12 fixed-point
/// (4096 = one 30fps frame).  Framerate-independent.
void SkinMesh_Tick(SkinAnimState* state, lua_State* L, int32_t dt12);

}  // namespace psxsplash
