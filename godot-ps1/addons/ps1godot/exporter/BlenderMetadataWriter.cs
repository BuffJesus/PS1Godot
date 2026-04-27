#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace PS1Godot.Exporter;

// Symmetric counterpart to BlenderMetadataReader: walks the active
// scene's PS1MeshInstance / PS1MeshGroup nodes and writes
// `<mesh_id>.ps1meshmeta.json` sidecars matching the Blender wire
// format byte-for-byte (same field order, same JSON shape, same enum
// spellings).
//
// Both directions write to the SAME shared sidecar files — the
// .ps1meshmeta.json IS the round-trip storage. After writing here, the
// Blender add-on's import operator can read these back into Object
// PropertyGroups and the .blend reflects what the Godot author did.
//
// Auto-generates asset_id + mesh_id on first export per the
// integration plan § 5.3 round-trip rule, persists them back to the
// node so the next round-trip is stable. Caller is expected to save
// the .tscn afterwards to durably keep the new IDs.
//
// Design notes:
//   - Field order matches metadata_exporter.py exactly (schema version
//     + identity at top, streaming/grouping next, render policy,
//     materials trailing). Diff-friendly + the test_register.py
//     wire-format dump can byte-compare.
//   - Materials array uses Godot Material.ResourceName as both
//     blender_name + material_id since the Godot side doesn't yet
//     have per-material PS1 properties (Phase 5).
//   - disc_id always 1 — Godot side doesn't have a multi-disc concept
//     yet; matches the Blender side's default_disc_id.
public static class BlenderMetadataWriter
{
    public sealed class Result
    {
        public int Written = 0;
        public int Skipped = 0;          // EditorOnly / Ignore / hidden
        public int IdsGenerated = 0;     // count of nodes that got new asset_id or mesh_id
        public int IoErrors = 0;
        public List<string> Paths = new();
    }

    public static Result WriteScene(Node sceneRoot, string outputDir, int discId = 1)
    {
        var result = new Result();
        if (string.IsNullOrEmpty(outputDir))
        {
            GD.PushError("[PS1Godot] BlenderMetadataWriter: outputDir is empty.");
            return result;
        }

        try
        {
            Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex)
        {
            GD.PushError($"[PS1Godot] Could not create sidecar dir '{outputDir}': {ex.Message}");
            result.IoErrors++;
            return result;
        }

        WriteSubtree(sceneRoot, outputDir, discId, result);
        return result;
    }

    private static void WriteSubtree(Node n, string outputDir, int discId, Result result)
    {
        if (n is PS1MeshInstance pmi && ShouldWrite(pmi.MeshRole, pmi.ExportMode))
        {
            if (IdAutoGen.EnsureIds(pmi)) result.IdsGenerated++;
            var payload = BuildPayloadForInstance(pmi, discId);
            WritePayload(payload, pmi.MeshId, outputDir, result);
        }
        else if (n is PS1MeshGroup pmg && ShouldWrite(pmg.MeshRole, pmg.ExportMode))
        {
            if (IdAutoGen.EnsureIds(pmg)) result.IdsGenerated++;
            var payload = BuildPayloadForGroup(pmg, discId);
            WritePayload(payload, pmg.MeshId, outputDir, result);
        }
        else if (n is PS1MeshInstance || n is PS1MeshGroup)
        {
            // Was a tagged node but role/export_mode said skip.
            result.Skipped++;
        }

        foreach (var child in n.GetChildren())
        {
            WriteSubtree(child, outputDir, discId, result);
        }
    }

    private static bool ShouldWrite(MeshRole role, ExportMode mode) =>
        role != MeshRole.EditorOnly && mode != ExportMode.Ignore;

    // ── Payload builders ────────────────────────────────────────────
    //
    // Field order matches metadata_exporter._payload_for_object exactly.
    // Use a Dictionary<string, object?> so the JSON serializer preserves
    // insertion order (required since the Blender side's pretty-print
    // sorts by insertion order, not alphabetically).

    private static Dictionary<string, object?> BuildPayloadForInstance(PS1MeshInstance pmi, int discId)
    {
        var materials = new List<Dictionary<string, object?>>();
        if (pmi.Mesh != null)
        {
            for (int s = 0; s < pmi.Mesh.GetSurfaceCount(); s++)
            {
                var mat = pmi.GetSurfaceOverrideMaterial(s) ?? pmi.Mesh.SurfaceGetMaterial(s);
                if (mat == null) continue;
                materials.Add(BuildMaterialPayload(mat, pmi.AlphaMode, pmi.AtlasGroup));
            }
        }
        return BuildCommonPayload(
            assetId:        pmi.AssetId,
            meshId:         pmi.MeshId,
            sourceName:     pmi.Name,
            chunkId:        pmi.ChunkId,
            regionId:       pmi.RegionId,
            discId:         discId,
            archiveId:      pmi.AreaArchiveId,
            meshRole:       pmi.MeshRole,
            exportMode:     pmi.ExportMode,
            drawPhase:      pmi.DrawPhase,
            shadingMode:    pmi.ShadingMode,
            alphaMode:      pmi.AlphaMode,
            materials:      materials);
    }

    private static Dictionary<string, object?> BuildPayloadForGroup(PS1MeshGroup pmg, int discId)
    {
        // PS1MeshGroup aggregates descendant meshes; flatten their
        // materials into the same list so the round-trip preserves
        // every distinct material the group exports.
        var materials = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectGroupMaterials(pmg, materials, seen, pmg.AlphaMode, pmg.AtlasGroup);

        string sourceName = string.IsNullOrEmpty(pmg.ObjectName) ? pmg.Name : pmg.ObjectName;
        return BuildCommonPayload(
            assetId:        pmg.AssetId,
            meshId:         pmg.MeshId,
            sourceName:     sourceName,
            chunkId:        pmg.ChunkId,
            regionId:       pmg.RegionId,
            discId:         discId,
            archiveId:      pmg.AreaArchiveId,
            meshRole:       pmg.MeshRole,
            exportMode:     pmg.ExportMode,
            drawPhase:      pmg.DrawPhase,
            shadingMode:    pmg.ShadingMode,
            alphaMode:      pmg.AlphaMode,
            materials:      materials);
    }

    private static void CollectGroupMaterials(
        Node n,
        List<Dictionary<string, object?>> materials,
        HashSet<string> seen,
        AlphaMode fallbackAlpha,
        AtlasGroup fallbackAtlas)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var mat = mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
                if (mat == null) continue;
                string key = mat.ResourceName ?? "";
                if (string.IsNullOrEmpty(key)) key = $"_anon_{materials.Count}";
                if (!seen.Add(key)) continue;
                materials.Add(BuildMaterialPayload(mat, fallbackAlpha, fallbackAtlas));
            }
        }
        foreach (var child in n.GetChildren())
        {
            CollectGroupMaterials(child, materials, seen, fallbackAlpha, fallbackAtlas);
        }
    }

    private static Dictionary<string, object?> BuildCommonPayload(
        string assetId, string meshId, string sourceName,
        string chunkId, string regionId, int discId, string archiveId,
        MeshRole meshRole, ExportMode exportMode, DrawPhase drawPhase,
        ShadingMode shadingMode, AlphaMode alphaMode,
        List<Dictionary<string, object?>> materials)
    {
        // Insertion order MATTERS for byte-equivalence with the Blender
        // exporter's pretty-print. Don't reorder.
        return new Dictionary<string, object?>
        {
            ["ps1godot_metadata_version"] = 1,
            ["asset_id"]                  = assetId,
            ["mesh_id"]                   = meshId,
            ["source_object_name"]        = sourceName,
            ["blend_file"]                = "",  // Godot-authored — no .blend origin
            ["chunk_id"]                  = chunkId,
            ["region_id"]                 = regionId,
            ["disc_id"]                   = discId,
            ["area_archive_id"]           = archiveId,
            ["mesh_role"]                 = meshRole.ToString(),
            ["export_mode"]               = exportMode.ToString(),
            ["draw_phase"]                = drawPhase.ToString(),
            ["shading_mode"]              = shadingMode.ToString(),
            ["alpha_mode"]                = alphaMode.ToString(),
            ["collision_layer"]           = "",  // Godot uses LayerMask int; not round-tripped today
            ["materials"]                 = materials,
        };
    }

    private static Dictionary<string, object?> BuildMaterialPayload(
        Material mat, AlphaMode meshAlpha, AtlasGroup meshAtlas)
    {
        // Per-material PS1 properties are Phase 5 (per-material types
        // on the Godot side). Until then, populate from the Material's
        // ResourceName + the parent mesh's mesh-level alpha/atlas
        // hints so the JSON is well-formed and round-trips.
        string name = string.IsNullOrEmpty(mat.ResourceName) ? "Material" : mat.ResourceName;
        return new Dictionary<string, object?>
        {
            ["blender_name"]    = name,
            ["material_id"]     = name,
            ["texture_page_id"] = "",
            ["clut_id"]         = "",
            ["palette_group"]   = "",
            ["atlas_group"]     = meshAtlas.ToString(),
            ["texture_format"]  = "Auto",
            ["alpha_mode"]      = meshAlpha.ToString(),
            ["force_no_filter"] = false,
            ["approved_16bpp"]  = false,
        };
    }

    // ── Disk write ──────────────────────────────────────────────────

    private static void WritePayload(Dictionary<string, object?> payload, string meshId, string outputDir, Result result)
    {
        string path = Path.Combine(outputDir, $"{meshId}.ps1meshmeta.json");
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                // Don't escape non-ASCII; the Blender writer doesn't
                // either (ensure_ascii=False), so unicode in IDs
                // round-trips byte-for-byte.
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            // The Blender writer ends with a single trailing newline
            // (json.dump + f.write("\n")) — match it.
            string json = JsonSerializer.Serialize(payload, options) + "\n";
            File.WriteAllText(path, json);
            result.Paths.Add(path);
            result.Written++;
        }
        catch (Exception ex)
        {
            GD.PushError($"[PS1Godot] Failed to write sidecar '{path}': {ex.Message}");
            result.IoErrors++;
        }
    }
}
#endif
