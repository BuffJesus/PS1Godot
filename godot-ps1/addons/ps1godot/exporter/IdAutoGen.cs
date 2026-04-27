#if TOOLS
using System;
using System.Text.RegularExpressions;
using Godot;

namespace PS1Godot.Exporter;

// Stable-ID auto-gen for PS1MeshInstance / PS1MeshGroup nodes — the
// Godot mirror of tools/blender-addon/.../utils/ids.py. Both tools
// follow the same rule: generate when empty, preserve when not.
//
// Used by BlenderMetadataWriter before writing a sidecar — no point
// emitting a JSON keyed on `<mesh_id>.ps1meshmeta.json` when mesh_id
// is empty. Idempotent: returns immediately when the node already has
// an asset_id + mesh_id.
public static class IdAutoGen
{
    // Allow ASCII letters, digits, dot, underscore, dash. Everything
    // else collapses to underscore. Matches the Python slugify pattern
    // in tools/blender-addon/.../utils/ids.py.
    private static readonly Regex SlugRe = new(@"[^A-Za-z0-9._-]+", RegexOptions.Compiled);

    /// <summary>Returns a fresh opaque asset_id (UUID hex, 32 chars).</summary>
    public static string NewAssetId() => Guid.NewGuid().ToString("N");

    /// <summary>Slugify a node name into a wire-safe ID fragment.</summary>
    public static string SlugifyName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "unnamed";
        string s = SlugRe.Replace(name, "_").Trim('_');
        return string.IsNullOrEmpty(s) ? "unnamed" : s;
    }

    /// <summary>Fill in empty asset_id / mesh_id on a PS1MeshInstance.</summary>
    /// <returns>True if any field was mutated (caller should mark scene dirty).</returns>
    public static bool EnsureIds(PS1MeshInstance pmi)
    {
        bool changed = false;
        if (string.IsNullOrEmpty(pmi.AssetId)) { pmi.AssetId = NewAssetId(); changed = true; }
        if (string.IsNullOrEmpty(pmi.MeshId))  { pmi.MeshId  = SlugifyName(pmi.Name); changed = true; }
        return changed;
    }

    /// <summary>Fill in empty asset_id / mesh_id on a PS1MeshGroup.</summary>
    public static bool EnsureIds(PS1MeshGroup pmg)
    {
        bool changed = false;
        if (string.IsNullOrEmpty(pmg.AssetId)) { pmg.AssetId = NewAssetId(); changed = true; }
        if (string.IsNullOrEmpty(pmg.MeshId))  { pmg.MeshId  = SlugifyName(pmg.Name); changed = true; }
        return changed;
    }
}
#endif
