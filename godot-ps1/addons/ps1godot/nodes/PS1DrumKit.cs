using Godot;

namespace PS1Godot;

// MIDI-note-keyed drum kit. Each entry maps a single MIDI note (kick =
// 36, snare = 38, closed hat = 42, etc.) to one PS1AudioClip. Drum
// kits replace the per-channel-per-drum binding pattern used today
// (which forces one PS1MusicChannel per drum) with a single resource
// the sequencer can index by note number.
//
// **Scaffold only — runtime ignores.** Phase 0 of the true sequenced
// audio migration (see docs/handoff-true-sequenced-audio-plan.md).
// Phase 2 wires the runtime + splashpack v28 to dispatch drum-channel
// notes through the kit instead of the legacy
// PS1MusicChannel.MidiNoteMin/Max filter pattern.
//
// MIDI channel 9 (10 in 1-indexed parlance) is conventionally the
// drum channel; the Phase 2 exporter will check the source MIDI for
// channel-9 events and require a DrumKit assignment to handle them.
//
// Storage: parallel arrays instead of a nested PS1DrumMapping
// Resource. Inspector shows them as separate arrays — slightly less
// elegant than nested resources but keeps the kit to one file and
// avoids a third [GlobalClass] resource type for a pure value bundle.
// All arrays MUST be the same length; the export-time validator
// surfaces a length mismatch as GD.PushError.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_drum_kit.svg")]
public partial class PS1DrumKit : Resource
{
    /// <summary>
    /// Stable name for cross-resource references. Defaults to the
    /// resource basename when blank.
    /// </summary>
    [ExportGroup("Identity")]
    [Export] public string KitName { get; set; } = "";

    /// <summary>
    /// MIDI note number for each drum (36=kick, 38=snare, 42=closedHat,
    /// 46=openHat, 49=crash, 51=ride, etc. — see General MIDI percussion
    /// standard).
    /// </summary>
    [ExportGroup("Mappings")]
    [Export]
    public Godot.Collections.Array<int> MidiNotes { get; set; } = new();

    /// <summary>
    /// Parallel array: PS1AudioClip name to play for each MidiNotes
    /// entry. Looked up by ClipName at export time.
    /// </summary>
    [Export]
    public Godot.Collections.Array<string> AudioClipNames { get; set; } = new();

    /// <summary>
    /// Parallel array: per-drum volume, 0-127. Defaults to 100 if the
    /// array is shorter than MidiNotes.
    /// </summary>
    [Export]
    public Godot.Collections.Array<int> Volumes { get; set; } = new();

    /// <summary>
    /// Parallel array: per-drum pan, 0=L / 64=center / 127=R. Defaults
    /// to 64 if the array is shorter than MidiNotes.
    /// </summary>
    [Export]
    public Godot.Collections.Array<int> Pans { get; set; } = new();

    /// <summary>
    /// Parallel array: choke-group id. Drums in the same non-zero
    /// group cut each other off when triggered — the canonical use is
    /// open hat / closed hat sharing one group so a closed hit
    /// silences a ringing open hat. 0 = no choke.
    /// </summary>
    [Export]
    public Godot.Collections.Array<int> ChokeGroups { get; set; } = new();

    /// <summary>
    /// Parallel array: voice-stealing priority, 0-255 (higher = more
    /// important). Kicks and snares typically get higher priority
    /// than ride or shaker. Defaults to 64 if the array is shorter
    /// than MidiNotes.
    /// </summary>
    [Export]
    public Godot.Collections.Array<int> Priorities { get; set; } = new();

    /// <summary>
    /// Export-time validation. Call from SceneCollector before writing drum
    /// data to surface parallel-array desync and empty kits early.
    /// </summary>
    public void Validate(string kitName)
    {
        int n = MidiNotes?.Count ?? 0;
        if (n == 0)
        {
            GD.PushWarning($"[PS1Godot] DrumKit '{kitName}': no MidiNotes entries — kit is empty and will produce silence.");
            return;
        }
        int clipCount = AudioClipNames?.Count ?? 0;
        if (clipCount < n)
        {
            GD.PushWarning($"[PS1Godot] DrumKit '{kitName}': AudioClipNames has {clipCount} entries but MidiNotes has {n}. " +
                           "Missing entries will have no sound. Keep the parallel arrays in sync.");
        }
    }
}
