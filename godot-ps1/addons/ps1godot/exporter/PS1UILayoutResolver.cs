using System.Collections.Generic;
using Godot;

namespace PS1Godot.Exporter;

// Walks a PS1UICanvas's child tree (which may contain layout containers
// like PS1UIHBox / PS1UIVBox / PS1UISizeBox / PS1UISpacer / PS1UIOverlay)
// and emits a flat list of (PS1UIElement, absolute-X, absolute-Y, W, H)
// tuples in the order they should render.
//
// This keeps the splashpack binary format identical to a flat hand-authored
// canvas: layout is purely an authoring convenience, resolved once at
// export. The runtime never sees containers.
//
// Layout rules per container (mirrors UMG slot semantics, simplified):
//   - PS1UICanvas (root): each direct child is anchor-resolved against the
//     PSX 320×240 reference rect via PS1UIAnchoring (existing code path).
//     Containers underneath then recurse with their own rect as constraint.
//   - PS1UIHBox: pack children left-to-right inside (rect - Padding).
//     Fixed-width children take their own Width; flex children split the
//     remaining space proportionally. SlotVAlign pins each child's Y.
//   - PS1UIVBox: same vertically.
//   - PS1UISizeBox: single-child wrapper. WidthOverride / HeightOverride
//     replace the child's desired size; Padding insets it.
//   - PS1UIOverlay: every child gets the full (rect - Padding) as its
//     slot, then SlotH/VAlign pins it. Z-order = scene-tree order
//     (first child drawn first, last on top).
//   - PS1UISpacer: pure layout token; consumes space in HBox/VBox, emits
//     no UIElementRecord.
//
// Slot fields (SlotHAlign / SlotVAlign / SlotFlex / SlotPadding) live on
// the CHILD widget. Containers also have their own slot fields so they
// can be nested.
public static class PS1UILayoutResolver
{
    public readonly struct Placed
    {
        public Placed(PS1UIElement el, int x, int y, int w, int h)
        {
            Element = el; X = x; Y = y; W = w; H = h;
        }
        public PS1UIElement Element { get; }
        public int X { get; }
        public int Y { get; }
        public int W { get; }
        public int H { get; }
    }

    public static List<Placed> Flatten(PS1UICanvas canvas)
    {
        var output = new List<Placed>();
        foreach (var child in canvas.GetChildren())
        {
            ResolveAtCanvasLevel(child, canvas, output);
        }
        return output;
    }

    // A widget directly under PS1UICanvas resolves its own rect via Anchor
    // + X/Y/W/H (existing flat-canvas behavior). Containers then walk
    // inward; PS1UIElement just lands at its anchor-resolved rect.
    private static void ResolveAtCanvasLevel(Node child, PS1UICanvas _canvas, List<Placed> output)
    {
        switch (child)
        {
            case PS1UIElement el:
            {
                var (x, y) = PS1UIAnchoring.Resolve(el);
                output.Add(new Placed(el, x, y, el.Width, el.Height));
                break;
            }
            case PS1UIHBox hbox:
            {
                var (x, y) = ResolveAnchor(hbox.Anchor, hbox.X, hbox.Y, hbox.Width, hbox.Height);
                ResolveHBox(hbox, x, y, hbox.Width, hbox.Height, output);
                break;
            }
            case PS1UIVBox vbox:
            {
                var (x, y) = ResolveAnchor(vbox.Anchor, vbox.X, vbox.Y, vbox.Width, vbox.Height);
                ResolveVBox(vbox, x, y, vbox.Width, vbox.Height, output);
                break;
            }
            case PS1UISizeBox sb:
            {
                int w = sb.WidthOverride  >= 0 ? sb.WidthOverride  : ChildDesiredWidth(sb);
                int h = sb.HeightOverride >= 0 ? sb.HeightOverride : ChildDesiredHeight(sb);
                var (x, y) = ResolveAnchor(sb.Anchor, sb.X, sb.Y, w, h);
                ResolveSizeBox(sb, x, y, w, h, output);
                break;
            }
            case PS1UIOverlay ov:
            {
                var (x, y) = ResolveAnchor(ov.Anchor, ov.X, ov.Y, ov.Width, ov.Height);
                ResolveOverlay(ov, x, y, ov.Width, ov.Height, output);
                break;
            }
            case PS1UISpacer:
                // Top-level spacer has no effect (canvas isn't a layout axis).
                break;
            case PS1UIModel:
                // PS1UIModel is handled by SceneCollector.CollectUIModels in a
                // parallel pass — it doesn't participate in UI-element layout.
                // Silently skip here so it doesn't trip the "unknown widget"
                // warning below.
                break;
            default:
                GD.PushWarning($"[PS1Godot] PS1UICanvas '{_canvas.Name}' has child '{child.Name}' " +
                               $"of type {child.GetType().Name} — not a PS1 UI widget, ignored.");
                break;
        }
    }

    // ─── Container layouts ───────────────────────────────────────────

    private static void ResolveHBox(PS1UIHBox box, int x, int y, int w, int h, List<Placed> output)
    {
        int innerX = x + box.Padding.X;
        int innerY = y + box.Padding.Y;
        int innerW = w - box.Padding.X - box.Padding.Z;
        int innerH = h - box.Padding.Y - box.Padding.W;

        var children = NonNullChildren(box);
        if (children.Count == 0) return;

        // Pass 1: sum fixed widths + flex weights.
        int fixedSum = 0;
        int flexSum  = 0;
        foreach (var c in children)
        {
            int flex = SlotFlex(c);
            if (flex > 0) flexSum += flex;
            else          fixedSum += MainAxisDesired(c, horizontal: true);
        }
        int spacingTotal = box.Spacing * (children.Count - 1);
        int flexSpace = innerW - fixedSum - spacingTotal;
        if (flexSpace < 0) flexSpace = 0;

        // Pass 2: place children left-to-right.
        int cursorX = innerX;
        for (int i = 0; i < children.Count; i++)
        {
            var c = children[i];
            int flex = SlotFlex(c);
            int slotW = flex > 0 ? (flexSpace * flex / flexSum)
                                 : MainAxisDesired(c, horizontal: true);
            int slotH = innerH;
            int slotX = cursorX;
            int slotY = innerY;

            PlaceInSlot(c, slotX, slotY, slotW, slotH,
                        defaultHAlign: PS1UISlotAlign.Fill,    // already sized horizontally
                        defaultVAlign: box.DefaultVAlign,
                        output: output);

            cursorX += slotW + box.Spacing;
        }
    }

    private static void ResolveVBox(PS1UIVBox box, int x, int y, int w, int h, List<Placed> output)
    {
        int innerX = x + box.Padding.X;
        int innerY = y + box.Padding.Y;
        int innerW = w - box.Padding.X - box.Padding.Z;
        int innerH = h - box.Padding.Y - box.Padding.W;

        var children = NonNullChildren(box);
        if (children.Count == 0) return;

        int fixedSum = 0;
        int flexSum  = 0;
        foreach (var c in children)
        {
            int flex = SlotFlex(c);
            if (flex > 0) flexSum += flex;
            else          fixedSum += MainAxisDesired(c, horizontal: false);
        }
        int spacingTotal = box.Spacing * (children.Count - 1);
        int flexSpace = innerH - fixedSum - spacingTotal;
        if (flexSpace < 0) flexSpace = 0;

        int cursorY = innerY;
        for (int i = 0; i < children.Count; i++)
        {
            var c = children[i];
            int flex = SlotFlex(c);
            int slotH = flex > 0 ? (flexSpace * flex / flexSum)
                                 : MainAxisDesired(c, horizontal: false);
            int slotW = innerW;
            int slotX = innerX;
            int slotY = cursorY;

            PlaceInSlot(c, slotX, slotY, slotW, slotH,
                        defaultHAlign: box.DefaultHAlign,
                        defaultVAlign: PS1UISlotAlign.Fill,
                        output: output);

            cursorY += slotH + box.Spacing;
        }
    }

    private static void ResolveSizeBox(PS1UISizeBox sb, int x, int y, int w, int h, List<Placed> output)
    {
        int innerX = x + sb.Padding.X;
        int innerY = y + sb.Padding.Y;
        int innerW = w - sb.Padding.X - sb.Padding.Z;
        int innerH = h - sb.Padding.Y - sb.Padding.W;

        var children = NonNullChildren(sb);
        if (children.Count == 0) return;
        if (children.Count > 1)
        {
            GD.PushWarning($"[PS1Godot] PS1UISizeBox '{sb.Name}' has {children.Count} children; " +
                           "only the first is laid out. Wrap multiple children in an Overlay or HBox/VBox.");
        }
        PlaceInSlot(children[0], innerX, innerY, innerW, innerH,
                    defaultHAlign: PS1UISlotAlign.Fill,
                    defaultVAlign: PS1UISlotAlign.Fill,
                    output: output);
    }

    private static void ResolveOverlay(PS1UIOverlay ov, int x, int y, int w, int h, List<Placed> output)
    {
        int innerX = x + ov.Padding.X;
        int innerY = y + ov.Padding.Y;
        int innerW = w - ov.Padding.X - ov.Padding.Z;
        int innerH = h - ov.Padding.Y - ov.Padding.W;

        foreach (var c in NonNullChildren(ov))
        {
            PlaceInSlot(c, innerX, innerY, innerW, innerH,
                        defaultHAlign: ov.DefaultHAlign,
                        defaultVAlign: ov.DefaultVAlign,
                        output: output);
        }
    }

    // ─── Slot placement (cross-axis pin + recurse for containers) ──

    // Given a child node and the slot it was assigned (slotX..slotW), pin
    // the child within the slot per its SlotHAlign / SlotVAlign and
    // recurse into containers. PS1UIElements emit a Placed record.
    private static void PlaceInSlot(Node child, int slotX, int slotY, int slotW, int slotH,
                                     PS1UISlotAlign defaultHAlign, PS1UISlotAlign defaultVAlign,
                                     List<Placed> output)
    {
        // Inset by per-child SlotPadding before alignment.
        Vector4I sp = SlotPadding(child);
        int padX = slotX + sp.X;
        int padY = slotY + sp.Y;
        int padW = slotW - sp.X - sp.Z;
        int padH = slotH - sp.Y - sp.W;
        if (padW < 0) padW = 0;
        if (padH < 0) padH = 0;

        var hAlign = ResolveAlign(SlotHAlign(child), defaultHAlign);
        var vAlign = ResolveAlign(SlotVAlign(child), defaultVAlign);

        int desiredW = ChildDesiredWidth(child);
        int desiredH = ChildDesiredHeight(child);

        int finalW = hAlign == PS1UISlotAlign.Fill ? padW : System.Math.Min(desiredW, padW);
        int finalH = vAlign == PS1UISlotAlign.Fill ? padH : System.Math.Min(desiredH, padH);
        int finalX = AxisPin(padX, padW, finalW, hAlign);
        int finalY = AxisPin(padY, padH, finalH, vAlign);

        switch (child)
        {
            case PS1UIElement el:
                output.Add(new Placed(el, finalX, finalY, finalW, finalH));
                break;
            case PS1UIHBox hbox:
                ResolveHBox(hbox, finalX, finalY, finalW, finalH, output);
                break;
            case PS1UIVBox vbox:
                ResolveVBox(vbox, finalX, finalY, finalW, finalH, output);
                break;
            case PS1UISizeBox sb:
                ResolveSizeBox(sb, finalX, finalY, finalW, finalH, output);
                break;
            case PS1UIOverlay ov:
                ResolveOverlay(ov, finalX, finalY, finalW, finalH, output);
                break;
            case PS1UISpacer:
                // Spacer renders nothing; its only purpose is to consume slot space.
                break;
        }
    }

    private static int AxisPin(int slotOrigin, int slotSize, int finalSize, PS1UISlotAlign align)
    {
        switch (align)
        {
            case PS1UISlotAlign.Center: return slotOrigin + (slotSize - finalSize) / 2;
            case PS1UISlotAlign.End:    return slotOrigin + slotSize - finalSize;
            case PS1UISlotAlign.Fill:   return slotOrigin;
            case PS1UISlotAlign.Start:
            default:                    return slotOrigin;
        }
    }

    private static PS1UISlotAlign ResolveAlign(PS1UISlotAlign requested, PS1UISlotAlign fallback)
    {
        return requested == PS1UISlotAlign.Inherit ? fallback : requested;
    }

    // ─── Per-widget property accessors (unify slot fields across types) ──

    private static int SlotFlex(Node n) => n switch
    {
        PS1UIElement el => el.SlotFlex,
        PS1UIHBox hb    => hb.SlotFlex,
        PS1UIVBox vb    => vb.SlotFlex,
        PS1UISizeBox sb => sb.SlotFlex,
        PS1UIOverlay ov => ov.SlotFlex,
        PS1UISpacer sp  => sp.SlotFlex,
        _ => 0,
    };

    private static PS1UISlotAlign SlotHAlign(Node n) => n switch
    {
        PS1UIElement el => el.SlotHAlign,
        PS1UIHBox hb    => hb.SlotHAlign,
        PS1UIVBox vb    => vb.SlotHAlign,
        PS1UISizeBox sb => sb.SlotHAlign,
        PS1UIOverlay ov => ov.SlotHAlign,
        _ => PS1UISlotAlign.Inherit,
    };

    private static PS1UISlotAlign SlotVAlign(Node n) => n switch
    {
        PS1UIElement el => el.SlotVAlign,
        PS1UIHBox hb    => hb.SlotVAlign,
        PS1UIVBox vb    => vb.SlotVAlign,
        PS1UISizeBox sb => sb.SlotVAlign,
        PS1UIOverlay ov => ov.SlotVAlign,
        _ => PS1UISlotAlign.Inherit,
    };

    private static Vector4I SlotPadding(Node n) => n switch
    {
        PS1UIElement el => el.SlotPadding,
        PS1UIHBox hb    => hb.SlotPadding,
        PS1UIVBox vb    => vb.SlotPadding,
        PS1UISizeBox sb => sb.SlotPadding,
        PS1UIOverlay ov => ov.SlotPadding,
        _ => Vector4I.Zero,
    };

    private static int ChildDesiredWidth(Node n) => n switch
    {
        PS1UIElement el => el.Width,
        PS1UIHBox hb    => hb.Width,
        PS1UIVBox vb    => vb.Width,
        PS1UISizeBox sb => sb.WidthOverride >= 0 ? sb.WidthOverride : 0,
        PS1UIOverlay ov => ov.Width,
        PS1UISpacer sp  => sp.Width,
        _               => 0,
    };

    private static int ChildDesiredHeight(Node n) => n switch
    {
        PS1UIElement el => el.Height,
        PS1UIHBox hb    => hb.Height,
        PS1UIVBox vb    => vb.Height,
        PS1UISizeBox sb => sb.HeightOverride >= 0 ? sb.HeightOverride : 0,
        PS1UIOverlay ov => ov.Height,
        PS1UISpacer sp  => sp.Height,
        _               => 0,
    };

    // For HBox: returns desired width. For VBox: returns desired height.
    private static int MainAxisDesired(Node n, bool horizontal)
        => horizontal ? ChildDesiredWidth(n) : ChildDesiredHeight(n);

    private static List<Node> NonNullChildren(Node parent)
    {
        var output = new List<Node>();
        foreach (var c in parent.GetChildren())
        {
            if (c is PS1UIElement || c is PS1UIHBox || c is PS1UIVBox
                || c is PS1UISizeBox || c is PS1UIOverlay || c is PS1UISpacer)
            {
                output.Add(c);
            }
        }
        return output;
    }

    // Anchor resolution against the PSX 320×240 reference rect, given an
    // explicit (W, H) for the rect being placed.
    private static (int X, int Y) ResolveAnchor(PS1UIAnchor anchor, int x, int y, int w, int h)
    {
        const int PsxW = 320, PsxH = 240;
        switch (anchor)
        {
            case PS1UIAnchor.Custom:
            case PS1UIAnchor.TopLeft:     return (x, y);
            case PS1UIAnchor.TopCenter:   return (PsxW / 2 - w / 2 + x, y);
            case PS1UIAnchor.TopRight:    return (PsxW - w - x, y);
            case PS1UIAnchor.CenterLeft:  return (x, PsxH / 2 - h / 2 + y);
            case PS1UIAnchor.Center:      return (PsxW / 2 - w / 2 + x, PsxH / 2 - h / 2 + y);
            case PS1UIAnchor.CenterRight: return (PsxW - w - x, PsxH / 2 - h / 2 + y);
            case PS1UIAnchor.BottomLeft:  return (x, PsxH - h - y);
            case PS1UIAnchor.BottomCenter:return (PsxW / 2 - w / 2 + x, PsxH - h - y);
            case PS1UIAnchor.BottomRight: return (PsxW - w - x, PsxH - h - y);
            default:                       return (x, y);
        }
    }
}
