namespace PS1Godot.Exporter;

// PSX VRAM pixel: 16 bits laid out as STP|B|G|R (1+5+5+5).
//
// The PS1 hardware reserves the all-zero word (0x0000) as a transparent
// sentinel for masking. Any opaque color that quantizes to pure black must
// be bumped off of that exact pattern — we use (1,1,1) with the STP bit set
// to match SplashEdit's behavior.
public struct VRAMPixel
{
    public ushort R;
    public ushort G;
    public ushort B;
    public bool SemiTransparent;

    public ushort Pack()
    {
        return (ushort)((SemiTransparent ? 1 << 15 : 0)
            | ((B & 0x1F) << 10)
            | ((G & 0x1F) << 5)
            |  (R & 0x1F));
    }

    public static VRAMPixel FromColor01(float r, float g, float b)
    {
        var p = new VRAMPixel
        {
            R = (ushort)System.Math.Clamp((int)(r * 31f + 0.5f), 0, 31),
            G = (ushort)System.Math.Clamp((int)(g * 31f + 0.5f), 0, 31),
            B = (ushort)System.Math.Clamp((int)(b * 31f + 0.5f), 0, 31),
        };
        if (p.Pack() == 0x0000)
        {
            p.R = 1; p.G = 1; p.B = 1; p.SemiTransparent = true;
        }
        return p;
    }

    // Explicit 0x0000 sentinel — the PSX GPU skips any textured-prim
    // pixel whose VRAM word is the all-zero pattern (regardless of
    // opaque/semi-trans mode). Use for palette index 0 of textures
    // with alpha-key transparency, and for 16bpp direct pixels whose
    // input alpha was 0.
    public static VRAMPixel Transparent() => new VRAMPixel();
}
