#if TOOLS
using Godot;

namespace PS1Godot.UI;

// Lightweight stats walker for the dock's Scene section. Counts what
// the export pipeline cares about without invoking the full
// SceneCollector (which logs, allocates texture atlases, and writes
// splashpacks). Runs on every scene-change tick so it must stay cheap.
public static class SceneStats
{
    public readonly struct Result
    {
        public readonly bool HasPS1Scene;
        public readonly string? SceneName;
        public readonly int MeshCount;
        public readonly int TriangleCount;
        public readonly int AudioClipCount;
        public readonly int TargetTriangles;   // Budget from PS1Scene, 0 if unset.
        public readonly int MaxActors;
        public readonly int MaxTexturePages;

        public Result(bool hasScene, string? name, int meshes, int tris, int audio,
                      int targetTris, int maxActors, int maxTexPages)
        {
            HasPS1Scene = hasScene;
            SceneName = name;
            MeshCount = meshes;
            TriangleCount = tris;
            AudioClipCount = audio;
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
            return new Result(false, null, 0, 0, 0, 0, 0, 0);
        }

        var scene = FindFirst<PS1Scene>(root);
        if (scene == null)
        {
            return new Result(false, null, 0, 0, 0, 0, 0, 0);
        }

        int meshes = 0;
        int tris = 0;
        int audio = scene.AudioClips?.Count ?? 0;
        WalkCounts(root, ref meshes, ref tris);

        return new Result(
            hasScene: true,
            name: root.Name,
            meshes: meshes,
            tris: tris,
            audio: audio,
            targetTris: scene.TargetTriangles,
            maxActors: scene.MaxActors,
            maxTexPages: scene.MaxTexturePages);
    }

    private static void WalkCounts(Node n, ref int meshes, ref int tris)
    {
        if (n is PS1MeshInstance pmi && pmi.Mesh != null)
        {
            meshes++;
            int surfaceCount = pmi.Mesh.GetSurfaceCount();
            for (int s = 0; s < surfaceCount; s++)
            {
                var arrays = pmi.Mesh.SurfaceGetArrays(s);
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                tris += indices.Length > 0 ? indices.Length / 3 : 0;
            }
        }

        foreach (var child in n.GetChildren())
        {
            WalkCounts(child, ref meshes, ref tris);
        }
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
