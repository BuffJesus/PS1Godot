using Godot;

namespace PS1Godot;

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
}
