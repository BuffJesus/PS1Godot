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
    // PCdrv host-filesystem build: no CD bus, no XA path. Accept the
    // call so SceneManager wiring stays uniform; play() short-circuits
    // with a clear log.
    return false;
#endif
}

void XaAudioBackend::enableSpuCdInput() {
#ifdef LOADER_CDROM
    // Mirrors MusicManager::playCDDATrack (musicmanager.cpp:20-24).
    // SPU_CTRL bit 0 enables CD audio input; SPU_VOL_CD_LEFT / RIGHT
    // are 16-bit volumes (0x7fff = max). Idempotent — only writes
    // when the bit isn't already set. The PSX SPU's XA-decode path
    // shares this mixer with CDDA, so enabling it once covers both.
    if (!(SPU_CTRL & 0x1)) {
        SPU_CTRL |= 0x1;
        SPU_VOL_CD_LEFT  = 0x7fff;
        SPU_VOL_CD_RIGHT = 0x7fff;
    }
#endif
}

void XaAudioBackend::muteSpuCdInput() {
#ifdef LOADER_CDROM
    SPU_VOL_CD_LEFT  = 0;
    SPU_VOL_CD_RIGHT = 0;
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
    if (mSceneXaLba == 0) {
        ramsyscall_printf("[XA] play: no XA file LBA cached for this scene (iso9660 lookup missed?)\n");
        return false;
    }

    // Translate (mSceneXaLba + sidecarOffset) → absolute disc LBA.
    // Sidecar offsets are byte addresses into the .xa sidecar file;
    // ISO 9660 LBA units are 2048-byte sectors. mkpsxiso writes the
    // .xa file as Form-2 sectors but reports the file in 2048 B
    // sector counts via ISO 9660. So sectorsIntoFile = offset / 2048.
    uint32_t sectorsIntoFile = sidecarOffset >> 11;     // /2048
    uint32_t startLba        = mSceneXaLba + sectorsIntoFile;
    uint32_t sectorCount     = (sidecarSize + 2047) >> 11;  // ceil

    // Convert LBA → MSF. PSX disc LBAs start at 0 = MSF(00:02:00); the
    // 150-sector 2-second leadin is part of the addressing convention.
    uint32_t lbaForMsf = startLba + 150;
    uint32_t mins      = lbaForMsf / 75 / 60;
    uint32_t secs      = (lbaForMsf / 75) % 60;
    uint32_t frames    = lbaForMsf % 75;

    ramsyscall_printf("[XA] play: sidecarOffset=%u size=%u -> LBA=%u (MSF %02u:%02u:%02u), %u sectors\n",
                      (unsigned)sidecarOffset, (unsigned)sidecarSize,
                      (unsigned)startLba, (unsigned)mins, (unsigned)secs, (unsigned)frames,
                      (unsigned)sectorCount);

    // SPU side is ready: enable the CD-input mixer so any audio the
    // drive streams will be audible. Safe to call early; at worst we
    // briefly route silence into the mixer until the drive starts.
    enableSpuCdInput();

    // ───────────────────────────────────────────────────────────────
    // TODO state-machine work (see xaaudio.hh header notes for the
    // three architectural options):
    //
    //   1. SETMODE 0x24       — XA on, ignore CD-DA bitstream
    //   2. SETLOC mins,secs,frames — seek target
    //   3. (optional) SETFILTER 0,0 — filter on file=0 channel=0
    //      (matches psxavenc -F 0 -C 0 default)
    //   4. READS              — start continuous read; drive
    //      autonomously streams Form-2 sectors into SPU CD-input
    //      until PAUSE/STOP
    //
    // Each command needs its IRQ acknowledge handled before the next
    // is sent. PSYQo's Action<S> machinery normally handles this but
    // is access-locked from outside the library. Picking one of the
    // three header options unlocks ~80-120 lines of state machine.
    // ───────────────────────────────────────────────────────────────

    mCurrentOffset = sidecarOffset;
    mCurrentSize   = sidecarSize;
    mPlaying = false;  // flip to true once SETMODE/SETLOC/READS land
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
    // Always mute the CD-input mixer first so any in-flight buffered
    // sectors don't continue playing while the command pipeline is
    // shutting down.
    muteSpuCdInput();
    if (!mPlaying) return;
    // TODO once the play state machine lands: send PAUSE (CDL=9) and
    // wait for ack before clearing mPlaying. For now, just flip the
    // flag — drive will keep streaming until you load another scene.
    mPlaying = false;
    ramsyscall_printf("[XA] stop: SPU CD-input muted; PAUSE command TODO when play() state machine lands.\n");
#else
    /* PCdrv build: no CD bus, nothing to stop. */
#endif
}

} // namespace psxsplash
