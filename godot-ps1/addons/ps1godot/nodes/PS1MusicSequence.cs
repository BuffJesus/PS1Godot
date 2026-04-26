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
[Icon("res://addons/ps1godot/icons/ps1_music_sequence.svg")]
public partial class PS1MusicSequence : Resource
{
    [ExportGroup("Source")]
    // Relative or res:// path to the source .mid. Parsed at export.
    [Export(PropertyHint.File, "*.mid,*.midi")]
    public string MidiFile { get; set; } = "";

    // Lua-facing name. Defaults to the .mid filename (without extension)
    // if left blank.
    [Export] public string SequenceName { get; set; } = "";

    [ExportGroup("Playback")]
    // Tempo override. Most .mid files carry a tempo meta-event which the
    // exporter honours — set this only when the source file's tempo is
    // missing or wrong. 0 = use the file's tempo (or 120 as fallback).
    [Export(PropertyHint.Range, "0,300,1,suffix:bpm")]
    public int BpmOverride { get; set; } = 0;

    // When the sequencer reaches end-of-events, it resumes from this
    // beat. -1 = no loop (one-shot). 0 = loop back to the start.
    [Export(PropertyHint.Range, "-1,255,1")]
    public int LoopStartBeat { get; set; } = 0;

    [ExportGroup("Channels")]
    // Per-MIDI-channel bindings to PS1AudioClips. Channels not listed
    // here are silent — drop a PS1MusicChannel per MIDI channel the
    // .mid uses, match MidiChannel to the source, and point AudioClipName
    // at an entry in PS1Scene.AudioClips. Max 24 per sequence (runtime
    // MusicSequencer::MAX_CHANNELS, matches SPU MAX_VOICES). Up to
    // MAX_SEQUENCES=8 sequences may coexist in one splashpack.
    //
    // Phase 2 (see docs/handoff-true-sequenced-audio-plan.md): a channel
    // whose Instrument has multiple PS1SampleRegions expands into one
    // binding per region at export time. KeyMin/KeyMax on each region
    // becomes that binding's MidiNoteMin/Max — the existing pitch-range
    // routing in PS1MSerializer routes each NoteOn to the matching
    // region. Watch the post-expansion binding count vs the 24 cap.
    [Export]
    public Godot.Collections.Array<PS1MusicChannel> Channels { get; set; } = new();

    [ExportGroup("Drums")]
    // Optional drum kit. When set, every NoteOn whose MIDI channel
    // equals DrumMidiChannel routes through the kit instead of the
    // regular Channels list — each PS1DrumKit mapping becomes its own
    // binding (MidiNoteMin == MidiNoteMax == kit-mapping note), and
    // pitch is locked to the sample's native rate (Percussion=true on
    // the binding). Lets one resource describe the whole percussion
    // section without a PS1MusicChannel per drum.
    //
    // The kit doesn't filter unmapped notes — drum notes with no entry
    // in the kit's MidiNotes array are silent and surface a warning
    // at export time.
    [Export] public PS1DrumKit? DrumKit { get; set; }

    // The MIDI channel whose notes the DrumKit catches. GM convention
    // is channel 9 (1-indexed channel 10), the standard drum bus on
    // every commercial DAW. 0-15 valid; -1 disables the kit even when
    // DrumKit is set, useful for temporarily muting drums.
    [Export(PropertyHint.Range, "-1,15,1")]
    public int DrumMidiChannel { get; set; } = 9;
}
