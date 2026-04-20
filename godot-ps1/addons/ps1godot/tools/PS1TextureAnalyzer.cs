using System.Collections.Generic;
using Godot;

namespace PS1Godot.Tools;

// Classifies images by their fit into PS1 VRAM constraints:
//   - 256×256 TPage limit per dimension
//   - 4bpp CLUT (≤16 unique colors, ~½ byte/pixel + 32-byte palette)
//   - 8bpp CLUT (≤256 unique colors, ~1 byte/pixel + 512-byte palette)
//   - 16bpp direct (anything more, costs 2 bytes/pixel)
//
// Intended as a pre-export sanity pass: catch too-big or too-colorful textures
// before the Phase 2 exporter silently quantizes or rejects them.
public static class PS1TextureAnalyzer
{
    public enum Verdict
    {
        Clut4bpp,     // Fits 4-bit CLUT — cheapest
        Clut8bpp,     // Fits 8-bit CLUT
        Direct16bpp,  // Too many colors — stored direct, 2× VRAM cost
        TooBig,       // Dimensions exceed PS1 TPage limit
    }

    public readonly struct Report
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int UniqueColors;
        public readonly bool HasTransparency;
        public readonly Verdict Verdict;
        public readonly int VramBytes;
        public readonly string Note;

        public Report(int w, int h, int colors, bool alpha, Verdict v, int vram, string note)
        {
            Width = w; Height = h; UniqueColors = colors;
            HasTransparency = alpha; Verdict = v; VramBytes = vram; Note = note;
        }
    }

    public static Report Analyze(Image image)
    {
        int w = image.GetWidth();
        int h = image.GetHeight();

        // Count unique colors. Early-exit above 256 since anything beyond
        // that classification-wise collapses into "16bpp direct."
        var colors = new HashSet<uint>();
        bool hasAlpha = false;
        bool bailedEarly = false;

        for (int y = 0; y < h && !bailedEarly; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color c = image.GetPixel(x, y);
                if (c.A < 0.999f) hasAlpha = true;
                uint packed = (uint)((byte)(c.R * 255) << 24
                                   | (byte)(c.G * 255) << 16
                                   | (byte)(c.B * 255) << 8
                                   |  (byte)(c.A * 255));
                colors.Add(packed);
                if (colors.Count > 256)
                {
                    bailedEarly = true;
                    break;
                }
            }
        }

        int unique = bailedEarly ? 257 : colors.Count;

        if (w > 256 || h > 256)
        {
            return new Report(w, h, unique, hasAlpha, Verdict.TooBig, 0,
                $"{w}×{h} exceeds 256×256 PS1 TPage limit — must split or shrink.");
        }
        if (unique <= 16)
        {
            int vram = (w * h) / 2 + 16 * 2;
            return new Report(w, h, unique, hasAlpha, Verdict.Clut4bpp, vram,
                $"Fits 4bpp CLUT ({unique}/16 colors).");
        }
        if (unique <= 256)
        {
            int vram = w * h + 256 * 2;
            return new Report(w, h, unique, hasAlpha, Verdict.Clut8bpp, vram,
                $"Needs 8bpp CLUT ({unique}/256 colors).");
        }
        int vramDirect = w * h * 2;
        return new Report(w, h, unique, hasAlpha, Verdict.Direct16bpp, vramDirect,
            $"Exceeds CLUT capacity (>256 unique colors) — 16bpp direct = 2× VRAM.");
    }

    public static List<string> FindProjectImages(string rootPath = "res://")
    {
        var results = new List<string>();
        WalkDir(rootPath, results);
        return results;
    }

    private static void WalkDir(string path, List<string> results)
    {
        using var dir = DirAccess.Open(path);
        if (dir == null) return;

        dir.ListDirBegin();
        while (true)
        {
            string name = dir.GetNext();
            if (string.IsNullOrEmpty(name)) break;
            // Skip hidden dirs (.godot, .idea, .import, ...) and our own plugin tree
            if (name.StartsWith(".")) continue;
            if (name == "addons" && path == "res://") continue;

            // Preserve existing trailing slash — `res://` collapses to `res:`
            // if we TrimEnd('/'). Only add a separator when needed.
            string full = path.EndsWith("/") ? path + name : path + "/" + name;
            if (dir.CurrentIsDir())
            {
                WalkDir(full, results);
            }
            else
            {
                string lower = name.ToLowerInvariant();
                if (lower.EndsWith(".png") || lower.EndsWith(".jpg")
                    || lower.EndsWith(".jpeg") || lower.EndsWith(".webp")
                    || lower.EndsWith(".svg") || lower.EndsWith(".tga")
                    || lower.EndsWith(".bmp"))
                {
                    results.Add(full);
                }
            }
        }
        dir.ListDirEnd();
    }
}
