#if TOOLS
using System.Collections.Generic;
using Godot;
using PS1Godot.Exporter;

namespace PS1Godot.UI;

// Lightweight stats walker for the dock's Scene section. Counts what
// the export pipeline cares about without invoking the full
// SceneCollector (which logs, allocates texture atlases, and writes
// splashpacks). Runs on every scene-change tick so it must stay cheap.
public static class SceneStats
{
    // PSX VRAM is 1 MB total but the framebuffer (double-buffered 320×240
    // ×16bpp ≈ 300 KB) and a few system rects eat the back half — the
    // exporter's atlas only fills the front 1024×256 = 512 KB. SPU RAM is
    // 512 KB but the back 256 KB is reverb buffer + reserved, so usable
    // sample storage caps at 256 KB. Match what the exporter actually
    // packs so the dock bars predict over-budget exports instead of
    // greenlighting them.
    public const int VramBudgetBytes = 512 * 1024;
    public const int SpuBudgetBytes  = 256 * 1024;

    // PSX fill-rate ceiling. The GPU paints ~28 Mpix/s for textured+
    // Gouraud — at 30 fps × 320×240 that's ~12× the screen area as a
    // hard ceiling, dropping to ~6-8× practical after VBlank + UI + clears.
    // Budget against 8× so authors land in green territory by default.
    // See docs/fill-rate-budget.md for the math.
    public const float FillRateBudgetScreenAreas = 8.0f;
    // Doc's transparency cost factor: semi-trans path runs ~1.5× a flat fill.
    public const float TranslucentFillCostFactor = 1.5f;

    public readonly struct Result
    {
        public readonly bool HasPS1Scene;
        public readonly string? SceneName;
        public readonly int MeshCount;
        public readonly int TriangleCount;
        public readonly int AudioClipCount;
        public readonly int UniqueTextureCount;
        public readonly long VramEstimateBytes;
        public readonly long SpuEstimateBytes;
        public readonly int TargetTriangles;   // Budget from PS1Scene, 0 if unset.
        public readonly int MaxActors;
        public readonly int MaxTexturePages;
        public readonly int TexturePageEstimate;   // Rough page count based on VRAM estimate.
        // Static fill-rate estimate: sum of (per-mesh screen-area ratio ×
        // translucency factor). 0 when no Camera3D was found in the tree
        // (no viewpoint to project against). Reported as a multiple of
        // viewport area, comparable directly to FillRateBudgetScreenAreas.
        public readonly float FillRateScreenAreas;
        public readonly bool  FillRateCameraFound;

        public Result(bool hasScene, string? name, int meshes, int tris, int audio,
                      int textures, long vramBytes, long spuBytes,
                      int targetTris, int maxActors, int maxTexPages,
                      int texPageEstimate,
                      float fillRateAreas, bool fillRateCameraFound)
        {
            HasPS1Scene = hasScene;
            SceneName = name;
            MeshCount = meshes;
            TriangleCount = tris;
            AudioClipCount = audio;
            UniqueTextureCount = textures;
            VramEstimateBytes = vramBytes;
            SpuEstimateBytes = spuBytes;
            TargetTriangles = targetTris;
            MaxActors = maxActors;
            MaxTexturePages = maxTexPages;
            TexturePageEstimate = texPageEstimate;
            FillRateScreenAreas = fillRateAreas;
            FillRateCameraFound = fillRateCameraFound;
        }
    }

    // Returns `HasPS1Scene = false` when no PS1Scene is in the tree so the
    // dock can show a "drop a PS1Scene here" hint rather than zeros.
    public static Result Compute(Node? root)
    {
        if (root == null)
        {
            return new Result(false, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0f, false);
        }

        var scene = FindFirst<PS1Scene>(root);
        if (scene == null)
        {
            return new Result(false, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0f, false);
        }

        int meshes = 0;
        int tris = 0;
        int audio = scene.AudioClips?.Count ?? 0;
        var textureKeys = new HashSet<string>();
        long vramBytes = 0;
        long spuBytes = EstimateSpuBytes(scene);

        WalkMeshes(root, ref meshes, ref tris, textureKeys, ref vramBytes);
        WalkUIAndSky(root, textureKeys, ref vramBytes);

        // Rough texture-page estimate. Each 8bpp page is 256×256 = 64 KB
        // of pixel data; 4bpp halves that but shares the same VRAM column.
        // Using unique texture count as a conservative upper bound (small
        // textures pack into shared atlases, but we'd rather warn early).
        int texPageEstimate = textureKeys.Count;

        // Fill-rate estimate (docs/fill-rate-budget.md Stage 1, AABB
        // approximation). Walk the same meshes a second time collecting
        // (worldAabb, translucent); pick the first Camera3D as a viewpoint
        // and project each AABB's bounding-rect on screen. Sum weighted
        // by the translucency factor. No camera → no estimate.
        //
        // Stage 1 caveats — documented here so future-you knows what's
        // intentional vs broken:
        //  - First Camera3D in tree-order wins. Scenes with multiple
        //    cameras (player rig + cinematic) get whichever the walker
        //    hits first. Stage 2's FillRateSampleCameras fixes this.
        //  - UI sprites (PS1UIElement.Image) and sky billboards
        //    (PS1Sky) skip this walk; they paint real pixels but
        //    aren't accounted for here. TODO Stage 1.1 — feed both
        //    into the estimate alongside meshes.
        var fillMeshes = new List<(Aabb world, bool translucent)>();
        CollectFillRateMeshes(root, fillMeshes, groupTranslucent: false);
        var fillCam = FindFirst<Camera3D>(root);
        float fillRateAreas = 0f;
        bool camFound = fillCam != null && fillCam.IsInsideTree();
        if (camFound)
        {
            foreach (var (worldAabb, isTrans) in fillMeshes)
            {
                float ratio = ProjectedAabbScreenRatio(worldAabb, fillCam!);
                fillRateAreas += ratio * (isTrans ? TranslucentFillCostFactor : 1.0f);
            }
        }

        return new Result(
            hasScene: true,
            name: root.Name,
            meshes: meshes,
            tris: tris,
            audio: audio,
            textures: textureKeys.Count,
            vramBytes: vramBytes,
            spuBytes: spuBytes,
            targetTris: scene.TargetTriangles,
            maxActors: scene.MaxActors,
            maxTexPages: scene.MaxTexturePages,
            texPageEstimate: texPageEstimate,
            fillRateAreas: fillRateAreas,
            fillRateCameraFound: camFound);
    }

    // Second-pass walker: collects per-mesh AABB + translucency for the
    // fill-rate estimator. Mirrors WalkMeshes/WalkMeshGroupDescendants so
    // PS1MeshGroup's children inherit the group's Translucent flag.
    private static void CollectFillRateMeshes(Node n,
                                              List<(Aabb world, bool translucent)> sink,
                                              bool groupTranslucent)
    {
        if (n is PS1MeshGroup group)
        {
            // Walk children with the group's translucency flag inherited.
            foreach (var child in group.GetChildren())
            {
                if (child is Node c) CollectFillRateMeshes(c, sink, group.Translucent);
            }
            return;
        }

        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            // Local AABB → world via GlobalTransform. PS1MeshInstance's
            // Translucent flag wins over any group inheritance; plain
            // MeshInstance3D children of a PS1MeshGroup take the group
            // flag passed in.
            bool isTrans = groupTranslucent;
            if (n is PS1MeshInstance pmi) isTrans = pmi.Translucent;
            Aabb local = mi.Mesh.GetAabb();
            Aabb world = mi.GlobalTransform * local;
            sink.Add((world, isTrans));
        }

        foreach (var child in n.GetChildren())
        {
            if (child is Node c) CollectFillRateMeshes(c, sink, groupTranslucent);
        }
    }

    // Screen-area ratio for a world-space AABB projected through `cam`.
    // Returns (projectedRectArea / viewportArea) — a unitless multiple
    // suitable for summing across meshes. AABBs entirely behind the camera
    // return 0. Bounding-rect approximation is an upper bound on true
    // painted pixels; a Stage-2 per-triangle pass tightens this.
    //
    // Near-plane underestimate: AABB straddling the camera (1-7 corners
    // in front, the rest behind) gets the bbox computed from the visible
    // corners only — under-counts when the projected geometry actually
    // extends past those corners. The dock label says "AABB upper bound"
    // but technically isn't an upper bound at near-plane straddles.
    // Authors who get close enough for this to matter usually already
    // see the perf hit.
    private static float ProjectedAabbScreenRatio(Aabb world, Camera3D cam)
    {
        var vp = cam.GetViewport();
        if (vp == null) return 0f;
        Vector2 vpSize = vp.GetVisibleRect().Size;
        if (vpSize.X <= 0f || vpSize.Y <= 0f) return 0f;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        int infront = 0;
        for (int i = 0; i < 8; i++)
        {
            var corner = world.Position + new Vector3(
                (i & 1) != 0 ? world.Size.X : 0,
                (i & 2) != 0 ? world.Size.Y : 0,
                (i & 4) != 0 ? world.Size.Z : 0);
            if (cam.IsPositionBehind(corner)) continue;
            infront++;
            Vector2 s = cam.UnprojectPosition(corner);
            if (s.X < minX) minX = s.X;
            if (s.X > maxX) maxX = s.X;
            if (s.Y < minY) minY = s.Y;
            if (s.Y > maxY) maxY = s.Y;
        }
        if (infront == 0) return 0f;
        // Clamp to viewport — paint outside the screen costs nothing.
        if (minX < 0) minX = 0;
        if (minY < 0) minY = 0;
        if (maxX > vpSize.X) maxX = vpSize.X;
        if (maxY > vpSize.Y) maxY = vpSize.Y;
        if (maxX <= minX || maxY <= minY) return 0f;
        return ((maxX - minX) * (maxY - minY)) / (vpSize.X * vpSize.Y);
    }

    private static void WalkMeshes(Node n, ref int meshes, ref int tris,
                                   HashSet<string> textureKeys, ref long vramBytes)
    {
        if (n is PS1MeshGroup group)
        {
            // PS1MeshGroup merges every descendant MeshInstance3D into a
            // single exported GameObject — those children are typically
            // plain MeshInstance3D (FBX-decomposed body parts), not
            // PS1MeshInstance, so the regular branch below misses them.
            WalkMeshGroupDescendants(group, group.BitDepth, ref meshes,
                                     ref tris, textureKeys, ref vramBytes);
            return;  // Don't recurse — the group owns its subtree.
        }

        if (n is PS1MeshInstance pmi && pmi.Mesh != null)
        {
            meshes++;
            int surfaceCount = pmi.Mesh.GetSurfaceCount();
            for (int s = 0; s < surfaceCount; s++)
            {
                var arrays = pmi.Mesh.SurfaceGetArrays(s);
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                tris += indices.Length > 0 ? indices.Length / 3 : 0;

                // VRAM per unique (resource path, bit depth) pair — same
                // dedup key the exporter's texture cache uses.
                var tex = ExtractAlbedoTexture(pmi.MaterialOverride
                    ?? pmi.GetSurfaceOverrideMaterial(s)
                    ?? pmi.Mesh.SurfaceGetMaterial(s));
                if (tex != null && !string.IsNullOrEmpty(tex.ResourcePath))
                {
                    string key = $"{tex.ResourcePath}|{pmi.BitDepth}";
                    if (textureKeys.Add(key))
                    {
                        vramBytes += EstimateTextureVramBytes(tex, pmi.BitDepth);
                    }
                }
            }
        }

        foreach (var child in n.GetChildren())
        {
            WalkMeshes(child, ref meshes, ref tris, textureKeys, ref vramBytes);
        }
    }

    private static void WalkMeshGroupDescendants(Node n, PSXBPP groupBpp,
                                                 ref int meshes, ref int tris,
                                                 HashSet<string> textureKeys,
                                                 ref long vramBytes)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            meshes++;
            int surfaceCount = mi.Mesh.GetSurfaceCount();
            for (int s = 0; s < surfaceCount; s++)
            {
                var arrays = mi.Mesh.SurfaceGetArrays(s);
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                tris += indices.Length > 0 ? indices.Length / 3 : 0;

                var tex = ExtractAlbedoTexture(mi.MaterialOverride
                    ?? mi.GetSurfaceOverrideMaterial(s)
                    ?? mi.Mesh.SurfaceGetMaterial(s));
                if (tex != null && !string.IsNullOrEmpty(tex.ResourcePath))
                {
                    string key = $"{tex.ResourcePath}|{groupBpp}";
                    if (textureKeys.Add(key))
                    {
                        vramBytes += EstimateTextureVramBytes(tex, groupBpp);
                    }
                }
            }
        }

        foreach (var child in n.GetChildren())
        {
            WalkMeshGroupDescendants(child, groupBpp, ref meshes, ref tris,
                                     textureKeys, ref vramBytes);
        }
    }

    // UI Image elements + PS1Sky textures live in the same VRAM atlas as
    // mesh textures. The mesh walker only sees PS1MeshInstance / PS1MeshGroup,
    // so without this pass the dock under-reports VRAM by however much UI +
    // sky art consumes — typically the bezel, HUD plates, font, and skybox.
    private static void WalkUIAndSky(Node n, HashSet<string> textureKeys,
                                     ref long vramBytes)
    {
        if (n is PS1UIElement ui && ui.Type == PS1UIElementType.Image
            && ui.Texture != null && !string.IsNullOrEmpty(ui.Texture.ResourcePath))
        {
            string key = $"{ui.Texture.ResourcePath}|{ui.BitDepth}";
            if (textureKeys.Add(key))
            {
                vramBytes += EstimateTextureVramBytes(ui.Texture, ui.BitDepth);
            }
        }
        else if (n is PS1Sky sky && sky.Texture != null
                 && !string.IsNullOrEmpty(sky.Texture.ResourcePath))
        {
            string key = $"{sky.Texture.ResourcePath}|{sky.BitDepth}";
            if (textureKeys.Add(key))
            {
                vramBytes += EstimateTextureVramBytes(sky.Texture, sky.BitDepth);
            }
        }

        foreach (var child in n.GetChildren())
        {
            WalkUIAndSky(child, textureKeys, ref vramBytes);
        }
    }

    // Rough VRAM footprint for a single texture at a given bit depth,
    // counting pixel data + one CLUT. The real exporter quantizes
    // sources >256 px down to 256 px (VRAM page max), so we clamp
    // dimensions here to match what would actually ship.
    private static long EstimateTextureVramBytes(Texture2D tex, PSXBPP bpp)
    {
        int w = System.Math.Min(tex.GetWidth(), 256);
        int h = System.Math.Min(tex.GetHeight(), 256);
        int pixels = w * h;
        long textureBytes = bpp switch
        {
            PSXBPP.TEX_4BIT => pixels / 2,         // 4 bits per pixel
            PSXBPP.TEX_8BIT => pixels,             // 1 byte per pixel
            PSXBPP.TEX_16BIT => pixels * 2L,       // 16-bit direct color
            _ => pixels,
        };
        long clutBytes = bpp switch
        {
            PSXBPP.TEX_4BIT => 16 * 2,              // 16 entries × 16-bit
            PSXBPP.TEX_8BIT => 256 * 2,             // 256 entries × 16-bit
            _ => 0,                                          // 16bpp direct, no CLUT
        };
        return textureBytes + clutBytes;
    }

    // ADPCM size ≈ samples × 16 / 28 (PSX SPU compresses 28 samples into
    // a 16-byte block). We derive samples from the AudioStreamWav's raw
    // data length; the mix() stage in the real exporter may downsample,
    // but this is close enough to flag "over budget" before export.
    //
    // Only Gameplay-residency clips count — MenuOnly and LoadOnDemand
    // clips aren't expected to coexist with gameplay SPU state. Tracks
    // Phase 2.5 REF-GAP-9.
    private static long EstimateSpuBytes(PS1Scene scene)
    {
        if (scene.AudioClips == null) return 0;
        long total = 0;
        foreach (var clip in scene.AudioClips)
        {
            if (clip == null) continue;
            if (clip.Residency != PS1AudioClipResidency.Gameplay) continue;
            if (clip.Stream is not AudioStreamWav wav) continue;
            int bytesPerSample = wav.Format switch
            {
                AudioStreamWav.FormatEnum.Format8Bits => 1,
                AudioStreamWav.FormatEnum.Format16Bits => 2,
                _ => 2,
            };
            int channels = wav.Stereo ? 2 : 1;
            long samples = wav.Data.Length / (bytesPerSample * channels);
            total += (samples * 16) / 28;
        }
        return total;
    }

    private static Texture2D? ExtractAlbedoTexture(Material? mat)
    {
        if (mat == null) return null;
        if (mat is StandardMaterial3D std) return std.AlbedoTexture;
        if (mat is ShaderMaterial sm)
        {
            var val = sm.GetShaderParameter("albedo_tex");
            if (val.VariantType == Variant.Type.Object)
            {
                return val.As<Texture2D>();
            }
        }
        return null;
    }

    private static T? FindFirst<T>(Node n) where T : Node
    {
        if (n is T match)
        {
            return match;
        }
        foreach (var child in n.GetChildren())
        {
            var found = FindFirst<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
#endif
