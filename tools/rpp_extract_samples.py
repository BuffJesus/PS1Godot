#!/usr/bin/env python3
"""Walk a Reaper .rpp file, find every VST instance with the track it
belongs to, and decode any WAV path it references (ReaSamplOmatic5000
stores them base64-encoded). Also flags ReaSynth instances for manual
parameter inspection."""
import base64, re, sys

VST_START = re.compile(r'^\s*<VST\s+"([^"]+)"')
TRACK_START = re.compile(r'^\s*<TRACK\s')
NAME_LINE = re.compile(r'^\s*NAME\s+("([^"]*)"|(\S+))')
B64_LINE = re.compile(r'^\s*([A-Za-z0-9+/=]{4,})\s*$')
CLOSE_LINE = re.compile(r'^\s*>\s*$')

def main(path):
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        lines = f.readlines()

    # Walk lines. State machine:
    #   - Track <TRACK ... > sections with their first NAME line.
    #   - Inside each, find <VST ... > … > blocks, gather base64 lines.
    cur_track = None
    track_naming = False  # waiting for first NAME inside a track
    in_vst = False
    vst_name = None
    vst_b64 = []

    findings = []

    for line in lines:
        if TRACK_START.search(line):
            cur_track = '?'
            track_naming = True
            continue
        if track_naming:
            m = NAME_LINE.match(line)
            if m:
                cur_track = m.group(2) if m.group(2) is not None else m.group(3)
                track_naming = False
                continue

        m = VST_START.match(line)
        if m:
            in_vst = True
            vst_name = m.group(1)
            vst_b64 = []
            continue

        if in_vst:
            if CLOSE_LINE.match(line):
                # End of VST block — pass the LIST of lines, not concat.
                sample_path = decode_sample_path(vst_b64)
                if sample_path:
                    findings.append((cur_track, vst_name, f'sample: {sample_path}'))
                elif 'ReaSynth' in vst_name:
                    findings.append((cur_track, vst_name, f'synth (b64 chunks={len(vst_b64)})'))
                elif 'ReaSamplOmatic' in vst_name:
                    findings.append((cur_track, vst_name, 'sample: <NO PATH FOUND — check sample loaded?>'))
                in_vst = False
                vst_name = None
                vst_b64 = []
                continue
            mb = B64_LINE.match(line)
            if mb:
                # Skip the trailing "AFByb2dyYW0gMQAQAAAA" preset-name line — it always appears
                vst_b64.append(mb.group(1))
            # ignore FLOATPOS / FXID / WAK / BYPASS / etc.

    # Print, grouped by track
    print(f'Found {len(findings)} VST instances across the project.\n')
    last_track = None
    for tn, fx, info in findings:
        if tn != last_track:
            print(f'[Track: {tn}]')
            last_track = tn
        print(f'    {fx}')
        print(f'        -> {info}')

def decode_sample_path(b64_lines_combined):
    """ReaSamplOmatic5000 stores its state across multiple base64 lines —
    each line is a separate base64 chunk that decodes to part of the
    plugin state. The path appears on its own (plus optionally a
    second line continuation). Try decoding each chunk independently
    AND the concatenation; whichever produces a recognizable path wins."""
    if not b64_lines_combined:
        return None

    # Find a path by searching every individual line PLUS the concatenation
    # of consecutive lines (Reaper splits long paths across two lines).
    candidates = []
    if isinstance(b64_lines_combined, list):
        lines = b64_lines_combined
    else:
        lines = [b64_lines_combined]

    # Try each line alone, then each adjacent pair, then triple
    for n in (1, 2, 3):
        for i in range(len(lines) - n + 1):
            chunk = ''.join(lines[i:i+n])
            try:
                raw = base64.b64decode(chunk + '=' * (-len(chunk) % 4))
            except Exception:
                continue
            for m in re.finditer(rb'[A-Za-z]:\\[\x20-\x7e]+?\.wav', raw):
                candidates.append(m.group(0).decode('utf-8', errors='replace'))
    if candidates:
        # Return the longest unique candidate (avoids partial path matches)
        return max(set(candidates), key=len)
    return None

if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else 'RetroAdventureSong.rpp')
