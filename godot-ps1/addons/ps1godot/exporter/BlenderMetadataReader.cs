#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace PS1Godot.Exporter;

// Reads `<mesh_id>.ps1meshmeta.json` sidecars produced by the Blender
// add-on (tools/blender-addon/ps1godot_blender/exporters/
// metadata_exporter.py) and applies the metadata to matching
// PS1MeshInstance / PS1MeshGroup nodes in the active scene.
//
// Match strategy: by mesh_id, falling back to source_object_name. The
// Blender side auto-derives mesh_id from the Blender Object's name
// (slugified), so by default it matches the Godot scene-tree Name.
// Authors who customize mesh_id on the Blender side should set the
// matching MeshId on the Godot node manually, OR we can later add a
// "find by AssetId in saved manifest" pass.
//
// Wire identifiers must match the C# enum-member names verbatim — see
// exporter/PS1Metadata.cs for the round-trip-stability contract.
//
// **Read-only with respect to disk.** This reader never writes back to
// the .blend or the JSON. Authors edit the Blender add-on, export
// fresh sidecars, then run this reader to bring the values into the
// .tscn. Save the scene to persist.
//
// Cross-references:
//   docs/ps1godot_blender_addon_integration_plan.md § 5.3 round-trip rule
//   tools/blender-addon/ps1godot_blender/utils/json_io.py SCHEMA_VERSION
public static class BlenderMetadataReader
{
    // We accept any sidecar version <= MaxSupportedVersion. Bump this
    // when we add fields the importer needs to read; older sidecars
    // fall through cleanly because unknown fields are ignored.
    private const int MaxSupportedVersion = 1;

    public sealed class Result
    {
        public int SidecarsFound = 0;
        public int Applied       = 0;       // matched + wrote at least one field
        public int Unmatched     = 0;       // sidecar with no scene node
        public int VersionSkip   = 0;       // sidecar from a future schema
        public int ParseError    = 0;       // malformed JSON / IO error
        public List<string> UnmatchedNames = new();
    }

    /// <summary>
    /// Walk every JSON sidecar under <paramref name="sidecarDir"/>
    /// (recursive) and apply matching metadata to nodes under
    /// <paramref name="sceneRoot"/>. Returns aggregate stats.
    /// </summary>
    public static Result Apply(string sidecarDir, Node sceneRoot)
    {
        var result = new Result();
        if (string.IsNullOrEmpty(sidecarDir) || !Directory.Exists(sidecarDir))
        {
            GD.PushWarning($"[PS1Godot] Blender sidecar dir '{sidecarDir}' does not exist; nothing to apply.");
            return result;
        }

        // Index nodes by name + MeshId so multiple sidecars resolve in
        // one tree walk. Build it once; the apply pass below does N
        // dict lookups instead of N tree walks.
        var byName = new Dictionary<string, Node3D>(StringComparer.Ordinal);
        var byMeshId = new Dictionary<string, Node3D>(StringComparer.Ordinal);
        IndexNodes(sceneRoot, byName, byMeshId);

        foreach (var path in Directory.EnumerateFiles(sidecarDir, "*.ps1meshmeta.json", SearchOption.AllDirectories))
        {
            result.SidecarsFound++;
            try
            {
                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                int version = root.TryGetProperty("ps1godot_metadata_version", out var v) ? v.GetInt32() : 1;
                if (version > MaxSupportedVersion)
                {
                    result.VersionSkip++;
                    GD.PushWarning($"[PS1Godot] Sidecar '{path}' is schema v{version} (max supported {MaxSupportedVersion}); skipped.");
                    continue;
                }

                string meshId = root.TryGetProperty("mesh_id", out var m) ? (m.GetString() ?? "") : "";
                string srcName = root.TryGetProperty("source_object_name", out var s) ? (s.GetString() ?? "") : "";

                Node3D? match =
                    (!string.IsNullOrEmpty(meshId)  && byMeshId.TryGetValue(meshId,  out var byId)   ? byId   : null) ??
                    (!string.IsNullOrEmpty(srcName) && byName.TryGetValue(srcName, out var byNm)   ? byNm   : null) ??
                    (!string.IsNullOrEmpty(meshId)  && byName.TryGetValue(meshId,  out var byIdNm) ? byIdNm : null);

                if (match == null)
                {
                    result.Unmatched++;
                    result.UnmatchedNames.Add($"{Path.GetFileName(path)} (mesh_id='{meshId}', source='{srcName}')");
                    continue;
                }

                ApplyToNode(match, root);
                result.Applied++;
            }
            catch (Exception ex)
            {
                result.ParseError++;
                GD.PushWarning($"[PS1Godot] Failed to read sidecar '{path}': {ex.Message}");
            }
        }

        return result;
    }

    // ── Indexer: visits every PS1MeshInstance / PS1MeshGroup once. ──
    private static void IndexNodes(Node n, Dictionary<string, Node3D> byName, Dictionary<string, Node3D> byMeshId)
    {
        if (n is PS1MeshInstance pmi)
        {
            byName[pmi.Name] = pmi;
            if (!string.IsNullOrEmpty(pmi.MeshId)) byMeshId[pmi.MeshId] = pmi;
        }
        else if (n is PS1MeshGroup pmg)
        {
            byName[pmg.Name] = pmg;
            if (!string.IsNullOrEmpty(pmg.MeshId)) byMeshId[pmg.MeshId] = pmg;
        }
        foreach (var child in n.GetChildren())
        {
            IndexNodes(child, byName, byMeshId);
        }
    }

    // ── Per-node application. ───────────────────────────────────────
    //
    // Each setter is wrapped in TryAssign so a single bad enum value
    // (e.g. a wire spelling we haven't added yet) doesn't poison the
    // whole apply pass — we log and skip that one field.
    private static void ApplyToNode(Node3D node, JsonElement root)
    {
        if (node is PS1MeshInstance pmi)
        {
            ApplyCommon(pmi, root,
                setMeshRole:    v => pmi.MeshRole = v,
                setExportMode:  v => pmi.ExportMode = v,
                setDrawPhase:   v => pmi.DrawPhase = v,
                setShadingMode: v => pmi.ShadingMode = v,
                setAlphaMode:   v => pmi.AlphaMode = v,
                setAtlasGroup:  v => pmi.AtlasGroup = v,
                setResidency:   v => pmi.Residency = v,
                setAssetId:     v => pmi.AssetId = v,
                setMeshId:      v => pmi.MeshId = v,
                setChunkId:     v => pmi.ChunkId = v,
                setRegionId:    v => pmi.RegionId = v,
                setArchiveId:   v => pmi.AreaArchiveId = v);
        }
        else if (node is PS1MeshGroup pmg)
        {
            ApplyCommon(pmg, root,
                setMeshRole:    v => pmg.MeshRole = v,
                setExportMode:  v => pmg.ExportMode = v,
                setDrawPhase:   v => pmg.DrawPhase = v,
                setShadingMode: v => pmg.ShadingMode = v,
                setAlphaMode:   v => pmg.AlphaMode = v,
                setAtlasGroup:  v => pmg.AtlasGroup = v,
                setResidency:   v => pmg.Residency = v,
                setAssetId:     v => pmg.AssetId = v,
                setMeshId:      v => pmg.MeshId = v,
                setChunkId:     v => pmg.ChunkId = v,
                setRegionId:    v => pmg.RegionId = v,
                setArchiveId:   v => pmg.AreaArchiveId = v);
        }
    }

    private static void ApplyCommon(
        Node3D node, JsonElement root,
        Action<MeshRole> setMeshRole,
        Action<ExportMode> setExportMode,
        Action<DrawPhase> setDrawPhase,
        Action<ShadingMode> setShadingMode,
        Action<AlphaMode> setAlphaMode,
        Action<AtlasGroup> setAtlasGroup,
        Action<Residency> setResidency,
        Action<string> setAssetId,
        Action<string> setMeshId,
        Action<string> setChunkId,
        Action<string> setRegionId,
        Action<string> setArchiveId)
    {
        TryAssignEnum<MeshRole>(root, "mesh_role",   setMeshRole,   node.Name);
        TryAssignEnum<ExportMode>(root, "export_mode", setExportMode, node.Name);
        TryAssignEnum<DrawPhase>(root, "draw_phase",  setDrawPhase,  node.Name);
        TryAssignEnum<ShadingMode>(root, "shading_mode", setShadingMode, node.Name);
        TryAssignEnum<AlphaMode>(root, "alpha_mode",  setAlphaMode,  node.Name);
        TryAssignEnum<AtlasGroup>(root, "atlas_group", setAtlasGroup, node.Name);
        TryAssignEnum<Residency>(root, "residency",   setResidency,  node.Name);

        TryAssignString(root, "asset_id",        setAssetId);
        TryAssignString(root, "mesh_id",         setMeshId);
        TryAssignString(root, "chunk_id",        setChunkId);
        TryAssignString(root, "region_id",       setRegionId);
        TryAssignString(root, "area_archive_id", setArchiveId);
    }

    private static void TryAssignEnum<TEnum>(JsonElement root, string field, Action<TEnum> setter, string nodeName)
        where TEnum : struct, Enum
    {
        if (!root.TryGetProperty(field, out var el)) return;
        if (el.ValueKind != JsonValueKind.String) return;
        string? wire = el.GetString();
        if (string.IsNullOrEmpty(wire)) return;
        if (Enum.TryParse<TEnum>(wire, ignoreCase: false, out var parsed))
        {
            setter(parsed);
        }
        else
        {
            GD.PushWarning($"[PS1Godot] Sidecar for '{nodeName}': unknown {typeof(TEnum).Name} value '{wire}' — kept default.");
        }
    }

    private static void TryAssignString(JsonElement root, string field, Action<string> setter)
    {
        if (!root.TryGetProperty(field, out var el)) return;
        if (el.ValueKind != JsonValueKind.String) return;
        string val = el.GetString() ?? "";
        // Empty strings are legal — Blender side preserves "explicit
        // empty" vs "absent". An empty wire value still overwrites
        // the existing field, which keeps the round-trip honest.
        setter(val);
    }
}
#endif
