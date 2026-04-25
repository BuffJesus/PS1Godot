using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Quantized PSX texture: pixels packed into VRAM-ready 16-bit words + optional
// CLUT. Created from a Godot Image via FromGodotImage; placement in VRAM is
// filled in later by VRAMPacker.
public sealed class PSXTexture
{
    public int Width;           // original pixel width
    public int Height;          // original pixel height
    public int QuantizedWidth;  // width in VRAM-horizontal units (= Width/4 for 4bpp, /2 for 8bpp, /1 for 16bpp)
    public PSXBPP BitDepth;

    // Dedup key — Godot resource path of the source image. Texture instances
    // sharing a resource path + BitDepth collapse to one atlas entry.
    public string SourcePath = "";

    // Pixel data packed into VRAM-word-sized pixels. Indexed [x, y] where x is
    // in QuantizedWidth units; the value is the word that lands in VRAM at
    // (PackingX+x, PackingY+y).
    public VRAMPixel[,] ImageData = new VRAMPixel[0, 0];

    // Palette — null for 16bpp direct textures.
    public List<VRAMPixel>? ColorPalette;

    // Position within the owning atlas (byte addresses in VRAM-word units).
    public byte PackingX;
    public byte PackingY;
    // Which VRAM "tpage" the atlas ended up in (tpage = column 64px × row 256px).
    public byte TexpageX;
    public byte TexpageY;
    // CLUT location: ClutPackingX is pre-divided by 16 (CLUTs must sit on 16-pixel
    // X boundaries in VRAM). ClutPackingY is the raw Y coordinate.
    public ushort ClutPackingX;
    public ushort ClutPackingY;

    public static PSXTexture FromGodotImage(Image img, PSXBPP bpp, string sourcePath)
    {
        // Godot textures can arrive in any format. Compressed formats
        // (VRAM-compressed PNGs, the default import preset) can't be sampled
        // with GetPixel — decompress first. Always duplicate before mutating
        // so we don't write back into the shared cached Texture2D.
        bool needsDuplicate = img.IsCompressed() || img.GetFormat() != Image.Format.Rgba8;
        if (needsDuplicate) img = (Image)img.Duplicate();
        if (img.IsCompressed()) img.Decompress();
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);

        var t = new PSXTexture
        {
            Width = img.GetWidth(),
            Height = img.GetHeight(),
            BitDepth = bpp,
            SourcePath = sourcePath,
        };

        t.QuantizedWidth = bpp switch
        {
            PSXBPP.TEX_4BIT => t.Width / 4,
            PSXBPP.TEX_8BIT => t.Width / 2,
            _ => t.Width,
        };

        // Detect alpha-key transparency. Treat any pixel with α below
        // ~0.5 as fully transparent — matches the PS1's binary
        // "drawn / not drawn" model (no per-pixel alpha blending).
        // Authors get cleaner edges on quantized 4bpp art than they
        // would with a hard α==0 cutoff.
        const float AlphaKeyThreshold = 0.5f;
        bool[,]? transparentMask = null;
        bool hasAlphaKey = false;
        for (int y = 0; y < t.Height && !hasAlphaKey; y++)
        {
            for (int x = 0; x < t.Width; x++)
            {
                if (img.GetPixel(x, y).A < AlphaKeyThreshold)
                {
                    hasAlphaKey = true;
                    break;
                }
            }
        }
        if (hasAlphaKey)
        {
            transparentMask = new bool[t.Width, t.Height];
            for (int y = 0; y < t.Height; y++)
                for (int x = 0; x < t.Width; x++)
                    transparentMask[x, y] = img.GetPixel(x, y).A < AlphaKeyThreshold;
        }

        if (bpp == PSXBPP.TEX_16BIT)
        {
            t.ImageData = new VRAMPixel[t.Width, t.Height];
            for (int y = 0; y < t.Height; y++)
            {
                for (int x = 0; x < t.Width; x++)
                {
                    // Godot Image origin is top-left, same as PSX VRAM — direct
                    // copy, no Y-flip needed. (SplashEdit flipped here because
                    // Unity UVs put (0,0) at bottom-left; Godot UVs match PSX.)
                    if (hasAlphaKey && transparentMask![x, y])
                    {
                        t.ImageData[x, y] = VRAMPixel.Transparent();
                    }
                    else
                    {
                        var c = img.GetPixel(x, y);
                        t.ImageData[x, y] = VRAMPixel.FromColor01(c.R, c.G, c.B);
                    }
                }
            }
            t.ColorPalette = null;
            return t;
        }

        // Alpha-key path for paletted modes: reserve palette index 0 as
        // the transparent sentinel (0x0000), quantize visible pixels into
        // the remaining (maxColors - 1) slots, then map masked input
        // pixels back to index 0. Quantizer is only fed opaque pixels so
        // its centroid placement isn't dragged toward the masked colors.
        int maxColors = bpp.PaletteSize();
        ImageProcessing.QuantizedResult q;
        if (hasAlphaKey)
        {
            // Build an opaque-only image for the quantizer. Masked pixels
            // get replaced with the centroid color of their nearest
            // opaque neighbor (or just (0,0,0) when the whole row is
            // masked) — they'll be overridden to index 0 afterward, but
            // Floyd-Steinberg dithering still propagates error through
            // these cells, so a sane stand-in keeps speckles down.
            var opaqueOnly = (Image)img.Duplicate();
            for (int y = 0; y < t.Height; y++)
                for (int x = 0; x < t.Width; x++)
                    if (transparentMask![x, y])
                        opaqueOnly.SetPixel(x, y, new Color(0f, 0f, 0f, 1f));

            q = ImageProcessing.Quantize(opaqueOnly, maxColors - 1);
            // Shift quantized indices by +1 so palette[0] is reserved.
            for (int y = 0; y < t.Height; y++)
                for (int x = 0; x < t.Width; x++)
                    q.Indices[x, y] = transparentMask![x, y] ? 0 : (q.Indices[x, y] + 1);
        }
        else
        {
            q = ImageProcessing.Quantize(img, maxColors);
        }

        t.ColorPalette = new List<VRAMPixel>(maxColors);
        if (hasAlphaKey)
            t.ColorPalette.Add(VRAMPixel.Transparent()); // index 0 = 0x0000
        foreach (var c in q.Palette)
            t.ColorPalette.Add(VRAMPixel.FromColor01(c.X, c.Y, c.Z));

        // Pad to exactly maxColors (16 for 4bpp, 256 for 8bpp). The PSX VRAM
        // DMA transfers 32-bit words, so the CLUT upload — width=length pixels,
        // height=1 — fails with "Odd number of pixels to transfer" when the
        // palette size is odd. Unused entries here are never referenced by
        // any pixel index; they exist purely to round the transfer size.
        while (t.ColorPalette.Count < maxColors)
            t.ColorPalette.Add(new VRAMPixel()); // 0x0000 = transparent-black

        // Pack palette indices into VRAM words. For 4bpp: 4 pixels per word
        // (nibbles 0..3 in LSB-first order). For 8bpp: 2 pixels per word
        // (low byte then high byte). The result is a VRAMPixel whose numeric
        // value is just the packed indices — we're reusing the struct as a
        // 16-bit storage cell, not as actual RGB data.
        t.ImageData = new VRAMPixel[t.QuantizedWidth, t.Height];
        int groupSize = bpp == PSXBPP.TEX_8BIT ? 2 : 4;

        for (int y = 0; y < t.Height; y++)
        {
            for (int group = 0; group < t.QuantizedWidth; group++)
            {
                int baseX = group * groupSize;
                ushort packed;
                if (bpp == PSXBPP.TEX_8BIT)
                {
                    int i1 = q.Indices[baseX + 0, y] & 0xFF;
                    int i2 = q.Indices[baseX + 1, y] & 0xFF;
                    packed = (ushort)((i2 << 8) | i1);
                }
                else // 4bpp
                {
                    int i1 = q.Indices[baseX + 0, y] & 0xF;
                    int i2 = q.Indices[baseX + 1, y] & 0xF;
                    int i3 = q.Indices[baseX + 2, y] & 0xF;
                    int i4 = q.Indices[baseX + 3, y] & 0xF;
                    packed = (ushort)((i4 << 12) | (i3 << 8) | (i2 << 4) | i1);
                }
                // Unpack into the struct — it's just bits here, not real color.
                var p = new VRAMPixel
                {
                    SemiTransparent = (packed & 0x8000) != 0,
                    B = (ushort)((packed >> 10) & 0x1F),
                    G = (ushort)((packed >> 5) & 0x1F),
                    R = (ushort)(packed & 0x1F),
                };
                t.ImageData[group, y] = p;
            }
        }
        return t;
    }
}
