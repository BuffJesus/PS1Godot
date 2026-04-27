#if TOOLS
using System.Collections.Generic;
using System.Text;
using PS1Godot.Exporter;

namespace PS1Godot.UI;

// Aggregate "what happened on the last export" snapshot. Built by
// PS1GodotPlugin during OnExportEmptySplashpack, fed to the dock so a
// single "Last export: N issues" line can summarize the validation
// reports without spamming a side panel. Tooltip on that line carries
// the per-category breakdown.
//
// Counts here are summed across every scene exported in a single run
// (scene_0 + each PS1Scene.SubScenes child). Dock shows totals;
// tooltip lists category subtotals + the worst-N mesh-cleanup names
// so the author has something concrete to click on.
public sealed class LastExportSummary
{
    public int ScenesExported;
    public int TextureWarnings;
    public int AudioWarnings;
    public int AnimationWarnings;
    public int UVDirtyMeshes;
    public int MeshCleanupCandidates;          // reuse-factor < threshold
    public long MeshBytesSavedByPooling;       // sum across scenes (v31 vs legacy)

    private readonly List<MeshDedupSummaryEntry> _worstMeshes = new();

    // PS1GodotPlugin calls this once per ExportOneScene invocation,
    // accumulating per-scene results into the running totals.
    public void Add(SceneData data, int textureWarnings, int audioWarnings,
                    int animationWarnings, int uvDirtyMeshes)
    {
        ScenesExported++;
        TextureWarnings    += textureWarnings;
        AudioWarnings      += audioWarnings;
        AnimationWarnings  += animationWarnings;
        UVDirtyMeshes      += uvDirtyMeshes;

        if (data.MeshDedup is { } dedup)
        {
            MeshBytesSavedByPooling += dedup.TotalBytesSaved;
            // Reuse-factor < 1.5× = "this asset has duplicated vertex data
            // in the source that the v31 pool can't recover" → flag for
            // Blender Merge By Distance / similar cleanup.
            foreach (var entry in dedup.WorstReuse)
            {
                if (entry.ReuseFactor < 1.5f)
                {
                    MeshCleanupCandidates++;
                    _worstMeshes.Add(entry);
                }
            }
        }
    }

    public int TotalIssues =>
        TextureWarnings + AudioWarnings + AnimationWarnings + UVDirtyMeshes + MeshCleanupCandidates;

    // Two-line one-shot label: top line = headline issue count, bottom
    // = mesh-bytes-saved (positive feedback). Empty when nothing was
    // exported — dock hides the row.
    public string LabelText
    {
        get
        {
            if (ScenesExported == 0) return "";
            int issues = TotalIssues;
            string head = issues == 0
                ? $"Last export: {ScenesExported} scene(s), no issues"
                : $"Last export: {issues} issue(s) across {ScenesExported} scene(s)";
            string saved = MeshBytesSavedByPooling > 0
                ? $"  ({MeshBytesSavedByPooling / 1024:n0} KB saved by v31 mesh pool)"
                : "";
            return head + saved;
        }
    }

    // Multi-line tooltip. Hover on the dock label to see which
    // categories drove the count and the worst mesh-cleanup candidates
    // by name. Plain text; the dock label widget renders as-is.
    public string TooltipText
    {
        get
        {
            if (ScenesExported == 0) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"Texture warnings:   {TextureWarnings}");
            sb.AppendLine($"Audio warnings:     {AudioWarnings}");
            sb.AppendLine($"Animation warnings: {AnimationWarnings}");
            sb.AppendLine($"UV-dirty meshes:    {UVDirtyMeshes}");
            sb.AppendLine($"Mesh cleanup candidates: {MeshCleanupCandidates}");
            if (_worstMeshes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Lowest-reuse meshes (Blender 'Merge By Distance' targets):");
                _worstMeshes.Sort((a, b) => a.ReuseFactor.CompareTo(b.ReuseFactor));
                int show = System.Math.Min(_worstMeshes.Count, 8);
                for (int i = 0; i < show; i++)
                {
                    var m = _worstMeshes[i];
                    sb.AppendLine($"  {m.MeshName}: {m.TriCount} tris / {m.UniqueVertices} verts ({m.ReuseFactor:F2}× reuse)");
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
#endif
