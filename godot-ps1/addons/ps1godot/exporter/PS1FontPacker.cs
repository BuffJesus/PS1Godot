using Godot;

namespace PS1Godot.Exporter;

// Pack a PS1UIFontAsset's RGBA8 bitmap into the 4bpp format the PSX
// runtime expects. Font glyphs use a 2-entry CLUT ({0, 0x7FFF} =
// transparent + white) the runtime overlays at upload time; see
// psxsplash-main/src/uisystem.cpp:uploadFonts. We only need to
// distinguish "ink" from "background" — palette index 1 for pixels
// whose source alpha ≥ 0.5, index 0 otherwise.
//
// Output byte layout:
//   byte[row * 128 + col] where col ∈ [0, 127] packs two pixels:
//     low nibble  = palette index of pixel at x = col * 2
//     high nibble = palette index of pixel at x = col * 2 + 1
// Atlas is always 256 px wide (matches PSX texture-page width), so
// each row is exactly 128 bytes. Total size = 128 × atlasHeight.
public static class PS1FontPacker
{
    public const int AtlasWidth = 256;
    public const int RowStride = 128;      // 256 px / 2 px per byte
    private const float InkThreshold = 0.5f;

    public static byte[] Pack4bpp(Image bitmap)
    {
        if (bitmap == null)
            throw new System.ArgumentNullException(nameof(bitmap));
        if (bitmap.GetWidth() != AtlasWidth)
            throw new System.InvalidOperationException(
                $"PS1FontPacker: atlas must be {AtlasWidth} px wide, got {bitmap.GetWidth()}.");

        int h = bitmap.GetHeight();
        var bytes = new byte[RowStride * h];

        for (int y = 0; y < h; y++)
        {
            int rowBase = y * RowStride;
            for (int x = 0; x < AtlasWidth; x += 2)
            {
                var left = bitmap.GetPixel(x, y);
                var right = bitmap.GetPixel(x + 1, y);
                byte lo = (left.A >= InkThreshold) ? (byte)1 : (byte)0;
                byte hi = (right.A >= InkThreshold) ? (byte)1 : (byte)0;
                bytes[rowBase + (x >> 1)] = (byte)(lo | (hi << 4));
            }
        }
        return bytes;
    }
}
