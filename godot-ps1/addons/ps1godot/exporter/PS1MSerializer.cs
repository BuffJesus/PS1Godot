using System;
using System.Collections.Generic;
using System.IO;
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
    // Maximum channels per sequence (matches MusicSequencer::MAX_CHANNELS).
    public const int MaxChannels = 8;

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
    }

    // Produce a PS1M blob.  bindings = the user's PS1MusicChannel list
    // (already resolved against the audio clip table).  loopStartBeat
    // is in beats (quarter-notes); -1 = no loop.
    public static byte[] Serialize(MidiParser.ParsedMidi midi,
                                    IReadOnlyList<ChannelBinding> bindings,
                                    int? bpmOverride,
                                    int loopStartBeat)
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
            events.Add(EncodeEvent(n.AbsoluteTick, (byte)packed, kind, n.Note, n.Velocity));
        }

        if (events.Count == 0)
            throw new InvalidOperationException(
                "PS1MSerializer: parsed MIDI has no playable notes (after channel filtering). Check the MIDI channel bindings.");

        // Convert loopStartBeat → loopStartTick. -1 → 0xFFFFFFFF (no loop).
        uint loopStartTick = loopStartBeat < 0
            ? 0xFFFFFFFFu
            : (uint)(loopStartBeat * tpq);

        // Write the blob.
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // Header (16 bytes).
        w.Write((byte)'P'); w.Write((byte)'S'); w.Write((byte)'1'); w.Write((byte)'M');
        w.Write((ushort)bpm);
        w.Write((ushort)tpq);
        w.Write((byte)bindings.Count);
        w.Write((byte)0);                         // pad0
        w.Write((ushort)Math.Min(events.Count, ushort.MaxValue));
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
            w.Write((ushort)0);                   // pad
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
}
