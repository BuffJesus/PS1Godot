using Godot;

namespace PS1Godot;

// Sequenced-music resource. Source file is a Standard MIDI File (.mid);
// the exporter parses it at build time and produces a compact PS1M
// blob that the runtime's MusicSequencer plays back, using the scene's
// PS1AudioClip entries as the sample bank.
//
// In Lua: Music.Play("<SequenceName>") — the same name-table pattern
// Audio clips use. Multiple sequences can ship in one scene; only one
// plays at a time.
[Tool]
[GlobalClass]
public partial class PS1MusicSequence : Resource
{
    // Relative or res:// path to the source .mid. Parsed at export.
    [Export(PropertyHint.File, "*.mid,*.midi")]
    public string MidiFile { get; set; } = "";

    // Lua-facing name. Defaults to the .mid filename (without extension)
    // if left blank.
    [Export] public string SequenceName { get; set; } = "";

    // Tempo override. Most .mid files carry a tempo meta-event which the
    // exporter honours — set this only when the source file's tempo is
    // missing or wrong. 0 = use the file's tempo (or 120 as fallback).
    [Export(PropertyHint.Range, "0,300,1")]
    public int BpmOverride { get; set; } = 0;

    // When the sequencer reaches end-of-events, it resumes from this
    // beat. -1 = no loop (one-shot). 0 = loop back to the start.
    [Export(PropertyHint.Range, "-1,255,1")]
    public int LoopStartBeat { get; set; } = 0;

    // Per-MIDI-channel bindings to PS1AudioClips. Channels not listed
    // here are silent — drop a PS1MusicChannel per MIDI channel the
    // .mid uses, match MidiChannel to the source, and point AudioClipName
    // at an entry in PS1Scene.AudioClips. Max 8 per sequence (runtime
    // MusicSequencer::MAX_CHANNELS).
    [Export]
    public Godot.Collections.Array<PS1MusicChannel> Channels { get; set; } = new();
}
