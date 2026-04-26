#pragma once

#include <psyqo/cdrom-device.hh>
#include <psyqo/spu.hh>

namespace psxsplash {

// XA-ADPCM streaming backend. Bridges the splashpack v27 XA table
// (per-clip sidecar offset + size in scene.<n>.xa) to PSX hardware
// XA-ADPCM playback via psyqo::CDRomDevice + the SPU XA voice path.
//
// Status: SCAFFOLD. The class API is stable and the SceneManager wires
// it through, so Audio.PlayMusic XA dispatch can call into here. The
// actual SETMODE + sector reader coroutine + SPU DMA feeder is marked
// TODO in the .cpp — see the function bodies and ROADMAP Phase 3.
//
// Why scaffold-only: PSYQo gives us cdrom.test() (raw command buffer),
// CDRomDevice::readSectors() (callback / Task / coroutine flavors),
// and SPU::dmaWrite(), but no XA-specific helpers. The SETMODE 0x24
// flag, Form-2 sector handling (2336 B vs 2048 B), and SPU XA voice
// (channel 4) register pokes have to be wired here. Estimated work
// ~150-300 lines once the tooling lets us actually test on a real ISO
// in PCSX-Redux full-CD mode (PCdrv is XA-blind).
//
// Lifetime + ownership: SceneManager owns one instance, calls init()
// once at boot. The CDRomDevice* is borrowed (psxsplash main owns it
// alongside MusicManager). On PCdrv builds (LOADER_CDROM undefined)
// every method is a no-op log — XA needs the CD bus.
class XaAudioBackend {
public:
    XaAudioBackend();

    // Bind to the project's CDRomDevice + SPU. Idempotent; safe to call
    // before main scene init. Returns false on PCdrv builds where the
    // backend can't function.
    bool init(psyqo::CDRomDevice* cdrom, psyqo::SPU* spu);

    // Start streaming an XA payload at (LBA-equivalent offset) +
    // length. The sidecar byte offset comes from the splashpack v27 XA
    // table; the conversion to disc LBA happens at ISO build time
    // (mkpsxiso). For now we accept the byte offset/size and treat it
    // as a placeholder — the real ISO layout will replace this with a
    // start-LBA + sector-count pair.
    //
    // Returns true if playback started, false on:
    //   - LOADER_CDROM not defined (PCdrv build)
    //   - cdrom not initialized
    //   - already playing (call stop() first)
    bool play(uint32_t sidecarOffset, uint32_t sidecarSize);

    // Halt the XA stream. Idempotent.
    void stop();

    bool isPlaying() const { return mPlaying; }

private:
    psyqo::CDRomDevice* mCDRom = nullptr;
    psyqo::SPU*         mSPU   = nullptr;
    bool                mPlaying = false;
    uint32_t            mCurrentOffset = 0;
    uint32_t            mCurrentSize   = 0;
};

} // namespace psxsplash
