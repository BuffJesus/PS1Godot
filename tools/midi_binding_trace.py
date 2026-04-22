#!/usr/bin/env python3
"""
Trace what the PS1M exporter would do with a given MIDI + channel
binding set. Answers "why are notes being skipped?" by showing:
  - How many notes are on each source MIDI channel.
  - How many survive each binding's filter.
  - How the surviving notes collide in time on the mono-per-packed-
    channel sequencer (two notes within N ms on the same binding → the
    earlier one gets silenced by the later).

Usage:
    python tools/midi_binding_trace.py <path.mid> <midi_ch,...>

    e.g. python tools/midi_binding_trace.py foo.mid 0,1,2,4,6
"""
import struct
import sys


def read_var(b, i):
    v = 0
    while True:
        c = b[i]
        i += 1
        v = (v << 7) | (c & 0x7F)
        if not (c & 0x80):
            return v, i


def parse_track(data, pos):
    if data[pos:pos + 4] != b"MTrk":
        raise RuntimeError("MTrk missing")
    tlen = struct.unpack(">I", data[pos + 4:pos + 8])[0]
    i = pos + 8
    end = i + tlen
    abs_tick = 0
    running = 0
    notes = []  # (abs_tick, ch, kind, note, vel)
    tempos = []
    while i < end:
        d, i = read_var(data, i)
        abs_tick += d
        b0 = data[i]
        if b0 & 0x80:
            running = b0
            i += 1
            status = b0
        else:
            status = running
        if status == 0xFF:
            mt = data[i]; i += 1
            ml, i = read_var(data, i)
            payload = data[i:i + ml]
            i += ml
            if mt == 0x51 and ml == 3:
                us = (payload[0] << 16) | (payload[1] << 8) | payload[2]
                tempos.append((abs_tick, us))
            if mt == 0x2F:
                break
        elif status in (0xF0, 0xF7):
            sl, i = read_var(data, i)
            i += sl
        else:
            hi = status & 0xF0
            ch = status & 0x0F
            if hi == 0x90:
                n = data[i]; v = data[i + 1]; i += 2
                kind = "on" if v > 0 else "off"
                notes.append((abs_tick, ch, kind, n, v))
            elif hi == 0x80:
                n = data[i]; v = data[i + 1]; i += 2
                notes.append((abs_tick, ch, "off", n, v))
            elif hi in (0xC0, 0xD0):
                i += 1
            else:
                i += 2
    return notes, tempos, end


def main(path, bound_channels):
    with open(path, "rb") as f:
        data = f.read()
    fmt, ntracks, div = struct.unpack(">HHH", data[8:14])
    print(f"format={fmt} tracks={ntracks} tpq={div}")
    pos = 8 + struct.unpack(">I", data[4:8])[0]
    all_notes = []
    all_tempos = []
    for _ in range(ntracks):
        nt, tm, pos = parse_track(data, pos)
        all_notes.extend(nt)
        all_tempos.extend(tm)
    # Pick initial tempo.
    tempo = all_tempos[0][1] if all_tempos else 500000
    tick_sec = tempo / 1_000_000 / div  # seconds per tick

    # Sort by tick, NoteOff before NoteOn at same tick.
    def key(e):
        tick, ch, kind, note, vel = e
        return (tick, 0 if kind == "off" else 1)
    all_notes.sort(key=key)

    # Count per source channel.
    per_ch = {}
    for _, ch, kind, _, vel in all_notes:
        if kind == "on":
            per_ch[ch] = per_ch.get(ch, 0) + 1
    print("\nSource-channel note-on counts:")
    for ch in sorted(per_ch):
        mark = "  BOUND  " if ch in bound_channels else "  DROPPED"
        print(f"  ch{ch:2d}: {per_ch[ch]:4d} notes{mark}")

    # Simulate binding filter + mono-per-binding retriggering.
    bound_set = set(bound_channels)
    # Sequential packed index so runtime voice = 0..N-1.
    packed_of = {ch: i for i, ch in enumerate(sorted(bound_set))}
    surviving = [e for e in all_notes if e[1] in bound_set]
    print(f"\nSurviving events after channel filter: {len(surviving)}")

    # How close are consecutive note-ons on each packed channel? If
    # shorter than the sample length, the earlier note cuts.
    by_packed = {}
    for tick, ch, kind, note, vel in surviving:
        if kind != "on": continue
        p = packed_of[ch]
        by_packed.setdefault(p, []).append((tick, note))
    print("\nIntra-channel note density (packed voice):")
    for p in sorted(by_packed):
        evs = by_packed[p]
        if len(evs) < 2:
            print(f"  voice{p} (src ch{[k for k,v in packed_of.items() if v==p][0]}): "
                  f"{len(evs)} notes (no retrigger concern)")
            continue
        gaps = [(evs[i + 1][0] - evs[i][0]) * tick_sec for i in range(len(evs) - 1)]
        mn, mx, avg = min(gaps), max(gaps), sum(gaps) / len(gaps)
        src_ch = [k for k, v in packed_of.items() if v == p][0]
        print(f"  voice{p} (src ch{src_ch}): {len(evs)} notes, "
              f"gap min={mn*1000:.0f}ms max={mx*1000:.0f}ms avg={avg*1000:.0f}ms")

    # Note pitches out-of-range vs. the ±36-semitone pitch table cap.
    print("\nPitch-shift range check (BaseNote=69 → ±36 semitone window = 33..105):")
    for ch, p in packed_of.items():
        notes_on_ch = [n for _, c, k, n, _ in surviving if c == ch and k == "on"]
        if not notes_on_ch: continue
        lo, hi = min(notes_on_ch), max(notes_on_ch)
        shift_lo, shift_hi = lo - 69, hi - 69
        outliers = [n for n in notes_on_ch if n < 33 or n > 105]
        mark = f"  ({len(outliers)} notes out of ±36-st table)" if outliers else ""
        print(f"  ch{ch}: notes {lo}..{hi} → semitone offset {shift_lo:+d}..{shift_hi:+d}{mark}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("usage: midi_binding_trace.py <path.mid> <midi_ch,comma,separated>")
        sys.exit(1)
    channels = [int(x) for x in sys.argv[2].split(",")]
    main(sys.argv[1], channels)
