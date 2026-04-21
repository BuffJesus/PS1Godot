#!/usr/bin/env python3
"""Convert the real Reaper sample bank (Mitch's Music Kit) used by
RetroAdventureSong.rpp into PSX-friendly WAVs that fit the SPU budget.

For each source: read with stdlib wave, downmix stereo->mono if needed,
resample to TARGET_SR (22050 Hz) via simple linear interpolation,
trim to MAX_DURATION, write 16-bit PCM mono WAV.

Output names match the AudioClip names already wired in demo.tscn:
  inst_kick / inst_snare / inst_hat / inst_bass / inst_lead.
"""
import math, os, struct, sys, wave

TARGET_SR = 22050
SAMPLES = [
    # (output_name, source_path, max_duration_seconds, gain_factor)
    ('inst_kick.wav',
     "D:/Documents/Reaper/Instruments/Mitch's Music Kit/Mitch's Music Kit/Samples/Drums/Electronic Kit/Kick_3.wav",
     0.45, 1.0),
    ('inst_snare.wav',
     "D:/Documents/Reaper/Instruments/Mitch's Music Kit/Mitch's Music Kit/Samples/Drums/Electronic Kit/Snare_3.wav",
     0.35, 1.0),
    ('inst_hat.wav',
     "D:/Documents/Reaper/Instruments/Mitch's Music Kit/Mitch's Music Kit/Samples/Drums/Electronic Kit/HiHat_4.wav",
     0.20, 1.0),
    ('inst_bass.wav',
     "D:/Documents/Reaper/Instruments/Mitch's Music Kit/Mitch's Music Kit/Samples/Synth Bass/Retro Bass.wav",
     0.60, 1.0),
    ('inst_lead.wav',
     "D:/Documents/Reaper/Instruments/Mitch's Music Kit/Mitch's Music Kit/Samples/Synth Lead/Brass Synth.wav",
     0.50, 1.0),
]

def read_wav_to_mono_int16(path):
    """Returns (samples_int16_list, source_sample_rate). Handles
    8/16/24/32-bit PCM and 32-bit IEEE float, mono/stereo input —
    includes a manual RIFF parser because Python's stdlib `wave`
    module rejects format-tag 3 (IEEE float) outright."""
    with open(path, 'rb') as f:
        data = f.read()
    if data[:4] != b'RIFF' or data[8:12] != b'WAVE':
        raise ValueError(f'{path}: not a RIFF/WAVE file')
    pos = 12
    fmt_tag = nch = sr = bits = None
    pcm_data = None
    while pos < len(data):
        chunk_id = data[pos:pos+4]
        chunk_size = struct.unpack('<I', data[pos+4:pos+8])[0]
        body = data[pos+8:pos+8+chunk_size]
        if chunk_id == b'fmt ':
            fmt_tag, nch, sr, _byterate, _block, bits = struct.unpack('<HHIIHH', body[:16])
        elif chunk_id == b'data':
            pcm_data = body
        pos += 8 + chunk_size + (chunk_size & 1)  # chunks are word-aligned
    if fmt_tag is None or pcm_data is None:
        raise ValueError(f'{path}: missing fmt or data chunk')

    sw = bits // 8
    if fmt_tag == 1:  # PCM int
        if sw == 1:
            decoded = [(b - 128) * 256 for b in pcm_data]
        elif sw == 2:
            decoded = list(struct.unpack('<' + 'h' * (len(pcm_data) // 2), pcm_data))
        elif sw == 3:
            decoded = []
            for i in range(0, len(pcm_data), 3):
                v = pcm_data[i] | (pcm_data[i+1] << 8) | (pcm_data[i+2] << 16)
                if v & 0x800000: v -= 0x1000000
                decoded.append(v >> 8)
        elif sw == 4:
            decoded = list(struct.unpack('<' + 'i' * (len(pcm_data) // 4), pcm_data))
            decoded = [v >> 16 for v in decoded]
        else:
            raise ValueError(f'{path}: unsupported PCM bit depth {bits}')
    elif fmt_tag == 3:  # IEEE float
        if sw == 4:
            floats = struct.unpack('<' + 'f' * (len(pcm_data) // 4), pcm_data)
            decoded = [int(max(-1.0, min(1.0, v)) * 32767) for v in floats]
        elif sw == 8:
            doubles = struct.unpack('<' + 'd' * (len(pcm_data) // 8), pcm_data)
            decoded = [int(max(-1.0, min(1.0, v)) * 32767) for v in doubles]
        else:
            raise ValueError(f'{path}: unsupported float bit depth {bits}')
    else:
        raise ValueError(f'{path}: unsupported WAV format tag {fmt_tag}')

    if nch == 1:
        return decoded, sr
    if nch == 2:
        return [(decoded[i] + decoded[i+1]) // 2 for i in range(0, len(decoded), 2)], sr
    return decoded[::nch], sr

def linear_resample(samples, src_sr, dst_sr):
    if src_sr == dst_sr:
        return samples[:]
    ratio = src_sr / dst_sr
    n_out = int(len(samples) / ratio)
    out = [0] * n_out
    for i in range(n_out):
        src_pos = i * ratio
        idx = int(src_pos)
        frac = src_pos - idx
        if idx + 1 < len(samples):
            out[i] = int(samples[idx] * (1 - frac) + samples[idx + 1] * frac)
        elif idx < len(samples):
            out[i] = samples[idx]
    return out

def write_wav(path, samples, sr):
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        clamped = bytes()
        buf = bytearray()
        for s in samples:
            if s > 32767: s = 32767
            elif s < -32767: s = -32767
            buf.extend(struct.pack('<h', s))
        w.writeframes(bytes(buf))

def fade_out(samples, fade_samples):
    """Apply a linear fade-out at the end so trimmed samples don't pop."""
    n = len(samples)
    fade_samples = min(fade_samples, n)
    for i in range(fade_samples):
        gain = 1.0 - (i / fade_samples)
        idx = n - fade_samples + i
        samples[idx] = int(samples[idx] * gain)
    return samples

def convert_one(out_name, src_path, max_dur, gain_factor, out_dir):
    if not os.path.exists(src_path):
        print(f'  ! {out_name}: source missing — {src_path}')
        return
    samples, src_sr = read_wav_to_mono_int16(src_path)
    samples = linear_resample(samples, src_sr, TARGET_SR)
    max_samples = int(TARGET_SR * max_dur)
    trimmed_to = min(len(samples), max_samples)
    samples = samples[:trimmed_to]
    if gain_factor != 1.0:
        samples = [int(s * gain_factor) for s in samples]
    # ~10 ms fade-out to avoid an audible pop at the trim point
    samples = fade_out(samples, int(TARGET_SR * 0.01))
    out_path = os.path.join(out_dir, out_name)
    write_wav(out_path, samples, TARGET_SR)
    bytes_out = len(samples) * 2
    print(f'  {out_name:18s} src_sr={src_sr:5d}  out={trimmed_to:6d} samples '
          f'({trimmed_to/TARGET_SR:.2f}s, {bytes_out//1024}KB raw, '
          f'~{bytes_out*4//(7*1024)}KB ADPCM)')

def main(out_dir):
    os.makedirs(out_dir, exist_ok=True)
    print(f'Converting samples into {out_dir}:')
    for name, src, dur, gain in SAMPLES:
        convert_one(name, src, dur, gain, out_dir)

if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else '.')
