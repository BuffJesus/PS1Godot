using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace PS1Godot.Exporter;

// RGB palette quantizer — K-means to build up to N centroids, KD-tree for
// nearest-neighbor lookup during Floyd-Steinberg dithering. Ported from
// SplashEdit's ImageProcessing.cs; the algorithm choice is deliberately
// basic (10 K-means iterations, fixed, no convergence check) because the
// input images are small (typically ≤256×256) and export time matters
// more than a marginal quality bump.
public static class ImageProcessing
{
    public struct QuantizedResult
    {
        public int[,] Indices;        // [width, height] → palette index
        public List<Vector3> Palette; // RGB floats in [0,1]
    }

    public static QuantizedResult Quantize(Image img, int maxColors)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = img.GetPixel(x, y);

        var unique = pixels.Select(c => new Vector3(c.R, c.G, c.B)).Distinct().ToList();
        if (unique.Count <= maxColors)
            return ConvertDirect(pixels, w, h);

        var palette = KMeans(unique, maxColors);
        var kd = new KDTree(palette);

        var indices = new int[w, h];
        // Floyd-Steinberg dithering, in-place on the working pixel buffer.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int pIdx = y * w + x;
                var oldColor = new Vector3(pixels[pIdx].R, pixels[pIdx].G, pixels[pIdx].B);
                int nearest = kd.FindNearestIndex(oldColor);
                indices[x, y] = nearest;

                var error = oldColor - palette[nearest];
                PropagateError(pixels, w, h, x, y, error);
            }
        }
        return new QuantizedResult { Indices = indices, Palette = palette };
    }

    private static List<Vector3> KMeans(List<Vector3> colors, int k)
    {
        var centroids = Enumerable.Range(0, k)
            .Select(i => colors[i * colors.Count / k])
            .ToList();

        for (int iter = 0; iter < 10; iter++)
        {
            var clusters = Enumerable.Range(0, k).Select(_ => new List<Vector3>()).ToList();
            foreach (var c in colors)
            {
                int closest = 0;
                float closestDist = float.MaxValue;
                for (int j = 0; j < k; j++)
                {
                    float d = (centroids[j] - c).LengthSquared();
                    if (d < closestDist) { closestDist = d; closest = j; }
                }
                clusters[closest].Add(c);
            }
            for (int j = 0; j < k; j++)
            {
                if (clusters[j].Count > 0)
                {
                    Vector3 sum = Vector3.Zero;
                    foreach (var c in clusters[j]) sum += c;
                    centroids[j] = sum / clusters[j].Count;
                }
            }
        }
        return centroids;
    }

    private static void PropagateError(Color[] pixels, int w, int h, int x, int y, Vector3 err)
    {
        void Add(int dx, int dy, float factor)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) return;
            int i = ny * w + nx;
            pixels[i].R += err.X * factor;
            pixels[i].G += err.Y * factor;
            pixels[i].B += err.Z * factor;
        }
        Add(1, 0, 7f / 16f);
        Add(-1, 1, 3f / 16f);
        Add(0, 1, 5f / 16f);
        Add(1, 1, 1f / 16f);
    }

    private static QuantizedResult ConvertDirect(Color[] pixels, int w, int h)
    {
        var indices = new int[w, h];
        var palette = new List<Vector3>();
        var seen = new Dictionary<Vector3, int>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = new Vector3(pixels[y * w + x].R, pixels[y * w + x].G, pixels[y * w + x].B);
                if (!seen.TryGetValue(c, out int idx))
                {
                    idx = palette.Count;
                    palette.Add(c);
                    seen[c] = idx;
                }
                indices[x, y] = idx;
            }
        }
        return new QuantizedResult { Indices = indices, Palette = palette };
    }

    private sealed class KDTree
    {
        private sealed class Node
        {
            public Vector3 Point;
            public int Index;
            public Node? Left, Right;
        }

        private readonly Node? _root;

        public KDTree(List<Vector3> points)
        {
            var items = new List<(Vector3 p, int i)>(points.Count);
            for (int i = 0; i < points.Count; i++) items.Add((points[i], i));
            _root = Build(items, 0);
        }

        private static Node? Build(List<(Vector3 p, int i)> items, int depth)
        {
            if (items.Count == 0) return null;
            int axis = depth % 3;
            items.Sort((a, b) => Axis(a.p, axis).CompareTo(Axis(b.p, axis)));
            int median = items.Count / 2;
            return new Node
            {
                Point = items[median].p,
                Index = items[median].i,
                Left = Build(items.Take(median).ToList(), depth + 1),
                Right = Build(items.Skip(median + 1).ToList(), depth + 1),
            };
        }

        public int FindNearestIndex(Vector3 target) => Nearest(_root!, target, 0, _root!).Index;

        private static Node Nearest(Node node, Vector3 target, int depth, Node best)
        {
            if (node == null) return best;
            if ((target - node.Point).LengthSquared() < (target - best.Point).LengthSquared())
                best = node;
            int axis = depth % 3;
            Node? first = Axis(target, axis) < Axis(node.Point, axis) ? node.Left : node.Right;
            Node? second = ReferenceEquals(first, node.Left) ? node.Right : node.Left;
            if (first != null) best = Nearest(first, target, depth + 1, best);
            float diff = Axis(target, axis) - Axis(node.Point, axis);
            if (diff * diff < (target - best.Point).LengthSquared() && second != null)
                best = Nearest(second, target, depth + 1, best);
            return best;
        }

        private static float Axis(Vector3 v, int a) => a switch { 0 => v.X, 1 => v.Y, _ => v.Z };
    }
}
