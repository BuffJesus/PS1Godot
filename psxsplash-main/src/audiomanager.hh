#pragma once

#include <stdint.h>

namespace psxsplash {

static constexpr int MAX_AUDIO_CLIPS = 32;

static constexpr int MAX_VOICES = 24;

static constexpr uint32_t SPU_RAM_START = 0x1010;
static constexpr uint32_t SPU_RAM_END = 0x80000;

static constexpr uint32_t DEFAULT_ADSR = 0x000A000F;

struct AudioClip {
  uint32_t spuAddr;
  uint32_t size;
  uint16_t sampleRate;
  bool loop;
  bool loaded;
};

/// Manages SPU voices and audio clip playback.
///
///   init()
///   loadClip(index, data, size, rate, loop) -> bool
///   play(clipIndex)                         -> channel
///   play(clipIndex, volume, pan)            -> channel
///   stopVoice(channel)
///   stopAll()
///   setVoiceVolume(channel, vol, pan)
///
/// Volume is 0-128 (0=silent, 128=max). Pan is 0-127 (64=center).
class AudioManager {
public:
  /// Initialize SPU hardware and reset state
  void init();

  /// Upload ADPCM data to SPU RAM and register as clip index.
  /// Data must be 16-byte aligned. Returns true on success.
  bool loadClip(int clipIndex, const uint8_t *adpcmData, uint32_t sizeBytes,
                uint16_t sampleRate, bool loop);

  /// Play a clip by index. Returns channel (0-23), or -1 if full.
  /// Volume: 0-128 (128=max). Pan: 0 (left) to 127 (right), 64 = center.
  /// Skips voices [0, m_reservedForMusic) — those belong to the
  /// MusicSequencer.
  int play(int clipIndex, int volume = 128, int pan = 64);

  /// Place a clip onto a specific voice. Used by the MusicSequencer
  /// to retrigger notes on its reserved pool without going through the
  /// generic free-voice search. Returns `voice` on success, -1 on
  /// invalid input. Caller must guarantee the voice is in the
  /// sequencer's reserved range to avoid stomping on dialog/SFX.
  int playOnVoice(int voice, int clipIndex, int volume = 128, int pan = 64);

  /// Reserve the first `n` SPU voices (indices 0..n-1) for the music
  /// sequencer. Subsequent calls to play() will skip these and only
  /// allocate from voices [n, MAX_VOICES). Pass 0 to release the
  /// reservation when music stops. Clamped to [0, MAX_VOICES].
  void reserveVoices(int n);

  /// Number of voices currently reserved for music.
  int reservedVoices() const { return m_reservedForMusic; }

  /// Stop a specific channel
  void stopVoice(int channel);

  /// Stop all playing channels
  void stopAll();

  /// Set volume/pan on a playing channel
  void setVoiceVolume(int channel, int volume, int pan = 64);

  /// Playback length of a loaded clip, expressed in 60 Hz frames.
  /// Returns 0 for unloaded / invalid indices or looped clips (no
  /// natural end). Looped clips return 0 so callers fall back to a
  /// sensible default (they don't auto-stop on their own).
  uint32_t getClipDurationFrames(int clipIndex) const;

  /// Get total SPU RAM used by loaded clips
  uint32_t getUsedSPURam() const { return m_nextAddr - SPU_RAM_START; }

  /// Get total SPU RAM available
  uint32_t getTotalSPURam() const { return SPU_RAM_END - SPU_RAM_START; }

  /// Get number of loaded clips
  int getLoadedClipCount() const;

  /// Reset all clips and free SPU RAM
  void reset();

private:
  /// Convert 0-128 volume to hardware 0-0x3FFF (fixed-volume mode)
  static uint16_t volToHw(int v);

  // Internal: shared write-to-voice path used by both play() (after it
  // resolves a free voice) and playOnVoice() (caller-supplied voice).
  void writeVoice(int voice, const AudioClip &clip, int volume, int pan);

  AudioClip m_clips[MAX_AUDIO_CLIPS];
  uint32_t m_nextAddr = SPU_RAM_START;
  // Voices [0, m_reservedForMusic) are not allocated by play().
  // MusicSequencer owns them while it's active.
  int m_reservedForMusic = 0;
};

} // namespace psxsplash
