#if TOOLS
using System;
using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Phase L2 — vertex-color ambient occlusion bake.
//
// PS1 art at its best (Silent Hill, FFIX, MGS) shipped corner-cavity
// darkening baked into vertex colors. Lambertian alone makes flat-
// shaded geometry look plasticky; AO darkens the recesses and sells
// the chunky aesthetic. This operator computes that AO term and
// multiplies it into PS1MeshInstance.BakedColors so authors can
// layer it over a directional bake (or use it standalone).
//
// Algorithm:
//   For each vertex of each selected PS1MeshInstance:
//     1. Origin = world position + normal × bias (avoid self-hit at source)
//     2. Generate N stratified hemisphere directions oriented along normal
//     3. For each direction, ray-test against ALL collected scene
//        triangles (Möller–Trumbore, brute force). Count hits.
//     4. AO = 1 − (hits/N) × strength
//     5. New color = existing[i] × AO
//   Write the new color array back to BakedColors.
//
// We don't require authored colliders — the bake collects triangles
// directly from every visible PS1MeshInstance / PS1MeshGroup mesh in
// the scene. That means AO works on any scene the moment it's
// authored, no collision-shape dance.
//
// Brute force is intentional. For a scene of ~5k triangles with 12
// rays/vertex on a 200-vertex mesh, that's 12M ray-tri tests — runs
// in 1-2 seconds. A BVH would be 10× faster but adds complexity we
// don't need yet. Future tier when authors complain about scenes
// >50k triangles.
//
// Layering with directional bakes:
//   - Run "Bake Vertex Lighting from Scene Lights" first → fills
//     BakedColors with the directional Lambert.
//   - Run this AO bake → MULTIPLIES the existing colors by the AO
//     term (corners darken; flat areas keep the lighting).
//   - Order doesn't matter mathematically since both are
//     multiplicative, but running AO first then directional would
//     produce the same result.
public static class VertexAOBaker
{
    private const float PSXVertexBakeCeiling = 0.8f;

    public sealed class Options
    {
        public int RayCount = 12;
        public float MaxRayDistance = 0.5f;   // metres
        public float Strength = 0.5f;          // 0 = no AO, 1 = pure occlusion → black at 100% hit
        public float Bias = 0.001f;            // metres — push origin off surface
    }

    public sealed class Result
    {
        public int MeshesBaked = 0;
        public int VerticesPainted = 0;
        public long TrianglesInScene = 0;
        public long RaysCast = 0;
        public int Skipped = 0;
        public List<string> SkippedReasons = new();
    }

    public static Result Bake(Node sceneRoot, IReadOnlyList<Node> selection, Options opts)
    {
        var result = new Result();

        // 1. Collect occluder triangles from every visible mesh in the
        //    scene (selected or not — we want full-scene occlusion).
        var triangles = new List<(Vector3 a, Vector3 b, Vector3 c)>();
        CollectTriangles(sceneRoot, triangles);
        result.TrianglesInScene = triangles.Count;

        if (triangles.Count == 0)
        {
            // Edge case: nothing to occlude against, AO is no-op.
            // Don't mutate BakedColors — preserves whatever directional
            // bake the author already had.
            return result;
        }

        // 2. Build cosine-weighted hemisphere samples (canonical
        //    space, +Z = up). Reused across every vertex via TBN
        //    rotation.
        var canonicalSamples = BuildHemisphereSamples(opts.RayCount);

        // 3. For each selected mesh, bake.
        foreach (var node in selection)
        {
            if (node is not PS1MeshInstance pmi)
            {
                continue;
            }
            if (pmi.Mesh == null)
            {
                result.Skipped++;
                result.SkippedReasons.Add($"{pmi.Name}: no Mesh assigned");
                continue;
            }
            if (pmi.Mesh.GetSurfaceCount() != 1)
            {
                result.Skipped++;
                result.SkippedReasons.Add(
                    $"{pmi.Name}: {pmi.Mesh.GetSurfaceCount()} surfaces — Phase L2 single-surface only.");
                continue;
            }

            var arrays = pmi.Mesh.SurfaceGetArrays(0);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            if (verts.Length == 0)
            {
                result.Skipped++;
                result.SkippedReasons.Add($"{pmi.Name}: surface 0 has 0 vertices");
                continue;
            }
            bool haveNormals = normals.Length == verts.Length;

            var meshWorld = pmi.GlobalTransform;
            var meshRot = meshWorld.Basis;

            // Existing BakedColors (typically from a prior directional
            // bake). Multiply AO into them; if missing, start from
            // white-at-the-PSX-ceiling.
            var existing = pmi.BakedColors;
            bool hasExisting = existing != null && existing.Length == verts.Length;

            var output = new Color[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 wp = meshWorld * verts[i];
                Vector3 wn = haveNormals
                    ? (meshRot * normals[i]).Normalized()
                    : Vector3.Up;
                Vector3 origin = wp + wn * opts.Bias;

                int hits = 0;
                BuildTangentBasis(wn, out Vector3 tan, out Vector3 bit);
                foreach (var local in canonicalSamples)
                {
                    Vector3 dir = (tan * local.X + bit * local.Y + wn * local.Z).Normalized();
                    if (RayHitsAnyTriangle(origin, dir, opts.MaxRayDistance, triangles))
                    {
                        hits++;
                    }
                }
                result.RaysCast += opts.RayCount;

                float ao = 1.0f - (hits / (float)opts.RayCount) * opts.Strength;
                if (ao < 0.0f) ao = 0.0f;

                Color baseC = hasExisting ? existing![i] : new Color(PSXVertexBakeCeiling, PSXVertexBakeCeiling, PSXVertexBakeCeiling, 1.0f);
                output[i] = new Color(
                    Mathf.Clamp(baseC.R * ao, 0.0f, PSXVertexBakeCeiling),
                    Mathf.Clamp(baseC.G * ao, 0.0f, PSXVertexBakeCeiling),
                    Mathf.Clamp(baseC.B * ao, 0.0f, PSXVertexBakeCeiling),
                    1.0f);
            }
            pmi.BakedColors = output;
            result.MeshesBaked++;
            result.VerticesPainted += output.Length;
        }

        return result;
    }

    // ── Triangle collection ─────────────────────────────────────────

    /// <summary>Walks every PS1MeshInstance / PS1MeshGroup descendant,
    /// transforms each triangle into world space, appends to the list.
    /// PS1MeshGroup descends through to its child MeshInstance3D nodes.</summary>
    private static void CollectTriangles(Node n, List<(Vector3, Vector3, Vector3)> dest)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null && mi.Visible)
        {
            AppendMeshTriangles(mi, dest);
        }
        foreach (var child in n.GetChildren())
        {
            CollectTriangles(child, dest);
        }
    }

    private static void AppendMeshTriangles(MeshInstance3D mi, List<(Vector3, Vector3, Vector3)> dest)
    {
        var world = mi.GlobalTransform;
        var mesh = mi.Mesh!;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();

            int triCount = indices.Length > 0 ? indices.Length / 3 : verts.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                int i0 = indices.Length > 0 ? indices[t * 3 + 0] : t * 3 + 0;
                int i1 = indices.Length > 0 ? indices[t * 3 + 1] : t * 3 + 1;
                int i2 = indices.Length > 0 ? indices[t * 3 + 2] : t * 3 + 2;
                Vector3 a = world * verts[i0];
                Vector3 b = world * verts[i1];
                Vector3 c = world * verts[i2];
                dest.Add((a, b, c));
            }
        }
    }

    // ── Hemisphere sampling ─────────────────────────────────────────

    /// <summary>Cosine-weighted hemisphere samples in canonical space
    /// (+Z up). Fibonacci spiral pattern — even coverage for any N.</summary>
    private static Vector3[] BuildHemisphereSamples(int count)
    {
        if (count < 1) count = 1;
        var samples = new Vector3[count];
        float goldenAngle = MathF.PI * (3.0f - MathF.Sqrt(5.0f));   // ≈ 2.39996
        for (int i = 0; i < count; i++)
        {
            // Cosine weighting: z = (i + 0.5) / count maps i = 0 → straight
            // down (z = 0.5/N → almost grazing), i = N-1 → pole (z near 1).
            // Skewing samples toward the pole biases AO toward the
            // normal direction, matching what the diffuse-cosine
            // integral wants.
            float t = (i + 0.5f) / count;
            float z = t;
            float r = MathF.Sqrt(MathF.Max(0.0f, 1.0f - z * z));
            float phi = goldenAngle * i;
            samples[i] = new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), z);
        }
        return samples;
    }

    /// <summary>Build an orthonormal basis (T, B, N) given N. Used to
    /// transform canonical-hemisphere samples into the vertex's local
    /// frame.</summary>
    private static void BuildTangentBasis(Vector3 n, out Vector3 tangent, out Vector3 bitangent)
    {
        Vector3 helper = MathF.Abs(n.X) > 0.99f ? Vector3.Up : Vector3.Right;
        tangent = n.Cross(helper).Normalized();
        bitangent = n.Cross(tangent);
    }

    // ── Ray-triangle test ───────────────────────────────────────────

    private static bool RayHitsAnyTriangle(
        Vector3 origin, Vector3 dir, float maxDist,
        List<(Vector3 a, Vector3 b, Vector3 c)> triangles)
    {
        // Brute force. Future: BVH or octree if scene-wide AO becomes
        // too slow. For ~5-10k triangles this loop is sub-second per
        // vertex.
        for (int i = 0; i < triangles.Count; i++)
        {
            var (a, b, c) = triangles[i];
            if (RayIntersectsTriangle(origin, dir, maxDist, a, b, c))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Möller–Trumbore. Returns true when the ray
    /// `origin + t × dir` hits the triangle (a, b, c) at t in
    /// (epsilon, maxDist).</summary>
    private static bool RayIntersectsTriangle(
        Vector3 origin, Vector3 dir, float maxDist,
        Vector3 a, Vector3 b, Vector3 c)
    {
        const float EPS = 1e-6f;
        Vector3 e1 = b - a;
        Vector3 e2 = c - a;
        Vector3 p = dir.Cross(e2);
        float det = e1.Dot(p);
        if (det > -EPS && det < EPS) return false;          // ray parallel to triangle
        float invDet = 1.0f / det;
        Vector3 toV0 = origin - a;
        float u = toV0.Dot(p) * invDet;
        if (u < 0.0f || u > 1.0f) return false;
        Vector3 q = toV0.Cross(e1);
        float v = dir.Dot(q) * invDet;
        if (v < 0.0f || u + v > 1.0f) return false;
        float distance = e2.Dot(q) * invDet;
        return distance > EPS && distance < maxDist;
    }
}
#endif
