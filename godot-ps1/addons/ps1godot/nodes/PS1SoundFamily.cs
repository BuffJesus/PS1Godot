using Godot;

namespace PS1Godot;

// Variation pool: pick one of N similar clips and dispatch with
// jittered pitch / volume / pan. Replaces "I authored 8 footstep
// WAVs and round-robin them" with "I authored 2 footsteps and let
// the runtime randomise."
//
// Lua: Sound.PlayFamily("<FamilyName>"). Picks a clip from
// AudioClipNames (with anti-repeat if AvoidRepeat is true), applies
// the configured jitter, and dispatches via AudioManager.play with
// the family's priority.
//
// **Phase 5 Stage A:** authoring scaffold. Runtime ignores families
// until Stage B ships the dispatch path.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_audio_clip.svg")]
public partial class PS1SoundFamily : Resource
{
    [ExportGroup("Identity")]
    [Export] public string FamilyName { get; set; } = "";

    [ExportGroup("Variants")]
    // PS1AudioClip names this family draws from. Each Sound.PlayFamily
    // picks one at random (or round-robin if AvoidRepeat is true).
    // 1-N entries valid; 1 entry = no clip variety, only param jitter.
    [Export]
    public Godot.Collections.Array<string> AudioClipNames { get; set; } = new();

    // When true, runtime tracks the most recently played clip and
    // re-rolls until it picks a different one. Avoids the back-to-
    // back identical sample that breaks the variation illusion.
    // No-op when AudioClipNames has only one entry.
    [Export] public bool AvoidRepeat { get; set; } = true;

    [ExportGroup("Jitter")]
    // Pitch range applied per dispatch, in semitones.
    // Random offset = uniform(PitchSemitonesMin, PitchSemitonesMax).
    // Typical: -3..+3 for footsteps; -1..+1 for UI ticks; ±0 to disable.
    [Export(PropertyHint.Range, "-12,12,1,suffix:st")]
    public int PitchSemitonesMin { get; set; } = -2;

    [Export(PropertyHint.Range, "-12,12,1,suffix:st")]
    public int PitchSemitonesMax { get; set; } = 2;

    // Volume range, 0-128. Random pick per dispatch, applied as the
    // base volume the runtime hands to AudioManager.play.
    [Export(PropertyHint.Range, "0,128,1")]
    public int VolumeMin { get; set; } = 96;

    [Export(PropertyHint.Range, "0,128,1")]
    public int VolumeMax { get; set; } = 120;

    // Pan jitter offset around the centre. 0 = mono / no jitter.
    // Typical: 6 for subtle stereo movement on footsteps.
    [Export(PropertyHint.Range, "0,32,1")]
    public int PanJitter { get; set; } = 0;

    [ExportGroup("Voice budget")]
    [Export(PropertyHint.Range, "0,255,1")]
    public int Priority { get; set; } = 64;

    // Anti-spam: drops back-to-back triggers within this many frames.
    // Typical: 3 for footsteps so stair-running doesn't paper-thin
    // into a buzz. 0 = no cooldown.
    [Export(PropertyHint.Range, "0,60,1,suffix:frames")]
    public int CooldownFrames { get; set; } = 0;
}
