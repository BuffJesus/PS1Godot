#!/usr/bin/env python3
"""Quick-and-dirty SMF inspector: tempo, ticks-per-quarter, channel
note counts, note range per channel, and total runtime estimate."""
import sys, struct

def read_var(b, i):
    v = 0
    while True:
        c = b[i]; i += 1
        v = (v << 7) | (c & 0x7F)
        if not (c & 0x80):
            return v, i

def main(path):
    with open(path, 'rb') as f:
        data = f.read()
    if data[:4] != b'MThd':
        print('not a MIDI file'); return
    hlen = struct.unpack('>I', data[4:8])[0]
    fmt, tracks, div = struct.unpack('>HHH', data[8:14])
    print(f'format={fmt} tracks={tracks} division={div}')
    if div & 0x8000:
        print('SMPTE division — unsupported'); return
    tpq = div
    print(f'ticksPerQuarter={tpq}')

    pos = 8 + hlen
    tempo_us_per_q = 500000  # default 120 BPM
    first_tempo_set = False
    chan_notes = {i: 0 for i in range(16)}
    chan_min = {i: 128 for i in range(16)}
    chan_max = {i: -1 for i in range(16)}
    chan_first_tick = {i: None for i in range(16)}
    chan_last_tick = {i: 0 for i in range(16)}
    track_lengths = []
    track_names = []
    track_programs = {}  # (track, channel) -> program

    for t in range(tracks):
        if data[pos:pos+4] != b'MTrk':
            print(f'track {t}: missing MTrk @ {pos}')
            return
        tlen = struct.unpack('>I', data[pos+4:pos+8])[0]
        end = pos + 8 + tlen
        i = pos + 8
        abs_tick = 0
        running = 0
        track_name = ''
        while i < end:
            delta, i = read_var(data, i)
            abs_tick += delta
            status = data[i]
            if status < 0x80:
                status = running
            else:
                i += 1
                if status < 0xF0:
                    running = status
            if status == 0xFF:
                meta = data[i]; i += 1
                mlen, i = read_var(data, i)
                blob = data[i:i+mlen]
                if meta == 0x51 and mlen == 3:
                    new_tempo = (blob[0] << 16) | (blob[1] << 8) | blob[2]
                    if not first_tempo_set:
                        tempo_us_per_q = new_tempo
                        first_tempo_set = True
                elif meta == 0x03:
                    try: track_name = blob.decode('utf-8', 'replace')
                    except: pass
                elif meta == 0x04:
                    pass  # instrument name
                i += mlen
            elif status in (0xF0, 0xF7):
                slen, i = read_var(data, i)
                i += slen
            else:
                kind = status & 0xF0
                ch = status & 0x0F
                if kind in (0x80, 0x90, 0xA0, 0xB0, 0xE0):
                    n = data[i]; v = data[i+1]; i += 2
                    if kind == 0x90 and v > 0:
                        chan_notes[ch] += 1
                        chan_min[ch] = min(chan_min[ch], n)
                        chan_max[ch] = max(chan_max[ch], n)
                        if chan_first_tick[ch] is None:
                            chan_first_tick[ch] = abs_tick
                        chan_last_tick[ch] = max(chan_last_tick[ch], abs_tick)
                elif kind == 0xC0:
                    p = data[i]; i += 1
                    track_programs[(t, ch)] = p
                elif kind == 0xD0:
                    i += 1
                else:
                    i += 1
        track_lengths.append(abs_tick)
        track_names.append(track_name)
        pos = end

    bpm = round(60_000_000 / tempo_us_per_q, 2) if tempo_us_per_q else 0
    print(f'tempo (initial): {tempo_us_per_q} us/q  ({bpm} BPM)')
    max_ticks = max(track_lengths) if track_lengths else 0
    secs = (max_ticks / tpq) * (tempo_us_per_q / 1_000_000)
    print(f'longest track: {max_ticks} ticks (~{secs:.2f}s @ initial tempo)')
    print(f'beats: {max_ticks / tpq:.1f}')
    print()
    print('Track names:')
    for ti, name in enumerate(track_names):
        print(f'  track[{ti}] = "{name}" len={track_lengths[ti]}')
    print()
    print('Channel usage (only channels with notes):')
    for c in range(16):
        if chan_notes[c] == 0: continue
        prog_list = sorted({p for (_, ch), p in track_programs.items() if ch == c})
        print(f'  ch{c:2d}: notes={chan_notes[c]:4d}  range={chan_min[c]:3d}-{chan_max[c]:3d}  '
              f'firstTick={chan_first_tick[c]}  lastTick={chan_last_tick[c]}  programs={prog_list}')

if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else 'RetroAdventureSong.mid')
