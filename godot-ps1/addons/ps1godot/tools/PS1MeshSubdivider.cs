using System.Collections.Generic;
using Godot;

namespace PS1Godot.Tools;

// Splits each triangle of a mesh into 4 smaller triangles (corner, corner,
// corner, center) by introducing midpoint vertices on every edge.
// Amplifies the PS1 affine-warp look in Godot: more tris = shorter spans
// for perspective-correct UV interpolation to "spread out," so the warping
// becomes visible per-face the way it was on real hardware.
//
// Destructive by design. Callers that want to revert must keep the original
// mesh resource themselves.
public static class PS1MeshSubdivider
{
    public static ArrayMesh Subdivide(Mesh source, int levels = 1)
    {
        var result = new ArrayMesh();
        if (source == null) return result;
        levels = Mathf.Clamp(levels, 0, 4);

        for (int s = 0; s < source.GetSurfaceCount(); s++)
        {
            var arrays = source.SurfaceGetArrays(s);
            for (int i = 0; i < levels; i++)
                arrays = SubdivideOnce(arrays);
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            var mat = source.SurfaceGetMaterial(s);
            if (mat != null) result.SurfaceSetMaterial(s, mat);
        }
        return result;
    }

    private static Godot.Collections.Array SubdivideOnce(Godot.Collections.Array src)
    {
        var verts = src[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = Has(src, Mesh.ArrayType.Normal) ? src[(int)Mesh.ArrayType.Normal].AsVector3Array() : null;
        var uvs = Has(src, Mesh.ArrayType.TexUV) ? src[(int)Mesh.ArrayType.TexUV].AsVector2Array() : null;
        var colors = Has(src, Mesh.ArrayType.Color) ? src[(int)Mesh.ArrayType.Color].AsColorArray() : null;
        var indices = Has(src, Mesh.ArrayType.Index) ? src[(int)Mesh.ArrayType.Index].AsInt32Array() : null;

        // Godot primitive meshes (BoxMesh, PlaneMesh, ...) often use indices.
        // Raw meshes might not — synthesize sequential indices so the rest
        // of the pass is uniform.
        if (indices == null || indices.Length == 0)
        {
            indices = new int[verts.Length];
            for (int k = 0; k < verts.Length; k++) indices[k] = k;
        }

        var nv = new List<Vector3>();
        var nn = normals != null ? new List<Vector3>() : null;
        var nu = uvs != null ? new List<Vector2>() : null;
        var nc = colors != null ? new List<Color>() : null;
        var ni = new List<int>();

        int triCount = indices.Length / 3;
        for (int t = 0; t < triCount; t++)
        {
            int i0 = indices[t * 3], i1 = indices[t * 3 + 1], i2 = indices[t * 3 + 2];
            int b = nv.Count;

            // Corners 0,1,2 then midpoints m01=3, m12=4, m20=5
            nv.Add(verts[i0]); nv.Add(verts[i1]); nv.Add(verts[i2]);
            nv.Add((verts[i0] + verts[i1]) * 0.5f);
            nv.Add((verts[i1] + verts[i2]) * 0.5f);
            nv.Add((verts[i2] + verts[i0]) * 0.5f);

            if (nn != null)
            {
                nn.Add(normals![i0]); nn.Add(normals[i1]); nn.Add(normals[i2]);
                nn.Add(((normals[i0] + normals[i1]) * 0.5f).Normalized());
                nn.Add(((normals[i1] + normals[i2]) * 0.5f).Normalized());
                nn.Add(((normals[i2] + normals[i0]) * 0.5f).Normalized());
            }
            if (nu != null)
            {
                nu.Add(uvs![i0]); nu.Add(uvs[i1]); nu.Add(uvs[i2]);
                nu.Add((uvs[i0] + uvs[i1]) * 0.5f);
                nu.Add((uvs[i1] + uvs[i2]) * 0.5f);
                nu.Add((uvs[i2] + uvs[i0]) * 0.5f);
            }
            if (nc != null)
            {
                nc.Add(colors![i0]); nc.Add(colors[i1]); nc.Add(colors[i2]);
                nc.Add((colors[i0] + colors[i1]) * 0.5f);
                nc.Add((colors[i1] + colors[i2]) * 0.5f);
                nc.Add((colors[i2] + colors[i0]) * 0.5f);
            }

            // 4 children, winding preserved
            ni.Add(b + 0); ni.Add(b + 3); ni.Add(b + 5);
            ni.Add(b + 1); ni.Add(b + 4); ni.Add(b + 3);
            ni.Add(b + 2); ni.Add(b + 5); ni.Add(b + 4);
            ni.Add(b + 3); ni.Add(b + 4); ni.Add(b + 5);
        }

        var dst = new Godot.Collections.Array();
        dst.Resize((int)Mesh.ArrayType.Max);
        dst[(int)Mesh.ArrayType.Vertex] = nv.ToArray();
        if (nn != null) dst[(int)Mesh.ArrayType.Normal] = nn.ToArray();
        if (nu != null) dst[(int)Mesh.ArrayType.TexUV] = nu.ToArray();
        if (nc != null) dst[(int)Mesh.ArrayType.Color] = nc.ToArray();
        dst[(int)Mesh.ArrayType.Index] = ni.ToArray();
        return dst;
    }

    private static bool Has(Godot.Collections.Array arr, Mesh.ArrayType t)
    {
        return arr[(int)t].VariantType != Variant.Type.Nil;
    }

    public static int CountTriangles(Mesh mesh)
    {
        if (mesh == null) return 0;
        int total = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arr = mesh.SurfaceGetArrays(s);
            if (Has(arr, Mesh.ArrayType.Index))
                total += arr[(int)Mesh.ArrayType.Index].AsInt32Array().Length / 3;
            else
                total += arr[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length / 3;
        }
        return total;
    }
}
