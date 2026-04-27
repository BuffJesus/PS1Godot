using Godot;

namespace PS1Godot;

// One frame-keyed event in a PS1SoundMacro. Each event triggers a
// single sample dispatch through AudioManager when the macro's local
// frame counter reaches Frame.
//
// PitchOffset is in semitones (positive = higher). The runtime applies
// the same psyqo pitch-shift table as MusicSequencer — same octave
// range (±36 semitones cap), same rounding behaviour.
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

// A composite sound effect built from a sequence of frame-keyed
// sample events. Replaces hand-baked composite WAVs (chest open
// = wood + metal + sparkle) with three short clips + an event list.
//
// Macros run on the SFX voice pool — they never reserve voices and
// can run many instances concurrently, voice-allocator-permitting.
// MaxVoices caps simultaneous macro instances; CooldownFrames
// prevents the same macro from retriggering too quickly (footstep
// spam, button mash).
//
// Lua: Sound.PlayMacro("<MacroName>"). Returns a handle that
// Sound.StopMacro can use to cancel an in-flight macro early.
//
// **Phase 5 Stage A:** authoring scaffold. Runtime ignores macros
// until Stage B ships SoundMacroSequencer.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_audio_clip.svg")]
public partial class PS1SoundMacro : Resource
{
    [ExportGroup("Identity")]
    [Export] public string MacroName { get; set; } = "";

    [ExportGroup("Events")]
    // Frame-ordered event list. Authoring order doesn't matter at
    // runtime — the exporter sorts by Frame ascending.
    [Export]
    public Godot.Collections.Array<PS1SoundMacroEvent> Events { get; set; } = new();

    [ExportGroup("Voice budget")]
    // Max simultaneous instances of this specific macro. 0 = no cap
    // (limited only by the SFX voice pool). Useful for footsteps
    // (cap 2 — two feet) and impact stingers (cap 1 — anti-spam).
    [Export(PropertyHint.Range, "0,8,1")]
    public int MaxVoices { get; set; } = 0;

    // Voice-stealing priority for the underlying voice allocator. The
    // first sample event of the macro reserves a voice at this
    // priority; subsequent events on the same macro instance reuse
    // (or layer on) that voice. 0-255, default DEFAULT_SFX_PRIORITY=64.
    [Export(PropertyHint.Range, "0,255,1")]
    public int Priority { get; set; } = 64;

    // Frames after a trigger during which subsequent
    // Sound.PlayMacro("name") calls are dropped. Anti-spam for
    // button-mash callers and rapid animation events. 0 = no cooldown.
    [Export(PropertyHint.Range, "0,300,1,suffix:frames")]
    public int CooldownFrames { get; set; } = 0;
}
