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

        // Detect bindings that exactly duplicate (channel, track,
        // note-range) — silent ones-after-the-other would be confusing.
        // Note-range *overlap* across bindings on the same (ch, track)
        // is allowed — the routing pass picks the first binding whose
        // range matches each note.
        var exactSet = new HashSet<(int, int, int, int)>(bindings.Count);
        for (int i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            var key = (b.MidiChannel, b.MidiTrackIndex, b.MidiNoteMin, b.MidiNoteMax);
            if (!exactSet.Add(key))
                throw new InvalidOperationException(
                    $"PS1MSerializer: duplicate binding for MIDI channel {b.MidiChannel}, track {b.MidiTrackIndex}, notes {b.MidiNoteMin}-{b.MidiNoteMax}.");
        }

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

        // Translate MIDI notes into PS1M events. Routing per note:
        //   1. iterate bindings in order; pick the first one whose
        //      MidiChannel matches AND (MidiTrackIndex == -1 || matches
        //      the note's source track) AND note in [Min..Max].
        //   2. track-specific bindings ranked above wildcards in the
        //      same scan: we do TWO passes — exact-track first, then
        //      wildcard — so the natural binding order doesn't matter.
        //   3. unmatched notes are dropped, with a one-shot warning per
        //      (channel, track) combo so noisy MIDIs don't spam.
        var events = new List<byte[]>(midi.Notes.Count);
        var seenUnbound = new HashSet<(int ch, int track)>();

        foreach (var n in midi.Notes)
        {
            int packed = -1;
            // Pass 1: bindings with explicit MidiTrackIndex matching this note.
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.MidiChannel == n.Channel && b.MidiTrackIndex == n.Track
                    && n.Note >= b.MidiNoteMin && n.Note <= b.MidiNoteMax)
                {
                    packed = i; break;
                }
            }
            // Pass 2: wildcards (MidiTrackIndex = -1) on the same channel.
            if (packed < 0)
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    var b = bindings[i];
                    if (b.MidiChannel == n.Channel && b.MidiTrackIndex < 0
                        && n.Note >= b.MidiNoteMin && n.Note <= b.MidiNoteMax)
                    {
                        packed = i; break;
                    }
                }
            }
            if (packed < 0)
            {
                if (seenUnbound.Add((n.Channel, n.Track)))
                    GD.PushWarning($"[PS1Godot] MIDI channel {n.Channel} / track {n.Track} (note {n.Note}) has no matching PS1MusicChannel binding — those notes won't play.");
                continue;
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
