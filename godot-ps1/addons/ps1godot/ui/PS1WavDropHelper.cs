#if TOOLS
using Godot;
using System.Collections.Generic;

namespace PS1Godot.UI;

// Shared drop-target plumbing for .wav files coming out of Godot's
// FileSystem dock. Used by PS1SoundMacroEventList +
// PS1SoundFamilyVariantList to turn a drag-drop into:
//   1) ensure a PS1AudioClip wrapping the .wav exists in the active
//      scene's PS1Scene.AudioClips (idempotent — drops a second time
//      reuse the existing clip),
//   2) return the resolved clip name(s) so the caller can append to
//      its own AudioClipNames / event list.
//
// FileSystem-dock drag data shape is {type: "files", files: [...]}.
// We only accept .wav (PSX export pipeline expects PCM WAV; .mp3/.ogg
// would just produce a clear "convert to WAV" error at export anyway).
internal static class PS1WavDropHelper
{
    public static bool IsWavDrop(Variant data)
    {
        var dict = data.AsGodotDictionary();
        if (dict == null || dict.Count == 0) return false;
        if (!dict.ContainsKey("type")) return false;
        if (dict["type"].AsString() != "files") return false;
        if (!dict.ContainsKey("files")) return false;
        var files = dict["files"].AsStringArray();
        if (files == null || files.Length == 0) return false;
        foreach (var f in files)
        {
            if (f != null && f.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static List<string> ExtractWavPaths(Variant data)
    {
        var result = new List<string>();
        var dict = data.AsGodotDictionary();
        if (dict == null || !dict.ContainsKey("files")) return result;
        var files = dict["files"].AsStringArray();
        if (files == null) return result;
        foreach (var f in files)
        {
            if (!string.IsNullOrEmpty(f) &&
                f.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase))
            {
                result.Add(f);
            }
        }
        return result;
    }

    // Idempotent: returns the existing clip name if one already wraps
    // this .wav, otherwise creates a new PS1AudioClip and appends it
    // to the scene's AudioClips. Returns "" when the scene has no
    // PS1Scene root (caller should silently no-op).
    public static string EnsureClipInScene(string wavPath)
    {
        var ps1Scene = FindPS1Scene();
        if (ps1Scene == null) return "";

        if (ps1Scene.AudioClips == null)
            ps1Scene.AudioClips = new Godot.Collections.Array<PS1AudioClip>();

        // Reuse if a clip already references this .wav.
        foreach (var existing in ps1Scene.AudioClips)
        {
            if (existing?.Stream?.ResourcePath == wavPath)
                return ResolveClipName(existing);
        }

        var stream = ResourceLoader.Load<AudioStream>(wavPath);
        if (stream == null)
        {
            GD.PushWarning($"[PS1Godot] Drop: failed to load '{wavPath}' as AudioStream — " +
                           "is it imported? Try Reimport on the file in FileSystem.");
            return "";
        }

        string name = System.IO.Path.GetFileNameWithoutExtension(wavPath);
        var clip = new PS1AudioClip { ClipName = name, Stream = stream };
        ps1Scene.AudioClips.Add(clip);
        // PS1Scene is a Node3D, not a Resource — no EmitChanged. Mutating
        // the AudioClips array dirties the scene file via Godot's normal
        // tracking; NotifyPropertyListChanged refreshes any open inspector
        // showing the array.
        ps1Scene.NotifyPropertyListChanged();
        GD.Print($"[PS1Godot] Drop: added '{name}' to PS1Scene.AudioClips ({wavPath}).");
        return name;
    }

    private static string ResolveClipName(PS1AudioClip? clip)
    {
        if (clip == null) return "";
        if (!string.IsNullOrWhiteSpace(clip.ClipName)) return clip.ClipName;
        if (!string.IsNullOrEmpty(clip.Stream?.ResourcePath))
            return System.IO.Path.GetFileNameWithoutExtension(clip.Stream.ResourcePath);
        return "";
    }

    private static PS1Scene? FindPS1Scene()
    {
        var root = EditorInterface.Singleton?.GetEditedSceneRoot();
        return root == null ? null : Walk(root);

        static PS1Scene? Walk(Node n)
        {
            if (n is PS1Scene s) return s;
            foreach (var c in n.GetChildren())
                if (c is Node child)
                {
                    var found = Walk(child);
                    if (found != null) return found;
                }
            return null;
        }
    }
}
#endif
