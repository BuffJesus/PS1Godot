#include "audiomanager.hh"

#include "common/hardware/dma.h"
#include "common/hardware/spu.h"
#include <psyqo/kernel.hh>
#include <psyqo/spu.hh>
#include <psyqo/xprintf.h>

namespace psxsplash {

uint16_t AudioManager::volToHw(int v) {
  if (v <= 0)
    return 0;
  if (v >= 128)
    return 0x3fff;
  return static_cast<uint16_t>((v * 0x3fff) / 128);
}

void AudioManager::init() {
  psyqo::SPU::initialize();

  m_nextAddr = SPU_RAM_START;

  for (int i = 0; i < MAX_AUDIO_CLIPS; i++) {
    m_clips[i].loaded = false;
  }
  // Phase 4: clear the voice-allocation table. Every voice starts Free
  // with priority 0 — first allocation will pick voice 0 in the SFX
  // pool, identical to the pre-Phase-4 behaviour for empty scenes.
  for (int i = 0; i < MAX_VOICES; i++) {
    m_voices[i] = {};
  }
  m_voiceAllocCounter = 0;
}

uint32_t AudioManager::getClipDurationFrames(int clipIndex) const {
  if (clipIndex < 0 || clipIndex >= MAX_AUDIO_CLIPS) return 0;
  const AudioClip &c = m_clips[clipIndex];
  if (!c.loaded || c.loop || c.sampleRate == 0 || c.size == 0) return 0;
  // ADPCM: 16 bytes per block encodes 28 samples.
  uint32_t samples = (c.size / 16u) * 28u;
  // frames = samples / sampleRate * 60.  Reorder to avoid losing
  // precision on short clips: (samples * 60) / sampleRate.
  return (samples * 60u) / c.sampleRate;
}

void AudioManager::reset() {
  stopAll();  // also clears m_voices[]
  for (int i = 0; i < MAX_AUDIO_CLIPS; i++) {
    m_clips[i].loaded = false;
  }
  m_nextAddr = SPU_RAM_START;
  m_voiceAllocCounter = 0;
}

bool AudioManager::loadClip(int clipIndex, const uint8_t *adpcmData,
                            uint32_t sizeBytes, uint16_t sampleRate,
                            bool loop) {
  if (clipIndex < 0 || clipIndex >= MAX_AUDIO_CLIPS)
    return false;
  if (!adpcmData || sizeBytes == 0)
    return false;

  // check for and skip VAG header if present
  if (sizeBytes >= 48) {
    const char *magic = reinterpret_cast<const char *>(adpcmData);
    if (magic[0] == 'V' && magic[1] == 'A' && magic[2] == 'G' &&
        magic[3] == 'p') {
      adpcmData += 48;
      sizeBytes -= 48;
    }
  }

  uint32_t addr = (m_nextAddr + 15) & ~15u;
  uint32_t alignedSize = (sizeBytes + 15) & ~15u;

  if (addr + alignedSize > SPU_RAM_END) {
    return false;
  }

  const uint8_t *src = adpcmData;
  uint32_t remaining = alignedSize;
  uint32_t dstAddr = addr;
  while (remaining > 0) {
    uint32_t bytesThisRound = (remaining > 65520u) ? 65520u : remaining;
    bytesThisRound &= ~15u; // 16-byte block alignment
    if (bytesThisRound == 0)
      break;

    uint16_t dmaSizeParam = (uint16_t)(bytesThisRound / 4);
    psyqo::SPU::dmaWrite(dstAddr, src, dmaSizeParam, 4);

    while (DMA_CTRL[DMA_SPU].CHCR & (1 << 24)) {
    }

    src += bytesThisRound;
    dstAddr += bytesThisRound;
    remaining -= bytesThisRound;
  }

  SPU_CTRL &= ~(0b11 << 4);

  m_clips[clipIndex].spuAddr = addr;
  m_clips[clipIndex].size = sizeBytes;
  m_clips[clipIndex].sampleRate = sampleRate;
  m_clips[clipIndex].loop = loop;
  m_clips[clipIndex].loaded = true;

  m_nextAddr = addr + alignedSize;
  return true;
}

int AudioManager::play(int clipIndex, int volume, int pan, uint8_t priority) {
  if (clipIndex < 0 || clipIndex >= MAX_AUDIO_CLIPS ||
      !m_clips[clipIndex].loaded) {
    return -1;
  }

  int ch = allocateVoice(VoiceOwner::SFX, priority);
  if (ch < 0) return -1;

  writeVoice(ch, m_clips[clipIndex], volume, pan);
  return ch;
}

int AudioManager::allocateVoice(VoiceOwner owner, uint8_t priority) {
  int start = m_reservedForMusic;
  if (start < 0) start = 0;
  if (start >= MAX_VOICES) return -1;

  // Pass 1: Free voice in the pool — instant reuse, no eviction.
  for (int v = start; v < MAX_VOICES; v++) {
    if (m_voices[v].owner == VoiceOwner::Free) {
      m_voices[v].owner    = owner;
      m_voices[v].priority = priority;
      m_voices[v].allocTick = ++m_voiceAllocCounter;
      m_voices[v].released = false;
      return v;
    }
  }

  // Pass 2: Released voice — key-off issued, envelope decaying. The
  // existing sample is on its way out; replacing it is inaudible (or
  // close to it) and far better than dropping the new sound.
  for (int v = start; v < MAX_VOICES; v++) {
    if (m_voices[v].released) {
      m_voices[v].owner    = owner;
      m_voices[v].priority = priority;
      m_voices[v].allocTick = ++m_voiceAllocCounter;
      m_voices[v].released = false;
      return v;
    }
  }

  // Pass 3: Steal the lowest-priority active voice IF its priority is
  // strictly less than ours. Tie-break by oldest allocTick. Equal-or-
  // higher priority voices never get evicted — important sounds stay.
  int   stealIdx  = -1;
  uint8_t stealPri = priority;  // must beat strictly
  uint32_t stealAge = 0;
  for (int v = start; v < MAX_VOICES; v++) {
    if (m_voices[v].owner == VoiceOwner::Free) continue;  // (already handled)
    if (m_voices[v].priority < stealPri) {
      stealIdx = v;
      stealPri = m_voices[v].priority;
      stealAge = m_voices[v].allocTick;
    } else if (m_voices[v].priority == stealPri && stealIdx >= 0
               && m_voices[v].allocTick < stealAge) {
      // Same lowest-prio so far, but this one is older — prefer it.
      stealIdx = v;
      stealAge = m_voices[v].allocTick;
    }
  }
  if (stealIdx >= 0) {
    // Hard cut on the stolen voice so the new note retriggers cleanly.
    psyqo::SPU::silenceChannels(1u << stealIdx);
    m_voices[stealIdx].owner    = owner;
    m_voices[stealIdx].priority = priority;
    m_voices[stealIdx].allocTick = ++m_voiceAllocCounter;
    m_voices[stealIdx].released = false;
    return stealIdx;
  }

  // Pass 4: pool exhausted, every voice is >= our priority. Drop.
  return -1;
}

int AudioManager::playOnVoice(int voice, int clipIndex, int volume, int pan) {
  if (voice < 0 || voice >= MAX_VOICES) return -1;
  if (clipIndex < 0 || clipIndex >= MAX_AUDIO_CLIPS ||
      !m_clips[clipIndex].loaded) {
    return -1;
  }
  writeVoice(voice, m_clips[clipIndex], volume, pan);
  // Phase 4: explicit-voice claim is always Music (only MusicSequencer
  // calls this). Pin priority at MUSIC_PRIORITY so allocateVoice never
  // steals music slots even if SFX prio creeps up via Lua-set values.
  m_voices[voice].owner    = VoiceOwner::Music;
  m_voices[voice].priority = MUSIC_PRIORITY;
  m_voices[voice].allocTick = ++m_voiceAllocCounter;
  m_voices[voice].released = false;
  return voice;
}

void AudioManager::reserveVoices(int n) {
  if (n < 0) n = 0;
  if (n > MAX_VOICES) n = MAX_VOICES;
  // Shrinking the reservation: voices that drop out of the music pool
  // become available to allocateVoice again. Mark them Free so the
  // next play() can reuse them. Voices currently in use as SFX in the
  // newly-music range stay where they are — playOnVoice will overwrite
  // them when the sequencer claims a slot, which matches the pre-
  // Phase-4 "music key-cycles SFX" behaviour.
  if (n < m_reservedForMusic) {
    for (int v = n; v < m_reservedForMusic; v++) {
      if (m_voices[v].owner == VoiceOwner::Music) {
        m_voices[v] = {};
      }
    }
  }
  m_reservedForMusic = n;
}

void AudioManager::writeVoice(int ch, const AudioClip &clip, int volume,
                              int pan) {
  uint16_t vol = volToHw(volume);
  uint16_t leftVol = vol;
  uint16_t rightVol = vol;
  if (pan != 64) {
    int p = pan < 0 ? 0 : (pan > 127 ? 127 : pan);
    leftVol = (uint16_t)((uint32_t)vol * (127 - p) / 127);
    rightVol = (uint16_t)((uint32_t)vol * p / 127);
  }

  constexpr uint16_t DUMMY_SPU_ADDR = 0x1000;
  if (clip.loop) {
    SPU_VOICES[ch].sampleRepeatAddr = static_cast<uint16_t>(clip.spuAddr / 8);
  } else {
    SPU_VOICES[ch].sampleRepeatAddr = DUMMY_SPU_ADDR / 8;
  }

  psyqo::SPU::ChannelPlaybackConfig config;
  config.sampleRate.value =
      static_cast<uint16_t>(((uint32_t)clip.sampleRate << 12) / 44100);
  config.volumeLeft = leftVol;
  config.volumeRight = rightVol;
  config.adsr = DEFAULT_ADSR;

  if (ch > 15) {
    SPU_KEY_OFF_HIGH = 1 << (ch - 16);
  } else {
    SPU_KEY_OFF_LOW = 1 << ch;
  }

  SPU_VOICES[ch].volumeLeft = config.volumeLeft;
  SPU_VOICES[ch].volumeRight = config.volumeRight;
  SPU_VOICES[ch].sampleRate = config.sampleRate.value;
  SPU_VOICES[ch].sampleStartAddr = static_cast<uint16_t>(clip.spuAddr / 8);
  SPU_VOICES[ch].ad = config.adsr & 0xFFFF;
  SPU_VOICES[ch].sr = (config.adsr >> 16) & 0xFFFF;

  if (ch > 15) {
    SPU_KEY_ON_HIGH = 1 << (ch - 16);
  } else {
    SPU_KEY_ON_LOW = 1 << ch;
  }
}

void AudioManager::stopVoice(int channel) {
  if (channel < 0 || channel >= MAX_VOICES)
    return;
  psyqo::SPU::silenceChannels(1u << channel);
  // Phase 4: mark released so allocateVoice prefers this slot for the
  // next reuse pass. The hardware envelope is decaying; stomping the
  // slot with a new note here is inaudible.
  m_voices[channel].released = true;
}

void AudioManager::stopAll() {
  psyqo::SPU::silenceChannels(0x00FFFFFFu);
  for (int i = 0; i < MAX_VOICES; i++) {
    m_voices[i] = {};
  }
}

void AudioManager::setVoiceVolume(int channel, int volume, int pan) {
  if (channel < 0 || channel >= MAX_VOICES)
    return;
  uint16_t vol = volToHw(volume);
  if (pan == 64) {
    SPU_VOICES[channel].volumeLeft = vol;
    SPU_VOICES[channel].volumeRight = vol;
  } else {
    int p = pan < 0 ? 0 : (pan > 127 ? 127 : pan);
    SPU_VOICES[channel].volumeLeft =
        (uint16_t)((uint32_t)vol * (127 - p) / 127);
    SPU_VOICES[channel].volumeRight = (uint16_t)((uint32_t)vol * p / 127);
  }
}

int AudioManager::getLoadedClipCount() const {
  int count = 0;
  for (int i = 0; i < MAX_AUDIO_CLIPS; i++) {
    if (m_clips[i].loaded)
      count++;
  }
  return count;
}

} // namespace psxsplash
