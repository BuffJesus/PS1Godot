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

        public Result(bool hasScene, string? name, int meshes, int tris, int audio,
                      int textures, long vramBytes, long spuBytes,
                      int targetTris, int maxActors, int maxTexPages)
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
        }
    }

    // Returns `HasPS1Scene = false` when no PS1Scene is in the tree so the
    // dock can show a "drop a PS1Scene here" hint rather than zeros.
    public static Result Compute(Node? root)
    {
        if (root == null)
        {
            return new Result(false, null, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var scene = FindFirst<PS1Scene>(root);
        if (scene == null)
        {
            return new Result(false, null, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        int meshes = 0;
        int tris = 0;
        int audio = scene.AudioClips?.Count ?? 0;
        var textureKeys = new HashSet<string>();
        long vramBytes = 0;
        long spuBytes = EstimateSpuBytes(scene);

        WalkMeshes(root, ref meshes, ref tris, textureKeys, ref vramBytes);
        WalkUIAndSky(root, textureKeys, ref vramBytes);

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
            maxTexPages: scene.MaxTexturePages);
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
