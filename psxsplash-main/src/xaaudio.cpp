#include "xaaudio.hh"

#if defined(LOADER_CDROM)
#include "cdromhelper.hh"
#endif

#include <psyqo/cdrom-commandbuffer.hh>
#include <psyqo/hardware/cdrom.hh>
#include <psyqo/kernel.hh>
#include <psyqo/xprintf.h>
#include <EASTL/atomic.h>

// common/hardware/spu.h is a C header that #defines names colliding
// with psyqo's C++ Register<> templates (e.g. DPCR/DICR). Include it
// last so the C++ headers above resolve their symbols first.
#include <common/hardware/spu.h>

namespace {

// XA streaming state machine. Mirrors the PlayCDDAAction pattern in
// psyqo/src/cdrom-device-cdda.cpp but with SETMODE 0x24 (XA + ignore
// CD-DA) and READS instead of CDDA's SETMODE 0x01 + PLAY.
//
// Once READS is acknowledged the drive autonomously streams Form-2
// sectors at the disc's native rate; the SPU's XA-decode hardware
// (channel 4) picks them off the CD-input bus and plays them. The
// only thing the CPU does after that is wait for the drive to hit
// EOF or for our stop() to send PAUSE.
//
// State enum values must be unique within the CDRomDevice (per
// PlayCDDAActionState's leading comment).
enum class XaActionState : uint8_t {
    IDLE = 0,
    SETMODE = 110,
    SETLOC,
    READS,
    STREAMING,
    STOPPING,
    STOPPING_ACK,
};

class XaPlayAction : public psyqo::CDRomDevice::Action<XaActionState> {
  public:
    XaPlayAction() : Action("XaPlayAction") {}

    void start(psyqo::CDRomDevice* device, psyqo::MSF msf,
               eastl::function<void(bool)>&& callback) {
        psyqo::Kernel::assert(device->isIdle(),
                              "XaPlayAction::start() called while another action is in progress");
        registerMe(device);
        setCallback(eastl::move(callback));
        m_msf = msf;
        setState(XaActionState::SETMODE);
        eastl::atomic_signal_fence(eastl::memory_order_release);
        // SETMODE 0x24:
        //   bit 2 (0x04) = XA-ADPCM enable
        //   bit 5 (0x20) = ignore-CD-DA bitstream (drive emits Form-2
        //                  data instead of audio CDDA on data tracks)
        psyqo::Hardware::CDRom::Command.send(
            psyqo::Hardware::CDRom::CDL::SETMODE, 0x24);
    }

    bool acknowledge(const psyqo::CDRomDevice::Response&) override {
        switch (getState()) {
            case XaActionState::SETMODE:
                setState(XaActionState::SETLOC);
                psyqo::Hardware::CDRom::Command.send(
                    psyqo::Hardware::CDRom::CDL::SETLOC,
                    psyqo::itob(m_msf.m),
                    psyqo::itob(m_msf.s),
                    psyqo::itob(m_msf.f));
                return false;
            case XaActionState::SETLOC:
                setState(XaActionState::READS);
                psyqo::Hardware::CDRom::Command.send(
                    psyqo::Hardware::CDRom::CDL::READS);
                return false;
            case XaActionState::READS:
                // Drive accepted READS — XA streaming has begun. The
                // SPU XA hardware takes it from here. Resolve the
                // caller's "did playback start" promise and stay in
                // STREAMING until either the drive ends or stop() is
                // called.
                setState(XaActionState::STREAMING);
                queueCallbackFromISR(true);
                return false;
            case XaActionState::STREAMING:
                // Periodic ack frames during streaming — drive sends
                // these to update playback location. We don't surface
                // it to Lua yet (mirroring how MusicManager only
                // surfaces tellCDDA on demand), so just absorb.
                return false;
            case XaActionState::STOPPING:
                setState(XaActionState::STOPPING_ACK);
                return false;
            default:
                psyqo::Kernel::abort("XaPlayAction got CDROM ack in wrong state");
                return false;
        }
    }

    bool complete(const psyqo::CDRomDevice::Response&) override {
        if (getState() == XaActionState::STOPPING_ACK) {
            setSuccess(true);
            return true;
        }
        // Other completes ignored — READS doesn't really "complete"
        // until either EOF (handled by end()) or PAUSE.
        return false;
    }

    bool end(const psyqo::CDRomDevice::Response&) override {
        // Drive reached the end of the readable region. STREAMING
        // transitions to "done"; the caller's start() callback was
        // already fired with success=true, so we just mark the action
        // resolved.
        if (getState() == XaActionState::STOPPING) return false;
        setSuccess(true);
        return true;
    }

    // Send PAUSE to halt the stream. Caller has already verified the
    // action is the active one (m_device == this action's device).
    void stop() {
        if (getState() == XaActionState::STREAMING) {
            setState(XaActionState::STOPPING);
            psyqo::Hardware::CDRom::Command.send(psyqo::Hardware::CDRom::CDL::PAUSE);
        }
    }

    psyqo::MSF m_msf;
};

XaPlayAction s_xaPlayAction;

}  // namespace

namespace psxsplash {

XaAudioBackend::XaAudioBackend() = default;

bool XaAudioBackend::init(psyqo::CDRomDevice* cdrom) {
#ifdef LOADER_CDROM
    mCDRom = cdrom;
    return cdrom != nullptr;
#else
    (void)cdrom;
    return false;
#endif
}

void XaAudioBackend::enableSpuCdInput() {
#ifdef LOADER_CDROM
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
    if (mCDRom == nullptr) {
        ramsyscall_printf("[XA] play: backend not init'd\n");
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
    if (!mCDRom->isIdle()) {
        ramsyscall_printf("[XA] play: CDRom busy with another action; try again later\n");
        return false;
    }

    // sidecarOffset / 2048 = sectors past the file start. mkpsxiso
    // writes the .xa file as Form-2 sectors but ISO 9660 reports the
    // file in 2048 B sector counts, so 2048 is the right divisor here.
    uint32_t sectorsIntoFile = sidecarOffset >> 11;
    uint32_t startLba        = mSceneXaLba + sectorsIntoFile;
    uint32_t sectorCount     = (sidecarSize + 2047) >> 11;

    // LBA → MSF. PSX disc addressing prepends 150 sectors of leadin
    // (2 seconds * 75 sectors/sec) so MSF is computed on (LBA + 150).
    uint32_t lbaForMsf = startLba + 150;
    psyqo::MSF msf;
    msf.m = (uint8_t)(lbaForMsf / 75 / 60);
    msf.s = (uint8_t)((lbaForMsf / 75) % 60);
    msf.f = (uint8_t)(lbaForMsf % 75);

    ramsyscall_printf("[XA] play: sidecarOffset=%u size=%u -> LBA=%u (MSF %02u:%02u:%02u), %u sectors\n",
                      (unsigned)sidecarOffset, (unsigned)sidecarSize,
                      (unsigned)startLba, (unsigned)msf.m, (unsigned)msf.s, (unsigned)msf.f,
                      (unsigned)sectorCount);

    // SceneManager::loadScene puts the drive to sleep with SilenceDrive
    // (CD IRQ masked) once the scene's data files are read. Mirror
    // MusicManager::playCDDATrack and re-arm the IRQ before sending
    // the SETMODE/SETLOC/READS command sequence.
    CDRomHelper::WakeDrive();
    enableSpuCdInput();

    mPlaying = true;
    mCurrentOffset = sidecarOffset;
    mCurrentSize   = sidecarSize;
    s_xaPlayAction.start(mCDRom, msf, [this](bool success) {
        if (!success) {
            ramsyscall_printf("[XA] play: drive command sequence failed\n");
            mPlaying = false;
            muteSpuCdInput();
        } else {
            ramsyscall_printf("[XA] play: drive streaming started\n");
        }
    });
    return true;
#else
    (void)sidecarOffset;
    (void)sidecarSize;
    ramsyscall_printf("[XA] play: PCdrv build; XA needs LOADER_CDROM (real ISO).\n");
    return false;
#endif
}

void XaAudioBackend::stop() {
#ifdef LOADER_CDROM
    muteSpuCdInput();
    if (!mPlaying) return;
    s_xaPlayAction.stop();
    mPlaying = false;
    ramsyscall_printf("[XA] stop: SPU CD-input muted; PAUSE sent.\n");
#endif
}

} // namespace psxsplash
