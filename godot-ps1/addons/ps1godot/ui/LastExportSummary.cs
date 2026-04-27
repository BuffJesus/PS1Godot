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
public enum SummarySeverity { Ok, Warning, Error }

public sealed class LastExportSummary
{
    public int ScenesExported;
    public int TextureWarnings;
    public int AudioWarnings;
    public int AnimationWarnings;
    public int UVDirtyMeshes;                  // promoted to ERROR — PSX will misrender
    public int MeshCleanupCandidates;          // reuse-factor < threshold (informational)
    public long MeshBytesSavedByPooling;       // sum across scenes (v31 vs legacy)

    private readonly List<MeshDedupSummaryEntry> _worstMeshes = new();
    // Top offender for the headline. First UV-dirty mesh (error) takes
    // priority; otherwise lowest-reuse mesh; empty when nothing fired.
    public string TopOffenderName { get; private set; } = "";
    public string TopOffenderReason { get; private set; } = "";

    // Per-offender list driving the dock's click-to-focus rows. Each
    // entry has a node name + a one-line reason. Errors come before
    // warnings so the dock can render them in tier order without
    // re-sorting.
    public sealed record Offender(string Name, string Reason, SummarySeverity Tier);
    private readonly List<Offender> _offenders = new();
    public IReadOnlyList<Offender> Offenders => _offenders;

    // PS1GodotPlugin calls this once per ExportOneScene invocation,
    // accumulating per-scene results into the running totals.
    public void Add(SceneData data, int textureWarnings, int audioWarnings,
                    int animationWarnings, int uvDirtyMeshes,
                    IReadOnlyList<string>? uvDirtyMeshNames = null)
    {
        ScenesExported++;
        TextureWarnings    += textureWarnings;
        AudioWarnings      += audioWarnings;
        AnimationWarnings  += animationWarnings;
        UVDirtyMeshes      += uvDirtyMeshes;

        // Errors first — every UV-dirty mesh becomes a click-to-focus
        // row. The top offender headline picks the first error.
        if (uvDirtyMeshNames != null)
        {
            foreach (var n in uvDirtyMeshNames)
            {
                if (string.IsNullOrEmpty(n)) continue;
                _offenders.Add(new Offender(n, "UV out-of-range", SummarySeverity.Error));
                if (string.IsNullOrEmpty(TopOffenderName))
                {
                    TopOffenderName = n;
                    TopOffenderReason = "UV out-of-range";
                }
            }
        }

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
                    _offenders.Add(new Offender(
                        entry.MeshName,
                        $"low vertex reuse ({entry.ReuseFactor:F2}×) — Blender Merge-By-Distance candidate",
                        SummarySeverity.Warning));
                }
            }
        }

        // No errors fired but warnings did — surface the worst-reuse
        // mesh name as a secondary headline.
        if (string.IsNullOrEmpty(TopOffenderName) && _worstMeshes.Count > 0)
        {
            _worstMeshes.Sort((a, b) => a.ReuseFactor.CompareTo(b.ReuseFactor));
            var worst = _worstMeshes[0];
            TopOffenderName = worst.MeshName;
            TopOffenderReason = $"low vertex reuse ({worst.ReuseFactor:F2}×)";
        }
    }

    public int Errors   => UVDirtyMeshes;       // PSX will misrender — hard fail
    public int Warnings => TextureWarnings + AudioWarnings + AnimationWarnings + MeshCleanupCandidates;
    public int TotalIssues => Errors + Warnings;

    public SummarySeverity Severity =>
        Errors > 0   ? SummarySeverity.Error   :
        Warnings > 0 ? SummarySeverity.Warning :
                       SummarySeverity.Ok;

    // Headline label, severity-aware. Examples:
    //   "✓ Last export: 4 scenes, no issues  (157 KB saved by v31 mesh pool)"
    //   "▲ 3 warnings — low vertex reuse: s_bookcase_a (1.18×)"
    //   "✗ 1 error: UV out-of-range on hallway_blood_decal (+ 2 warnings)"
    // Empty when nothing was exported — dock hides the row.
    public string LabelText
    {
        get
        {
            if (ScenesExported == 0) return "";

            string saved = MeshBytesSavedByPooling > 0
                ? $"  ({MeshBytesSavedByPooling / 1024:n0} KB saved by v31 mesh pool)"
                : "";

            if (Errors > 0)
            {
                string offender = !string.IsNullOrEmpty(TopOffenderName)
                    ? $": {TopOffenderReason} on {TopOffenderName}"
                    : "";
                string warnTail = Warnings > 0 ? $" (+ {Warnings} warning(s))" : "";
                return $"✗ {Errors} error(s){offender}{warnTail}";
            }
            if (Warnings > 0)
            {
                string offender = !string.IsNullOrEmpty(TopOffenderName)
                    ? $" — {TopOffenderReason}: {TopOffenderName}"
                    : "";
                return $"▲ {Warnings} warning(s){offender}";
            }
            return $"✓ Last export: {ScenesExported} scene(s), no issues{saved}";
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
