using Godot;

namespace PS1Godot;

// One frame-keyed event in a PS1SoundMacro. Each event triggers a
// single sample dispatch through AudioManager when the macro's local
// frame counter reaches Frame.
//
// PitchOffset is in semitones (positive = higher). The runtime applies
// the same psyqo pitch-shift table as MusicSequencer — same octave
// range (±36 semitones cap), same rounding behaviour.
//
// Lives in its own file (vs. nested in PS1SoundMacro.cs) so Godot
// generates a distinct .uid for the type — required for tscn
// sub_resource deserialization to disambiguate from PS1SoundMacro
// instances on the same parent.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_audio_clip.svg")]
public partial class PS1SoundMacroEvent : Resource
{
    // Local frame within the macro. 0 = trigger frame. Macros tick on
    // the same 30 FPS clock as the rest of the runtime.
    [Export(PropertyHint.Range, "0,3600,1,suffix:frames")]
    public int Frame { get; set; } = 0;

    // PS1AudioClip name (looked up against the scene's clip table at
    // runtime). Empty = silent event slot (placeholder during
    // authoring).
    [Export] public string AudioClipName { get; set; } = "";

    // 0-128 (128 = full). Stacks with the scene-wide master volume
    // and the macro's overall volume.
    [Export(PropertyHint.Range, "0,128,1")]
    public int Volume { get; set; } = 128;

    // 0 = full left, 64 = centre, 127 = full right.
    [Export(PropertyHint.Range, "0,127,1")]
    public int Pan { get; set; } = 64;

    // Semitone shift relative to the clip's authored pitch. Common
    // uses: -3 to +3 for variation, +12 for "child voice" pitch-up,
    // -12 for sub-bass thump on impacts.
    [Export(PropertyHint.Range, "-24,24,1,suffix:st")]
    public int PitchOffset { get; set; } = 0;
}
