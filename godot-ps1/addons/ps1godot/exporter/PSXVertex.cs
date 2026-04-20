namespace PS1Godot.Exporter;

// Vertex format the splashpack writer consumes. Bit-for-bit matches what
// PSXSceneWriter.WriteVertex* expects. Don't reorder fields — keep them lined
// up with splashedit-main/Runtime/PSXMesh.cs:PSXVertex so the byte layout
// stays identical.
public struct PSXVertex
{
    public short vx, vy, vz;     // 4.12 fixed-point local position
    public short nx, ny, nz;     // 4.12 fixed-point normal
    public byte u, v;            // texture coords (atlas-relative, byte range)
    public byte r, g, b;         // vertex color
}

public struct Tri
{
    public PSXVertex v0;
    public PSXVertex v1;
    public PSXVertex v2;

    /// <summary>-1 = untextured (POLY_G3 vertex-color triangle on PSX).</summary>
    public int TextureIndex;

    public bool IsUntextured => TextureIndex == -1;
}
