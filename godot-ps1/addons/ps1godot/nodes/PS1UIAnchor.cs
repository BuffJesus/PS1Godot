using Godot;

namespace PS1Godot;

// 9-position anchor for PS1UIElement placement. Replaces the
// "count pixels from the top-left" workflow that the Custom default
// still supports. At export time (SceneCollector.EmitUICanvas) the
// anchor + the element's X / Y / W / H are resolved to a final
// absolute top-left in PSX screen coords — the splashpack binary
// is byte-identical to what a pixel-counting author would have
// typed by hand, so no runtime format bump.
//
// Convention for X / Y when Anchor is non-Custom:
//   - For edge-aligned axes (left/right, top/bottom), X/Y are
//     **insets** — distances from that edge toward the screen
//     center. Positive moves the element onto the screen. Negative
//     pushes it off (useful for animating a HUD in from offscreen).
//   - For center-aligned axes, X/Y are **offsets from the center**.
//     Positive moves right/down, negative moves left/up.
//
// Concrete examples:
//   TopRight,  X=8, Y=8  → HUD corner with 8 px padding.
//   BottomCenter, X=0, Y=16 → dialog box horizontally centered, 16
//                             px above the bottom edge.
//   Center, X=0, Y=0 → element's center aligned with (160, 120).
//
// Custom is the default for backward compatibility: X / Y are the
// absolute top-left corner, same as before this type existed.
public enum PS1UIAnchor
{
    Custom = 0,
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

// Pure resolution helper shared by the exporter (SceneCollector) and
// the editor preview (PS1UICanvasEditor) so both agree on where an
// element actually lands. Keeping this logic in one place means a
// future "drag on the overlay also updates inset" story stays
// trivially invertible.
public static class PS1UIAnchoring
{
    // PSX reference resolution.
    public const int PsxWidth = 320;
    public const int PsxHeight = 240;

    // Returns the absolute top-left corner of `el` on a 320×240 PSX
    // screen, given its Anchor, X, Y, Width, Height.
    public static (int X, int Y) Resolve(PS1UIElement el)
    {
        int w = el.Width;
        int h = el.Height;

        switch (el.Anchor)
        {
            case PS1UIAnchor.Custom:
            case PS1UIAnchor.TopLeft:
                return (el.X, el.Y);

            case PS1UIAnchor.TopCenter:
                return (PsxWidth / 2 - w / 2 + el.X, el.Y);

            case PS1UIAnchor.TopRight:
                return (PsxWidth - w - el.X, el.Y);

            case PS1UIAnchor.CenterLeft:
                return (el.X, PsxHeight / 2 - h / 2 + el.Y);

            case PS1UIAnchor.Center:
                return (PsxWidth / 2 - w / 2 + el.X,
                        PsxHeight / 2 - h / 2 + el.Y);

            case PS1UIAnchor.CenterRight:
                return (PsxWidth - w - el.X,
                        PsxHeight / 2 - h / 2 + el.Y);

            case PS1UIAnchor.BottomLeft:
                return (el.X, PsxHeight - h - el.Y);

            case PS1UIAnchor.BottomCenter:
                return (PsxWidth / 2 - w / 2 + el.X,
                        PsxHeight - h - el.Y);

            case PS1UIAnchor.BottomRight:
                return (PsxWidth - w - el.X,
                        PsxHeight - h - el.Y);

            default:
                return (el.X, el.Y);
        }
    }
}
