#include "xaaudio.hh"

#include <psyqo/xprintf.h>
#include <common/hardware/spu.h>

namespace psxsplash {

XaAudioBackend::XaAudioBackend() = default;

bool XaAudioBackend::init(psyqo::CDRomDevice* cdrom, psyqo::SPU* spu) {
#ifdef LOADER_CDROM
    mCDRom = cdrom;
    mSPU   = spu;
    return cdrom != nullptr && spu != nullptr;
#else
    (void)cdrom;
    (void)spu;
    // PCdrv host filesystem build: no real CD-ROM, no XA path. We still
    // accept the call so SceneManager wiring is uniform — every play()
    // will short-circuit with a clear log.
    return false;
#endif
}

bool XaAudioBackend::play(uint32_t sidecarOffset, uint32_t sidecarSize) {
#ifdef LOADER_CDROM
    if (mCDRom == nullptr || mSPU == nullptr) {
        ramsyscall_printf("[XA] play: backend not init'd (cdrom=%p spu=%p)\n", mCDRom, mSPU);
        return false;
    }
    if (mPlaying) {
        ramsyscall_printf("[XA] play: already playing; call stop() first\n");
        return false;
    }

    // ──────────────────────────────────────────────────────────────────
    // TODO Phase 3 finish line — actual XA streaming. Pieces:
    //
    //   1. SETMODE 0x24 (XA + ignore CD-DA) via cdrom.test() with raw
    //      `Hardware::CDRom::CDRomCommandBuffer`. PSYQo doesn't wrap
    //      this; build the buffer manually.
    //
    //   2. SETFILTER (optional) if we use multiple XA file/channel
    //      combinations. Defaults (file=0 channel=0) match the
    //      psxavenc -F 0 -C 0 we emit on the build side.
    //
    //   3. Translate `sidecarOffset` into a disc LBA. mkpsxiso writes
    //      the .xa file at a known LBA (recorded in iso9660 entry).
    //      We probably need either:
    //        a) at boot, parse iso9660 root dir → look up "scene_<n>.xa"
    //           → record start LBA. Add to SceneManager state.
    //        b) burn the LBA into the splashpack at ISO build time
    //           (mkpsxiso → patch the .splashpack post-build).
    //      Option (b) is faster runtime but couples build steps.
    //      Pick whichever is cleaner once mkpsxiso config lands.
    //
    //   4. SPU XA voice setup: enable SPU CD-input (SPU_VOL_CD_LEFT /
    //      RIGHT to non-zero), assert SPU XA mode bit. PSYQo's spu.hh
    //      doesn't expose this — write SPU registers directly via
    //      common/hardware/spu.h or memory-mapped register pokes.
    //
    //   5. Spawn a coroutine reader: loop `co_await
    //      cdrom.readSectors(currentLBA, batch, sectorBuf)` and feed
    //      sectors to SPU as long as mPlaying is true. PSX hardware
    //      auto-decodes XA from CD into voice 4; the CPU just keeps
    //      sectors flowing.
    //
    //   6. Stop condition: sectorsRead >= sidecarSize/2336 (Form-2
    //      payload size), or external stop() call.
    // ──────────────────────────────────────────────────────────────────

    ramsyscall_printf("[XA] play scaffold: offset=%u size=%u — backend not yet wired (Phase 3 finish).\n",
                      (unsigned)sidecarOffset, (unsigned)sidecarSize);
    mCurrentOffset = sidecarOffset;
    mCurrentSize   = sidecarSize;
    mPlaying = false;  // flip to true once the actual stream starts
    return false;
#else
    (void)sidecarOffset;
    (void)sidecarSize;
    ramsyscall_printf("[XA] play: PCdrv build; XA needs LOADER_CDROM (real ISO).\n");
    return false;
#endif
}

void XaAudioBackend::stop() {
#ifdef LOADER_CDROM
    if (!mPlaying) return;
    // TODO: cancel the reader coroutine, send STOP/PAUSE to drive,
    // mute SPU CD-input, free any DMA buffers. Mirror MusicManager's
    // stopCDDA pattern.
    mPlaying = false;
    ramsyscall_printf("[XA] stop scaffold — reader cancellation TODO.\n");
#else
    /* PCdrv build: nothing to stop. */
#endif
}

} // namespace psxsplash
