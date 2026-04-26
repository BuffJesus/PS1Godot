using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PS1Godot.Exporter;

// Minimal Standard MIDI File (SMF) parser. Only the subset needed to
// produce a flat, absolute-tick event list for PS1M serialization:
// note on/off, tempo meta events, and the ticks-per-quarter-note from
// the header. Ignores meta text, controllers (except when
// ChannelVolume mapping is added later), program changes, system
// exclusive, etc.
//
// Supports format 0 (single track) and format 1 (multi-track, all
// tracks start at tick 0 and are merged by absolute tick). Format 2
// (sequential tracks) is not supported — we'd need a different
// merging strategy and SMF2 is almost never used.
public static class MidiParser
{
    public enum MidiEventKind
    {
        NoteOn,
        NoteOff,
    }

    public readonly struct MidiNoteEvent
    {
        public uint AbsoluteTick { get; init; }
        public MidiEventKind Kind { get; init; }
        public byte Channel { get; init; }     // 0-15
        public byte Note { get; init; }        // 0-127
        public byte Velocity { get; init; }    // 0-127
        // Source track index (format-1 SMFs put each instrument on its
        // own MTrk while sometimes leaving every event on MIDI channel
        // 0). Bindings can filter by track so two tracks-worth of notes
        // don't stomp each other on the same packed channel.
        public int Track { get; init; }
    }

    public readonly struct MidiTempoEvent
    {
        public uint AbsoluteTick { get; init; }
        // Microseconds per quarter-note (SMF standard). 500000 = 120 BPM.
        public uint MicrosPerQuarter { get; init; }
    }

    // Counts of MIDI channel events the parser intentionally discards.
    // Surfaced by the exporter as scaffold-stage warnings — the planned
    // sequenced-audio engine will consume these (program changes select
    // instruments, controllers drive channel volume/pan/expression,
    // pitch bend feeds the SPU pitch register), but until that lands
    // the source MIDI's intent is silently lost. The counters let the
    // author spot when a .mid is going to behave differently on PSX
    // than in their DAW.
    public sealed class SkippedEventCounts
    {
        public int ProgramChange { get; set; }   // 0xC0
        public int Controller { get; set; }      // 0xB0 (volume, pan, sustain, expression, ...)
        public int PitchBend { get; set; }       // 0xE0
        public int Aftertouch { get; set; }      // 0xA0 (poly) + 0xD0 (channel)
    }

    public sealed class ParsedMidi
    {
        public int TicksPerQuarter { get; init; }
        public int TrackCount { get; init; }
        public List<MidiNoteEvent> Notes { get; init; } = new();
        public List<MidiTempoEvent> Tempos { get; init; } = new();
        public SkippedEventCounts SkippedCounts { get; init; } = new();
    }

    public static ParsedMidi Parse(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 14)
            throw new InvalidDataException("MIDI file too short for a valid header.");

        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);

        // MThd header.
        if (ReadChunkId(r) != "MThd")
            throw new InvalidDataException("MIDI file missing MThd header.");
        uint headerLen = ReadU32BE(r);
        if (headerLen < 6) throw new InvalidDataException("MThd header length < 6.");
        ushort format = ReadU16BE(r);
        ushort trackCount = ReadU16BE(r);
        ushort division = ReadU16BE(r);
        // Skip any extra header bytes past the required 6.
        if (headerLen > 6) r.BaseStream.Seek(headerLen - 6, SeekOrigin.Current);

        if (format == 2)
            throw new InvalidDataException("MIDI format 2 (sequential tracks) is not supported.");

        if ((division & 0x8000) != 0)
            throw new InvalidDataException("SMPTE-division MIDI files are not supported; use ticks-per-quarter.");

        int tpq = division;
        var notes = new List<MidiNoteEvent>();
        var tempos = new List<MidiTempoEvent>();
        var skipped = new SkippedEventCounts();

        for (int t = 0; t < trackCount; t++)
        {
            if (ReadChunkId(r) != "MTrk")
            {
                // Skip unknown chunk types per the SMF spec.
                uint skipLen = ReadU32BE(r);
                r.BaseStream.Seek(skipLen, SeekOrigin.Current);
                t--;
                continue;
            }
            uint trackLen = ReadU32BE(r);
            long trackEnd = r.BaseStream.Position + trackLen;
            ParseTrack(r, trackEnd, notes, tempos, skipped, t);
        }

        // Sort events by absolute tick (format 1 tracks started at 0 each).
        // At the same tick: NoteOff MUST come before NoteOn. Otherwise a
        // sequence like  Off(60) + On(60)  at one tick would dispatch as
        // On then Off — and our mono-per-channel runtime sees the Off
        // matching the just-started note's number and silences it. That
        // kills every repeated-pitch note in the song. Putting Off first
        // closes the prior note cleanly, then On retriggers fresh.
        //
        // OrderBy().ThenBy() is stable; List.Sort() isn't (introsort). The
        // secondary keys (Track, Channel, Note) ensure re-exports of the
        // same .mid produce byte-identical output — useful for CI diffs
        // and reproducing audio bugs from a known seed.
        notes = notes
            .OrderBy(n => n.AbsoluteTick)
            .ThenByDescending(n => (int)n.Kind)
            .ThenBy(n => n.Track)
            .ThenBy(n => n.Channel)
            .ThenBy(n => n.Note)
            .ToList();
        tempos = tempos
            .OrderBy(t => t.AbsoluteTick)
            .ToList();

        return new ParsedMidi
        {
            TicksPerQuarter = tpq,
            TrackCount = trackCount,
            Notes = notes,
            Tempos = tempos,
            SkippedCounts = skipped,
        };
    }

    private static void ParseTrack(BinaryReader r, long trackEnd,
                                    List<MidiNoteEvent> notes,
                                    List<MidiTempoEvent> tempos,
                                    SkippedEventCounts skipped,
                                    int trackIndex)
    {
        uint absTick = 0;
        byte runningStatus = 0;

        while (r.BaseStream.Position < trackEnd)
        {
            uint delta = ReadVarLen(r);
            absTick += delta;

            byte status = r.ReadByte();
            if (status < 0x80)
            {
                // Running status — reuse previous status byte, current byte
                // is actually data1.
                r.BaseStream.Seek(-1, SeekOrigin.Current);
                if (runningStatus == 0)
                    throw new InvalidDataException("MIDI track used running status before any status byte.");
                status = runningStatus;
            }
            else if (status < 0xF0)
            {
                runningStatus = status;
            }

            if (status == 0xFF)
            {
                // Meta event: type (1 B) + varlen length + data.
                byte metaType = r.ReadByte();
                uint metaLen = ReadVarLen(r);
                long metaStart = r.BaseStream.Position;
                if (metaType == 0x51 && metaLen == 3)
                {
                    // Set tempo: 3-byte big-endian micros per quarter.
                    byte b0 = r.ReadByte();
                    byte b1 = r.ReadByte();
                    byte b2 = r.ReadByte();
                    uint mpq = ((uint)b0 << 16) | ((uint)b1 << 8) | b2;
                    tempos.Add(new MidiTempoEvent { AbsoluteTick = absTick, MicrosPerQuarter = mpq });
                }
                r.BaseStream.Seek(metaStart + metaLen, SeekOrigin.Begin);
            }
            else if (status == 0xF0 || status == 0xF7)
            {
                // SysEx — skip.
                uint sysLen = ReadVarLen(r);
                r.BaseStream.Seek(sysLen, SeekOrigin.Current);
            }
            else if (status >= 0xF1 && status <= 0xFE)
            {
                // System Common (F1-F6) and System Realtime (F8-FE).
                // Rare in SMF files but legal. Eating a fixed 1 byte
                // (old default behaviour) desynced the track parser for
                // any message with a data-byte count other than 1.
                //   F1 MTC quarter-frame: 1 data byte
                //   F2 Song Position:     2 data bytes
                //   F3 Song Select:       1 data byte
                //   F6 Tune Request:      0 data bytes
                //   F8..FE Realtime:      0 data bytes each
                //   F4, F5, FD are reserved/undefined — treat as 0-byte.
                switch (status)
                {
                    case 0xF1: case 0xF3:
                        r.ReadByte();
                        break;
                    case 0xF2:
                        r.ReadByte(); r.ReadByte();
                        break;
                    default:
                        // F4, F5, F6, F8, F9, FA, FB, FC, FD, FE — 0 data bytes.
                        break;
                }
            }
            else
            {
                byte kind = (byte)(status & 0xF0);
                byte channel = (byte)(status & 0x0F);
                switch (kind)
                {
                    case 0x80: // Note off
                    {
                        byte note = r.ReadByte();
                        byte vel = r.ReadByte();
                        notes.Add(new MidiNoteEvent
                        {
                            AbsoluteTick = absTick,
                            Kind = MidiEventKind.NoteOff,
                            Channel = channel,
                            Note = note,
                            Velocity = vel,
                            Track = trackIndex,
                        });
                        break;
                    }
                    case 0x90: // Note on (velocity 0 means off)
                    {
                        byte note = r.ReadByte();
                        byte vel = r.ReadByte();
                        notes.Add(new MidiNoteEvent
                        {
                            AbsoluteTick = absTick,
                            Kind = (vel == 0) ? MidiEventKind.NoteOff : MidiEventKind.NoteOn,
                            Channel = channel,
                            Note = note,
                            Velocity = vel,
                            Track = trackIndex,
                        });
                        break;
                    }
                    case 0xA0: // Poly aftertouch — skip, 2 data bytes
                        r.ReadByte(); r.ReadByte();
                        skipped.Aftertouch++;
                        break;
                    case 0xB0: // CC — skip, 2 data bytes
                        r.ReadByte(); r.ReadByte();
                        skipped.Controller++;
                        break;
                    case 0xE0: // Pitch bend — skip, 2 data bytes
                        r.ReadByte(); r.ReadByte();
                        skipped.PitchBend++;
                        break;
                    case 0xC0: // Program change — skip, 1 data byte
                        r.ReadByte();
                        skipped.ProgramChange++;
                        break;
                    case 0xD0: // Channel aftertouch — skip, 1 data byte
                        r.ReadByte();
                        skipped.Aftertouch++;
                        break;
                    default:
                        // Unreachable: status is verified 0x80..0xEF above.
                        throw new InvalidDataException(
                            $"MIDI parser: unexpected channel status byte 0x{status:X2} at position {r.BaseStream.Position - 1}.");
                }
            }
        }
    }

    private static string ReadChunkId(BinaryReader r)
    {
        return Encoding.ASCII.GetString(r.ReadBytes(4));
    }

    private static uint ReadU32BE(BinaryReader r)
    {
        byte a = r.ReadByte(), b = r.ReadByte(), c = r.ReadByte(), d = r.ReadByte();
        return ((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | d;
    }

    private static ushort ReadU16BE(BinaryReader r)
    {
        byte a = r.ReadByte(), b = r.ReadByte();
        return (ushort)(((uint)a << 8) | b);
    }

    // Variable-length quantity (7 bits per byte, high bit = continue).
    private static uint ReadVarLen(BinaryReader r)
    {
        uint v = 0;
        for (int i = 0; i < 4; i++)
        {
            byte b = r.ReadByte();
            v = (v << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0) return v;
        }
        throw new InvalidDataException("MIDI varlen exceeded 4 bytes.");
    }
}
