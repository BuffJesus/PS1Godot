using Godot;

namespace PS1Godot;

// When this clip is expected to live in SPU RAM. Only `Gameplay` clips
// count against the scene's SPU budget — MenuOnly and LoadOnDemand
// clips are either resident during a menu state or streamed/loaded on
// trigger. Tracks Phase 2.5 REF-GAP-9 ("per-area SPU accounting") in
// the roadmap. For now the flag is advisory: all clips still ship in
// the initial .spu blob, but the dock's SPU bar subtracts non-resident
// clips so the author sees the budget they'd have once streaming ships.
public enum PS1AudioClipResidency
{
    // Always resident — sound effects, looping music, ambient beds.
    Gameplay = 0,
    // Only resident while a menu/pause canvas is active. Freed
    // otherwise. Good for menu confirm/cancel SFX.
    MenuOnly = 1,
    // Streamed from disc (XA/CDDA) or loaded on explicit trigger.
    // Good for long dialog that's rare enough to not need RAM residency.
    LoadOnDemand = 2,
}

// Authored PS1 audio clip. Points at an imported Godot AudioStreamWav; at
// export time we resample/downmix if needed, encode to PSX SPU ADPCM, and
// stuff the bytes in the splashpack's .spu sidecar file.
//
// In Lua: Audio.Play("<ClipName>") — the name table in the splashpack lets
// the runtime resolve names to clip indices.
[Tool]
[GlobalClass]
public partial class PS1AudioClip : Resource
{
    // Accepts any AudioStream so users can drop in whatever Godot imported,
    // but at export time we only decode AudioStreamWav (raw PCM in .Data).
    // .mp3 / .ogg imports as AudioStreamMP3 / AudioStreamOggVorbis, which
    // don't expose samples synchronously — the exporter reports a clear
    // error and asks for WAV conversion. Rationale: PS1 audio pipeline is
    // lossy already (ADPCM); going mp3 → wav → adpcm keeps the lossy step
    // count the same and avoids pulling in an mp3 decoder dependency.
    [Export] public AudioStream? Stream { get; set; }

    // Name used by Lua Audio.Play("..."). Falls back to the resource basename
    // if empty, but authored names are stable across renames of the asset
    // file.
    [Export] public string ClipName { get; set; } = "";

    // Whether the SPU should repeat this sample. On-hardware this writes the
    // sampleRepeatAddr register; loop points are encoded in the ADPCM stream's
    // final block flags byte.
    [Export] public bool Loop { get; set; } = false;

    // How aggressively this clip is kept in SPU RAM. Affects only the dock's
    // SPU budget display for now; streaming / on-demand loading lands with
    // Phase 2.5. Defaults to Gameplay for backwards compatibility — mark
    // event-triggered dialog and menu-specific SFX explicitly to reclaim
    // the budget.
    [Export] public PS1AudioClipResidency Residency { get; set; } = PS1AudioClipResidency.Gameplay;
}
