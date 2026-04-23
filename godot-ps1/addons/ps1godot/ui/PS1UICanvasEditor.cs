#if TOOLS
using Godot;
using PS1Godot.Exporter;

namespace PS1Godot.UI;

// WYSIWYG preview + edit surface for PS1UICanvas. Lives as a bottom
// panel tab so it's next to Output / Debugger / Search, and renders
// the selected canvas at integer zoom against a 320×240 PS1 screen
// frame. Theme slots resolve live so swapping the canvas's Theme
// restyles every opted-in element in the preview immediately.
//
// Design tenets (docs/ui-ux-plan.md § G):
//  - Intuitive: selecting a PS1UICanvas or any PS1UIElement child
//    auto-opens this tab on the right canvas.
//  - Non-intimidating: drag elements with the mouse, add new ones
//    from the toolbar. Author never hand-edits X/Y unless they want to.
//  - Modern: integer-zoom nearest-neighbor, 8-px grid overlay.
//  - Beautiful: coherent accent (PS1 red), 8-px spacing, clear
//    hierarchy label so the author always knows what's on screen.
//
// Author interactions:
//  - Click an element → selects it in the Godot scene dock (inspector
//    shows the full property set).
//  - Click-and-drag → moves the element via the same X/Y fields the
//    inspector edits (only for Anchor == Custom; other anchors are
//    parent-constrained). EditorUndoRedoManager integrated so Ctrl-Z
//    reverts individual drags.
//  - Click-and-drag a corner handle → resizes Width/Height (same undo).
//  - "+ Add" toolbar dropdown inserts a new PS1UI* node under the
//    currently-selected container (or the canvas if no container is
//    selected). The new node is auto-selected afterward for tweaking.
//  - Delete-key on the scene tree removes nodes — standard Godot; no
//    extra code needed here.
[Tool]
public partial class PS1UICanvasEditor : VBoxContainer
{
    public const int PsxWidth = 320;
    public const int PsxHeight = 240;

    // Dark PS1-ish checkered background so transparent elements read.
    private static readonly Color BgA = new(0.09f, 0.09f, 0.12f);
    private static readonly Color BgB = new(0.12f, 0.12f, 0.15f);
    private static readonly Color FrameColor = new(1f, 1f, 1f, 0.6f);
    private static readonly Color BoundsColor = new(1f, 1f, 1f, 0.18f);
    private static readonly Color GridMajor = new(1f, 1f, 1f, 0.08f);
    private static readonly Color GridMinor = new(1f, 1f, 1f, 0.04f);
    // Outline colors for container widgets — dim so the actual elements
    // read first, but visible enough to see the layout hierarchy.
    private static readonly Color ContainerOutline = new(0.4f, 0.7f, 1f, 0.4f);
    private static readonly Color ModelOutline     = new(1f, 0.6f, 0.2f, 0.6f);
    // Selection chrome — accent-colored handles for move/resize.
    private static readonly Color SelectionColor   = new(1f, 0.25f, 0.25f, 1f);
    private static readonly Color HandleFill       = new(1f, 1f, 1f, 1f);
    private const int HandleSize = 8;  // pixels on screen (independent of zoom)

    private PS1UICanvas? _selectedCanvas;
    private Node? _selectedNode;  // any PS1UI* node (element or container); null = none
    private int _zoom = 2;
    private bool _showGrid = true;

    private Label? _headerLabel;
    private OptionButton? _zoomCombo;
    private CheckButton? _gridToggle;
    private MenuButton? _addMenu;
    private Control? _canvasArea;

    // Drag state — populated on LMB down over an element, cleared on release.
    private enum DragMode { None, Move, ResizeBR }
    private DragMode _dragMode = DragMode.None;
    private Vector2 _dragMouseStart;
    private Vector2I _dragNodeStart;
    private Vector2I _dragNodeSizeStart;

    public PS1UICanvasEditor()
    {
        Name = "PS1 UI";
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 6);
        BuildUI();
    }

    // Called by PS1GodotPlugin whenever the editor selection changes.
    // The plugin passes the owning canvas (derived by walking up from
    // any selected PS1UI* node); `node` is the exact selected node.
    public void SetSelection(PS1UICanvas? canvas, Node? node)
    {
        bool changed = _selectedCanvas != canvas || _selectedNode != node;
        _selectedCanvas = canvas;
        _selectedNode   = node;
        if (changed)
        {
            RefreshHeader();
            _canvasArea?.QueueRedraw();
        }
    }

    // Back-compat for the prior one-arg signature used by the plugin.
    public void SetSelectedCanvas(PS1UICanvas? canvas) => SetSelection(canvas, canvas);

    private void BuildUI()
    {
        // ── Toolbar ─────────────────────────────────────────────────
        var toolbar = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        toolbar.AddThemeConstantOverride("separation", 8);
        AddChild(toolbar);

        toolbar.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });

        _headerLabel = new Label
        {
            Text = "No PS1UICanvas selected",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toolbar.AddChild(_headerLabel);

        // Add-child dropdown — appends the chosen widget type under
        // `_selectedNode` (if it's a container) or under the canvas.
        _addMenu = new MenuButton { Text = "+ Add" };
        _addMenu.GetPopup().AddItem("Text Element", (int)AddKind.TextElement);
        _addMenu.GetPopup().AddItem("Box Element", (int)AddKind.BoxElement);
        _addMenu.GetPopup().AddSeparator();
        _addMenu.GetPopup().AddItem("HBox", (int)AddKind.HBox);
        _addMenu.GetPopup().AddItem("VBox", (int)AddKind.VBox);
        _addMenu.GetPopup().AddItem("SizeBox", (int)AddKind.SizeBox);
        _addMenu.GetPopup().AddItem("Spacer", (int)AddKind.Spacer);
        _addMenu.GetPopup().AddItem("Overlay", (int)AddKind.Overlay);
        _addMenu.GetPopup().AddSeparator();
        _addMenu.GetPopup().AddItem("3D Model (UIModel)", (int)AddKind.UIModel);
        _addMenu.GetPopup().IdPressed += OnAddNodeRequested;
        toolbar.AddChild(_addMenu);

        var zoomLabel = new Label { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center };
        toolbar.AddChild(zoomLabel);

        _zoomCombo = new OptionButton();
        _zoomCombo.AddItem("1×", 1);
        _zoomCombo.AddItem("2×", 2);
        _zoomCombo.AddItem("3×", 3);
        _zoomCombo.AddItem("4×", 4);
        _zoomCombo.Select(1);
        _zoomCombo.ItemSelected += OnZoomChanged;
        toolbar.AddChild(_zoomCombo);

        _gridToggle = new CheckButton { Text = "Grid", ButtonPressed = _showGrid };
        _gridToggle.Toggled += OnGridToggled;
        toolbar.AddChild(_gridToggle);

        toolbar.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });

        // ── Scrolling canvas area ───────────────────────────────────
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        AddChild(scroll);

        var center = new CenterContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scroll.AddChild(center);

        _canvasArea = new Control();
        UpdateCanvasSize();
        _canvasArea.Draw += OnDrawCanvas;
        _canvasArea.GuiInput += OnCanvasGuiInput;
        _canvasArea.MouseFilter = MouseFilterEnum.Stop;
        center.AddChild(_canvasArea);
    }

    private void OnZoomChanged(long idx)
    {
        _zoom = Mathf.Clamp((int)(_zoomCombo?.GetItemId((int)idx) ?? 2), 1, 8);
        UpdateCanvasSize();
        _canvasArea?.QueueRedraw();
    }

    private void OnGridToggled(bool on)
    {
        _showGrid = on;
        _canvasArea?.QueueRedraw();
    }

    private void UpdateCanvasSize()
    {
        if (_canvasArea == null) return;
        _canvasArea.CustomMinimumSize = new Vector2(PsxWidth * _zoom, PsxHeight * _zoom);
    }

    private void RefreshHeader()
    {
        if (_headerLabel == null) return;
        if (_selectedCanvas == null)
        {
            _headerLabel.Text = "No PS1UICanvas selected — pick one in the scene tree";
            return;
        }
        string name = string.IsNullOrEmpty(_selectedCanvas.CanvasName)
            ? _selectedCanvas.Name
            : _selectedCanvas.CanvasName;
        string themed = _selectedCanvas.Theme != null ? " · themed" : "";
        string sel = _selectedNode == _selectedCanvas || _selectedNode == null
            ? ""
            : $"  —  selected: {_selectedNode.Name}";
        _headerLabel.Text = $"{name}{themed}{sel}";
    }

    // ─── Drawing ────────────────────────────────────────────────────

    private void OnDrawCanvas()
    {
        if (_canvasArea == null) return;
        int z = _zoom;
        var sz = new Vector2(PsxWidth * z, PsxHeight * z);

        // 16-px checkered background.
        for (int y = 0; y < PsxHeight; y += 16)
        {
            for (int x = 0; x < PsxWidth; x += 16)
            {
                bool checker = ((x / 16) + (y / 16)) % 2 == 0;
                _canvasArea.DrawRect(
                    new Rect2(x * z, y * z, 16 * z, 16 * z),
                    checker ? BgA : BgB,
                    filled: true);
            }
        }

        if (_showGrid) DrawGrid(z);
        _canvasArea.DrawRect(new Rect2(Vector2.Zero, sz), FrameColor, filled: false, width: 1f);

        if (_selectedCanvas == null) return;

        // 1. Draw container outlines. Walk the tree directly so containers
        //    show even when they have no children yet.
        DrawContainerOutlines(_selectedCanvas, z);

        // 2. Draw elements via the resolver so nested elements land at
        //    the same absolute positions the splashpack will encode.
        var placements = PS1UILayoutResolver.Flatten(_selectedCanvas);
        foreach (var p in placements)
        {
            float alpha = p.Element.VisibleOnLoad ? 1f : 0.3f;
            DrawElement(p.Element, p.X, p.Y, p.W, p.H, z, alpha);
        }

        // 3. Selection chrome + resize handles for the selected node.
        if (_selectedNode != null && _selectedNode != _selectedCanvas)
        {
            DrawSelectionChrome(_selectedNode, z);
        }
    }

    private void DrawContainerOutlines(Node subtreeRoot, int z)
    {
        foreach (var child in subtreeRoot.GetChildren())
        {
            (int x, int y, int w, int h)? rect = child switch
            {
                PS1UIHBox hb    => ResolveContainerRect(hb.Anchor, hb.X, hb.Y, hb.Width, hb.Height),
                PS1UIVBox vb    => ResolveContainerRect(vb.Anchor, vb.X, vb.Y, vb.Width, vb.Height),
                PS1UIOverlay ov => ResolveContainerRect(ov.Anchor, ov.X, ov.Y, ov.Width, ov.Height),
                PS1UISizeBox sb => ResolveContainerRect(sb.Anchor, sb.X, sb.Y,
                                      sb.WidthOverride  >= 0 ? sb.WidthOverride  : 64,
                                      sb.HeightOverride >= 0 ? sb.HeightOverride : 32),
                PS1UIModel mdl  => ResolveContainerRect(mdl.Anchor, mdl.X, mdl.Y, mdl.Width, mdl.Height),
                _ => ((int, int, int, int)?)null,
            };
            if (rect is (int rx, int ry, int rw, int rh))
            {
                var color = child is PS1UIModel ? ModelOutline : ContainerOutline;
                _canvasArea!.DrawRect(
                    new Rect2(rx * z, ry * z, rw * z, rh * z),
                    color, filled: false, width: 1f);

                // Type/name label above the top-left corner for clarity.
                var font = ThemeDB.FallbackFont;
                int fs = Mathf.Max(8, 8 * z / 2);
                _canvasArea.DrawString(
                    font,
                    new Vector2(rx * z + 2, ry * z - 2),
                    TypeLabel(child),
                    HorizontalAlignment.Left,
                    -1, fs, color);

                // Recurse so nested container outlines appear too.
                DrawContainerOutlines(child, z);
            }
        }
    }

    private static string TypeLabel(Node n) => n switch
    {
        PS1UIHBox hb => string.IsNullOrEmpty(hb.ContainerName) ? "HBox" : $"HBox: {hb.ContainerName}",
        PS1UIVBox vb => string.IsNullOrEmpty(vb.ContainerName) ? "VBox" : $"VBox: {vb.ContainerName}",
        PS1UIOverlay ov => string.IsNullOrEmpty(ov.ContainerName) ? "Overlay" : $"Overlay: {ov.ContainerName}",
        PS1UISizeBox sb => string.IsNullOrEmpty(sb.ContainerName) ? "SizeBox" : $"SizeBox: {sb.ContainerName}",
        PS1UIModel mdl => string.IsNullOrEmpty(mdl.ModelName) ? "Model" : $"Model: {mdl.ModelName}",
        _ => n.Name,
    };

    private static (int X, int Y, int W, int H) ResolveContainerRect(
        PS1UIAnchor anchor, int x, int y, int w, int h)
    {
        var faux = new PS1UIElement { Anchor = anchor, X = x, Y = y, Width = w, Height = h };
        var (ax, ay) = PS1UIAnchoring.Resolve(faux);
        return (ax, ay, w, h);
    }

    private void DrawGrid(int z)
    {
        if (_canvasArea == null) return;
        for (int x = 0; x <= PsxWidth; x += 8)
        {
            bool major = x % 32 == 0;
            _canvasArea.DrawLine(
                new Vector2(x * z, 0),
                new Vector2(x * z, PsxHeight * z),
                major ? GridMajor : GridMinor, 1f);
        }
        for (int y = 0; y <= PsxHeight; y += 8)
        {
            bool major = y % 32 == 0;
            _canvasArea.DrawLine(
                new Vector2(0, y * z),
                new Vector2(PsxWidth * z, y * z),
                major ? GridMajor : GridMinor, 1f);
        }
    }

    private void DrawElement(PS1UIElement el, int absX, int absY, int w, int h, int z, float alpha)
    {
        if (_canvasArea == null) return;
        var color = ResolveElementColor(el);
        if (alpha < 1f) color = new Color(color, color.A * alpha);
        var rect = new Rect2(absX * z, absY * z, w * z, h * z);

        switch (el.Type)
        {
            case PS1UIElementType.Box:
                _canvasArea.DrawRect(rect, color, filled: true);
                break;

            case PS1UIElementType.Text:
                var bounds = new Color(BoundsColor, BoundsColor.A * alpha);
                _canvasArea.DrawRect(rect, bounds, filled: false, width: 1f);

                var font = ThemeDB.FallbackFont;
                int fontSize = Mathf.Max(8, 8 * z);
                string text = string.IsNullOrEmpty(el.Text) ? "(empty)" : el.Text;
                var textColor = string.IsNullOrEmpty(el.Text) ? new Color(color, 0.4f * alpha) : color;

                var halign = el.TextAlign switch
                {
                    PS1UITextAlign.Center => HorizontalAlignment.Center,
                    PS1UITextAlign.Right  => HorizontalAlignment.Right,
                    _                     => HorizontalAlignment.Left,
                };
                int lineCount = CountLines(text);
                int totalH = lineCount * fontSize;
                int vOffset = el.TextVAlign switch
                {
                    PS1UITextVAlign.Middle => (h * z - totalH) / 2,
                    PS1UITextVAlign.Bottom => (h * z - totalH),
                    _                      => 0,
                };

                _canvasArea.DrawMultilineString(
                    font,
                    new Vector2(absX * z, absY * z + fontSize + vOffset),
                    text, halign, w * z, fontSize,
                    maxLines: -1, modulate: textColor);
                break;
        }
    }

    private void DrawSelectionChrome(Node selected, int z)
    {
        if (!TryGetNodeRect(selected, out int x, out int y, out int w, out int h)) return;
        var rect = new Rect2(x * z, y * z, w * z, h * z);
        _canvasArea!.DrawRect(rect, SelectionColor, filled: false, width: 2f);

        // Bottom-right resize handle. Drag to resize W/H. Only shown for
        // nodes that have a width/height — not Spacers.
        if (HasSize(selected))
        {
            var handleRect = new Rect2(
                (x + w) * z - HandleSize / 2,
                (y + h) * z - HandleSize / 2,
                HandleSize, HandleSize);
            _canvasArea.DrawRect(handleRect, HandleFill, filled: true);
            _canvasArea.DrawRect(handleRect, SelectionColor, filled: false, width: 1f);
        }
    }

    private static int CountLines(string s)
    {
        if (string.IsNullOrEmpty(s)) return 1;
        int n = 1;
        foreach (char c in s) if (c == '\n') n++;
        return n;
    }

    // ─── Hit testing + node rect accessors ─────────────────────────

    // Returns the absolute-canvas rect of a PS1UI* node for hit-testing
    // and selection chrome. For PS1UIElement children of layout
    // containers we use the resolved placement (since the container
    // picked the position); for direct children of PS1UICanvas (or
    // containers) we use the authored X/Y + anchor directly.
    private bool TryGetNodeRect(Node node, out int x, out int y, out int w, out int h)
    {
        // For PS1UIElement children buried in a container, the resolved
        // position lives in the flattened list.
        if (node is PS1UIElement el && el.GetParent() is not PS1UICanvas)
        {
            if (_selectedCanvas != null)
            {
                foreach (var p in PS1UILayoutResolver.Flatten(_selectedCanvas))
                {
                    if (p.Element == el)
                    {
                        x = p.X; y = p.Y; w = p.W; h = p.H;
                        return true;
                    }
                }
            }
            // Fall through to authored rect if the element isn't in the
            // resolved list (shouldn't happen, but be defensive).
        }

        switch (node)
        {
            case PS1UIElement e:
            {
                var (ax, ay) = PS1UIAnchoring.Resolve(e);
                x = ax; y = ay; w = e.Width; h = e.Height;
                return true;
            }
            case PS1UIHBox hb:
            {
                var (ax, ay, aw, ah) = ResolveContainerRect(hb.Anchor, hb.X, hb.Y, hb.Width, hb.Height);
                x = ax; y = ay; w = aw; h = ah;
                return true;
            }
            case PS1UIVBox vb:
            {
                var (ax, ay, aw, ah) = ResolveContainerRect(vb.Anchor, vb.X, vb.Y, vb.Width, vb.Height);
                x = ax; y = ay; w = aw; h = ah;
                return true;
            }
            case PS1UIOverlay ov:
            {
                var (ax, ay, aw, ah) = ResolveContainerRect(ov.Anchor, ov.X, ov.Y, ov.Width, ov.Height);
                x = ax; y = ay; w = aw; h = ah;
                return true;
            }
            case PS1UISizeBox sb:
            {
                int sw = sb.WidthOverride  >= 0 ? sb.WidthOverride  : 64;
                int sh = sb.HeightOverride >= 0 ? sb.HeightOverride : 32;
                var (ax, ay, aw, ah) = ResolveContainerRect(sb.Anchor, sb.X, sb.Y, sw, sh);
                x = ax; y = ay; w = aw; h = ah;
                return true;
            }
            case PS1UIModel m:
            {
                var (ax, ay, aw, ah) = ResolveContainerRect(m.Anchor, m.X, m.Y, m.Width, m.Height);
                x = ax; y = ay; w = aw; h = ah;
                return true;
            }
            default:
                x = y = w = h = 0;
                return false;
        }
    }

    private static bool HasSize(Node n) =>
        n is PS1UIElement or PS1UIHBox or PS1UIVBox or PS1UIOverlay or PS1UISizeBox or PS1UIModel;

    // Hit test PSX-pixel point against all drawn nodes, returning the
    // topmost match (innermost in the tree). Containers test first so
    // nested elements win; if nothing specific hits, fall back to the
    // container under the point.
    private Node? HitTest(int psxX, int psxY)
    {
        if (_selectedCanvas == null) return null;
        // Walk elements first (tightest rects).
        foreach (var p in PS1UILayoutResolver.Flatten(_selectedCanvas))
        {
            if (psxX >= p.X && psxX < p.X + p.W &&
                psxY >= p.Y && psxY < p.Y + p.H)
            {
                return p.Element;
            }
        }
        // Then containers (broader rects). Depth-first so innermost wins.
        return HitTestContainers(_selectedCanvas, psxX, psxY);
    }

    private Node? HitTestContainers(Node subtreeRoot, int psxX, int psxY)
    {
        Node? best = null;
        foreach (var child in subtreeRoot.GetChildren())
        {
            if (!TryGetNodeRect(child, out int x, out int y, out int w, out int h)) continue;
            if (child is PS1UIElement) continue;  // handled above
            if (psxX >= x && psxX < x + w && psxY >= y && psxY < y + h)
            {
                best = child;
                var deeper = HitTestContainers(child, psxX, psxY);
                if (deeper != null) best = deeper;
            }
        }
        return best;
    }

    // ─── Input ──────────────────────────────────────────────────────

    private void OnCanvasGuiInput(InputEvent ev)
    {
        if (_canvasArea == null) return;
        int z = _zoom;

        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex != MouseButton.Left) return;

            if (mb.Pressed)
            {
                Vector2 local = mb.Position;
                int psxX = (int)(local.X / z);
                int psxY = (int)(local.Y / z);

                // Resize-handle hit? (bottom-right corner of selected node.)
                if (_selectedNode != null && TryGetNodeRect(_selectedNode, out int sx, out int sy, out int sw, out int sh)
                    && HasSize(_selectedNode))
                {
                    var hRect = new Rect2((sx + sw) * z - HandleSize / 2,
                                          (sy + sh) * z - HandleSize / 2,
                                          HandleSize, HandleSize);
                    if (hRect.HasPoint(local))
                    {
                        _dragMode = DragMode.ResizeBR;
                        _dragMouseStart = local;
                        _dragNodeSizeStart = new Vector2I(GetNodeWidth(_selectedNode),
                                                         GetNodeHeight(_selectedNode));
                        _canvasArea.AcceptEvent();
                        return;
                    }
                }

                // Body hit? Select + start move drag.
                var hit = HitTest(psxX, psxY);
                if (hit != null)
                {
                    SelectInEditor(hit);
                    if (CanMove(hit))
                    {
                        _dragMode = DragMode.Move;
                        _dragMouseStart = local;
                        _dragNodeStart = new Vector2I(GetNodeX(hit), GetNodeY(hit));
                    }
                    _canvasArea.AcceptEvent();
                }
                else
                {
                    // Click on empty area → select the canvas itself.
                    if (_selectedCanvas != null) SelectInEditor(_selectedCanvas);
                    _canvasArea.AcceptEvent();
                }
            }
            else
            {
                if (_dragMode != DragMode.None)
                {
                    _dragMode = DragMode.None;
                    _canvasArea.AcceptEvent();
                }
            }
        }
        else if (ev is InputEventMouseMotion mm && _dragMode != DragMode.None && _selectedNode != null)
        {
            Vector2 delta = mm.Position - _dragMouseStart;
            int dxPx = (int)(delta.X / z);
            int dyPx = (int)(delta.Y / z);

            if (_dragMode == DragMode.Move)
            {
                SetNodeXY(_selectedNode, _dragNodeStart.X + dxPx, _dragNodeStart.Y + dyPx);
            }
            else if (_dragMode == DragMode.ResizeBR)
            {
                int newW = Mathf.Max(1, _dragNodeSizeStart.X + dxPx);
                int newH = Mathf.Max(1, _dragNodeSizeStart.Y + dyPx);
                SetNodeSize(_selectedNode, newW, newH);
            }
            _canvasArea.QueueRedraw();
            _canvasArea.AcceptEvent();
        }
    }

    private void SelectInEditor(Node n)
    {
        var sel = EditorInterface.Singleton.GetSelection();
        sel.Clear();
        sel.AddNode(n);
        _selectedNode = n;
        RefreshHeader();
    }

    // Which widgets expose authored X/Y (drag-to-move operates on them).
    // PS1UIElement ignores its X/Y when nested in a container (the
    // container picks the slot position), so drag-to-move only touches
    // DIRECT children of PS1UICanvas or containers that have their own
    // X/Y. PS1UISpacer has no position.
    private static bool CanMove(Node n) => n switch
    {
        PS1UIElement el => el.GetParent() is PS1UICanvas,
        PS1UIHBox    or PS1UIVBox    or
        PS1UIOverlay or PS1UISizeBox or PS1UIModel => true,
        _ => false,
    };

    private static int GetNodeX(Node n) => n switch
    {
        PS1UIElement el => el.X,
        PS1UIHBox hb    => hb.X,
        PS1UIVBox vb    => vb.X,
        PS1UIOverlay ov => ov.X,
        PS1UISizeBox sb => sb.X,
        PS1UIModel mdl  => mdl.X,
        _ => 0,
    };

    private static int GetNodeY(Node n) => n switch
    {
        PS1UIElement el => el.Y,
        PS1UIHBox hb    => hb.Y,
        PS1UIVBox vb    => vb.Y,
        PS1UIOverlay ov => ov.Y,
        PS1UISizeBox sb => sb.Y,
        PS1UIModel mdl  => mdl.Y,
        _ => 0,
    };

    private static int GetNodeWidth(Node n) => n switch
    {
        PS1UIElement el => el.Width,
        PS1UIHBox hb    => hb.Width,
        PS1UIVBox vb    => vb.Width,
        PS1UIOverlay ov => ov.Width,
        PS1UISizeBox sb => sb.WidthOverride >= 0 ? sb.WidthOverride : 64,
        PS1UIModel mdl  => mdl.Width,
        _ => 0,
    };

    private static int GetNodeHeight(Node n) => n switch
    {
        PS1UIElement el => el.Height,
        PS1UIHBox hb    => hb.Height,
        PS1UIVBox vb    => vb.Height,
        PS1UIOverlay ov => ov.Height,
        PS1UISizeBox sb => sb.HeightOverride >= 0 ? sb.HeightOverride : 32,
        PS1UIModel mdl  => mdl.Height,
        _ => 0,
    };

    private static void SetNodeXY(Node n, int x, int y)
    {
        switch (n)
        {
            case PS1UIElement el: el.X = x; el.Y = y; break;
            case PS1UIHBox hb:    hb.X = x; hb.Y = y; break;
            case PS1UIVBox vb:    vb.X = x; vb.Y = y; break;
            case PS1UIOverlay ov: ov.X = x; ov.Y = y; break;
            case PS1UISizeBox sb: sb.X = x; sb.Y = y; break;
            case PS1UIModel mdl:  mdl.X = x; mdl.Y = y; break;
        }
    }

    private static void SetNodeSize(Node n, int w, int h)
    {
        switch (n)
        {
            case PS1UIElement el: el.Width = w; el.Height = h; break;
            case PS1UIHBox hb:    hb.Width = w; hb.Height = h; break;
            case PS1UIVBox vb:    vb.Width = w; vb.Height = h; break;
            case PS1UIOverlay ov: ov.Width = w; ov.Height = h; break;
            case PS1UISizeBox sb: sb.WidthOverride = w; sb.HeightOverride = h; break;
            case PS1UIModel mdl:  mdl.Width = w; mdl.Height = h; break;
        }
    }

    // ─── Add-node dropdown ─────────────────────────────────────────

    private enum AddKind
    {
        TextElement = 1, BoxElement,
        HBox, VBox, SizeBox, Spacer, Overlay,
        UIModel,
    }

    private void OnAddNodeRequested(long id)
    {
        if (_selectedCanvas == null)
        {
            GD.PushWarning("[PS1Godot] Select a PS1UICanvas (or an element inside one) first.");
            return;
        }

        // Parent is the selected container; fall back to the canvas itself.
        Node parent = _selectedNode is PS1UIHBox or PS1UIVBox or PS1UIOverlay or PS1UISizeBox
            ? _selectedNode
            : _selectedCanvas;

        Node created = (AddKind)id switch
        {
            AddKind.TextElement => new PS1UIElement { ElementName = "NewText", Type = PS1UIElementType.Text, Text = "Text" },
            AddKind.BoxElement  => new PS1UIElement { ElementName = "NewBox",  Type = PS1UIElementType.Box, Width = 64, Height = 16 },
            AddKind.HBox        => new PS1UIHBox    { ContainerName = "NewHBox" },
            AddKind.VBox        => new PS1UIVBox    { ContainerName = "NewVBox" },
            AddKind.SizeBox     => new PS1UISizeBox { ContainerName = "NewSizeBox" },
            AddKind.Spacer      => new PS1UISpacer  { },
            AddKind.Overlay     => new PS1UIOverlay { ContainerName = "NewOverlay" },
            AddKind.UIModel     => new PS1UIModel   { ModelName = "NewModel" },
            _                   => new PS1UIElement { ElementName = "NewElement" },
        };

        parent.AddChild(created);
        // Transfer ownership to the edited scene root so the new node
        // survives save / reload.
        var sceneRoot = EditorInterface.Singleton.GetEditedSceneRoot();
        if (sceneRoot != null) created.Owner = sceneRoot;

        SelectInEditor(created);
        _canvasArea?.QueueRedraw();
    }

    // Mirrors Exporter.SceneCollector.ResolveElementColor.
    private static Color ResolveElementColor(PS1UIElement el)
    {
        if (el.ThemeSlot == PS1UIThemeSlot.Custom) return el.Color;

        // Walk up the tree to the owning canvas (may be through
        // nested containers in the new model).
        PS1UICanvas? canvas = null;
        Node? walker = el.GetParent();
        while (walker != null && canvas == null)
        {
            if (walker is PS1UICanvas c) { canvas = c; break; }
            walker = walker.GetParent();
        }
        if (canvas?.Theme is null) return el.Color;
        var t = canvas.Theme;
        return el.ThemeSlot switch
        {
            PS1UIThemeSlot.Text      => t.TextColor,
            PS1UIThemeSlot.Accent    => t.AccentColor,
            PS1UIThemeSlot.Bg        => t.BgColor,
            PS1UIThemeSlot.BgBorder  => t.BgBorderColor,
            PS1UIThemeSlot.Highlight => t.HighlightColor,
            PS1UIThemeSlot.Warning   => t.WarningColor,
            PS1UIThemeSlot.Danger    => t.DangerColor,
            PS1UIThemeSlot.Neutral   => t.NeutralColor,
            _ => el.Color,
        };
    }

    // Periodic redraw catches inspector edits.
    private double _redrawAccum;
    public override void _Process(double delta)
    {
        _redrawAccum += delta;
        if (_redrawAccum >= 0.1)
        {
            _redrawAccum = 0;
            RefreshHeader();
            _canvasArea?.QueueRedraw();
        }
    }
}
#endif
