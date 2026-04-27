#if TOOLS
using System.Collections.Generic;
using System.IO;
using Godot;

namespace PS1Godot.Exporter;

// Per-scene texture validation pass — runs at the end of export, walks
// the resolved PSXTexture list, and prints one row per atlas entry plus
// WARN rows for risky shapes the budget bars can't catch on their own.
//
// Today the exporter prints scene-aggregate VRAM bytes and the dock
// shows red bars when over budget; this fills the gap by surfacing
// *which* asset is driving the cost so the author can act. Designed to
// be additive — print only, no behavioral change.
//
// Cross-references:
//   docs/ps1_asset_pipeline_plan.md — the policy this validates (texture
//                                     section + warning catalogue).
//   SceneStats.cs                  — same VRAM cost model (kept in sync).
public static class TextureValidationReport
{
    // Printed only when the texture is over the page-max in either dim.
    private const int PageMaxPx = 256;

    // Largest 16bpp texture we'll allow without a WARN. Anything beyond
    // this should be cutscene / menu residency, not gameplay-resident.
    private const long Sixteen16BppQuietBytes = 16 * 1024;

    public sealed record Row(
        string Name,
        int Width,
        int Height,
        PSXBPP Bpp,
        long EstVramBytes,
        bool HasAlphaKey,
        int UseCount,
        string? Warning);

    // Returns the number of WARN-level rows emitted (0 if every texture
    // is fine). Plugin sums this into the dock's "Last export" line.
    public static int EmitForScene(SceneData data, int sceneIndex)
    {
        if (data.Textures.Count == 0)
        {
            GD.Print($"[PS1Godot]   Texture report: scene[{sceneIndex}] has no textures.");
            return 0;
        }

        // Count how many distinct GameObjects reference each texture index.
        // Used by the reuse auditor: a texture used by exactly one mesh is
        // a candidate for baking into a shared world/character atlas
        // instead of carrying its own atlas slot + CLUT.
        var useCounts = new int[data.Textures.Count];
        foreach (var obj in data.Objects)
        {
            if (obj.Mesh == null) continue;
            // Object can hit multiple texture indices via different
            // surface materials; count each distinct index once per obj.
            var seenForObj = new HashSet<int>();
            foreach (var tri in obj.Mesh.Triangles)
            {
                int ti = tri.TextureIndex;
                if (ti >= 0 && ti < useCounts.Length && seenForObj.Add(ti))
                {
                    useCounts[ti]++;
                }
            }
        }

        var rows = new List<Row>(data.Textures.Count);
        long totalVram = 0;
        int warnCount = 0;

        for (int i = 0; i < data.Textures.Count; i++)
        {
            var t = data.Textures[i];
            long vramBytes = EstimateVramBytes(t);
            totalVram += vramBytes;

            bool hasAlphaKey = t.HasAlphaKey;
            int useCount = useCounts[i];

            string? warning = ClassifyRisk(t, vramBytes, hasAlphaKey, useCount);
            if (warning != null) warnCount++;

            rows.Add(new Row(
                Name: ShortName(t.SourcePath),
                Width: t.Width,
                Height: t.Height,
                Bpp: t.BitDepth,
                EstVramBytes: vramBytes,
                HasAlphaKey: hasAlphaKey,
                UseCount: useCount,
                Warning: warning));
        }

        // Sort biggest first so over-budget scenes show the offenders at the top.
        rows.Sort((a, b) => b.EstVramBytes.CompareTo(a.EstVramBytes));

        GD.Print($"[PS1Godot] Texture report scene[{sceneIndex}]: {rows.Count} unique textures, {totalVram} B total VRAM, {warnCount} warning(s).");
        GD.Print("[PS1Godot]   name                                          dim     bpp   alpha    uses  est VRAM  warn");
        foreach (var r in rows)
        {
            string dim = $"{r.Width}x{r.Height}";
            string bpp = r.Bpp switch
            {
                PSXBPP.TEX_4BIT  => "4bpp",
                PSXBPP.TEX_8BIT  => "8bpp",
                PSXBPP.TEX_16BIT => "16bpp",
                _ => "?",
            };
            string alpha = r.HasAlphaKey ? "cutout" : "opaque";
            string vramKb = $"{r.EstVramBytes / 1024.0:F1} KB";
            string warn = r.Warning ?? "";
            GD.Print($"[PS1Godot]   {Truncate(r.Name, 45),-45} {dim,-7} {bpp,-5} {alpha,-7}  {r.UseCount,4}  {vramKb,9}  {warn}");
        }

        return warnCount;
    }

    // Mirrors SceneStats.EstimateTextureVramBytes — keep in sync. Counts
    // pixel data + one CLUT (16-entry @ 32 B for 4bpp, 256-entry @ 512 B
    // for 8bpp). Source dimensions are clamped to 256 because the
    // exporter auto-downscales beyond that.
    private static long EstimateVramBytes(PSXTexture t)
    {
        int w = System.Math.Min(t.Width, PageMaxPx);
        int h = System.Math.Min(t.Height, PageMaxPx);
        long pixels = (long)w * h;
        long pixelBytes = t.BitDepth switch
        {
            PSXBPP.TEX_4BIT  => pixels / 2,
            PSXBPP.TEX_8BIT  => pixels,
            PSXBPP.TEX_16BIT => pixels * 2L,
            _ => pixels,
        };
        long clutBytes = t.BitDepth switch
        {
            PSXBPP.TEX_4BIT => 16L * 2,
            PSXBPP.TEX_8BIT => 256L * 2,
            _ => 0L,
        };
        return pixelBytes + clutBytes;
    }

    private static string? ClassifyRisk(PSXTexture t, long vramBytes, bool hasAlphaKey, int useCount)
    {
        // Source dim above the page max — exporter auto-downscales but the
        // author is paying VRAM for a bigger source than they need.
        if (t.Width > PageMaxPx || t.Height > PageMaxPx)
        {
            return $"OVERSIZED ({t.Width}x{t.Height} > 256 page max — downscaled)";
        }

        // 16bpp gameplay texture. The strategy doc reserves 16bpp for
        // cutscene / menu / title art only.
        if (t.BitDepth == PSXBPP.TEX_16BIT && vramBytes > Sixteen16BppQuietBytes)
        {
            return $"16BPP ({vramBytes / 1024.0:F1} KB) — should be 8bpp/4bpp unless cutscene-residency";
        }

        // Decals / UI alpha-key textures at 8bpp where 4bpp would do.
        // Heuristic: alpha-key + small (≤128 in both dims) + 8bpp.
        if (hasAlphaKey && t.BitDepth == PSXBPP.TEX_8BIT
            && t.Width <= 128 && t.Height <= 128)
        {
            return "small cutout at 8bpp — try 4bpp first (4× VRAM saving)";
        }

        // Reuse auditor: textures used by exactly one mesh are atlas
        // candidates. Skip the warn for tiny ones (< 4 KB) where atlas
        // overhead would cancel out the saving, and for cutouts that
        // need their own CLUT[0]=transparent slot anyway.
        if (useCount == 1 && vramBytes >= 4 * 1024 && !hasAlphaKey)
        {
            return $"used by 1 mesh ({vramBytes / 1024} KB) — bake into world atlas?";
        }

        return null;
    }

    private static string ShortName(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return "(unnamed)";
        // Drop the res:// prefix and any matching project prefix; keep the
        // last 2-3 path segments so the user can still tell similar files
        // (e.g. blood/horror/decal versions) apart.
        string name = sourcePath.Replace("res://", "");
        var parts = name.Split('/');
        if (parts.Length <= 3) return name;
        return ".../" + string.Join("/", parts, parts.Length - 3, 3);
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";
}
#endif
