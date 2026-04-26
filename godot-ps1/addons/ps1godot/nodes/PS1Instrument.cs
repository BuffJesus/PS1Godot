using Godot;

namespace PS1Godot;

// Bank-style instrument definition. One instrument = one logical
// "voice" of an orchestra (piano, bass, soft bell, etc.). Replaces
// the legacy PS1MusicChannel.AudioClipName direct binding for
// multi-sample / pitch-shifted instruments.
//
// **Scaffold only — runtime ignores.** Phase 0 of the true sequenced
// audio migration (see docs/handoff-true-sequenced-audio-plan.md).
// Phase 1 wires PS1MusicChannel.Instrument (optional) so a single-region
// instrument behaves identically to the existing direct binding;
// Phase 2 lights up multi-region keymaps and program-change events
// when the splashpack format bumps to v28.
//
// At NoteOn time the runtime walks Regions in declaration order and
// picks the first region whose KeyMin/KeyMax + VelocityMin/VelocityMax
// bracket the incoming note. Unmatched notes are silent.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_music_sequence.svg")]
public partial class PS1Instrument : Resource
{
    [ExportGroup("Identity")]
    // Stable name for cross-resource references (channel bindings,
    // future ProgramChange tables). Defaults to the resource basename
    // when blank.
    [Export] public string InstrumentName { get; set; } = "";

    // Stable numeric id used by MIDI ProgramChange events. 0-127 to
    // match General MIDI. Use 0 for the default/primary instrument
    // when no explicit program change is in the source MIDI.
    [Export(PropertyHint.Range, "0,127,1")]
    public int ProgramId { get; set; } = 0;

    [ExportGroup("Regions")]
    // Sample regions, scanned in declaration order at NoteOn time. A
    // single-region instrument with KeyMin=0 / KeyMax=127 is
    // wire-equivalent to the legacy PS1MusicChannel.AudioClipName +
    // BaseNoteMidi binding. Multi-region instruments cover wider
    // keyboards with fewer source samples (one bass sample per octave
    // pitch-shifted across the surrounding range).
    [Export]
    public Godot.Collections.Array<PS1SampleRegion> Regions { get; set; } = new();

    [ExportGroup("Default ADSR")]
    // Envelope used for any region that doesn't OverrideADSR. Values
    // map to PSX SPU ADSR register fields — Phase 3 packs them into
    // the existing AudioManager DEFAULT_ADSR slot at NoteOn time.
    [Export(PropertyHint.Range, "0,15,1")]  public int AttackRate  { get; set; } = 0;
    [Export(PropertyHint.Range, "0,15,1")]  public int DecayRate   { get; set; } = 10;
    [Export(PropertyHint.Range, "0,127,1")] public int SustainLevel { get; set; } = 100;
    [Export(PropertyHint.Range, "0,15,1")]  public int ReleaseRate { get; set; } = 15;

    [ExportGroup("Mix")]
    // Per-instrument volume multiplier, 0-127. Stacks with region
    // volume, channel volume, and master volume at runtime.
    [Export(PropertyHint.Range, "0,127,1")]
    public int Volume { get; set; } = 100;

    // 0 = full left, 64 = centre, 127 = full right.
    [Export(PropertyHint.Range, "0,127,1")]
    public int Pan { get; set; } = 64;

    [ExportGroup("Voice budget")]
    // Voice-stealing priority. Higher = more important. Phase 4 voice
    // allocator drops/steals lowest-priority voices first when the SPU
    // pool is exhausted. Use 0-127 to leave headroom for SFX (priority
    // 200+) and dialog (priority 250+).
    [Export(PropertyHint.Range, "0,255,1")]
    public int Priority { get; set; } = 64;

    // Hard cap on simultaneous voices for this instrument. 0 = no
    // limit (limited only by the global voice pool). Useful for
    // pad/string instruments that would otherwise eat the whole pool
    // on chord-heavy passages.
    [Export(PropertyHint.Range, "0,24,1")]
    public int PolyphonyLimit { get; set; } = 0;

    // Pitch-bend range in semitones. MIDI PitchBend events scale to
    // this range at NoteOn time. Default 2 matches the GM standard.
    [Export(PropertyHint.Range, "0,24,1,suffix:semitones")]
    public int PitchBendRange { get; set; } = 2;
}
