#if TOOLS
using System;
using System.Collections.Generic;
using Godot;
using PS1Godot.Exporter;

namespace PS1Godot.Tools;

// Regression tests for PS1MSerializer. Each test builds a small
// in-memory ParsedMidi that exercises one edge case from the MIDI
// bug audit, then asserts a structural property of the output blob.
// Not a full test framework — prints pass/fail to the Godot output,
// aggregates counts, returns true iff all passed.
//
// Run via Project > Tools > PS1Godot: Run MIDI Serializer Tests.
public static class MidiSerializerTests
{
    private delegate void TestFn();
    private static readonly (string Name, TestFn Run)[] s_tests =
    {
        ("B3: rescale ticks across two tempo segments",  TestTempoRescale),
        ("B3: single tempo at tick 0 passes through",    TestSingleTempoPassthrough),
        ("B3: pre-tempo region uses SMF default 120bpm", TestPreTempoDefault),
        ("B3: loop-start rescaled through tempo map",    TestLoopStartRescale),
        ("B4: serializer preserves NoteOff-before-On",   TestNoteOffBeforeNoteOnOrder),
        ("B7: >65535 events throws actionable error",    TestEventCountOverflow),
        ("Polyphony: chord spreads across lanes",        TestPolyphonyLanes),
        ("Polyphony: third overlapping note steals oldest", TestVoiceStealing),
        ("Empty bindings list rejected",                 TestEmptyBindingsThrows),
    };

    public static bool RunAll()
    {
        int pass = 0, fail = 0;
        GD.Print("[PS1Godot] Running MIDI serializer regression tests...");
        foreach (var (name, run) in s_tests)
        {
            try
            {
                run();
                GD.Print($"  pass  {name}");
                pass++;
            }
            catch (Exception e)
            {
                GD.PushError($"  FAIL  {name}: {e.Message}");
                fail++;
            }
        }
        GD.Print($"[PS1Godot] MIDI tests: {pass} passed, {fail} failed.");
        return fail == 0;
    }

    // ─── Fixtures ────────────────────────────────────────────────────

    private static MidiParser.ParsedMidi Midi(int tpq,
        List<MidiParser.MidiNoteEvent> notes,
        List<MidiParser.MidiTempoEvent>? tempos = null,
        int trackCount = 1) => new()
    {
        TicksPerQuarter = tpq,
        TrackCount = trackCount,
        Notes = notes,
        Tempos = tempos ?? new List<MidiParser.MidiTempoEvent>(),
    };

    private static MidiParser.MidiNoteEvent Note(uint tick,
        MidiParser.MidiEventKind kind, byte note = 60, byte ch = 0,
        byte vel = 100, int track = 0) => new()
    {
        AbsoluteTick = tick,
        Kind = kind,
        Channel = ch,
        Note = note,
        Velocity = vel,
        Track = track,
    };

    private static MidiParser.MidiTempoEvent Tempo(uint tick, uint mpq) =>
        new() { AbsoluteTick = tick, MicrosPerQuarter = mpq };

    private static PS1MSerializer.ChannelBinding Binding(int audioClip = 0,
        int midiCh = 0, int track = -1) => new()
    {
        MidiChannel = midiCh,
        MidiTrackIndex = track,
        AudioClipIndex = audioClip,
        BaseNoteMidi = 60,
        Volume = 100,
        Pan = 64,
        LoopSample = false,
        Percussion = false,
    };

    // ─── Blob readers (mirror PS1MSerializer byte layout) ────────────

    private static (ushort bpm, ushort tpq, byte chCount, ushort evCount, uint loopStart)
        ReadHeader(byte[] b)
    {
        if (b[0] != 'P' || b[1] != 'S' || b[2] != '1' || b[3] != 'M')
            throw new Exception("PS1M magic missing");
        ushort bpm = (ushort)(b[4] | (b[5] << 8));
        ushort tpq = (ushort)(b[6] | (b[7] << 8));
        byte chCount = b[8];
        ushort evCount = (ushort)(b[10] | (b[11] << 8));
        uint loopStart = (uint)(b[12] | (b[13] << 8) | (b[14] << 16) | (b[15] << 24));
        return (bpm, tpq, chCount, evCount, loopStart);
    }

    private static (uint tick, byte ch, byte kind, byte note, byte vel)
        ReadEvent(byte[] b, int chCount, int i)
    {
        int o = 16 + chCount * 8 + i * 8;
        uint tick = (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        return (tick, b[o + 4], b[o + 5], b[o + 6], b[o + 7]);
    }

    private static void AssertTrue(bool cond, string msg)
    {
        if (!cond) throw new Exception(msg);
    }

    private static void AssertEq<T>(T expected, T actual, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{context}: expected {expected}, got {actual}");
    }

    // ─── Tests ───────────────────────────────────────────────────────

    private static void TestTempoRescale()
    {
        // tpq=480, tempo map [(0→120bpm), (960→60bpm)]. Note at tick 1920:
        // 2 beats × 120bpm (1000ms) + 2 beats × 60bpm (2000ms) = 3000ms.
        // Rescaled to 120bpm reference: 3000ms × 480 / 500000µs = 2880 ticks.
        var midi = Midi(480,
            new()
            {
                Note(0, MidiParser.MidiEventKind.NoteOn),
                Note(1920, MidiParser.MidiEventKind.NoteOn, 61),
            },
            new()
            {
                Tempo(0, 500_000),
                Tempo(960, 1_000_000),
            });
        var blob = PS1MSerializer.Serialize(midi, new[] { Binding() }, null, -1);
        var (bpm, tpq, chCount, evCount, _) = ReadHeader(blob);
        AssertEq((ushort)120, bpm, "header bpm");
        AssertEq((ushort)480, tpq, "header tpq");
        AssertEq((ushort)2, evCount, "event count");
        AssertEq(0u, ReadEvent(blob, chCount, 0).tick, "event 0 tick");
        AssertEq(2880u, ReadEvent(blob, chCount, 1).tick, "event 1 tick (rescaled)");
    }

    private static void TestSingleTempoPassthrough()
    {
        var midi = Midi(480,
            new()
            {
                Note(100, MidiParser.MidiEventKind.NoteOn),
                Note(500, MidiParser.MidiEventKind.NoteOff),
            },
            new() { Tempo(0, 500_000) });
        var blob = PS1MSerializer.Serialize(midi, new[] { Binding() }, null, -1);
        var (_, _, chCount, evCount, _) = ReadHeader(blob);
        AssertEq((ushort)2, evCount, "event count");
        AssertEq(100u, ReadEvent(blob, chCount, 0).tick, "event 0 tick passthrough");
        AssertEq(500u, ReadEvent(blob, chCount, 1).tick, "event 1 tick passthrough");
    }

    private static void TestPreTempoDefault()
    {
        // First tempo at tick 500 (not 0) → ticks 0..500 use SMF default 500000 mpq.
        // Tempos[0] = 60bpm → refMpq = 1_000_000. Note at tick 960:
        //   seg0 (0..500, 500000 mpq): 500 × 500000 = 250_000_000
        //   seg1 (500..∞, 1000000 mpq): 460 × 1000000 = 460_000_000
        //   total = 710_000_000 / 1_000_000 = 710 ticks.
        var midi = Midi(480,
            new() { Note(960, MidiParser.MidiEventKind.NoteOn) },
            new() { Tempo(500, 1_000_000) });
        var blob = PS1MSerializer.Serialize(midi, new[] { Binding() }, null, -1);
        var (bpm, _, chCount, _, _) = ReadHeader(blob);
        AssertEq((ushort)60, bpm, "header bpm = first tempo's bpm");
        AssertEq(710u, ReadEvent(blob, chCount, 0).tick, "pre-tempo region uses default 120bpm");
    }

    private static void TestLoopStartRescale()
    {
        // Same tempo map as TestTempoRescale. loopStartBeat=4 → tick 1920 → 2880.
        var midi = Midi(480,
            new() { Note(0, MidiParser.MidiEventKind.NoteOn) },
            new() { Tempo(0, 500_000), Tempo(960, 1_000_000) });
        var blob = PS1MSerializer.Serialize(midi, new[] { Binding() }, null, 4);
        var (_, _, _, _, loopStart) = ReadHeader(blob);
        AssertEq(2880u, loopStart, "loop-start rescaled through tempo map");
    }

    private static void TestNoteOffBeforeNoteOnOrder()
    {
        // MidiParser is responsible for sorting NoteOff-before-NoteOn at the
        // same tick; PS1MSerializer must then preserve that order, otherwise a
        // re-trigger silences itself. Supply pre-sorted input and assert the
        // emitted events keep the order.
        var midi = Midi(480, new()
        {
            Note(0,   MidiParser.MidiEventKind.NoteOn),
            Note(480, MidiParser.MidiEventKind.NoteOff),
            Note(480, MidiParser.MidiEventKind.NoteOn),
        });
        var blob = PS1MSerializer.Serialize(midi, new[] { Binding() }, null, -1);
        var (_, _, chCount, evCount, _) = ReadHeader(blob);
        AssertEq((ushort)3, evCount, "event count");
        AssertEq((byte)0, ReadEvent(blob, chCount, 0).kind, "e0 NoteOn");
        AssertEq((byte)1, ReadEvent(blob, chCount, 1).kind, "e1 NoteOff");
        AssertEq((byte)0, ReadEvent(blob, chCount, 2).kind, "e2 NoteOn (retrigger after Off)");
    }

    private static void TestEventCountOverflow()
    {
        // Build 40_000 paired on/off events = 80_000 total, over the u16 limit.
        // Use 8 different notes so consecutive pairs don't collide on held-note
        // matching within the single binding.
        var notes = new List<MidiParser.MidiNoteEvent>(80_000);
        for (uint i = 0; i < 40_000; i++)
        {
            byte n = (byte)(60 + (i & 7));
            notes.Add(Note(i * 2, MidiParser.MidiEventKind.NoteOn, n));
            notes.Add(Note(i * 2 + 1, MidiParser.MidiEventKind.NoteOff, n));
        }
        var midi = Midi(480, notes);
        bool threw = false;
        try { PS1MSerializer.Serialize(midi, new[] { Binding() }, null, -1); }
        catch (InvalidOperationException e)
        {
            threw = true;
            AssertTrue(e.Message.Contains("65535") || e.Message.Contains("u16"),
                $"error message should mention the u16 limit: {e.Message}");
        }
        AssertTrue(threw, "expected InvalidOperationException on >65535 events");
    }

    private static void TestPolyphonyLanes()
    {
        // Two-note chord on one MIDI channel: with two bindings, one goes to
        // each lane rather than both piling onto the first.
        var midi = Midi(480, new()
        {
            Note(0, MidiParser.MidiEventKind.NoteOn, 60),
            Note(0, MidiParser.MidiEventKind.NoteOn, 64),
        });
        var bindings = new[] { Binding(audioClip: 0), Binding(audioClip: 1) };
        var blob = PS1MSerializer.Serialize(midi, bindings, null, -1);
        var (_, _, chCount, evCount, _) = ReadHeader(blob);
        AssertEq((ushort)2, evCount, "event count");
        var e0 = ReadEvent(blob, chCount, 0);
        var e1 = ReadEvent(blob, chCount, 1);
        AssertTrue(e0.ch != e1.ch, "chord spreads across two lanes (not same binding)");
    }

    private static void TestVoiceStealing()
    {
        // Three overlapping NoteOns, two bindings → third steals the oldest lane.
        var midi = Midi(480, new()
        {
            Note(0,   MidiParser.MidiEventKind.NoteOn, 60),
            Note(100, MidiParser.MidiEventKind.NoteOn, 64),
            Note(200, MidiParser.MidiEventKind.NoteOn, 67),
        });
        var bindings = new[] { Binding(audioClip: 0), Binding(audioClip: 1) };
        var blob = PS1MSerializer.Serialize(midi, bindings, null, -1);
        var (_, _, chCount, _, _) = ReadHeader(blob);
        var e0 = ReadEvent(blob, chCount, 0);
        var e1 = ReadEvent(blob, chCount, 1);
        var e2 = ReadEvent(blob, chCount, 2);
        AssertTrue(e0.ch != e1.ch, "first two notes take distinct lanes");
        AssertEq(e0.ch, e2.ch, "third note steals the oldest-held lane (same as first)");
    }

    private static void TestEmptyBindingsThrows()
    {
        var midi = Midi(480, new() { Note(0, MidiParser.MidiEventKind.NoteOn) });
        bool threw = false;
        try { PS1MSerializer.Serialize(midi, Array.Empty<PS1MSerializer.ChannelBinding>(), null, -1); }
        catch (InvalidOperationException) { threw = true; }
        AssertTrue(threw, "expected InvalidOperationException on empty bindings list");
    }
}
#endif
