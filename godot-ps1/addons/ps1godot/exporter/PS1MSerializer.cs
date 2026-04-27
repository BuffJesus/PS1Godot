using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace PS1Godot.Exporter;

// Packs a ParsedMidi + channel binding table into the runtime's PS1M
// binary format (see psxsplash-main/src/musicsequencer.hh and
// docs/sequenced-music-format.md).
//
// On-disk layout:
//   16 B MusicSequenceHeader
//    8 B MusicChannelEntry × channelCount
//    8 B MusicEvent × eventCount
//
// We collapse MIDI's per-channel notes onto our own contiguous channel
// indices (0..N-1) so the runtime's per-channel state arrays stay
// dense. The mapping happens here at export time.
public static class PS1MSerializer
{
    // Maximum channels per sequence (matches MusicSequencer::MAX_CHANNELS
    // in psxsplash-main/src/musicsequencer.hh). Matches MAX_VOICES=24
    // (audiomanager.hh) — voice reservation is dynamic per-scene, so
    // scenes with few music channels still leave plenty of SFX budget.
    public const int MaxChannels = 24;

    // BPM used when the source MIDI carries no tempo meta event AND the
    // user didn't override it. 120 is the SMF default.
    public const int DefaultBpm = 120;

    public sealed class ChannelBinding
    {
        public required int MidiChannel { get; init; }    // 0-15 (source MIDI)
        // Optional track filter. -1 = any track on the chosen MidiChannel.
        public int MidiTrackIndex { get; init; } = -1;
        // Optional note-range filter. Notes outside [MidiNoteMin, MidiNoteMax]
        // skip this binding. Defaults span the full MIDI range.
        public int MidiNoteMin { get; init; } = 0;
        public int MidiNoteMax { get; init; } = 127;
        public required int AudioClipIndex { get; init; } // resolved index into splashpack audio clips
        public required int BaseNoteMidi { get; init; }
        public required int Volume { get; init; }
        public required int Pan { get; init; }
        public required bool LoopSample { get; init; }
        public required bool Percussion { get; init; }
        // 0 = no choke. Non-zero = a noteOn on any channel sharing this
        // group silences this channel's currently-held note. Drum-kit
        // expansion populates this from PS1DrumKit.ChokeGroups[i];
        // melodic bindings leave it at 0.
        public int ChokeGroup { get; init; } = 0;
    }

    // Produce a PS1M (legacy) or PS2M (Phase 2.5 bank-driven) blob.
    //
    //   bindings           = the user's PS1MusicChannel list, already
    //                        resolved against the audio clip table.
    //   bpmOverride        = optional tempo override; null uses the
    //                        source MIDI's first SetTempo meta event.
    //   loopStartBeat      = in quarter-notes; -1 = no loop.
    //   channelDefaultPrograms = when non-null, switches the writer to
    //                        PS2M (magic "PS2M") and emits a u8[N]
    //                        default-program table after the channel
    //                        table. Length MUST equal bindings.Count
    //                        OR be empty (interpreted as "no per-channel
    //                        default; runtime defaults to 0"). When
    //                        null, emits legacy PS1M.
    //   programChanges     = source MIDI's preserved program-change
    //                        events. Only emitted into the wire stream
    //                        when channelDefaultPrograms is non-null
    //                        (PS2M). Pass null OR an empty list to
    //                        suppress.
    public static byte[] Serialize(MidiParser.ParsedMidi midi,
                                    IReadOnlyList<ChannelBinding> bindings,
                                    int? bpmOverride,
                                    int loopStartBeat,
                                    IReadOnlyList<byte>? channelDefaultPrograms = null,
                                    IReadOnlyList<MidiParser.MidiProgramChangeEvent>? programChanges = null)
    {
        if (bindings.Count == 0)
            throw new InvalidOperationException("PS1MSerializer needs at least one channel binding.");
        if (bindings.Count > MaxChannels)
            throw new InvalidOperationException(
                $"PS1MSerializer: {bindings.Count} channels exceeds runtime max of {MaxChannels}.");

        // Duplicate bindings on the same (MidiChannel, track, note-range)
        // are INTENTIONAL — they act as polyphony lanes, so chord notes
        // on a single MIDI channel can spread across multiple mono
        // voices instead of retriggering each other. The routing pass
        // below allocates new notes to the first free lane and recycles
        // oldest-held when every lane is busy. (Earlier builds rejected
        // duplicates; that fought against intra-channel chords in
        // every DAW-authored MIDI.)

        // Pick the effective tempo.  Manual override wins; otherwise use
        // the first SetTempo meta event; otherwise SMF default 120 BPM.
        int bpm;
        if (bpmOverride.HasValue && bpmOverride.Value > 0)
        {
            bpm = bpmOverride.Value;
        }
        else if (midi.Tempos.Count > 0)
        {
            // micros/quarter → BPM = 60_000_000 / mpq.
            uint mpq = midi.Tempos[0].MicrosPerQuarter;
            bpm = (mpq == 0) ? DefaultBpm : (int)(60_000_000u / mpq);
        }
        else
        {
            bpm = DefaultBpm;
        }
        if (bpm < 20) bpm = 20;
        if (bpm > 300) bpm = 300;

        int tpq = midi.TicksPerQuarter > 0 ? midi.TicksPerQuarter : 480;

        // PS1M's header carries one BPM for the whole track, but source MIDI
        // can change tempo mid-track via meta 0x51. We preserve those changes
        // by rescaling every event tick: compute the wall-clock micros each
        // event sits at under the source tempo map, then convert back to
        // ticks under the chosen reference tempo. Loop start (given in beats)
        // is rescaled the same way so a post-tempo-change loop point lands at
        // the correct wall-clock position.
        uint refMpq = (uint)(60_000_000 / bpm);
        var tempoSegs = new List<(uint startTick, uint mpq)>();
        // SMF default 120 BPM (500000 mpq) applies until the first explicit
        // tempo event. Seed a tick-0 segment if none exists.
        if (midi.Tempos.Count == 0 || midi.Tempos[0].AbsoluteTick > 0)
            tempoSegs.Add((0u, 500_000u));
        foreach (var t in midi.Tempos)
            tempoSegs.Add((t.AbsoluteTick, t.MicrosPerQuarter == 0 ? 500_000u : t.MicrosPerQuarter));
        bool rescale = tempoSegs.Count > 1;

        uint Rescale(uint srcTick)
        {
            if (!rescale) return srcTick;
            long scaled = 0; // sum(ticksInSeg × mpq), i.e. wallMicros × tpq
            for (int i = 0; i < tempoSegs.Count; i++)
            {
                uint segStart = tempoSegs[i].startTick;
                if (srcTick <= segStart) break;
                uint segEnd = (i + 1 < tempoSegs.Count) ? tempoSegs[i + 1].startTick : uint.MaxValue;
                uint ticksInSeg = Math.Min(srcTick, segEnd) - segStart;
                scaled += (long)ticksInSeg * tempoSegs[i].mpq;
            }
            return (uint)(scaled / refMpq);
        }

        // Translate MIDI notes into PS1M events with polyphony-aware
        // voice allocation:
        //
        //   1. For a note-on, collect every binding whose
        //      (MidiChannel, track, note-range) matches. Track-specific
        //      bindings are preferred over MidiTrackIndex=-1 wildcards
        //      (scanned in two passes so authored order doesn't matter).
        //   2. Among the matching bindings, pick the first one that's
        //      NOT currently holding a note. If all are busy, steal the
        //      oldest (lowest on-tick) — a "voice stealing" allocator
        //      matches how real MIDI synths handle polyphony overflow.
        //   3. Emit a PS1M note-on on the chosen binding's packed voice
        //      and record (binding → note, tick) so subsequent chord
        //      members land on a different lane.
        //   4. For a note-off, find the binding currently holding that
        //      note (same MidiChannel, filtering by track when explicit)
        //      and clear its state. Off events from stolen-out notes
        //      become no-ops (nothing holds that note anymore).
        //   5. Unmatched notes are dropped with a one-shot warning per
        //      (channel, track).
        var events = new List<byte[]>(midi.Notes.Count);
        var seenUnbound = new HashSet<(int ch, int track)>();

        // Per-binding held-note state. heldNote[i] = MIDI note number the
        // binding is currently playing, -1 if free. heldSince[i] = tick
        // the currently-held note started on (used for oldest-first
        // voice stealing).
        var heldNote = new int[bindings.Count];
        var heldSince = new uint[bindings.Count];
        for (int i = 0; i < bindings.Count; i++) heldNote[i] = -1;

        // Collect candidate bindings for (channel, track, note) in the
        // two-pass priority order the old routing used. Reused per-note
        // to avoid per-event allocation.
        var candidates = new List<int>(bindings.Count);

        foreach (var n in midi.Notes)
        {
            candidates.Clear();

            // Pass 1: bindings with an exact track match.
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.MidiChannel == n.Channel
                    && b.MidiTrackIndex == n.Track
                    && n.Note >= b.MidiNoteMin && n.Note <= b.MidiNoteMax)
                {
                    candidates.Add(i);
                }
            }
            // Pass 2: track-wildcard bindings (MidiTrackIndex == -1).
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.MidiChannel == n.Channel
                    && b.MidiTrackIndex < 0
                    && n.Note >= b.MidiNoteMin && n.Note <= b.MidiNoteMax)
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                if (seenUnbound.Add((n.Channel, n.Track)))
                    GD.PushWarning($"[PS1Godot] MIDI channel {n.Channel} / track {n.Track} (note {n.Note}) has no matching PS1MusicChannel binding — those notes won't play.");
                continue;
            }

            int packed = -1;

            if (n.Kind == MidiParser.MidiEventKind.NoteOn)
            {
                // Prefer an idle lane; fall back to stealing the oldest-held.
                int oldestIdx = candidates[0];
                uint oldestTick = heldSince[oldestIdx];
                foreach (int i in candidates)
                {
                    if (heldNote[i] < 0) { packed = i; break; }
                    if (heldSince[i] < oldestTick)
                    {
                        oldestTick = heldSince[i];
                        oldestIdx = i;
                    }
                }
                if (packed < 0) packed = oldestIdx;  // all busy — steal oldest
                heldNote[packed] = n.Note;
                heldSince[packed] = n.AbsoluteTick;
            }
            else // NoteOff
            {
                // Match the binding currently holding this exact note. If
                // none holds it (the note got stolen), drop the off.
                foreach (int i in candidates)
                {
                    if (heldNote[i] == n.Note)
                    {
                        packed = i;
                        heldNote[i] = -1;
                        break;
                    }
                }
                if (packed < 0) continue;
            }

            byte kind = n.Kind == MidiParser.MidiEventKind.NoteOn ? (byte)0 : (byte)1;
            events.Add(EncodeEvent(Rescale(n.AbsoluteTick), (byte)packed, kind, n.Note, n.Velocity));
        }

        // PS2M only: inject ProgramChange events (kind=4) into the
        // event stream. One source MIDI ProgramChange fans out to one
        // event per binding listening on that MIDI channel — runtime
        // m_channels state is indexed by runtime channel, not by source
        // MIDI channel, so each binding tracks its own currentProgram.
        bool emitPS2M = channelDefaultPrograms != null;
        if (emitPS2M && programChanges != null && programChanges.Count > 0)
        {
            foreach (var pc in programChanges)
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    if (bindings[i].MidiChannel != pc.Channel) continue;
                    // Track filter applies the same way as NoteOn: a
                    // track-specific binding only catches program
                    // changes from its track; a wildcard binding (-1)
                    // catches all.
                    if (bindings[i].MidiTrackIndex >= 0
                        && bindings[i].MidiTrackIndex != pc.Track) continue;
                    events.Add(EncodeEvent(Rescale(pc.AbsoluteTick), (byte)i, (byte)4, pc.Program, 0));
                }
            }
        }

        // Inject loop-bracket events from MIDI markers/cue points
        // tagged "loopStart"/"loopEnd" (kind=9 / kind=10). Works for
        // both PS1M and PS2M — older runtimes silently skip unknown
        // event kinds (default branch in MusicSequencer::dispatchEvent).
        // Channel field is unused for these; data1/data2 reserved.
        bool injectedLoopEvents = false;
        bool injectedMarkers = false;
        if (midi.Markers != null && midi.Markers.Count > 0)
        {
            foreach (var m in midi.Markers)
            {
                if (m.Kind == MidiParser.MidiMarkerKind.LoopStart)
                {
                    events.Add(EncodeEvent(Rescale(m.AbsoluteTick), 0, (byte)9, 0, 0));
                    injectedLoopEvents = true;
                }
                else if (m.Kind == MidiParser.MidiMarkerKind.LoopEnd)
                {
                    events.Add(EncodeEvent(Rescale(m.AbsoluteTick), 0, (byte)10, 0, 0));
                    injectedLoopEvents = true;
                }
                else
                {
                    // Generic text marker → kind=8 with 16-bit FNV-1a
                    // hash of the marker text in data1/data2. Lua side
                    // polls the last-fired hash via
                    // Music.GetLastMarkerHash() and compares against
                    // Music.MarkerHash("name"). 16 bits is enough for
                    // ~100 markers per song before birthday-paradox
                    // collisions become likely; rename collisions
                    // when they occur (no runtime collision check).
                    ushort hash = MarkerHash16(m.Text);
                    byte lo = (byte)(hash & 0xFF);
                    byte hi = (byte)((hash >> 8) & 0xFF);
                    events.Add(EncodeEvent(Rescale(m.AbsoluteTick), 0, (byte)8, lo, hi));
                    injectedMarkers = true;
                }
            }
        }

        // Inject pitch-bend events as kind=5. Like ProgramChange, one
        // source MIDI event on channel C fans out to one event per
        // binding listening on C — runtime channels are 0..N-1 and
        // each binding's pitchBend state is tracked separately.
        // data1 = bend14 LSB, data2 = bend14 MSB. Works for both PS1M
        // and PS2M; older runtimes default-skip kind=5.
        bool injectedPitchBends = false;
        if (midi.PitchBends != null && midi.PitchBends.Count > 0)
        {
            foreach (var pb in midi.PitchBends)
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    if (bindings[i].MidiChannel != pb.Channel) continue;
                    if (bindings[i].MidiTrackIndex >= 0
                        && bindings[i].MidiTrackIndex != pb.Track) continue;
                    byte lsb = (byte)(pb.Value14 & 0x7F);
                    byte msb = (byte)((pb.Value14 >> 7) & 0x7F);
                    events.Add(EncodeEvent(Rescale(pb.AbsoluteTick), (byte)i, (byte)5, lsb, msb));
                    injectedPitchBends = true;
                }
            }
        }

        // Inject controller events as kind=7. Same fan-out pattern as
        // PitchBend. data1 = controller# (0..127), data2 = value
        // (0..127). The runtime acts on a small whitelist (CC#7
        // channel volume, CC#10 pan); other CC#s are preserved into
        // the wire stream and silently ignored at runtime — adding a
        // new CC handler later doesn't need a format bump.
        bool injectedControllers = false;
        if (midi.Controllers != null && midi.Controllers.Count > 0)
        {
            foreach (var cc in midi.Controllers)
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    if (bindings[i].MidiChannel != cc.Channel) continue;
                    if (bindings[i].MidiTrackIndex >= 0
                        && bindings[i].MidiTrackIndex != cc.Track) continue;
                    events.Add(EncodeEvent(Rescale(cc.AbsoluteTick), (byte)i, (byte)7, cc.Controller, cc.Value));
                    injectedControllers = true;
                }
            }
        }

        // Re-sort if we injected anything, so kind=4/5/7/9/10 events
        // interleave correctly with notes. Decode each event's first
        // 4 bytes as little-endian tick. Stable sort preserves
        // declaration order at ties so NoteOff-before-NoteOn (same
        // tick) is preserved.
        bool injectedAnything = injectedLoopEvents
            || injectedPitchBends
            || injectedControllers
            || injectedMarkers
            || (emitPS2M && programChanges != null && programChanges.Count > 0);
        if (injectedAnything)
        {
            events = events
                .Select((bytes, idx) => new { bytes, tick = (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24)), idx })
                .OrderBy(x => x.tick).ThenBy(x => x.idx)
                .Select(x => x.bytes)
                .ToList();
        }

        if (events.Count == 0)
            throw new InvalidOperationException(
                "PS1MSerializer: parsed MIDI has no playable notes (after channel filtering). Check the MIDI channel bindings.");

        // Header.eventCount is u16; we can't honestly represent >65535. Silently
        // clamping used to leave trailing blob bytes the runtime ignored — very
        // confusing when an author's long piece played back truncated without a
        // warning. Fail loud instead with an actionable message.
        if (events.Count > ushort.MaxValue)
            throw new InvalidOperationException(
                $"PS1MSerializer: {events.Count} events exceeds the format's u16 event-count limit ({ushort.MaxValue}). Split the sequence into shorter segments or drop some channels.");

        // Convert loopStartBeat → loopStartTick. -1 → 0xFFFFFFFF (no loop).
        // Rescale through the tempo map so "beat N" lands at the same
        // wall-clock position after tempo changes.
        uint loopStartTick = loopStartBeat < 0
            ? 0xFFFFFFFFu
            : Rescale((uint)(loopStartBeat * tpq));

        // Write the blob.
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // Header (16 bytes). Magic differs PS1M / PS2M; the rest of
        // the header is identical so the runtime parser can share the
        // 16-byte struct read.
        if (emitPS2M)
        {
            w.Write((byte)'P'); w.Write((byte)'S'); w.Write((byte)'2'); w.Write((byte)'M');
        }
        else
        {
            w.Write((byte)'P'); w.Write((byte)'S'); w.Write((byte)'1'); w.Write((byte)'M');
        }
        w.Write((ushort)bpm);
        w.Write((ushort)tpq);
        w.Write((byte)bindings.Count);
        w.Write((byte)0);                         // pad0
        w.Write((ushort)events.Count);
        w.Write(loopStartTick);

        // Channels (8 bytes each).
        foreach (var b in bindings)
        {
            byte flags = 0;
            if (b.LoopSample) flags |= 0x01;
            if (b.Percussion) flags |= 0x02;

            w.Write((ushort)Math.Clamp(b.AudioClipIndex, 0, ushort.MaxValue));
            w.Write((byte)Math.Clamp(b.BaseNoteMidi, 0, 127));
            w.Write((byte)Math.Clamp(b.Volume, 0, 127));
            w.Write((byte)Math.Clamp(b.Pan, 0, 127));
            w.Write(flags);
            // Repurposes the former 2-byte pad: chokeGroup byte +
            // 1 reserved byte. Older runtimes read pad=0, which reads
            // as chokeGroup=0 = no choke — backwards-compatible.
            w.Write((byte)Math.Clamp(b.ChokeGroup, 0, 255));
            w.Write((byte)0);                     // reserved
        }

        // PS2M only: u8[N] default-program table, padded to 4-byte
        // alignment. Caller supplies one entry per binding (or empty
        // → all 0). When the runtime parses this, each channel state
        // gets its currentProgram seeded at sequence start.
        if (emitPS2M)
        {
            int n = bindings.Count;
            for (int i = 0; i < n; i++)
            {
                byte prog = 0;
                if (channelDefaultPrograms!.Count > i) prog = channelDefaultPrograms[i];
                w.Write(prog);
            }
            int pad = ((n + 3) & ~3) - n;
            for (int p = 0; p < pad; p++) w.Write((byte)0);
        }

        // Events (8 bytes each), already encoded.
        foreach (var ev in events)
            w.Write(ev);

        return ms.ToArray();
    }

    private static byte[] EncodeEvent(uint tick, byte channel, byte kind, byte data1, byte data2)
    {
        var ev = new byte[8];
        ev[0] = (byte)(tick & 0xFF);
        ev[1] = (byte)((tick >> 8) & 0xFF);
        ev[2] = (byte)((tick >> 16) & 0xFF);
        ev[3] = (byte)((tick >> 24) & 0xFF);
        ev[4] = channel;
        ev[5] = kind;
        ev[6] = data1;
        ev[7] = data2;
        return ev;
    }

    // FNV-1a 32-bit folded to 16-bit. Used by kind=8 marker events to
    // identify the marker text without carrying it into the wire
    // format. Must match the Lua-side Music.MarkerHash helper bit-for-
    // bit (psxsplash-main/src/luaapi.cpp). Trim+lowercase to match the
    // case-insensitive convention loop markers already use.
    public static ushort MarkerHash16(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        string normalized = text.Trim().ToLowerInvariant();
        uint hash = 2166136261u; // FNV offset basis
        foreach (char c in normalized)
        {
            hash ^= (byte)c;
            hash *= 16777619u; // FNV prime
        }
        return (ushort)((hash & 0xFFFF) ^ (hash >> 16));
    }
}
