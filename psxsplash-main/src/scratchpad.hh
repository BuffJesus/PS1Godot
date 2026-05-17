#pragma once

// R3000A scratchpad allocation map. The PS1 has a 1 KB SRAM region at
// 0x1F800000 with single-cycle read/write — same speed as registers.
// Putting per-frame hot state there avoids the ~3-cycle RAM access tax
// that piles up across thousands of touches per frame.
//
// This header defines the address layout and typed accessors. Consumers
// migrate in later stages — Stage 1 (this file) is plumbing only, with
// static_asserts guarding the allocation map. See
// docs/scratchpad-cache.md for the design and per-region win estimates.
//
// Allocation map (1 KB total):
//
//   offset 0    – 32 : OT root pointers (8 × 4 bytes)
//   offset 32   – 80 : Current camera rotation (psyqo::Matrix33 = 36 B,
//                      padded to 48 B = next 16-byte boundary)
//   offset 80   – 96 : Current camera translation (psyqo::Vec3 = 12 B,
//                      padded to 16 B)
//   offset 96   – 192: Frustum planes (6 × 16 = 96 B placeholder)
//   offset 192  – 320: Per-frame scratch counters (32 × 4 bytes)
//   offset 320  – 576: Hot scratch — inner-loop locals (256 B)
//   offset 576  – 768: DMA chain head + small primitives (192 B)
//   offset 768  – 1024: Reserved for Lua VM hot path (256 B)
//
// The address range NEVER moves between builds — host-mode tests would
// fail loudly if a scratchpad accessor were dereferenced off-target,
// but no consumer should ship before host-mode coverage lands.

#include <stdint.h>

#include <psyqo/matrix.hh>
#include <psyqo/vector.hh>

namespace psxsplash::Scratchpad {

// HOST BUILDS: 0x1F800000 is PS1 SRAM, not a valid host address.
// Calling any accessor below from a host-mode-testing build will fault.
// Stages 2+ MUST land alongside host-mode-testing.md coverage that
// swaps these accessors for heap-backed shims under a build guard.
inline constexpr uintptr_t kBaseAddr   = 0x1F800000;
inline constexpr uintptr_t kEndAddr    = kBaseAddr + 1024;

// Region offsets — kept aligned to the 16-byte boundary the PSYQo
// types naturally want.
inline constexpr uintptr_t kOTRootsOffset      = 0;
inline constexpr uintptr_t kOTRootsSize        = 32;

inline constexpr uintptr_t kCameraRotOffset    = 32;
inline constexpr uintptr_t kCameraRotSize      = 48;   // Matrix33 (36) padded

inline constexpr uintptr_t kCameraTransOffset  = 80;
inline constexpr uintptr_t kCameraTransSize    = 16;   // Vec3 (12) padded

inline constexpr uintptr_t kFrustumOffset      = 96;
inline constexpr uintptr_t kFrustumSize        = 96;   // 6 planes × 16

inline constexpr uintptr_t kCountersOffset     = 192;
inline constexpr uintptr_t kCountersSize       = 128;  // 32 × uint32_t

inline constexpr uintptr_t kHotScratchOffset   = 320;
inline constexpr uintptr_t kHotScratchSize     = 256;

inline constexpr uintptr_t kDMAChainOffset     = 576;
inline constexpr uintptr_t kDMAChainSize       = 192;

inline constexpr uintptr_t kLuaVMOffset        = 768;
inline constexpr uintptr_t kLuaVMSize          = 256;

// ── Layout assertions ──────────────────────────────────────────────
// Regions must (a) start where the previous region ended and (b) not
// overflow the 1 KB scratchpad. Any future region addition triggers a
// build error if it would shift another or break the bound — this is
// the whole point of the static map.

static_assert(kOTRootsOffset + kOTRootsSize == kCameraRotOffset);
static_assert(kCameraRotOffset + kCameraRotSize == kCameraTransOffset);
static_assert(kCameraTransOffset + kCameraTransSize == kFrustumOffset);
static_assert(kFrustumOffset + kFrustumSize == kCountersOffset);
static_assert(kCountersOffset + kCountersSize == kHotScratchOffset);
static_assert(kHotScratchOffset + kHotScratchSize == kDMAChainOffset);
static_assert(kDMAChainOffset + kDMAChainSize == kLuaVMOffset);
static_assert(kLuaVMOffset + kLuaVMSize == 1024,
              "Scratchpad map must fill exactly 1024 bytes");

static_assert(sizeof(psyqo::Matrix33) <= kCameraRotSize,
              "psyqo::Matrix33 grew past camera-rotation region size");
static_assert(sizeof(psyqo::Vec3) <= kCameraTransSize,
              "psyqo::Vec3 grew past camera-translation region size");

// ── Typed accessors ────────────────────────────────────────────────
// Each returns a reference/pointer pinned at a hardware address. The
// MIPS compiler turns the dereference into a single `lw` from a known
// offset — no indirection cost. Consumers should call once at the
// top of a per-frame phase and reuse the local reference rather than
// re-resolving inside a hot loop.

// OT bucket roots, one slot per active ordering-table layer. Layout
// is consumer-defined (currently the Renderer treats them as plain
// uintptr_t handles).
inline uintptr_t* otRoots() {
    return reinterpret_cast<uintptr_t*>(kBaseAddr + kOTRootsOffset);
}

inline psyqo::Matrix33& cameraRotation() {
    return *reinterpret_cast<psyqo::Matrix33*>(kBaseAddr + kCameraRotOffset);
}

inline psyqo::Vec3& cameraTranslation() {
    return *reinterpret_cast<psyqo::Vec3*>(kBaseAddr + kCameraTransOffset);
}

// Frustum plane region. Layout TBD when the consumer migrates from
// renderer.cpp's stack-local Frustum into the scratchpad slot.
inline void* frustumPlanes() {
    return reinterpret_cast<void*>(kBaseAddr + kFrustumOffset);
}

// Per-frame counters — 32 uint32_t slots. Consumer indexes by enum
// (Lua call count, BVH visit count, etc). Cleared at frame start by
// whichever subsystem owns the slot.
inline uint32_t* counters() {
    return reinterpret_cast<uint32_t*>(kBaseAddr + kCountersOffset);
}

// Inner-loop scratch. Use a typed cast at the call site — this region
// is intentionally untyped so multiple short-lived hot paths can share
// it without an allocation discipline.
inline void* hotScratch() {
    return reinterpret_cast<void*>(kBaseAddr + kHotScratchOffset);
}

}  // namespace psxsplash::Scratchpad
