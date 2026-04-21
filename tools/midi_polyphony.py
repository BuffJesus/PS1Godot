#!/usr/bin/env python3
"""Per-track polyphony + note-range profiler. Answers:
- How many notes overlap at once on each track?
- What note range does each track use?
- Are there overlapping notes we'd drop under mono-per-channel?"""
import sys, struct

def read_var(b, i):
    v = 0
    while True:
        c = b[i]; i += 1
        v = (v << 7) | (c & 0x7F)
        if not (c & 0x80):
            return v, i

def parse_track(data, pos, tlen, t):
    end = pos + 8 + tlen
    i = pos + 8
    abs_tick = 0
    running = 0
    notes = []  # list of (on_tick, off_tick, note, vel)
    open_notes = {}  # note -> on_tick
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
            if meta == 0x03:
                try: track_name = blob.decode('utf-8', 'replace')
                except: pass
            i += mlen
        elif status in (0xF0, 0xF7):
            slen, i = read_var(data, i)
            i += slen
        else:
            kind = status & 0xF0
            ch = status & 0x0F
            if kind == 0x90:  # note on
                n = data[i]; v = data[i+1]; i += 2
                if v == 0:
                    # treat as note off
                    if n in open_notes:
                        notes.append((open_notes.pop(n), abs_tick, n, 0))
                else:
                    if n in open_notes:
                        # retrigger — close old, open new
                        notes.append((open_notes[n], abs_tick, n, v))
                    open_notes[n] = abs_tick
            elif kind == 0x80:  # note off
                n = data[i]; v = data[i+1]; i += 2
                if n in open_notes:
                    notes.append((open_notes.pop(n), abs_tick, n, v))
            elif kind in (0xA0, 0xB0, 0xE0):
                i += 2
            elif kind in (0xC0, 0xD0):
                i += 1
            else:
                i += 1
    # close any still-held notes at track end
    for n, t_on in open_notes.items():
        notes.append((t_on, abs_tick, n, 0))
    return track_name, notes, end

def max_polyphony(notes):
    # Sweep-line: +1 on each note-on, -1 on each note-off.
    events = []
    for on, off, n, v in notes:
        events.append((on, 1))
        events.append((off, -1))
    events.sort()
    cur = 0
    max_poly = 0
    for _, d in events:
        cur += d
        if cur > max_poly: max_poly = cur
    return max_poly

def main(path):
    with open(path, 'rb') as f:
        data = f.read()
    pos = 8 + struct.unpack('>I', data[4:8])[0]
    fmt, tracks, div = struct.unpack('>HHH', data[8:14])
    print(f'format={fmt} tracks={tracks} ticksPerQuarter={div}')
    print()
    for t in range(tracks):
        if data[pos:pos+4] != b'MTrk':
            pos += 8; continue
        tlen = struct.unpack('>I', data[pos+4:pos+8])[0]
        name, notes, end = parse_track(data, pos, tlen, t)
        pos = end
        if not notes:
            print(f'track[{t}] "{name}": empty')
            continue
        ranges = [n for _,_,n,_ in notes]
        poly = max_polyphony(notes)
        total = len(notes)
        overlaps = 0
        # Count note events whose on-tick falls strictly within another note's hold window.
        notes_sorted = sorted(notes)
        for i, (on, off, n, v) in enumerate(notes_sorted):
            for j in range(max(0, i-8), i):
                if notes_sorted[j][1] > on:  # still held
                    overlaps += 1
                    break
        print(f'track[{t}] "{name}": notes={total} range={min(ranges)}-{max(ranges)} '
              f'maxPolyphony={poly} overlappingNoteStarts={overlaps}')

if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else 'RetroAdventureSong.mid')
