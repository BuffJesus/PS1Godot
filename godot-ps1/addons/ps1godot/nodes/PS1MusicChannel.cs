using Godot;

namespace PS1Godot;

// Per-channel binding inside a PS1MusicSequence. Each MIDI channel
// referenced by the source .mid file maps to one PS1AudioClip via the
// scene's PS1Scene.AudioClips list (looked up by ClipName at export
// time). Notes on the MIDI channel become note-on / note-off events
// dispatched to that clip with a pitch shift derived from
// (note - BaseNoteMidi).
//
// Authoring tip: think of one channel = one instrument sample. A
// piano + bass + drum kit MIDI needs three PS1MusicChannel entries,
// each pointing at a different ADPCM sample.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_music_channel.svg")]
public partial class PS1MusicChannel : Resource
{
    /// <summary>
    /// 0-15. Matches the channel byte in the source MIDI's note events.
    /// </summary>
    [ExportGroup("MIDI filter")]
    [Export(PropertyHint.Range, "0,15,1")]
    public int MidiChannel { get; set; } = 0;

    /// <summary>
    /// Optional track-index filter. -1 = accept notes from any track on the
    /// chosen MidiChannel. Useful for format-1 MIDI files that put every
    /// track on MIDI channel 0 (common with DAW exports) — set per binding
    /// to keep tracks from stomping each other.
    /// </summary>
    [Export(PropertyHint.Range, "-1,31,1")]
    public int MidiTrackIndex { get; set; } = -1;

    /// <summary>
    /// Lower bound of the note-range filter. Only notes with MidiNoteMin
    /// &lt;= note &lt;= MidiNoteMax route to this channel. Combined with
    /// Percussion=true, this lets you map drum hits to dedicated samples:
    /// one channel for kick (36), one for snare (38), one for hi-hat (42).
    /// Set both bounds to the same value for a single-note pickup.
    /// </summary>
    [Export(PropertyHint.Range, "0,127,1")]
    public int MidiNoteMin { get; set; } = 0;

    /// <summary>
    /// Upper bound of the note-range filter. See MidiNoteMin.
    /// </summary>
    [Export(PropertyHint.Range, "0,127,1")]
    public int MidiNoteMax { get; set; } = 127;

    /// <summary>
    /// Optional reference to a PS1Instrument. When set, AudioClipName /
    /// BaseNoteMidi / LoopSample below are SILENTLY OVERRIDDEN at export by
    /// the instrument's first region. Leave null to use the direct fields.
    /// Multi-region keymap selection is Phase 2 — single-region instruments
    /// are wire-equivalent to the direct binding today.
    /// </summary>
    [ExportGroup("Sample")]
    [Export] public PS1Instrument? Instrument { get; set; }

    /// <summary>
    /// Name of a PS1AudioClip on PS1Scene.AudioClips. Empty = skipped (and
    /// warned about) at export. Ignored when Instrument is set.
    /// </summary>
    [Export] public string AudioClipName { get; set; } = "";

    /// <summary>
    /// The MIDI note number that plays the sample at its native pitch.
    /// Middle C = 60. Used to compute (incomingNote - BaseNoteMidi)
    /// semitones of shift, then converted to fp12 SPU pitch at runtime.
    /// Ignored when Instrument is set (resolved from Regions[0].RootKey).
    /// </summary>
    [Export(PropertyHint.Range, "0,127,1")]
    public int BaseNoteMidi { get; set; } = 60;

    /// <summary>
    /// Loop the sample for the duration of the held note (held drones,
    /// pads). When false, the sample plays once and stops on key-off or
    /// natural end. Ignored when Instrument is set (resolved from
    /// Regions[0].LoopEnabled).
    /// </summary>
    [Export] public bool LoopSample { get; set; } = false;

    /// <summary>
    /// Percussion mode: ignore note pitch shift, always play the sample at
    /// its native rate. Use this for one-shot drum kits where the MIDI note
    /// picks the drum (kit-mapped via separate channels) and pitch shifting
    /// would just sound wrong.
    /// </summary>
    [Export] public bool Percussion { get; set; } = false;

    /// <summary>
    /// Per-channel volume scaler, 0-127. Combined with note velocity and
    /// the sequence's master volume at runtime.
    /// </summary>
    [ExportGroup("Mix")]
    [Export(PropertyHint.Range, "0,127,1")]
    public int Volume { get; set; } = 100;

    /// <summary>
    /// Stereo pan. 0 = full left, 64 = centre, 127 = full right.
    /// </summary>
    [Export(PropertyHint.Range, "0,127,1")]
    public int Pan { get; set; } = 64;
}
