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
    /// <summary>
    /// Lookup name for Sound.PlayMacro("..."). Must be unique within the
    /// scene. Empty = the macro will never trigger (Sound.PlayMacro silently
    /// returns no-op when the name doesn't resolve).
    /// </summary>
    [ExportGroup("Identity")]
    [Export] public string MacroName { get; set; } = "";

    /// <summary>
    /// Frame-ordered event list. Authoring order doesn't matter — the
    /// exporter sorts by Frame ascending.
    /// </summary>
    [ExportGroup("Events")]
    [Export]
    public Godot.Collections.Array<PS1SoundMacroEvent> Events { get; set; } = new();

    /// <summary>
    /// Max simultaneous instances of this specific macro. 0 = no cap
    /// (limited only by the SFX voice pool). Useful for footsteps (cap 2 —
    /// two feet) and impact stingers (cap 1 — anti-spam).
    /// </summary>
    [ExportGroup("Voice budget")]
    [Export(PropertyHint.Range, "0,8,1")]
    public int MaxVoices { get; set; } = 0;

    /// <summary>
    /// Voice-stealing priority for the underlying voice allocator. The
    /// first sample event of the macro reserves a voice at this priority;
    /// subsequent events on the same macro instance reuse (or layer on)
    /// that voice. 0-255, default 64 (DEFAULT_SFX_PRIORITY).
    /// </summary>
    [Export(PropertyHint.Range, "0,255,1")]
    public int Priority { get; set; } = 64;

    /// <summary>
    /// Frames after a trigger during which subsequent Sound.PlayMacro
    /// calls for this macro are dropped. Anti-spam for button-mash callers
    /// and rapid animation events. 0 = no cooldown.
    /// </summary>
    [Export(PropertyHint.Range, "0,300,1,suffix:frames")]
    public int CooldownFrames { get; set; } = 0;
}
