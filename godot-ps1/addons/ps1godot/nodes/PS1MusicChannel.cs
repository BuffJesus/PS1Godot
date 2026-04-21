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
public partial class PS1MusicChannel : Resource
{
    // 0-15. Matches the channel byte in the source MIDI's note events.
    [Export(PropertyHint.Range, "0,15,1")]
    public int MidiChannel { get; set; } = 0;

    // Optional track-index filter. -1 = accept notes from any track on
    // the chosen MidiChannel. Useful for format-1 MIDI files that put
    // every track on MIDI channel 0 (common with DAW exports) — set
    // MidiTrackIndex per binding to keep tracks from stomping each other.
    [Export(PropertyHint.Range, "-1,31,1")]
    public int MidiTrackIndex { get; set; } = -1;

    // Optional note-range filter — only notes with MidiNoteMin <= note
    // <= MidiNoteMax route to this channel. Defaults to the full range
    // (0..127). Combined with Percussion=true, this lets you map drum
    // hits to dedicated samples: one channel for kick (note 36), one
    // for snare (38), one for hi-hat (42). Set both to the same value
    // for a single-note pickup.
    [Export(PropertyHint.Range, "0,127,1")]
    public int MidiNoteMin { get; set; } = 0;

    [Export(PropertyHint.Range, "0,127,1")]
    public int MidiNoteMax { get; set; } = 127;

    // Name of a PS1AudioClip on PS1Scene.AudioClips. Empty entries are
    // skipped (and warned about) at export.
    [Export] public string AudioClipName { get; set; } = "";

    // The MIDI note number that plays the sample at its native pitch.
    // Middle C = 60. Used to compute (incomingNote - BaseNoteMidi)
    // semitones of shift, then converted to fp12 SPU pitch at runtime.
    [Export(PropertyHint.Range, "0,127,1")]
    public int BaseNoteMidi { get; set; } = 60;

    // Per-channel volume scaler, 0-127. Combined with note velocity
    // and the sequence's master volume at runtime.
    [Export(PropertyHint.Range, "0,127,1")]
    public int Volume { get; set; } = 100;

    // 0 = full left, 64 = centre, 127 = full right.
    [Export(PropertyHint.Range, "0,127,1")]
    public int Pan { get; set; } = 64;

    // Loop the sample for the duration of the held note (held drones,
    // pads). When false, the sample plays once and stops on key-off
    // or natural end.
    [Export] public bool LoopSample { get; set; } = false;

    // Percussion mode: ignore note pitch shift, always play the sample
    // at its native rate. Use this for one-shot drum kits where the
    // MIDI note picks the drum (kit-mapped via separate channels) and
    // pitch shifting would just sound wrong.
    [Export] public bool Percussion { get; set; } = false;
}
