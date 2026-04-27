using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PS1Godot.Exporter;

// Host-side correctness check for the v31 static-mesh vertex-pool format.
//
// We run two writers on every static mesh during export:
//   1. The legacy WriteTri loop → "expected" 52 B per triangle
//   2. WriteStaticMeshPooled → "actual" pooled bytes
// Then we DECODE the pooled bytes back into the per-triangle layout
// (parsing MeshBlob + Vertex[] + Face[], reconstructing each Tri exactly
// the way the C++ runtime's expandTri inline does at render time) and
// byte-compare the reconstructed bytes against the legacy bytes.
//
// If they disagree, the diff offset + per-Tri-field hint pinpoints the
// bug deterministically on the host — no PSX run cycle needed. If they
// agree, the writer pipeline is encoding-correct *and* the runtime's
// expandTri reconstruction will produce the same Tri the legacy format
// would have stored on disk.
//
// The reconstruction logic here is a faithful port of the C++ inline:
//
//   inline Tri expandTri(const Vertex* verts, const Face& face) {
//       Tri t;
//       const Vertex& a = verts[face.i0];
//       const Vertex& b = verts[face.i1];
//       const Vertex& c = verts[face.i2];
//       t.v0 = a.pos; t.v1 = b.pos; t.v2 = c.pos;
//       t.normal = face.normal;
//       t.colorA = a.color; t.colorB = b.color; t.colorC = c.color;
//       t.uvA = a.uv; t.uvB = b.uv;
//       t.uvC = {};
//       t.uvC.u = c.uv.u; t.uvC.v = c.uv.v;
//       t.tpage = face.tpage;
//       t.clutX = face.clutX;
//       t.clutY = face.clutY;
//       t.padding = 0;
//   }
//
// Keep this file in lockstep with mesh.hh / SplashpackWriter.cs.
public static class MeshFormatRoundTripVerifier
{
    public sealed class Diff
    {
        public required int FirstByteIndex { get; init; }
        public required int TriIndex { get; init; }       // -1 = length mismatch
        public required int ByteWithinTri { get; init; }  // -1 = length mismatch
        public required byte ExpectedByte { get; init; }
        public required byte ActualByte { get; init; }
        public required string FieldHint { get; init; }
        public required int ExpectedLen { get; init; }
        public required int ActualLen { get; init; }
    }

    public static Diff? Verify(PSXMesh mesh, SceneData scene)
    {
        if (mesh?.Triangles == null || mesh.Triangles.Count == 0) return null;

        byte[] expectedBytes = SerializeLegacy(mesh, scene);
        byte[] pooledBytes   = SerializePooled(mesh, scene);
        byte[] actualBytes   = ReconstructFromPooled(pooledBytes);

        if (expectedBytes.Length != actualBytes.Length)
        {
            return new Diff
            {
                FirstByteIndex = System.Math.Min(expectedBytes.Length, actualBytes.Length),
                TriIndex       = -1,
                ByteWithinTri  = -1,
                ExpectedByte   = 0,
                ActualByte     = 0,
                FieldHint      = "length mismatch",
                ExpectedLen    = expectedBytes.Length,
                ActualLen      = actualBytes.Length,
            };
        }

        for (int i = 0; i < expectedBytes.Length; i++)
        {
            if (expectedBytes[i] != actualBytes[i])
            {
                int triIdx     = i / SplashpackWriter.TriSize;
                int byteInTri  = i % SplashpackWriter.TriSize;
                return new Diff
                {
                    FirstByteIndex = i,
                    TriIndex       = triIdx,
                    ByteWithinTri  = byteInTri,
                    ExpectedByte   = expectedBytes[i],
                    ActualByte     = actualBytes[i],
                    FieldHint      = FieldHintFor(byteInTri),
                    ExpectedLen    = expectedBytes.Length,
                    ActualLen      = actualBytes.Length,
                };
            }
        }

        return null;
    }

    // Map a byte offset within the 52-byte legacy Tri to a field name.
    // Layout (from psxsplash-main/src/mesh.hh and SplashpackWriter.WriteTri):
    //   0..5    v0 position (PackedVec3, 3×int16)
    //   6..11   v1 position
    //   12..17  v2 position
    //   18..23  face normal (PackedVec3 from v0.n*)
    //   24..27  v0 color (r, g, b, code)
    //   28..31  v1 color
    //   32..35  v2 color
    //   36..37  uvA (post-expander u, v)
    //   38..39  uvB
    //   40..41  uvC.u, uvC.v
    //   42..43  uvC padding (u16 zero)
    //   44..45  tpage
    //   46..47  clutX
    //   48..49  clutY
    //   50..51  Tri padding (u16 zero)
    private static string FieldHintFor(int byteInTri)
    {
        if (byteInTri < 6)  return "v0.pos";
        if (byteInTri < 12) return "v1.pos";
        if (byteInTri < 18) return "v2.pos";
        if (byteInTri < 24) return "face.normal";
        if (byteInTri < 28) return "v0.color";
        if (byteInTri < 32) return "v1.color";
        if (byteInTri < 36) return "v2.color";
        if (byteInTri < 38) return "uvA";
        if (byteInTri < 40) return "uvB";
        if (byteInTri < 42) return "uvC.u/v";
        if (byteInTri < 44) return "uvC.padding";
        if (byteInTri < 46) return "tpage";
        if (byteInTri < 48) return "clutX";
        if (byteInTri < 50) return "clutY";
        return "tri.padding";
    }

    private static byte[] SerializeLegacy(PSXMesh mesh, SceneData scene)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        foreach (var tri in mesh.Triangles)
        {
            SplashpackWriter.WriteTri(w, tri, scene);
        }
        return ms.ToArray();
    }

    private static byte[] SerializePooled(PSXMesh mesh, SceneData scene)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        SplashpackWriter.WriteStaticMeshPooled(w, mesh, scene);
        return ms.ToArray();
    }

    // Decode a pooled mesh blob back into the legacy 52-B-per-tri byte
    // stream. Mirrors the runtime expandTri exactly so any divergence
    // surfaces as a byte diff against the legacy serializer's output.
    private static byte[] ReconstructFromPooled(byte[] pooledBytes)
    {
        using var input = new MemoryStream(pooledBytes);
        using var inR = new BinaryReader(input);

        ushort vertexCount = inR.ReadUInt16();
        ushort triCount    = inR.ReadUInt16();

        var verts = new Vertex[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            short vx = inR.ReadInt16();
            short vy = inR.ReadInt16();
            short vz = inR.ReadInt16();
            byte u  = inR.ReadByte();
            byte v  = inR.ReadByte();
            byte r  = inR.ReadByte();
            byte g  = inR.ReadByte();
            byte b  = inR.ReadByte();
            byte code = inR.ReadByte();  // alpha / GTE code byte
            verts[i] = new Vertex { Vx = vx, Vy = vy, Vz = vz, U = u, V = v, R = r, G = g, B = b, Code = code };
        }

        using var output = new MemoryStream();
        using var outW = new BinaryWriter(output);

        for (int i = 0; i < triCount; i++)
        {
            ushort i0 = inR.ReadUInt16();
            ushort i1 = inR.ReadUInt16();
            ushort i2 = inR.ReadUInt16();
            short  nx = inR.ReadInt16();
            short  ny = inR.ReadInt16();
            short  nz = inR.ReadInt16();
            ushort tpage  = inR.ReadUInt16();
            ushort clutX  = inR.ReadUInt16();
            ushort clutY  = inR.ReadUInt16();
            ushort facePad = inR.ReadUInt16();  // unused

            ref var a = ref verts[i0];
            ref var b = ref verts[i1];
            ref var c = ref verts[i2];

            // 3 × PackedVec3 positions = 18 bytes
            outW.Write(a.Vx); outW.Write(a.Vy); outW.Write(a.Vz);
            outW.Write(b.Vx); outW.Write(b.Vy); outW.Write(b.Vz);
            outW.Write(c.Vx); outW.Write(c.Vy); outW.Write(c.Vz);

            // 1 × PackedVec3 face normal = 6 bytes
            outW.Write(nx); outW.Write(ny); outW.Write(nz);

            // 3 × Color = 12 bytes (r, g, b, code)
            outW.Write(a.R); outW.Write(a.G); outW.Write(a.B); outW.Write((byte)0);
            outW.Write(b.R); outW.Write(b.G); outW.Write(b.B); outW.Write((byte)0);
            outW.Write(c.R); outW.Write(c.G); outW.Write(c.B); outW.Write((byte)0);

            // UVs + tpage + clut = 16 bytes
            //   uvA (u, v)        — 2 B
            //   uvB (u, v)        — 2 B
            //   uvC (u, v, pad u16=0) — 4 B
            //   tpage             — 2 B
            //   clutX, clutY      — 4 B
            //   tri.padding       — 2 B (u16 zero)
            outW.Write(a.U); outW.Write(a.V);
            outW.Write(b.U); outW.Write(b.V);
            outW.Write(c.U); outW.Write(c.V);
            outW.Write((ushort)0);  // uvC padding
            outW.Write(tpage);
            outW.Write(clutX);
            outW.Write(clutY);
            outW.Write((ushort)0);  // tri.padding
        }

        return output.ToArray();
    }

    private struct Vertex
    {
        public short Vx, Vy, Vz;
        public byte U, V;
        public byte R, G, B, Code;
    }
}
