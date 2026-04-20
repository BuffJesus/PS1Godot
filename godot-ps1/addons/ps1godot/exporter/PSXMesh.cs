using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Godot Mesh → PSX-format triangle list.
//
// For each mesh surface, the caller supplies a texture index (or -1 for
// untextured). Untextured triangles use FlatColor on all three verts and
// zero UVs; textured triangles carry real UVs in texel-local coords. The
// splashpack writer applies atlas packing offsets at write time, so
// per-surface PackingX/Y don't reach this file.
public sealed class PSXMesh
{
    public List<Tri> Triangles { get; } = new();

    public static PSXMesh FromGodotMesh(MeshInstance3D node, float gteScaling,
        PS1MeshInstance.ColorMode colorMode, Color flatColor,
        int[] surfaceTextureIndices, List<PSXTexture> textures)
    {
        var psx = new PSXMesh();
        var mesh = node.Mesh;
        if (mesh == null) return psx;

        if (colorMode != PS1MeshInstance.ColorMode.FlatColor)
        {
            // Phase 2.5 lands BakedLighting; MeshVertexColors needs Color array support.
            GD.PushWarning($"[PS1Godot] {node.Name}: VertexColorMode={colorMode} not yet implemented; falling back to FlatColor.");
        }

        byte rByte = PSXTrig.ColorChannelToPSX(flatColor.R);
        byte gByte = PSXTrig.ColorChannelToPSX(flatColor.G);
        byte bByte = PSXTrig.ColorChannelToPSX(flatColor.B);

        int surfaceCount = mesh.GetSurfaceCount();
        for (int s = 0; s < surfaceCount; s++)
        {
            int texIdx = s < surfaceTextureIndices.Length ? surfaceTextureIndices[s] : -1;
            PSXTexture? tex = (texIdx >= 0 && texIdx < textures.Count) ? textures[texIdx] : null;

            var arrays = mesh.SurfaceGetArrays(s);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();

            int triCount = indices.Length > 0 ? indices.Length / 3 : verts.Length / 3;

            for (int t = 0; t < triCount; t++)
            {
                int i0 = indices.Length > 0 ? indices[t * 3 + 0] : t * 3 + 0;
                int i1 = indices.Length > 0 ? indices[t * 3 + 1] : t * 3 + 1;
                int i2 = indices.Length > 0 ? indices[t * 3 + 2] : t * 3 + 2;

                var p0 = verts[i0];
                var p1 = verts[i1];
                var p2 = verts[i2];

                // Y-negation in MakeVertex is a reflection that reverses
                // screen-space winding. Compensate with a single unconditional
                // swap so nclip (projected-cross sign) sees PSX-front-facing
                // CW for Godot-front-facing input.
                (i1, i2) = (i2, i1);
                (p1, p2) = (p2, p1);

                Vector2 uv0 = uvs.Length > i0 ? uvs[i0] : Vector2.Zero;
                Vector2 uv1 = uvs.Length > i1 ? uvs[i1] : Vector2.Zero;
                Vector2 uv2 = uvs.Length > i2 ? uvs[i2] : Vector2.Zero;

                psx.Triangles.Add(new Tri
                {
                    v0 = MakeVertex(p0, normals.Length > i0 ? normals[i0] : Vector3.Up, uv0, tex, gteScaling, rByte, gByte, bByte),
                    v1 = MakeVertex(p1, normals.Length > i1 ? normals[i1] : Vector3.Up, uv1, tex, gteScaling, rByte, gByte, bByte),
                    v2 = MakeVertex(p2, normals.Length > i2 ? normals[i2] : Vector3.Up, uv2, tex, gteScaling, rByte, gByte, bByte),
                    TextureIndex = texIdx,
                });
            }
        }

        return psx;
    }

    private static PSXVertex MakeVertex(Vector3 pos, Vector3 normal, Vector2 uv, PSXTexture? tex,
        float gteScaling, byte r, byte g, byte b)
    {
        byte uByte = 0, vByte = 0;
        if (tex != null)
        {
            // UV is in 0..1 texture-space. Multiply by texture pixel size to get
            // a texel-local byte coord. The writer adds PackingX*expander / PackingY
            // at write time to shift into atlas-local space.
            //
            // Godot UV origin is top-left, same as PSX VRAM — no Y flip needed.
            int maxU = System.Math.Max(tex.Width - 1, 0);
            int maxV = System.Math.Max(tex.Height - 1, 0);
            uByte = (byte)System.Math.Clamp((int)(uv.X * maxU + 0.5f), 0, 255);
            vByte = (byte)System.Math.Clamp((int)(uv.Y * maxV + 0.5f), 0, 255);
        }

        return new PSXVertex
        {
            // Godot → PSX: negate Y only (PS1 is Y-down). Z stays as-is — the
            // camera-direction mismatch (Godot looks -Z, PSX +Z) is solved by
            // rotating the camera 180° around Y in the psxsplash patch.
            vx = PSXTrig.ConvertCoordinateToPSX(pos.X, gteScaling),
            vy = PSXTrig.ConvertCoordinateToPSX(-pos.Y, gteScaling),
            vz = PSXTrig.ConvertCoordinateToPSX(pos.Z, gteScaling),

            nx = PSXTrig.ConvertToFixed12(normal.X),
            ny = PSXTrig.ConvertToFixed12(-normal.Y),
            nz = PSXTrig.ConvertToFixed12(normal.Z),

            u = uByte,
            v = vByte,
            r = r,
            g = g,
            b = b,
        };
    }
}
