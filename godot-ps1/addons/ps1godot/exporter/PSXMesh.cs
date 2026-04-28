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
        int[] surfaceTextureIndices, List<PSXTexture> textures,
        Color[]? bakedColorsSurface0 = null)
    {
        var psx = new PSXMesh();
        var mesh = node.Mesh;
        if (mesh == null) return psx;

        if (colorMode != PS1MeshInstance.ColorMode.FlatColor)
        {
            // Phase 2.5 lands BakedLighting; MeshVertexColors needs Color array support.
            GD.PushWarning($"[PS1Godot] {node.Name}: VertexColorMode={colorMode} not yet implemented; falling back to FlatColor.");
        }

        // Bake the node's effective scale (including ancestor scales) into
        // vertex positions. FBX/GLTF assets often import at non-1 internal
        // scale (cm, mm, etc.) and the author compensates by scaling either
        // the mesh or a parent so it looks right in the Godot viewport.
        // The exporter reads raw SurfaceGetArrays verts which don't include
        // any scale.
        Vector3 nodeScale = node.GlobalTransform.Basis.Scale;

        // Special case for skinned FBX imports (Mixamo etc.): Godot leaves
        // the vertices in the FBX's native units (typically cm, so
        // humanoid verts are ~180 tall) but bakes the cm→m unit-conversion
        // factor into each Skin bind-pose basis. Godot's own renderer
        // relies on that bind-scale to place the mesh at meter-scale, so
        // the host transform stays at identity (otherwise the scale is
        // applied twice and the mesh disappears in the viewport). Our
        // PSX pipeline uses orthonormalized bone matrices (scale stripped
        // so int16 fp12 fits), which means the EXPORTER has to apply the
        // cm→m factor itself — we pull it from the skin's bind basis
        // rather than relying on the node transform.
        if (node.Skin != null && node.Skin.GetBindCount() > 0)
        {
            Vector3 bindScale = node.Skin.GetBindPose(0).Basis.Scale;
            nodeScale *= bindScale;
        }
        // Use a relative tolerance large enough to ignore Godot's basis-
        // decomposition noise (we've seen ~1.5e-4 drift on "uniform"
        // cubes). Real non-uniform authoring is typically at least a few
        // percent apart, so 1% is generous without being alarmist.
        float scaleMean = (Mathf.Abs(nodeScale.X) + Mathf.Abs(nodeScale.Y) + Mathf.Abs(nodeScale.Z)) / 3f;
        float scaleTol = Mathf.Max(scaleMean * 0.01f, 1e-4f);
        bool nonUniformScale =
            Mathf.Abs(nodeScale.X - nodeScale.Y) > scaleTol ||
            Mathf.Abs(nodeScale.Y - nodeScale.Z) > scaleTol;
        if (nonUniformScale)
        {
            GD.PushWarning($"[PS1Godot] {node.Name}: non-uniform Scale {nodeScale}. Combined with PSX affine texture warp this can look visibly wrong. Prefer uniform scale, or bake the scale into the mesh asset.");
        }

        // Diagnostic: print pre- and post-scale extents for the first surface
        // so we can verify the scale bake matches authoring intent. Remove
        // once the scale pipeline is trusted.
        if (mesh.GetSurfaceCount() > 0)
        {
            var diag = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (diag.Length > 0)
            {
                Vector3 min = diag[0], max = diag[0];
                for (int i = 1; i < diag.Length; i++)
                {
                    min = new Vector3(Mathf.Min(min.X, diag[i].X), Mathf.Min(min.Y, diag[i].Y), Mathf.Min(min.Z, diag[i].Z));
                    max = new Vector3(Mathf.Max(max.X, diag[i].X), Mathf.Max(max.Y, diag[i].Y), Mathf.Max(max.Z, diag[i].Z));
                }
                Vector3 extentRaw = max - min;
                Vector3 extentScaled = extentRaw * nodeScale;
                GD.Print($"[PS1Godot] {node.Name} mesh extent: raw={extentRaw:F3} × scale={nodeScale:F3} → scaled={extentScaled:F3}");
            }
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
            // Mesh-intrinsic vertex colours (e.g., a Blender Cycles bake
            // imported through glTF, or any Godot ImportPlugin that
            // populates the COLOR channel). Used as a fallback when no
            // per-instance BakedColors override exists, so authors who
            // baked in Blender + re-imported don't have to re-bake on
            // the Godot side. Per-surface, length-matched against
            // verts so multi-material meshes can colour each surface
            // independently.
            var meshColors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();

            MeshLinter.RecordSurface(node.Name, uvs,
                tilingExpected: node is PS1MeshInstance tilingPmi && tilingPmi.TilingUV);

            int triCount = indices.Length > 0 ? indices.Length / 3 : verts.Length / 3;

            // Per-surface decision: prefer the per-instance BakedColors
            // override (Godot-side bake), fall back to the mesh's COLOR
            // channel (Blender-side bake), fall back to flat colour.
            bool useBaked0 = s == 0
                && bakedColorsSurface0 != null
                && bakedColorsSurface0.Length == verts.Length;
            bool useMeshColors = !useBaked0
                && meshColors.Length == verts.Length;

            for (int t = 0; t < triCount; t++)
            {
                int i0 = indices.Length > 0 ? indices[t * 3 + 0] : t * 3 + 0;
                int i1 = indices.Length > 0 ? indices[t * 3 + 1] : t * 3 + 1;
                int i2 = indices.Length > 0 ? indices[t * 3 + 2] : t * 3 + 2;

                var p0 = verts[i0] * nodeScale;
                var p1 = verts[i1] * nodeScale;
                var p2 = verts[i2] * nodeScale;

                // MakeVertex reflects in BOTH Y and Z. Two reflections compose
                // to a rotation, which preserves winding — no swap needed
                // (this used to flip i1↔i2 to compensate for a Y-only flip).
                // INVARIANT: SceneCollector.ComputeBoneIndices must match
                // whatever decision we make here. If you ever re-add an
                // (i1, i2) swap, mirror it there too or every skinned-mesh
                // vertex ends up with the wrong bone matrix.

                Vector2 uv0 = uvs.Length > i0 ? uvs[i0] : Vector2.Zero;
                Vector2 uv1 = uvs.Length > i1 ? uvs[i1] : Vector2.Zero;
                Vector2 uv2 = uvs.Length > i2 ? uvs[i2] : Vector2.Zero;

                (byte r0, byte g0, byte b0) = useBaked0
                    ? PSXBytesFromColor(bakedColorsSurface0![i0])
                    : useMeshColors
                        ? PSXBytesFromColor(meshColors[i0])
                        : (rByte, gByte, bByte);
                (byte r1, byte g1, byte b1) = useBaked0
                    ? PSXBytesFromColor(bakedColorsSurface0![i1])
                    : useMeshColors
                        ? PSXBytesFromColor(meshColors[i1])
                        : (rByte, gByte, bByte);
                (byte r2, byte g2, byte b2) = useBaked0
                    ? PSXBytesFromColor(bakedColorsSurface0![i2])
                    : useMeshColors
                        ? PSXBytesFromColor(meshColors[i2])
                        : (rByte, gByte, bByte);

                psx.Triangles.Add(new Tri
                {
                    v0 = MakeVertex(p0, normals.Length > i0 ? normals[i0] : Vector3.Up, uv0, tex, gteScaling, r0, g0, b0),
                    v1 = MakeVertex(p1, normals.Length > i1 ? normals[i1] : Vector3.Up, uv1, tex, gteScaling, r1, g1, b1),
                    v2 = MakeVertex(p2, normals.Length > i2 ? normals[i2] : Vector3.Up, uv2, tex, gteScaling, r2, g2, b2),
                    TextureIndex = texIdx,
                });
            }
        }

        return psx;
    }

    // Append one surface of `sub` to this PSXMesh, transforming vertex
    // positions by `subToGroup` (the sub-mesh's local-to-group-local
    // transform) before encoding. Used by PS1MeshGroup to merge several
    // MeshInstance3D children into a single PSXMesh whose verts all live
    // in the group's local space — the group's GameObject position +
    // rotation then apply to the whole lot at runtime.
    //
    // Same winding swap + Y-negation convention as FromGodotMesh, so
    // per-triangle front/back matches regardless of which path produced it.
    public void AppendFromGodotSurface(MeshInstance3D sub, int surfaceIdx,
        Transform3D subToGroup, int texIdx, PSXTexture? tex,
        float gteScaling, byte rByte, byte gByte, byte bByte,
        bool tilingExpected = false)
    {
        var mesh = sub.Mesh;
        if (mesh == null || surfaceIdx >= mesh.GetSurfaceCount()) return;

        var arrays = mesh.SurfaceGetArrays(surfaceIdx);
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        // Mesh-intrinsic vertex colours (Blender Cycles bake via glTF,
        // or any ImportPlugin that populates Mesh.ArrayType.Color).
        // Uses the mesh's own COLOR channel as the per-vertex tint when
        // present + length-matched. Falls back to the (rByte,gByte,bByte)
        // flat colour passed by the caller.
        var meshColors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        bool useMeshColors = meshColors.Length == verts.Length;

        MeshLinter.RecordSurface(sub.Name, uvs, tilingExpected);

        // Orthonormalize for normals so non-uniform child scales don't
        // warp the lighting vector. Positions still use the full transform
        // because verts SHOULD scale with the sub-mesh's authored transform.
        Basis normalBasis = subToGroup.Basis.Orthonormalized();

        int triCount = indices.Length > 0 ? indices.Length / 3 : verts.Length / 3;

        for (int t = 0; t < triCount; t++)
        {
            int i0 = indices.Length > 0 ? indices[t * 3 + 0] : t * 3 + 0;
            int i1 = indices.Length > 0 ? indices[t * 3 + 1] : t * 3 + 1;
            int i2 = indices.Length > 0 ? indices[t * 3 + 2] : t * 3 + 2;

            Vector3 p0 = subToGroup * verts[i0];
            Vector3 p1 = subToGroup * verts[i1];
            Vector3 p2 = subToGroup * verts[i2];

            Vector3 n0 = normalBasis * (normals.Length > i0 ? normals[i0] : Vector3.Up);
            Vector3 n1 = normalBasis * (normals.Length > i1 ? normals[i1] : Vector3.Up);
            Vector3 n2 = normalBasis * (normals.Length > i2 ? normals[i2] : Vector3.Up);

            // Same rationale as FromGodotMesh — Y+Z reflection is a rotation,
            // winding is preserved.

            Vector2 uv0 = uvs.Length > i0 ? uvs[i0] : Vector2.Zero;
            Vector2 uv1 = uvs.Length > i1 ? uvs[i1] : Vector2.Zero;
            Vector2 uv2 = uvs.Length > i2 ? uvs[i2] : Vector2.Zero;

            (byte r0, byte g0, byte b0) = useMeshColors
                ? PSXBytesFromColor(meshColors[i0]) : (rByte, gByte, bByte);
            (byte r1, byte g1, byte b1) = useMeshColors
                ? PSXBytesFromColor(meshColors[i1]) : (rByte, gByte, bByte);
            (byte r2, byte g2, byte b2) = useMeshColors
                ? PSXBytesFromColor(meshColors[i2]) : (rByte, gByte, bByte);

            Triangles.Add(new Tri
            {
                v0 = MakeVertex(p0, n0, uv0, tex, gteScaling, r0, g0, b0),
                v1 = MakeVertex(p1, n1, uv1, tex, gteScaling, r1, g1, b1),
                v2 = MakeVertex(p2, n2, uv2, tex, gteScaling, r2, g2, b2),
                TextureIndex = texIdx,
            });
        }
    }

    private static (byte r, byte g, byte b) PSXBytesFromColor(Color c)
    {
        // PSXTrig.ColorChannelToPSX clamps to [0, 255] and rounds.
        // Same conversion the rest of the pipeline uses for FlatColor.
        return (PSXTrig.ColorChannelToPSX(c.R),
                PSXTrig.ColorChannelToPSX(c.G),
                PSXTrig.ColorChannelToPSX(c.B));
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
            // Godot → PSX: reflect Y (PS1 is Y-down) AND Z (PSX looks +Z while
            // Godot looks -Z). Two reflections compose to a rotation so the
            // triangle's chirality is preserved — no winding swap needed.
            vx = PSXTrig.ConvertCoordinateToPSX( pos.X, gteScaling),
            vy = PSXTrig.ConvertCoordinateToPSX(-pos.Y, gteScaling),
            vz = PSXTrig.ConvertCoordinateToPSX(-pos.Z, gteScaling),

            nx = PSXTrig.ConvertToFixed12( normal.X),
            ny = PSXTrig.ConvertToFixed12(-normal.Y),
            nz = PSXTrig.ConvertToFixed12(-normal.Z),

            u = uByte,
            v = vByte,
            r = r,
            g = g,
            b = b,
        };
    }
}
