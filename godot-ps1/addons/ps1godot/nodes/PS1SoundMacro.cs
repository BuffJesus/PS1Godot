using Godot;

namespace PS1Godot;

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
