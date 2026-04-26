#pragma once

#include <psyqo/cdrom-device.hh>
#include <psyqo/iso9660-parser.hh>

namespace psxsplash {

// XA-ADPCM streaming backend. Bridges the splashpack v27 XA table
// (per-clip sidecar offset + size in `scene.<n>.xa`) to the PSX
// hardware XA path: psyqo::CDRomDevice → drive auto-streams Form-2
// sectors → SPU CD-input auto-decodes XA → audible. No SPU voice
// allocation needed; the SPU has dedicated XA-decode hardware on
// channel 4 that the drive feeds via IRQ.
//
// Pipeline:
//   - SPU CD-input enable mirrors MusicManager::playCDDATrack
//     (musicmanager.cpp:20-24).
//   - SceneManager looks up SCENE_<n>.XA via psyqo::ISO9660Parser at
//     scene boot and calls setSceneXaLba(); play() converts
//     (sidecarOffset / 2048) sectors past that start into an MSF.
//   - Drive command sequence (SETMODE 0x24 → SETLOC m,s,f → READS)
//     is driven by an XaPlayAction inside xaaudio.cpp's anon namespace,
//     subclassing psyqo::CDRomDevice::Action<S>. C++ access rules let
//     this work from outside the psyqo library — only ActionBase is
//     private; Action<S> itself is public, and the implementation only
//     touches public/protected members (registerMe, setState,
//     setCallback, queueCallbackFromISR). After READS is ack'd the
//     drive streams Form-2 sectors autonomously and the SPU's XA-decode
//     hardware (channel 4) picks them off the CD-input bus.
class XaAudioBackend {
public:
    XaAudioBackend();

    // Wire up to PSYQo. cdrom may be null on PCdrv builds (LOADER_CDROM
    // undefined); we accept the call so SceneManager wiring is uniform
    // and short-circuit at play() time. We don't take a psyqo::SPU here
    // because the XA path only touches the raw SPU_CTRL/SPU_VOL_CD_*
    // registers — psyqo::SPU is a voice/RAM allocator we have no need
    // for on the dedicated XA hardware channel.
    bool init(psyqo::CDRomDevice* cdrom);

    // Called by SceneManager after iso9660 lookup of the per-scene
    // SCENE_<n>.XA file. lba is in 2048-byte ISO sector units. 0 means
    // "no XA file on this scene's disc layout"; play() short-circuits.
    void setSceneXaLba(uint32_t lba) { mSceneXaLba = lba; }

    // Start streaming an XA payload at byte offset `sidecarOffset` into
    // the per-scene XA file. The sidecar offset is converted to an
    // absolute disc LBA via mSceneXaLba + offset/2048; that LBA is the
    // start of a Form-2 sector run that decodes natively in the SPU
    // XA voice.
    //
    // Returns true if playback was started, false on:
    //   - LOADER_CDROM not defined (PCdrv build)
    //   - cdrom not initialized
    //   - mSceneXaLba == 0 (no XA file on disc for this scene)
    //   - already playing (call stop() first)
    bool play(uint32_t sidecarOffset, uint32_t sidecarSize);

    // Halt the XA stream. Idempotent. Mutes SPU CD-input volumes and
    // sends PAUSE if streaming.
    void stop();

    bool isPlaying() const { return mPlaying; }

private:
    psyqo::CDRomDevice* mCDRom = nullptr;
    uint32_t            mSceneXaLba = 0;     // ISO LBA of SCENE_<n>.XA, 0 = none
    bool                mPlaying = false;
    uint32_t            mCurrentOffset = 0;
    uint32_t            mCurrentSize   = 0;

    // Enable SPU CD-input mixing at full volume. Idempotent — checks
    // SPU_CTRL bit 0 first. The drive's XA-ADPCM stream lands on this
    // mixer path automatically; without these volumes set, the SPU
    // throws the audio away.
    void enableSpuCdInput();
    void muteSpuCdInput();
};

} // namespace psxsplash
