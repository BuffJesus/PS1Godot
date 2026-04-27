#if TOOLS
using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Per-scene animation validation pass — runs at the end of export and
// walks AnimationRecord (per-object simple tracks) + SkinnedMeshRecord
// (baked skin clips) lists, printing a row per item plus WARN rows for
// shapes that will either ship dead data or quietly blow the splashpack
// size budget.
//
// Mirrors TextureValidationReport / AudioValidationReport — same
// per-row + warnings shape, same "print only, no behavioural change"
// contract. Returns a warning count for the dock summary line.
//
// Cross-references:
//   docs/ps1_asset_pipeline_plan.md — Slot A.A2 "Animation linter".
//   nodes/PS1Animation.cs           — author-side simple-track shape.
//   exporter/SceneData.cs           — AnimationRecord / SkinClipRecord.
public static class AnimationLinter
{
    // Per-frame bone pose footprint in v30+ format (BakedBonePose =
    // quat int16×4 + translation int16×3 = 14 B). One source of truth
    // is mesh.hh; we duplicate here so the report is standalone.
    private const int BakedPoseBytesPerBone = 14;

    // PSX vsync runs at 60 Hz NTSC / 50 Hz PAL. Animation playback
    // beyond ~30 fps wastes budget — interpolation in the runtime
    // already hides the difference. Anything above this gets a WARN.
    private const int SkinClipFpsCeiling = 30;

    // Bone count cap below which skinning pays for itself. The renderer
    // can technically handle more, but each bone adds 14 B/frame and
    // the per-tri-vertex bone-index byte. Above this the asset is a
    // Segmented or vertex-anim candidate (Phase 3 D3).
    private const int SkinBoneCountCeiling = 24;

    // Skin clip byte budget per clip — cumulative across N characters
    // a typical RPG party will easily blow the splashpack cap. WARN at
    // 24 KB, which is roughly 60 frames × 24 bones × 14 B.
    private const long SkinClipBigBytes = 24 * 1024;

    // Simple-track keyframe count above which the author probably
    // hand-stepped frame-by-frame instead of using interpolation. The
    // PS1 simple-track interp modes (Linear/Step) make dense KF tracks
    // mostly redundant.
    private const int SimpleTrackKeyframesDense = 32;

    public sealed record AnimRow(
        string Name,
        string Target,
        string TrackType,
        int TotalFrames,
        int Keyframes,
        string? Warning);

    public sealed record SkinRow(
        string MeshName,
        string ClipName,
        int FrameCount,
        int Fps,
        int BoneCount,
        long Bytes,
        bool Loop,
        string? Warning);

    public static int EmitForScene(SceneData data, int sceneIndex)
    {
        int warnCount = 0;

        warnCount += EmitSimpleTrackRows(data, sceneIndex);
        warnCount += EmitSkinClipRows(data, sceneIndex);

        return warnCount;
    }

    private static int EmitSimpleTrackRows(SceneData data, int sceneIndex)
    {
        if (data.Animations == null || data.Animations.Count == 0)
        {
            // Quiet — many scenes legitimately have no PS1Animation
            // nodes. Skin clips below get their own header.
            return 0;
        }

        var rows = new List<AnimRow>(data.Animations.Count);
        int warnCount = 0;
        foreach (var anim in data.Animations)
        {
            int kfCount = anim.Keyframes?.Count ?? 0;
            string? warning = ClassifySimpleTrack(anim, kfCount);
            if (warning != null) warnCount++;
            rows.Add(new AnimRow(
                Name: anim.Name,
                Target: anim.TargetObjectName,
                TrackType: anim.TrackType.ToString(),
                TotalFrames: anim.TotalFrames,
                Keyframes: kfCount,
                Warning: warning));
        }

        GD.Print($"[PS1Godot] Animation report scene[{sceneIndex}]: {rows.Count} simple track(s), {warnCount} warning(s).");
        GD.Print("[PS1Godot]   name                            target               track          frames   kfs  warn");
        foreach (var r in rows)
        {
            GD.Print($"[PS1Godot]   {Truncate(r.Name, 30),-30}  {Truncate(r.Target, 20),-20}  {r.TrackType,-13}  {r.TotalFrames,6}  {r.Keyframes,4}  {r.Warning ?? ""}");
        }

        return warnCount;
    }

    private static int EmitSkinClipRows(SceneData data, int sceneIndex)
    {
        if (data.SkinnedMeshes == null || data.SkinnedMeshes.Count == 0) return 0;

        // Count clips so the header can decide whether to print at all.
        int totalClips = 0;
        foreach (var sm in data.SkinnedMeshes) totalClips += sm.Clips?.Count ?? 0;
        if (totalClips == 0)
        {
            // Skinned mesh with rest-pose-only — no clips to lint.
            return 0;
        }

        var rows = new List<SkinRow>(totalClips);
        long totalBytes = 0;
        int warnCount = 0;

        foreach (var sm in data.SkinnedMeshes)
        {
            if (sm.Clips == null) continue;
            foreach (var clip in sm.Clips)
            {
                long bytes = clip.FrameData?.LongLength ?? 0;
                totalBytes += bytes;
                bool loop = (clip.Flags & 0x01) != 0;
                string? warning = ClassifySkinClip(sm, clip, bytes, loop);
                if (warning != null) warnCount++;
                rows.Add(new SkinRow(
                    MeshName: sm.Name,
                    ClipName: clip.Name,
                    FrameCount: clip.FrameCount,
                    Fps: clip.Fps,
                    BoneCount: sm.BoneCount,
                    Bytes: bytes,
                    Loop: loop,
                    Warning: warning));
            }
        }

        // Biggest clips first so over-budget exports show offenders on top.
        rows.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));

        GD.Print($"[PS1Godot] Skin clip report scene[{sceneIndex}]: {rows.Count} baked clip(s) across {data.SkinnedMeshes.Count} mesh(es), {totalBytes / 1024.0:F1} KB total, {warnCount} warning(s).");
        GD.Print("[PS1Godot]   mesh             clip                    frames  fps  bones  loop   bytes      warn");
        foreach (var r in rows)
        {
            string loop = r.Loop ? "loop" : "once";
            string size = $"{r.Bytes / 1024.0:F1} KB";
            GD.Print($"[PS1Godot]   {Truncate(r.MeshName, 16),-16}  {Truncate(r.ClipName, 22),-22}  {r.FrameCount,6}  {r.Fps,3}  {r.BoneCount,5}  {loop,-5}  {size,8}  {r.Warning ?? ""}");
        }

        return warnCount;
    }

    private static string? ClassifySimpleTrack(AnimationRecord anim, int kfCount)
    {
        // Dead anim: track exists but no keyframes — won't drive its
        // target. Easy authoring mistake.
        if (kfCount == 0)
        {
            return "no keyframes — track will not drive its target";
        }

        if (anim.TotalFrames == 0)
        {
            return "TotalFrames=0 — track playback ends instantly";
        }

        // A keyframe whose frame index is past the timeline length.
        // The runtime clamps but the author probably miscounted.
        foreach (var kf in anim.Keyframes!)
        {
            if (kf.Frame > anim.TotalFrames)
            {
                return $"keyframe at frame {kf.Frame} exceeds TotalFrames={anim.TotalFrames}";
            }
        }

        // Single keyframe = static value. Probably wanted a second one.
        if (kfCount == 1)
        {
            return "single keyframe — track plays as a constant pose";
        }

        // Author hand-stepped where interpolation would do. Not wrong,
        // but big simple tracks bloat the splashpack for no gameplay
        // benefit.
        if (kfCount > SimpleTrackKeyframesDense)
        {
            return $"{kfCount} keyframes — interp Linear/Step makes dense KFs redundant";
        }

        return null;
    }

    private static string? ClassifySkinClip(SkinnedMeshRecord sm, SkinClipRecord clip, long bytes, bool loop)
    {
        // Skin format invariant — frame count × bone count × 14 B
        // should match. If it doesn't, the writer or collector is
        // upstream broken; surface immediately rather than letting it
        // crash the runtime decoder.
        long expected = (long)clip.FrameCount * sm.BoneCount * BakedPoseBytesPerBone;
        if (bytes != expected)
        {
            return $"FrameData size {bytes} B != frameCount × boneCount × {BakedPoseBytesPerBone} ({expected} B) — corrupted clip";
        }

        if (clip.FrameCount == 0)
        {
            return "0 frames — clip plays as a no-op";
        }

        // Above 30 fps is wasted budget on PSX (vsync runs at 60 NTSC,
        // but the renderer interpolates and human eyes can't tell). The
        // baker should have downsampled.
        if (clip.Fps > SkinClipFpsCeiling)
        {
            return $"{clip.Fps} fps — downsample to ≤{SkinClipFpsCeiling} fps to halve clip bytes";
        }

        // High bone count = expensive per-frame storage AND per-frame
        // matrix decode. Above the ceiling, segmented-rigid (Phase 3 D3)
        // would usually fit better.
        if (sm.BoneCount > SkinBoneCountCeiling)
        {
            return $"{sm.BoneCount} bones — consider segmented-rigid (Phase 3 D3) for generic NPCs";
        }

        // Big resident clip — compounds across N characters. Authors
        // often forget that "looks fine" means "fine on a 1-character
        // demo scene"; a five-character party with this clip bumps
        // 5×.
        if (bytes >= SkinClipBigBytes && !loop)
        {
            return $"{bytes / 1024.0:F1} KB one-shot clip — consider shorter / lower-fps if reused across characters";
        }

        // Looping but very short — single-pose loops are usually
        // mis-authored. A 1-frame loop is just a static pose.
        if (loop && clip.FrameCount <= 2)
        {
            return $"loop with {clip.FrameCount} frame(s) — likely a static pose, drop the loop flag";
        }

        return null;
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
#endif
