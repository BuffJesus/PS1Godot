namespace PS1Godot.Exporter;

// PS1 GPU texture color modes. Maps directly to the bit-depth field in the
// TPAGE / texture-page register on the PSX side.
//
//   4bpp  — 16-color CLUT.   Smallest VRAM footprint; best for UI, simple
//           textures, anything with very few hues. 1px = 4 bits.
//   8bpp  — 256-color CLUT.  Default for most assets — good quality/budget.
//           1px = 8 bits.
//   16bpp — direct RGB555.   No palette; full color but 4× the VRAM of 4bpp.
//           Reserved for textures that genuinely need it (skies, photos).
public enum PSXBPP
{
    TEX_4BIT = 0,
    TEX_8BIT = 1,
    TEX_16BIT = 2,
}

public static class PSXBPPExt
{
    public static int Bits(this PSXBPP bpp) => bpp switch
    {
        PSXBPP.TEX_4BIT => 4,
        PSXBPP.TEX_8BIT => 8,
        PSXBPP.TEX_16BIT => 16,
        _ => 16,
    };

    public static int PaletteSize(this PSXBPP bpp) => bpp switch
    {
        PSXBPP.TEX_4BIT => 16,
        PSXBPP.TEX_8BIT => 256,
        PSXBPP.TEX_16BIT => 0,
        _ => 0,
    };
}
