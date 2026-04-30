using Godot;

namespace PS1Godot;

// Maps a contiguous note + velocity range to one PS1AudioClip sample.
// Owned by PS1Instrument; an instrument's Regions array is checked in
// declaration order at NoteOn time and the first region whose KeyMin/Max
// + VelocityMin/Max bracket the incoming note wins.
//
// **Scaffold only — runtime ignores.** Phase 0 of the true sequenced
// audio migration (see docs/handoff-true-sequenced-audio-plan.md). The
// authoring side lands now so a stable inspector exists for Phase 1+
// to build on; the runtime sequencer still uses the legacy
// PS1MusicChannel.AudioClipName + BaseNoteMidi path until Phase 2 bumps
// the splashpack format.
//
// XA constraint: AudioClipName must reference a PS1AudioClip whose
// Route is SPU (or Auto resolved to SPU). XA-routed clips stream via
// the disc's CD-input bus, not the SPU voice path, so the sequencer
// can't drive them. The Phase 2 exporter will GD.PushError on any
// region pointing at an XA/CDDA-routed clip.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_sample_region.svg")]
public partial class PS1SampleRegion : Resource
{
    /// <summary>
    /// Name of a PS1AudioClip on PS1Scene.AudioClips. Looked up by
    /// ClipName at export time, same lookup pattern as PS1MusicChannel.
    /// </summary>
    [ExportGroup("Sample")]
    [Export] public string AudioClipName { get; set; } = "";

    /// <summary>
    /// The MIDI note that plays this sample at its native pitch. Pitch
    /// shift = (incomingNote - RootKey) semitones, then mapped to the
    /// SPU's fp12 sample-rate register.
    /// </summary>
    [Export(PropertyHint.Range, "0,127,1")]
    public int RootKey { get; set; } = 60;

    /// <summary>
    /// Fine pitch trim, -100..+100 cents (one semitone). Combined with
    /// the RootKey-derived semitone shift before being converted to
    /// SPU pitch.
    /// </summary>
    [Export(PropertyHint.Range, "-100,100,1,suffix:cents")]
    public int TuneCents { get; set; } = 0;

    /// <summary>
    /// MIDI note range this region answers to. Inclusive on both sides.
    /// For multi-region instruments, gaps are silent and overlaps prefer
    /// earlier-declared regions (declaration-order wins).
    /// </summary>
    [ExportGroup("Range")]
    [Export(PropertyHint.Range, "0,127,1")]
    public int KeyMin { get; set; } = 0;

    [Export(PropertyHint.Range, "0,127,1")]
    public int KeyMax { get; set; } = 127;

    /// <summary>
    /// Velocity range. Lets a single region answer only to soft notes
    /// (e.g. KeyMin=0, VelMin=0, VelMax=63) while a louder layer
    /// covers VelMin=64..127 — classic two-velocity-layer instrument.
    /// </summary>
    [Export(PropertyHint.Range, "0,127,1")]
    public int VelocityMin { get; set; } = 0;

    [Export(PropertyHint.Range, "0,127,1")]
    public int VelocityMax { get; set; } = 127;

    /// <summary>
    /// When true, the SPU loops the sample between LoopStart and LoopEnd
    /// for as long as the note is held. The PSX SPU has dedicated loop
    /// address registers — looping is free at hardware level. Use for
    /// sustained instruments (strings, pads, organs).
    /// </summary>
    [ExportGroup("Loop")]
    [Export] public bool LoopEnabled { get; set; } = false;

    /// <summary>
    /// Loop bounds in sample frames. 0 = "from the start of the sample."
    /// -1 on LoopEnd = "to the end of the sample." Authors usually only
    /// need to set LoopStart (hold the body of a sustained sample) and
    /// leave LoopEnd at -1.
    /// </summary>
    [Export(PropertyHint.Range, "0,1048576,1,suffix:frames")]
    public int LoopStart { get; set; } = 0;

    [Export(PropertyHint.Range, "-1,1048576,1,suffix:frames")]
    public int LoopEnd { get; set; } = -1;

    /// <summary>
    /// Per-region volume multiplier, 0-127. Stacks with the parent
    /// PS1Instrument.Volume and the channel volume at runtime.
    /// </summary>
    [ExportGroup("Mix")]
    [Export(PropertyHint.Range, "0,127,1")]
    public int Volume { get; set; } = 100;

    /// <summary>
    /// 0 = full left, 64 = centre, 127 = full right. Stacks additively
    /// (clamped) with the parent instrument and channel pan.
    /// </summary>
    [Export(PropertyHint.Range, "0,127,1")]
    public int Pan { get; set; } = 64;

    /// <summary>
    /// When true, this region's A/D/S/R values replace the parent
    /// instrument's DefaultADSR. Lets a single instrument carry one
    /// body region with a slow attack and a "tap" region with a fast
    /// attack without splitting into two instruments.
    /// </summary>
    [ExportGroup("ADSR override")]
    [Export] public bool OverrideADSR { get; set; } = false;

    /// <summary>
    /// Attack/Decay/Sustain/Release as PSX SPU register-friendly bytes
    /// (0-15 each for envelope rates, 0-127 for sustain level). Phase 3
    /// wires these to the SPU ADSR registers via AudioManager. Layout
    /// matches the four channels of the existing
    /// audiomanager.cpp DEFAULT_ADSR pack so the export-time pack is a
    /// straight memcpy.
    /// </summary>
    [Export(PropertyHint.Range, "0,15,1")]  public int AttackRate  { get; set; } = 0;
    [Export(PropertyHint.Range, "0,15,1")]  public int DecayRate   { get; set; } = 10;
    [Export(PropertyHint.Range, "0,127,1")] public int SustainLevel { get; set; } = 100;
    [Export(PropertyHint.Range, "0,15,1")]  public int ReleaseRate { get; set; } = 15;

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        string name = property["name"].AsString();
        bool hidden = name switch
        {
            "LoopStart" or "LoopEnd" => !LoopEnabled,
            "AttackRate" or "DecayRate" or "SustainLevel" or "ReleaseRate" => !OverrideADSR,
            _ => false,
        };
        if (hidden) property["usage"] = (long)Godot.PropertyUsageFlags.Storage;
    }
}
