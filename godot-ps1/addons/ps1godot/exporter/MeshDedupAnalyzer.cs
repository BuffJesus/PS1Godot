using System.Collections.Generic;

namespace PS1Godot.Exporter;

// Vertex-pool dedup analyzer for the static-mesh format (splashpack
// v31, shipped Phase A.1). Computes how much each authored mesh shrinks
// from the legacy "Tri[] (52 B per tri, vertices expanded ×3)" layout
// to the live "Vertex[] + Face[]" pooled layout. Read-only — never
// mutates the input mesh, never touches the splashpack writer.
//
// We keep computing the legacy size as a comparison baseline so the
// summary line in the dock + console can report "saved X B vs the old
// format". The pooled size we compute here matches what
// SplashpackWriter.WriteStaticMeshPooled actually emits.
//
// Dedup key: (vx, vy, vz, u, v, r, g, b). Vertex normals are *not*
// part of the key because the on-disk Tri format already collapses to
// one face-normal per triangle, so two vertices that share pos+color+uv
// merge cleanly even if their authored vertex normals differ.
//
// Size model:
//   pre-pool layout: triCount × 52 bytes (legacy Tri struct, mesh.hh)
//   v31 pooled:      4 B header (vertexCount + triCount)
//                  + uniqueVertices × 12 B (pos 6 + color 4-aligned + uv 2)
//                  + triCount × 20 B (3 × u16 indices + face-normal 6 +
//                                     tpage 2 + clutX/Y 4 + pad 2)
//
// For a typical mesh with vertex reuse ≈ 2× (each vertex shared by two
// tris on average) the savings work out to ~50%; tighter manifold
// surfaces (≥ 4× reuse) push toward ~55%.
public static class MeshDedupAnalyzer
{
    private const int V29_BYTES_PER_TRI = 52;
    private const int V30_HEADER_BYTES   = 4;
    private const int V30_BYTES_PER_VERT = 12;
    private const int V30_BYTES_PER_TRI  = 20;

    public readonly struct Stats
    {
        public int TriCount        { get; init; }
        public int UniqueVertices  { get; init; }
        public int ExpandedVertices { get; init; }   // triCount × 3
        public int BytesV29        { get; init; }
        public int BytesV30        { get; init; }
        public int BytesSaved      { get; init; }
        public float SavingsPercent { get; init; }
        public float ReuseFactor    { get; init; }    // expanded / unique
    }

    private readonly struct VertexKey
    {
        public readonly short Vx, Vy, Vz;
        public readonly byte U, V, R, G, B;

        public VertexKey(PSXVertex v)
        {
            Vx = v.vx; Vy = v.vy; Vz = v.vz;
            U = v.u; V = v.v;
            R = v.r; G = v.g; B = v.b;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not VertexKey k) return false;
            return Vx == k.Vx && Vy == k.Vy && Vz == k.Vz
                && U == k.U && V == k.V
                && R == k.R && G == k.G && B == k.B;
        }

        public override int GetHashCode()
        {
            // 64-bit-ish hash folded to int, FNV-1a style.
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)(ushort)Vx) * 16777619u;
                h = (h ^ (uint)(ushort)Vy) * 16777619u;
                h = (h ^ (uint)(ushort)Vz) * 16777619u;
                h = (h ^ U) * 16777619u;
                h = (h ^ V) * 16777619u;
                h = (h ^ R) * 16777619u;
                h = (h ^ G) * 16777619u;
                h = (h ^ B) * 16777619u;
                return (int)h;
            }
        }
    }

    public static Stats Analyze(IReadOnlyList<Tri> triangles)
    {
        var pool = new HashSet<VertexKey>();
        int triCount = triangles?.Count ?? 0;
        if (triangles != null)
        {
            foreach (var tri in triangles)
            {
                pool.Add(new VertexKey(tri.v0));
                pool.Add(new VertexKey(tri.v1));
                pool.Add(new VertexKey(tri.v2));
            }
        }

        int unique   = pool.Count;
        int expanded = triCount * 3;
        int bytesOld = triCount * V29_BYTES_PER_TRI;
        int bytesNew = (triCount > 0)
            ? V30_HEADER_BYTES + unique * V30_BYTES_PER_VERT + triCount * V30_BYTES_PER_TRI
            : 0;
        int saved = bytesOld - bytesNew;
        float pct = (bytesOld > 0) ? (100f * saved / bytesOld) : 0f;
        float reuse = (unique > 0) ? ((float)expanded / unique) : 0f;

        return new Stats
        {
            TriCount         = triCount,
            UniqueVertices   = unique,
            ExpandedVertices = expanded,
            BytesV29         = bytesOld,
            BytesV30         = bytesNew,
            BytesSaved       = saved,
            SavingsPercent   = pct,
            ReuseFactor      = reuse,
        };
    }
}
