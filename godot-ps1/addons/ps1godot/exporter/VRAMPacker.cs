using System.Collections.Generic;
using System.Linq;

namespace PS1Godot.Exporter;

// Packs PSXTextures into the 1024×512 16-bit VRAM grid.
//
// Layout rules we inherit from SplashEdit / psxsplash:
//   • Framebuffers occupy (0,0)-(320,240) and (0,256)-(320,480). Double-buffered.
//   • The font column at x=960-1023 is reserved for system + custom fonts.
//   • Textures live inside 256-tall atlases whose width depends on bpp
//     (4bpp=64px, 8bpp=128px, 16bpp=256px). Atlases are placed on 64×256 grid.
//   • CLUTs sit elsewhere in VRAM, on 16-pixel X boundaries, 1 pixel tall.
//
// Output: each input texture is updated with its PackingX/Y (within atlas),
// TexpageX/Y (which tpage), and CLUT position. A simulated VRAM pixel grid
// is built so the writer can blit it to the .vram file.
public sealed class VRAMPacker
{
    public const int VramWidth = 1024;
    public const int VramHeight = 512;

    private sealed class Atlas
    {
        public PSXBPP BitDepth;
        public int Width;
        public int PositionX;
        public int PositionY;
        public bool Placed;
        public List<PSXTexture> Textures = new();
    }

    private struct RectI
    {
        public int X, Y, W, H;
        public bool Overlaps(RectI o) =>
            X < o.X + o.W && X + W > o.X &&
            Y < o.Y + o.H && Y + H > o.Y;
    }

    private readonly List<RectI> _reserved = new();
    private readonly List<Atlas> _atlases = new();
    private readonly List<RectI> _clutRects = new();
    public readonly VRAMPixel[,] Vram = new VRAMPixel[VramWidth, VramHeight];
    private readonly List<Atlas> FinalAtlases = new();

    public int AtlasCount => FinalAtlases.Count;

    public VRAMPacker()
    {
        // Framebuffer A (top-left 320×240) and B (below, 320×240).
        _reserved.Add(new RectI { X = 0, Y = 0,   W = 320, H = 240 });
        _reserved.Add(new RectI { X = 0, Y = 256, W = 320, H = 240 });
        // Font column reserved at x=960-1023 full height.
        _reserved.Add(new RectI { X = 960, Y = 0, W = 64, H = VramHeight });
    }

    public void Pack(IEnumerable<PSXTexture> textures)
    {
        // Group by bit-depth, widest (16bpp=256 per atlas) first.
        var byDepth = textures.GroupBy(t => t.BitDepth).OrderByDescending(g => (int)g.Key);
        foreach (var group in byDepth)
        {
            int atlasWidth = group.Key switch
            {
                PSXBPP.TEX_16BIT => 256,
                PSXBPP.TEX_8BIT  => 128,
                PSXBPP.TEX_4BIT  => 64,
                _ => 256,
            };
            Atlas atlas = NewAtlas(group.Key, atlasWidth);

            foreach (var tex in group.OrderByDescending(t => t.QuantizedWidth * t.Height))
            {
                if (!TryPlaceInAtlas(atlas, tex))
                {
                    atlas = NewAtlas(group.Key, atlasWidth);
                    if (!TryPlaceInAtlas(atlas, tex))
                    {
                        Godot.GD.PushError($"[PS1Godot] VRAMPacker: can't fit texture {tex.SourcePath} ({tex.QuantizedWidth}×{tex.Height}) in a {atlasWidth}-wide atlas. " +
                            "PSX VRAM is 1024×512 shared across framebuffers, textures, and CLUTs — there isn't enough contiguous space left. " +
                            "Try: lower BitDepth on large textures (8bpp→4bpp halves width), downscale oversized sources, or consolidate duplicate/similar textures into a shared atlas group.");
                    }
                }
            }
        }

        ArrangeAtlasesInVram();
        AllocateCluts();
        BuildVramGrid();
    }

    private Atlas NewAtlas(PSXBPP bpp, int width)
    {
        var a = new Atlas { BitDepth = bpp, Width = width };
        _atlases.Add(a);
        return a;
    }

    private static bool TryPlaceInAtlas(Atlas atlas, PSXTexture tex)
    {
        const int atlasHeight = 256;
        for (int y = 0; y + tex.Height <= atlasHeight; y++)
        {
            for (int x = 0; x + tex.QuantizedWidth <= atlas.Width; x++)
            {
                var cand = new RectI { X = x, Y = y, W = tex.QuantizedWidth, H = tex.Height };
                bool overlaps = false;
                foreach (var placed in atlas.Textures)
                {
                    var pr = new RectI { X = placed.PackingX, Y = placed.PackingY, W = placed.QuantizedWidth, H = placed.Height };
                    if (cand.Overlaps(pr)) { overlaps = true; break; }
                }
                if (!overlaps)
                {
                    tex.PackingX = (byte)x;
                    tex.PackingY = (byte)y;
                    atlas.Textures.Add(tex);
                    return true;
                }
            }
        }
        return false;
    }

    private void ArrangeAtlasesInVram()
    {
        // Place atlases in 64-column, 256-row grid, skipping reserved/other-atlas overlaps.
        foreach (var bpp in new[] { PSXBPP.TEX_16BIT, PSXBPP.TEX_8BIT, PSXBPP.TEX_4BIT })
        {
            foreach (var atlas in _atlases.Where(a => a.BitDepth == bpp && !a.Placed))
            {
                bool placed = false;
                for (int y = 0; y + 256 <= VramHeight && !placed; y += 256)
                {
                    for (int x = 0; x + atlas.Width <= VramWidth && !placed; x += 64)
                    {
                        var cand = new RectI { X = x, Y = y, W = atlas.Width, H = 256 };
                        if (RegionFree(cand))
                        {
                            atlas.PositionX = x;
                            atlas.PositionY = y;
                            atlas.Placed = true;
                            FinalAtlases.Add(atlas);
                            foreach (var t in atlas.Textures)
                            {
                                t.TexpageX = (byte)(x / 64);
                                t.TexpageY = (byte)(y / 256);
                            }
                            placed = true;
                        }
                    }
                }
                if (!placed)
                    Godot.GD.PushError($"[PS1Godot] VRAMPacker: no room for a {atlas.Width}×256 {bpp} atlas. " +
                        "All usable VRAM columns are occupied. Check the VRAM viewer (PS1 VRAM tab) to see what's consuming space. " +
                        "Try: reduce texture count, lower BitDepth on the largest textures, or split the scene into sub-scenes with separate VRAM budgets.");
            }
        }
    }

    private void AllocateCluts()
    {
        foreach (var atlas in FinalAtlases)
        {
            foreach (var tex in atlas.Textures)
            {
                if (tex.ColorPalette == null || tex.ColorPalette.Count == 0) continue;
                int cw = tex.ColorPalette.Count;
                bool placed = false;
                for (int x = 0; x + cw <= VramWidth && !placed; x += 16)
                {
                    for (int y = 0; y + 1 <= VramHeight && !placed; y++)
                    {
                        var cand = new RectI { X = x, Y = y, W = cw, H = 1 };
                        if (RegionFree(cand))
                        {
                            tex.ClutPackingX = (ushort)(x / 16);
                            tex.ClutPackingY = (ushort)y;
                            _clutRects.Add(cand);
                            placed = true;
                        }
                    }
                }
                if (!placed)
                    Godot.GD.PushError($"[PS1Godot] VRAMPacker: no room for a {cw}-entry CLUT. " +
                        "CLUTs (color look-up tables) pack into the narrow VRAM strip below the framebuffers. " +
                        "Too many unique palettes exhaust this space. Try: share materials across meshes so they reuse the same CLUT, or switch some 8bpp textures to 4bpp (16 CLUT entries instead of 256).");
            }
        }
    }

    private bool RegionFree(RectI r)
    {
        if (r.X + r.W > VramWidth) return false;
        if (r.Y + r.H > VramHeight) return false;
        foreach (var a in FinalAtlases)
        {
            var ar = new RectI { X = a.PositionX, Y = a.PositionY, W = a.Width, H = 256 };
            if (r.Overlaps(ar)) return false;
        }
        foreach (var res in _reserved) if (r.Overlaps(res)) return false;
        foreach (var c in _clutRects) if (r.Overlaps(c)) return false;
        return true;
    }

    private void BuildVramGrid()
    {
        foreach (var atlas in FinalAtlases)
        {
            foreach (var tex in atlas.Textures)
            {
                for (int y = 0; y < tex.Height; y++)
                    for (int x = 0; x < tex.QuantizedWidth; x++)
                        Vram[atlas.PositionX + tex.PackingX + x,
                             atlas.PositionY + tex.PackingY + y] = tex.ImageData[x, y];

                if (tex.ColorPalette != null && tex.BitDepth != PSXBPP.TEX_16BIT)
                {
                    int cx = tex.ClutPackingX * 16;
                    for (int i = 0; i < tex.ColorPalette.Count; i++)
                        Vram[cx + i, tex.ClutPackingY] = tex.ColorPalette[i];
                }
            }
        }
    }

    // TPage bit layout (matches PSX hardware and SplashEdit's TPageAttr):
    //   bits 0-3: pageX (0-15)
    //   bit  4  : pageY (0-1)
    //   bits 5-6: semi-trans mode
    //   bits 7-8: color mode (0=4bpp, 1=8bpp, 2=16bpp)
    //   bit  9  : dithering
    //   bit  10 : display area enable
    public static ushort BuildTpageAttr(byte pageX, byte pageY, PSXBPP bpp, bool dithering = true)
    {
        ushort info = 0;
        info |= (ushort)(pageX & 0x0F);
        info |= (ushort)((pageY & 0x01) << 4);
        // Semi-trans 0, display-area disabled — defaults.
        info |= (ushort)((((int)bpp) & 0x03) << 7);
        if (dithering) info |= 0x0200;
        return info;
    }

    // Atlas metadata is written for cursor-advancement only in v20 (loader
    // skips the contents). Expose the placed atlases so the writer emits the
    // right count with dummy bytes.
    public int PlacedAtlasCount => FinalAtlases.Count;

    // Enumerate CLUTs for the writer. Order matches what BuildVramGrid wrote
    // into the .vram pixel grid.
    public IEnumerable<PSXTexture> CLUTOwners()
    {
        foreach (var a in FinalAtlases)
            foreach (var t in a.Textures)
                if (t.ColorPalette != null && t.BitDepth != PSXBPP.TEX_16BIT)
                    yield return t;
    }

    // Enumerate atlases (placed only) so the writer can emit .vram atlas
    // chunks in order.
    public IEnumerable<(int vramX, int vramY, int width, int height, VRAMPixel[,] pixels)> EnumerateAtlasChunks()
    {
        foreach (var a in FinalAtlases)
        {
            var pix = new VRAMPixel[a.Width, 256];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < a.Width; x++)
                    pix[x, y] = Vram[a.PositionX + x, a.PositionY + y];
            yield return (a.PositionX, a.PositionY, a.Width, 256, pix);
        }
    }
}
