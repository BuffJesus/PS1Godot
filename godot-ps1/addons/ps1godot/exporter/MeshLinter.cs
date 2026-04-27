#if TOOLS
using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Mesh-time validation that runs alongside PSXMesh emission. Today the
// only check is UV out-of-range — the PSX rasteriser doesn't wrap or
// clamp UV coords; out-of-range UVs sample whatever happens to sit at
// that VRAM tpage offset (usually a neighbouring texture or palette
// data, rendered as garbage). Editor-time Godot won't flag this
// because its sampler wraps by default.
//
// Tracked as a Phase 3 ROADMAP item with 2 upvotes on the psxsplash
// Discord ("Fix UVs of meshes when exporting if the UV coordinates
// are too large").
//
// Pattern matches TextureValidationReport: accumulator is module-
// private, reset at scene start, emitted at scene end. Single-threaded
// exporter — no locking needed.
public static class MeshLinter
{
    private const float Tolerance = 0.001f;       // round-off slack
    private const float HardWarnAbove = 1.5f;     // tile-by-tile authored beyond this is suspicious

    private sealed class MeshStat
    {
        public int Surfaces;
        public int Vertices;
        public int OutOfRange;     // any UV outside [-tol, 1+tol]
        public int FarOutOfRange;  // any UV with |U|>1.5 or |V|>1.5
        public float UMin = float.MaxValue;
        public float UMax = float.MinValue;
        public float VMin = float.MaxValue;
        public float VMax = float.MinValue;
    }

    private static readonly Dictionary<string, MeshStat> _stats = new();

    public static void ResetForScene() => _stats.Clear();

    // Called by PSXMesh.FromGodotMesh / AppendFromGodotSurface for each
    // surface. Walks every UV in the surface and updates the per-mesh
    // accumulator. `meshName` is the GameObject-level name so the
    // emitted report points at the same identifier the user sees in
    // the export log.
    public static void RecordSurface(string meshName, Vector2[] uvs, bool tilingExpected = false)
    {
        if (uvs == null || uvs.Length == 0) return;
        if (tilingExpected) return;  // author flagged this mesh as intentionally tiling
        if (!_stats.TryGetValue(meshName, out var s))
        {
            s = new MeshStat();
            _stats[meshName] = s;
        }
        s.Surfaces++;
        s.Vertices += uvs.Length;
        foreach (var uv in uvs)
        {
            if (uv.X < s.UMin) s.UMin = uv.X;
            if (uv.X > s.UMax) s.UMax = uv.X;
            if (uv.Y < s.VMin) s.VMin = uv.Y;
            if (uv.Y > s.VMax) s.VMax = uv.Y;
            bool oob = uv.X < -Tolerance || uv.X > 1f + Tolerance
                    || uv.Y < -Tolerance || uv.Y > 1f + Tolerance;
            if (oob)
            {
                s.OutOfRange++;
                if (Mathf.Abs(uv.X) > HardWarnAbove || Mathf.Abs(uv.Y) > HardWarnAbove)
                    s.FarOutOfRange++;
            }
        }
    }

    // Print one row per mesh that had ANY out-of-range UV. Quiet when
    // every mesh is clean. Counts are absolute; pct is "% of verts that
    // sample outside [0,1]". The 1.5 threshold splits "tiled author
    // intent" from "definitely wrong" — meshes flagged Far reach into
    // adjacent atlas tiles regardless of the runtime's wrap mode.
    //
    // Returns the number of meshes with out-of-range UVs. Plugin sums
    // this into the dock's "Last export" line.
    public static int EmitForScene(int sceneIndex)
    {
        int dirtyCount = 0;
        foreach (var kv in _stats)
        {
            if (kv.Value.OutOfRange > 0) dirtyCount++;
        }
        if (dirtyCount == 0)
        {
            GD.Print($"[PS1Godot]   UV linter scene[{sceneIndex}]: all meshes clean.");
            return 0;
        }

        GD.PushWarning($"[PS1Godot] UV linter scene[{sceneIndex}]: {dirtyCount} mesh(es) with out-of-range UVs. Out-of-range UVs sample neighbouring VRAM data on PSX (no wrap/clamp). Fix at the source mesh or tag tiling explicitly.");
        GD.Print("[PS1Godot]   mesh                                 verts   oob   far   U range          V range");
        var sorted = new List<KeyValuePair<string, MeshStat>>(_stats);
        sorted.Sort((a, b) => b.Value.OutOfRange.CompareTo(a.Value.OutOfRange));
        foreach (var kv in sorted)
        {
            var s = kv.Value;
            if (s.OutOfRange == 0) continue;
            string name = kv.Key.Length <= 36 ? kv.Key : kv.Key[..35] + "…";
            string u = $"[{s.UMin:F2}, {s.UMax:F2}]";
            string v = $"[{s.VMin:F2}, {s.VMax:F2}]";
            GD.Print($"[PS1Godot]   {name,-36} {s.Vertices,5} {s.OutOfRange,5} {s.FarOutOfRange,5}   {u,-15}  {v,-15}");
        }

        return dirtyCount;
    }
}
#endif
