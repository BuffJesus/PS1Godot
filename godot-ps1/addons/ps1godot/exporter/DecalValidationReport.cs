#if TOOLS
using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Per-scene decal-stack pass. PS1 hardware blends each alpha-keyed /
// semi-transparent quad against the framebuffer separately, so stacking
// many of them at the same pixel costs fillrate proportional to the
// stack depth. The canonical bad case: 8 translucent decals (blood,
// scratches, shadows, a CRT bezel) all overlapping the centre of a
// 320×240 frame — every overlapped pixel becomes a serial blend chain.
//
// We can only check this at export time for *UI* canvases: each
// PS1UICanvas IS a 320×240 screen, and PS1UIElement.Translucent is
// authored explicitly. World-space mesh translucency is harder — the
// stack depth depends on camera angle, FOV, and player position, so
// AABB overlap in 3D doesn't reliably predict screen overlap. We skip
// the 3D side here; the 2D UI side covers HUDs, intros, score screens,
// title cards — exactly where authors stack effects most aggressively.
//
// Output: prints a per-scene summary line, names canvases whose
// translucent stack exceeds the threshold, returns warning count for
// the dock summary.
public static class DecalValidationReport
{
    // Stack depth that starts to hurt fillrate noticeably. PSX GPU's
    // translucent-blend cost is ~2× an opaque draw of the same rect;
    // 6 stacked = 12× the fillrate of a single opaque pass for those
    // pixels. Higher = author should consider baking the stack into a
    // single pre-blended texture.
    private const int StackDepthWarnThreshold = 6;

    public static int EmitForScene(SceneData data, int sceneIndex)
    {
        if (data.UICanvases.Count == 0) return 0;

        int warnCount = 0;
        var problemCanvases = new List<(string Name, int Depth, string WorstElement)>();

        foreach (var canvas in data.UICanvases)
        {
            // Only canvases authors expect to be on-screen at gameplay
            // time. Hidden-by-default menus / popups still cost fillrate
            // when shown but if they trigger this it's the author's
            // explicit choice, not a missed regression.
            if (!canvas.VisibleOnLoad) continue;

            // Loading-screen canvases ship in their own LoaderPack and
            // render once before the splashpack loads; fillrate budget
            // is moot during the load itself. Skip.
            if (canvas.Residency == PS1UIResidency.LoadingScreen) continue;

            var translucents = new List<UIElementRecord>();
            foreach (var el in canvas.Elements)
                if (el.Translucent && el.VisibleOnLoad)
                    translucents.Add(el);
            if (translucents.Count <= StackDepthWarnThreshold) continue;

            // Worst-case stack: for each element, count how many other
            // translucent siblings overlap its rect. Max+1 = at least
            // that many rects share at least one pixel of this element.
            // Lower bound on actual stack depth (a true grid scan would
            // catch tighter clusters) but accurate enough to flag the
            // problem.
            int maxOverlap = 0;
            string worstName = translucents[0].Name;
            for (int i = 0; i < translucents.Count; i++)
            {
                int n = 0;
                for (int j = 0; j < translucents.Count; j++)
                {
                    if (i == j) continue;
                    if (RectsOverlap(translucents[i], translucents[j])) n++;
                }
                if (n > maxOverlap)
                {
                    maxOverlap = n;
                    worstName = translucents[i].Name;
                }
            }

            int stackDepth = maxOverlap + 1; // +1 for the centre rect itself
            if (stackDepth > StackDepthWarnThreshold)
            {
                problemCanvases.Add((canvas.Name, stackDepth, worstName));
                warnCount++;
            }
        }

        if (warnCount == 0)
        {
            GD.Print($"[PS1Godot] Decal stack scene[{sceneIndex}]: clean " +
                     $"(no canvas exceeds {StackDepthWarnThreshold} stacked translucent quads).");
            return 0;
        }

        GD.Print($"[PS1Godot] Decal stack scene[{sceneIndex}]: {warnCount} canvas(es) over " +
                 $"the {StackDepthWarnThreshold}-quad threshold.");
        foreach (var p in problemCanvases)
        {
            GD.Print($"[PS1Godot]   '{p.Name}': stack depth >= {p.Depth} at element '{p.WorstElement}'. " +
                     $"PSX blends each translucent quad serially — bake the stack into one pre-blended " +
                     $"image, or split the canvas across multiple SortOrder layers so they don't all " +
                     $"render at once.");
        }
        return warnCount;
    }

    // Standard axis-aligned rect overlap (positive-area inclusive).
    // Element coords are PSX screen-space pixels (0..319 × 0..239 ish);
    // anchors are stripped here because they're not yet wired through
    // the writer (anchors get baked at export — for now Custom is the
    // only mode).
    private static bool RectsOverlap(UIElementRecord a, UIElementRecord b)
    {
        int aR = a.X + a.W;
        int aB = a.Y + a.H;
        int bR = b.X + b.W;
        int bB = b.Y + b.H;
        return a.X < bR && b.X < aR && a.Y < bB && b.Y < aB;
    }
}
#endif
