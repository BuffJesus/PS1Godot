#pragma once

#include <psyqo/cdrom-device.hh>
#include <psyqo/iso9660-parser.hh>
#include <psyqo/spu.hh>

namespace psxsplash {

// XA-ADPCM streaming backend. Bridges the splashpack v27 XA table
// (per-clip sidecar offset + size in `scene.<n>.xa`) to the PSX
// hardware XA path: psyqo::CDRomDevice → drive auto-streams Form-2
// sectors → SPU CD-input auto-decodes XA → audible. No SPU voice
// allocation needed; the SPU has dedicated XA-decode hardware on
// channel 4 that the drive feeds via IRQ.
//
// Status:
//   - SPU CD-input enable: implemented (mirrors MusicManager::playCDDATrack
//     pattern from musicmanager.cpp:20-24).
//   - LBA computation: implemented. Caller (SceneManager) does an
//     iso9660 lookup at scene boot via PSYQo's parser to find
//     SCENE_<n>.XA's starting LBA, hands the LBA to setSceneXaLba();
//     play() then converts (sidecarOffset / 2048) sectors past that
//     start into an MSF the drive can seek to.
//   - SETMODE 0x24 + SETLOC + READS state machine: TODO. PSYQo's
//     CDRomDevice::Action<S> template is a public interface but
//     extends a private ActionBase, so subclassing it from outside
//     the psyqo library is locked out by C++ access rules. Pick one:
//        a) Patch psyqo (`patches/psyqo-expose-action.diff`) to make
//           ActionBase protected/public, then add an XaPlayAction
//           here mirroring PlayCDDAAction (cdrom-device-cdda.cpp).
//        b) Drop to raw register I/O — wire a Kernel event handler
//           on the CDROM IRQ, pump Hardware::CDRom::Command.send()
//           directly. Skip the Action machinery entirely.
//        c) Vendor a tiny xa_play_action.cpp INTO psyqo's src/ via a
//           build-time copy step. Keeps the patch out of the tree
//           but adds a build-step dependency.
//      Whichever path: the command sequence is SETMODE 0x24 →
//      SETLOC m,s,f → READS, then drive autonomously streams until
//      PAUSE/STOP. ~80-120 lines of state machine code.
class XaAudioBackend {
public:
    XaAudioBackend();

    // Wire up to PSYQo. cdrom may be null on PCdrv builds (LOADER_CDROM
    // undefined); we accept the call so SceneManager wiring is uniform
    // and short-circuit at play() time. spu is always valid.
    bool init(psyqo::CDRomDevice* cdrom, psyqo::SPU* spu);

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
    //   - state machine still TODO (see header docs above)
    bool play(uint32_t sidecarOffset, uint32_t sidecarSize);

    // Halt the XA stream. Idempotent. Mutes SPU CD-input volumes;
    // when the state machine lands it will also send PAUSE/STOP.
    void stop();

    bool isPlaying() const { return mPlaying; }

private:
    psyqo::CDRomDevice* mCDRom = nullptr;
    psyqo::SPU*         mSPU   = nullptr;
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
