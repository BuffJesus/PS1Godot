using Godot;

namespace PS1Godot;

// One-shot audio cue triggered at a specific frame inside a PS1Cutscene.
// Add as a child of the PS1Cutscene node. The runtime fires Audio.Play
// with the resolved clip when the cutscene reaches Frame, then advances
// to the next event in the list.
//
// ClipName must match a PS1AudioClip.ClipName authored in
// PS1Scene.AudioClips. Resolved to a clip index at export.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_audio_event.svg")]
public partial class PS1AudioEvent : Node
{
    [ExportGroup("Timing")]
    // Cutscene frame to fire on (0-based, capped at the cutscene's
    // TotalFrames - 1).
    [Export(PropertyHint.Range, "0,8191,1,suffix:frames")]
    public int Frame { get; set; } = 0;

    [ExportGroup("Playback")]
    // Authored clip name — must match an entry in PS1Scene.AudioClips.
    // Empty / unresolved names produce a warning at export.
    [Export] public string ClipName { get; set; } = "";

    // 0–127, runtime maps to SPU hardware volume range. 100 ≈ standard
    // "loud but not max" gain, leaves headroom for SFX overlap.
    [Export(PropertyHint.Range, "0,127,1")]
    public int Volume { get; set; } = 100;

    // 0 = full left, 64 = centered, 127 = full right.
    [Export(PropertyHint.Range, "0,127,1")]
    public int Pan { get; set; } = 64;
}
