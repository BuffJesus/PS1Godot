#if TOOLS
using System.Collections.Generic;
using Godot;
using PS1Godot.Exporter;

namespace PS1Godot.UI;

// Renders a Texture2D as it would appear on PSX VRAM at a given bit
// depth. Used by PS1TexturePreviewInspector to give authors instant
// "how does this look quantized?" feedback in the inspector — no
// export round-trip required.
//
// Pipeline mirrors PSXTexture.FromGodotImage exactly so the preview
// matches the shipped output:
//   - Alpha < 0.5 → palette index 0 (transparent sentinel) for 4/8bpp,
//     or 0x0000 word for 16bpp.
//   - 4/8bpp: ImageProcessing.Quantize (median-cut + Floyd-Steinberg
//     dither) into 16/256 colors, then each palette entry clamped to
//     5 bits per channel (VRAMPixel encoding).
//   - 16bpp: each pixel directly clamped to 5-5-5.
//
// Output is an Image suitable for a TextureRect — RGBA8, 8-bit display
// values reconstructed from the 5-bit PSX quantization (so the preview
// shows the actual posterization the GPU would apply, not the source
// art's full color depth).
public static class PS1TexturePreview
{
    // Build a PSX-equivalent preview of `src` at the given bit depth.
    // Returns null only if `src` is null or zero-sized — caller should
    // hide the preview Control in that case.
    public static Image? Build(Image src, PSXBPP bpp)
    {
        if (src == null || src.GetWidth() == 0 || src.GetHeight() == 0)
            return null;

        // Decompress / convert to RGBA8 the same way PSXTexture does.
        bool needsDuplicate = src.IsCompressed() || src.GetFormat() != Image.Format.Rgba8;
        if (needsDuplicate) src = (Image)src.Duplicate();
        if (src.IsCompressed()) src.Decompress();
        if (src.GetFormat() != Image.Format.Rgba8) src.Convert(Image.Format.Rgba8);

        int w = src.GetWidth();
        int h = src.GetHeight();

        var preview = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);

        // Detect alpha-key pixels (alpha < 0.5 reads as transparent on
        // PSX). Match PSXTexture.FromGodotImage's threshold.
        const float AlphaKeyThreshold = 0.5f;
        bool[,]? mask = null;
        bool hasAlphaKey = false;
        for (int y = 0; y < h && !hasAlphaKey; y++)
            for (int x = 0; x < w; x++)
                if (src.GetPixel(x, y).A < AlphaKeyThreshold)
                {
                    hasAlphaKey = true;
                    break;
                }
        if (hasAlphaKey)
        {
            mask = new bool[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    mask[x, y] = src.GetPixel(x, y).A < AlphaKeyThreshold;
        }

        if (bpp == PSXBPP.TEX_16BIT)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (hasAlphaKey && mask![x, y])
                    {
                        preview.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                        continue;
                    }
                    var c = src.GetPixel(x, y);
                    preview.SetPixel(x, y, Quantize5Bit(c.R, c.G, c.B));
                }
            }
            return preview;
        }

        // 4bpp / 8bpp paletted path. Feed the quantizer an opaque-only
        // version of the source when alpha-key is in play — same trick
        // PSXTexture uses so masked pixels don't drag the centroids.
        int maxColors = bpp.PaletteSize();
        ImageProcessing.QuantizedResult q;
        if (hasAlphaKey)
        {
            var opaqueOnly = (Image)src.Duplicate();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (mask![x, y])
                        opaqueOnly.SetPixel(x, y, new Color(0f, 0f, 0f, 1f));
            q = ImageProcessing.Quantize(opaqueOnly, maxColors - 1);
            // Same +1 shift PSXTexture applies — palette index 0 reserved.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    q.Indices[x, y] = mask![x, y] ? 0 : (q.Indices[x, y] + 1);
        }
        else
        {
            q = ImageProcessing.Quantize(src, maxColors);
        }

        // Build palette as 5-bit-per-channel display colors. Index 0
        // becomes transparent when alpha-key is in play; otherwise it's
        // just the first quantizer centroid clamped to 5 bits.
        var palette = new List<Color>(maxColors);
        if (hasAlphaKey)
            palette.Add(new Color(0f, 0f, 0f, 0f));
        foreach (var v in q.Palette)
            palette.Add(Quantize5Bit(v.X, v.Y, v.Z));

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = q.Indices[x, y];
                if (idx < 0 || idx >= palette.Count)
                    idx = 0;
                preview.SetPixel(x, y, palette[idx]);
            }
        }
        return preview;
    }

    // Match VRAMPixel.FromColor01's per-channel clamp: round each float
    // [0,1] to the nearest fifth-bit step, then expand back to 8 bits
    // for display. Output Alpha=1 always — alpha-key handling happens
    // upstream in Build() before this is called.
    private static Color Quantize5Bit(float r, float g, float b)
    {
        int qr = System.Math.Clamp((int)(r * 31f + 0.5f), 0, 31);
        int qg = System.Math.Clamp((int)(g * 31f + 0.5f), 0, 31);
        int qb = System.Math.Clamp((int)(b * 31f + 0.5f), 0, 31);
        return new Color(qr / 31f, qg / 31f, qb / 31f, 1f);
    }
}
#endif
