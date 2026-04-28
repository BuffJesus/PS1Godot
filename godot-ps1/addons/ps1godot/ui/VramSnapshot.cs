#if TOOLS
using System.Collections.Generic;
using PS1Godot.Exporter;

namespace PS1Godot.UI;

// Frozen view of one scene's packed VRAM layout, captured after the
// splashpack writer runs. Decouples the dock from the live SceneData /
// VRAMPacker objects (which can be replaced or mutated on the next
// export) and gives us a small, self-contained payload to pass through
// signals or store across editor-tree teardowns.
//
// One snapshot per scene. The PS1VRAMViewerDock currently displays the
// last-exported scene's snapshot; a dropdown picker for multi-scene
// projects is a v2 follow-up.
public sealed class VramSnapshot
{
    public string SceneName { get; init; } = "";
    public int SceneIndex { get; init; }

    // Atlas regions in VRAM-word coords (1024×512 grid). Width is in
    // 16-bit words along X; height is pixel rows along Y.
    public IReadOnlyList<AtlasRect> Atlases { get; init; } = System.Array.Empty<AtlasRect>();

    // Per-texture sub-rects within atlases. Useful for showing how full
    // each atlas is — empty space inside an atlas is wasted residency.
    public IReadOnlyList<TextureRect> Textures { get; init; } = System.Array.Empty<TextureRect>();

    // CLUT positions. Each CLUT is `Length` words wide, 1 pixel tall.
    public IReadOnlyList<ClutRect> Cluts { get; init; } = System.Array.Empty<ClutRect>();

    // Total VRAM pixels covered by atlases + CLUTs (excludes reserved
    // framebuffer / font column). Used for the "X% used" header line.
    public long UsedPixels { get; init; }

    // Convenience flags so the dock can show "(empty)" hints without
    // peeking inside the lists.
    public bool IsEmpty => Atlases.Count == 0 && Cluts.Count == 0;

    public sealed record AtlasRect(int X, int Y, int Width, int Height, PSXBPP BitDepth);
    public sealed record TextureRect(int X, int Y, int Width, int Height, PSXBPP BitDepth, string Name);
    public sealed record ClutRect(int X, int Y, int Length, PSXBPP BitDepth, string OwnerTextureName);

    // Build a snapshot from a SceneData whose Packer has already run
    // (i.e. after SplashpackWriter.Write returns). Returns an empty
    // snapshot when the scene has no textures — the caller should
    // still call SetSnapshot so the dock can show "(no textures)".
    public static VramSnapshot Capture(SceneData scene, int sceneIndex)
    {
        var atlases = new List<AtlasRect>();
        var textures = new List<TextureRect>();
        var cluts = new List<ClutRect>();
        long usedPixels = 0;

        if (scene.Packer != null)
        {
            // Each atlas chunk reports its full residency footprint
            // (always 256 tall on PSX, width depends on bit depth).
            // The chunk's pixels include unused gaps between texture
            // sub-rects — the per-texture pass below visualises those.
            foreach (var chunk in scene.Packer.EnumerateAtlasChunks())
            {
                // EnumerateAtlasChunks doesn't expose the atlas's own
                // bit depth directly, but every texture inside an
                // atlas shares the parent's bpp. Sniff from the first
                // texture sitting in this chunk's region.
                PSXBPP bpp = SniffAtlasBpp(scene, chunk.vramX, chunk.vramY);
                atlases.Add(new AtlasRect(chunk.vramX, chunk.vramY, chunk.width, chunk.height, bpp));
                usedPixels += (long)chunk.width * chunk.height;
            }

            // Texture sub-rects: position is atlas origin + per-texture
            // PackingX/Y. QuantizedWidth is in VRAM words (matches the
            // atlas Width units), Height is pixel rows.
            for (int i = 0; i < scene.Textures.Count; i++)
            {
                var t = scene.Textures[i];
                int x = t.TexpageX * 64 + t.PackingX;
                int y = t.TexpageY * 256 + t.PackingY;
                textures.Add(new TextureRect(
                    x, y, t.QuantizedWidth, t.Height, t.BitDepth,
                    ShortName(t.SourcePath)));
            }

            // CLUTs: one strip per palette-bearing texture. ClutPackingX
            // is pre-divided by 16 (CLUTs sit on 16-px X boundaries),
            // so multiply back to get the actual VRAM X.
            foreach (var t in scene.Packer.CLUTOwners())
            {
                if (t.ColorPalette == null) continue;
                int len = t.ColorPalette.Count;
                cluts.Add(new ClutRect(
                    t.ClutPackingX * 16, t.ClutPackingY, len, t.BitDepth,
                    ShortName(t.SourcePath)));
                usedPixels += len;
            }
        }

        return new VramSnapshot
        {
            SceneName = string.IsNullOrEmpty(scene.ScenePath)
                ? $"scene_{sceneIndex}"
                : System.IO.Path.GetFileNameWithoutExtension(scene.ScenePath),
            SceneIndex = sceneIndex,
            Atlases = atlases,
            Textures = textures,
            Cluts = cluts,
            UsedPixels = usedPixels,
        };
    }

    // Find the bit depth of whichever atlas owns the (x, y) region.
    // Walks scene.Textures looking for one whose absolute VRAM
    // position falls inside the chunk; that texture's BitDepth equals
    // its atlas's. Falls back to 16bpp on a miss (defensive — every
    // placed atlas should own at least one texture).
    private static PSXBPP SniffAtlasBpp(SceneData scene, int chunkX, int chunkY)
    {
        foreach (var t in scene.Textures)
        {
            int tx = t.TexpageX * 64 + t.PackingX;
            int ty = t.TexpageY * 256 + t.PackingY;
            // Atlas chunks are exactly 256 tall and their width matches
            // the bit depth (4bpp=64, 8bpp=128, 16bpp=256), so an
            // exact-match on chunkX/chunkY of the atlas origin would
            // require the per-atlas placement table. Easier: any
            // texture that lands inside this chunk's row band shares
            // bit depth with the chunk.
            if (tx >= chunkX && ty >= chunkY && ty < chunkY + 256 &&
                tx < chunkX + 256)
                return t.BitDepth;
        }
        return PSXBPP.TEX_16BIT;
    }

    private static string ShortName(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return "(unnamed)";
        string name = sourcePath.Replace("res://", "");
        var parts = name.Split('/');
        if (parts.Length <= 2) return name;
        return ".../" + string.Join("/", parts, parts.Length - 2, 2);
    }
}
#endif
