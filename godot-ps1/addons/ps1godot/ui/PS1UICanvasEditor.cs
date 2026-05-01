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

    // Thumbnail strip — fixed size keeps layout predictable across
    // scenes with wildly different canvas counts.
    private const int ThumbWidth  = 96;   // 320 / 96 ≈ 3.33×
    private const int ThumbHeight = 72;   // 240 / 72 ≈ 3.33×

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
    private bool _showLayoutDebug = false;

    // Currently-subscribed theme reference. Tracked so SetSelection
    // can disconnect cleanly before re-subscribing on a different
    // canvas — Resource.Changed leaks the connection if the editor
    // is destroyed while the theme is still referenced elsewhere.
    private PS1Theme? _subscribedTheme;

    private Label? _headerLabel;
    private OptionButton? _zoomCombo;
    private CheckButton? _gridToggle;
    private CheckButton? _layoutDebugToggle;
    private MenuButton? _addMenu;
    private Control? _canvasArea;
    private ScrollContainer? _thumbStrip;
    private HBoxContainer? _thumbRow;

    // Drag state — populated on LMB down over an element, cleared on release.
    private enum DragMode { None, Move, ResizeBR }
    private DragMode _dragMode = DragMode.None;
    private Vector2 _dragMouseStart;
    private Vector2I _dragNodeStart;
    private Vector2I _dragNodeSizeStart;

    // One SubViewport per PS1UIModel. Each holds a Camera3D and a
    // duplicate of the node's Target mesh; the viewport auto-renders
    // (UpdateMode.Always) and its texture is drawn into the preview
    // rect, so authors see the actual model they're positioning.
    private sealed class ModelPreview
    {
        public SubViewport Viewport = null!;
        public Camera3D Camera = null!;
        public Node3D ModelRoot = null!;
        public string LastTargetPath = "";
    }
    private readonly System.Collections.Generic.Dictionary<PS1UIModel, ModelPreview> _modelPreviews = new();

    public PS1UICanvasEditor()
    {
        Name = "PS1 UI";
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 6);
        BuildUI();
    }

    public override void _Ready()
    {
        // Catch the in-place "swap the canvas's Theme reference" case —
        // the canvas itself doesn't emit a per-property signal, so we
        // listen for any inspector edit named Theme/theme and re-hook.
        var inspector = EditorInterface.Singleton?.GetInspector();
        if (inspector != null) inspector.PropertyEdited += OnInspectorPropertyEdited;
    }

    private void OnInspectorPropertyEdited(string property)
    {
        if (property == "Theme" || property == "theme")
            RewireThemeSubscription();
    }

    // Called by PS1GodotPlugin whenever the editor selection changes.
    // The plugin passes the owning canvas (derived by walking up from
    // any selected PS1UI* node); `node` is the exact selected node.
    public void SetSelection(PS1UICanvas? canvas, Node? node)
    {
        bool changed = _selectedCanvas != canvas || _selectedNode != node;
        bool canvasSwitched = _selectedCanvas != canvas;
        _selectedCanvas = canvas;
        _selectedNode   = node;
        if (canvasSwitched) ClearModelPreviews();
        if (changed)
        {
            RefreshHeader();
            _canvasArea?.QueueRedraw();
        }
        // Always rebuild the strip — covers the canvas-was-added case
        // (selection didn't change but the scene now has more canvases)
        // as well as the highlight-the-newly-selected-canvas case.
        RebuildThumbnailStrip();
        // Live theme preview: subscribe to the new canvas's Theme so
        // colour/font edits repaint without a reselect.
        RewireThemeSubscription();
    }

    // ─── Live theme preview ─────────────────────────────────────────
    // Resource.Changed fires whenever any [Export] property of the
    // theme is mutated — including from the inspector. We forward it
    // to the canvas redraw + thumbnail rebuild so swapping a colour
    // updates everything that's using the theme without requiring the
    // author to reselect the canvas.

    private void RewireThemeSubscription()
    {
        var newTheme = _selectedCanvas?.Theme;
        if (newTheme == _subscribedTheme) return;

        if (_subscribedTheme != null && IsInstanceValid(_subscribedTheme))
            _subscribedTheme.Changed -= OnThemeChanged;

        _subscribedTheme = newTheme;
        if (_subscribedTheme != null)
            _subscribedTheme.Changed += OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        _canvasArea?.QueueRedraw();
        // Thumbnails currently render shape-only, not theme-colored,
        // but a rebuild future-proofs against a richer thumb later.
        if (_thumbRow != null && _thumbRow.GetChildCount() > 0)
        {
            foreach (var c in _thumbRow.GetChildren())
                if (c is Control ctl) ctl.QueueRedraw();
        }
    }

    public override void _ExitTree()
    {
        ClearModelPreviews();
        if (_subscribedTheme != null && IsInstanceValid(_subscribedTheme))
        {
            _subscribedTheme.Changed -= OnThemeChanged;
            _subscribedTheme = null;
        }
        var inspector = EditorInterface.Singleton?.GetInspector();
        if (inspector != null) inspector.PropertyEdited -= OnInspectorPropertyEdited;
        base._ExitTree();
    }

    // ─── Thumbnail strip ────────────────────────────────────────────
    // Rebuilds the row of canvas miniatures from a fresh scene scan.
    // Cheap (one Control + a few DrawRect calls per canvas).

    private void RebuildThumbnailStrip()
    {
        if (_thumbRow == null) return;

        foreach (var c in _thumbRow.GetChildren()) c.QueueFree();

        var canvases = ScanSceneCanvases();
        if (canvases.Count == 0)
        {
            var hint = new Label
            {
                Text = "(no PS1UICanvas in scene)",
                VerticalAlignment = VerticalAlignment.Center,
            };
            hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
            _thumbRow.AddChild(hint);
            return;
        }

        foreach (var c in canvases)
        {
            var thumb = new CanvasThumb(c, c == _selectedCanvas);
            thumb.Pressed += () => SetSelection(c, c);
            _thumbRow.AddChild(thumb);
        }
    }

    private static System.Collections.Generic.List<PS1UICanvas> ScanSceneCanvases()
    {
        var result = new System.Collections.Generic.List<PS1UICanvas>();
        var root = EditorInterface.Singleton?.GetEditedSceneRoot();
        if (root == null) return result;
        Walk(root, result);
        return result;

        static void Walk(Node n, System.Collections.Generic.List<PS1UICanvas> acc)
        {
            if (n is PS1UICanvas c) acc.Add(c);
            foreach (var ch in n.GetChildren())
                if (ch is Node child) Walk(child, acc);
        }
    }

    // Single canvas miniature: 96x72 plate + label below. Custom _Draw
    // renders child PS1UIElement boxes scaled into the plate so the
    // author can recognise which canvas is which by layout shape, not
    // just by name.
    private sealed partial class CanvasThumb : VBoxContainer
    {
        [Signal] public delegate void PressedEventHandler();

        private readonly PS1UICanvas _canvas;
        private readonly bool _selected;
        private Control _plate = null!;

        public CanvasThumb(PS1UICanvas canvas, bool selected)
        {
            _canvas   = canvas;
            _selected = selected;
            CustomMinimumSize = new Vector2(ThumbWidth + 4, 0);
            MouseFilter = MouseFilterEnum.Pass;
        }

        public override void _Ready()
        {
            _plate = new Control
            {
                CustomMinimumSize = new Vector2(ThumbWidth, ThumbHeight),
                MouseFilter       = MouseFilterEnum.Stop,
            };
            _plate.Draw += DrawPlate;
            _plate.GuiInput += OnPlateInput;
            AddChild(_plate);

            string name = string.IsNullOrEmpty(_canvas.CanvasName)
                ? _canvas.Name
                : _canvas.CanvasName;
            var label = new Label
            {
                Text = name,
                HorizontalAlignment = HorizontalAlignment.Center,
                ClipText = true,
                CustomMinimumSize = new Vector2(ThumbWidth, 0),
            };
            label.AddThemeFontSizeOverride("font_size", 10);
            if (_selected)
                label.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
            AddChild(label);
        }

        private void OnPlateInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                EmitSignal(SignalName.Pressed);
                _plate.AcceptEvent();
            }
        }

        private void DrawPlate()
        {
            var rect = new Rect2(0, 0, ThumbWidth, ThumbHeight);

            // Background — selected canvas gets a subtly red tint so the
            // current pick reads at a glance even on monochrome themes.
            _plate.DrawRect(rect, _selected
                ? new Color(0.18f, 0.10f, 0.10f)
                : new Color(0.10f, 0.10f, 0.13f));

            // Border — accent-red when selected, neutral otherwise.
            var borderColor = _selected
                ? new Color(0.95f, 0.30f, 0.30f, 1f)
                : new Color(0.40f, 0.40f, 0.45f, 0.8f);
            _plate.DrawRect(rect, borderColor, filled: false, width: _selected ? 2f : 1f);

            // Element rectangles — walk PS1UIElement children, draw each
            // at scaled X/Y/W/H. Container hierarchies (HBox/VBox) just
            // recurse one level since their own X/Y is enough to suggest
            // layout.
            float sx = (float)ThumbWidth  / PsxWidth;
            float sy = (float)ThumbHeight / PsxHeight;
            DrawElementRecursive(_canvas, sx, sy);
        }

        private void DrawElementRecursive(Node parent, float sx, float sy)
        {
            foreach (var child in parent.GetChildren())
            {
                if (child is PS1UIElement el)
                {
                    if (el.Width <= 0 || el.Height <= 0) continue;
                    var r = new Rect2(el.X * sx, el.Y * sy, el.Width * sx, el.Height * sy);
                    Color fill = el.Type switch
                    {
                        PS1UIElementType.Image => new Color(0.30f, 0.55f, 0.85f, 0.7f),
                        PS1UIElementType.Text  => new Color(0.85f, 0.85f, 0.40f, 0.85f),
                        PS1UIElementType.Box   => new Color(0.50f, 0.85f, 0.50f, 0.5f),
                        _                       => new Color(0.7f,  0.7f,  0.7f,  0.5f),
                    };
                    _plate.DrawRect(r, fill);
                    _plate.DrawRect(r, new Color(0f, 0f, 0f, 0.4f), filled: false);
                }
                if (child is Node cn) DrawElementRecursive(cn, sx, sy);
            }
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
        _addMenu = new MenuButton
        {
            Text = "+ Add",
            TooltipText = "Add a UI element or container under the selected node " +
                          "(or under the canvas if nothing is selected). Boxes / Spacer participate " +
                          "in flexbox-style layout; raw Text/Box elements use anchor-based placement.",
        };
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

        _zoomCombo = new OptionButton
        {
            TooltipText = "Preview scale. The canvas itself is always 320×240 (PSX framebuffer); " +
                          "this only affects how big it draws in the editor.",
        };
        _zoomCombo.AddItem("1×", 1);
        _zoomCombo.AddItem("2×", 2);
        _zoomCombo.AddItem("3×", 3);
        _zoomCombo.AddItem("4×", 4);
        _zoomCombo.Select(1);
        _zoomCombo.ItemSelected += OnZoomChanged;
        toolbar.AddChild(_zoomCombo);

        _gridToggle = new CheckButton
        {
            Text = "Grid",
            ButtonPressed = _showGrid,
            TooltipText = "Overlay an 8-pixel grid. PSX text fonts are 8 px tall, so snapping " +
                          "elements to multiples of 8 keeps them pixel-aligned.",
        };
        _gridToggle.Toggled += OnGridToggled;
        toolbar.AddChild(_gridToggle);

        _layoutDebugToggle = new CheckButton
        {
            Text = "Layout",
            ButtonPressed = _showLayoutDebug,
            TooltipText = "Highlight every container (HBox/VBox/Overlay/SizeBox) with a brighter " +
                          "dashed border + a corner label showing its dimensions and flex params " +
                          "(e.g. 'HBox 240×80 sp=8'). Use when a child isn't where you expected — " +
                          "the label tells you what the layout resolver is actually using.",
        };
        _layoutDebugToggle.Toggled += pressed =>
        {
            _showLayoutDebug = pressed;
            _canvasArea?.QueueRedraw();
        };
        toolbar.AddChild(_layoutDebugToggle);

        toolbar.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });

        // ── Thumbnail strip ─────────────────────────────────────────
        // One miniature per PS1UICanvas in the active scene. Click a
        // thumb to swap selection without going through the scene
        // tree — useful when a scene has 5+ canvases (HUD, menu, fade,
        // background plates) and the tree is busy.
        _thumbStrip = new ScrollContainer
        {
            HorizontalScrollMode  = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode    = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize     = new Vector2(0, ThumbHeight + 28),
            SizeFlagsHorizontal   = SizeFlags.ExpandFill,
        };
        AddChild(_thumbStrip);

        _thumbRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _thumbRow.AddThemeConstantOverride("separation", 6);
        _thumbStrip.AddChild(_thumbRow);

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

        // 4. Optional layout debug overlay — bright orange container
        //    annotations on top of everything so they're never hidden.
        if (_showLayoutDebug)
        {
            DrawLayoutDebugOverlay(_selectedCanvas, z);
        }
    }

    // Container debug overlay: brighter dashed outline + corner label
    // with dimensions and any container-specific flex params (Spacing,
    // Width/HeightOverride). Call after the standard outlines so the
    // annotation paints on top.
    private void DrawLayoutDebugOverlay(Node subtreeRoot, int z)
    {
        var debugColor = new Color(1.0f, 0.55f, 0.10f, 0.95f);
        var labelBg    = new Color(0f, 0f, 0f, 0.65f);
        var font       = ThemeDB.FallbackFont;
        int fs         = Mathf.Max(9, 9 * z / 2);

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
                _ => ((int, int, int, int)?)null,
            };
            if (rect is (int rx, int ry, int rw, int rh))
            {
                DrawDashedRect(new Rect2(rx * z, ry * z, rw * z, rh * z), debugColor, dash: 4f);

                string label = BuildDebugLabel(child, rw, rh);
                var textSize = font.GetStringSize(label, HorizontalAlignment.Left, -1, fs);
                var labelPos = new Vector2(rx * z + 2, ry * z + textSize.Y);
                _canvasArea!.DrawRect(
                    new Rect2(labelPos.X - 2, labelPos.Y - textSize.Y, textSize.X + 4, textSize.Y + 2),
                    labelBg);
                _canvasArea.DrawString(font, labelPos, label,
                    HorizontalAlignment.Left, -1, fs, debugColor);

                DrawLayoutDebugOverlay(child, z);
            }
        }
    }

    private static string BuildDebugLabel(Node n, int w, int h) => n switch
    {
        PS1UIHBox hb    => $"HBox {w}×{h} sp={hb.Spacing}",
        PS1UIVBox vb    => $"VBox {w}×{h} sp={vb.Spacing}",
        PS1UIOverlay    => $"Overlay {w}×{h}",
        PS1UISizeBox sb => $"SizeBox {w}×{h}" +
                            (sb.WidthOverride  >= 0 ? $" w={sb.WidthOverride}"  : "") +
                            (sb.HeightOverride >= 0 ? $" h={sb.HeightOverride}" : ""),
        _               => $"{n.GetType().Name} {w}×{h}",
    };

    // Manual dashed rectangle — Godot's DrawRect(filled:false) only does
    // solid borders. 4-px dash + 2-px gap reads well at all zoom levels.
    private void DrawDashedRect(Rect2 r, Color color, float dash)
    {
        if (_canvasArea == null) return;
        DrawDashedSegment(new Vector2(r.Position.X,             r.Position.Y),
                          new Vector2(r.Position.X + r.Size.X,  r.Position.Y),             color, dash);
        DrawDashedSegment(new Vector2(r.Position.X + r.Size.X,  r.Position.Y),
                          new Vector2(r.Position.X + r.Size.X,  r.Position.Y + r.Size.Y),  color, dash);
        DrawDashedSegment(new Vector2(r.Position.X + r.Size.X,  r.Position.Y + r.Size.Y),
                          new Vector2(r.Position.X,             r.Position.Y + r.Size.Y),  color, dash);
        DrawDashedSegment(new Vector2(r.Position.X,             r.Position.Y + r.Size.Y),
                          new Vector2(r.Position.X,             r.Position.Y),             color, dash);
    }

    private void DrawDashedSegment(Vector2 a, Vector2 b, Color color, float dash)
    {
        if (_canvasArea == null) return;
        var dir = b - a;
        float len = dir.Length();
        if (len < 0.5f) return;
        var unit = dir / len;
        float gap = dash * 0.5f;
        float t = 0;
        while (t < len)
        {
            float t2 = Mathf.Min(t + dash, len);
            _canvasArea.DrawLine(a + unit * t, a + unit * t2, color, width: 1.5f);
            t = t2 + gap;
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
                var rectPx = new Rect2(rx * z, ry * z, rw * z, rh * z);

                // For PS1UIModel, render the actual target mesh inside
                // the rect via a per-node SubViewport. Drawn first so
                // the outline stays on top.
                if (child is PS1UIModel mdlPreview && rw > 0 && rh > 0)
                {
                    var prev = EnsureModelPreview(mdlPreview, (int)rectPx.Size.X, (int)rectPx.Size.Y);
                    var tex = prev.Viewport.GetTexture();
                    if (tex != null)
                        _canvasArea!.DrawTextureRect(tex, rectPx, tile: false);
                }

                _canvasArea!.DrawRect(rectPx, color, filled: false, width: 1f);

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
                // X/Y are insets from the anchor edge (see PS1UIAnchor docstring).
                // For far-side anchors (Right*/Bottom*), "increase inset" means
                // "move away from that edge toward center" which is the OPPOSITE
                // of the mouse direction. Flip the delta so drag follows cursor.
                var (effDx, effDy) = AnchorAdjustDelta(GetNodeAnchor(_selectedNode), dxPx, dyPx);
                SetNodeXY(_selectedNode, _dragNodeStart.X + effDx, _dragNodeStart.Y + effDy);
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

    private static PS1UIAnchor GetNodeAnchor(Node n) => n switch
    {
        PS1UIElement el => el.Anchor,
        PS1UIHBox hb    => hb.Anchor,
        PS1UIVBox vb    => vb.Anchor,
        PS1UIOverlay ov => ov.Anchor,
        PS1UISizeBox sb => sb.Anchor,
        PS1UIModel mdl  => mdl.Anchor,
        _               => PS1UIAnchor.Custom,
    };

    // Lazy-build the SubViewport + Camera3D + duplicated target mesh for
    // `mdl`. Reuses the viewport across redraws; only rebuilds the mesh
    // when Target changes. Size tracks the rect pixel extent so the
    // rendered preview is sharp at the current zoom. Camera is updated
    // on every call so OrbitYaw/Pitch/Distance edits reflect immediately.
    private ModelPreview EnsureModelPreview(PS1UIModel mdl, int pixelW, int pixelH)
    {
        if (!_modelPreviews.TryGetValue(mdl, out var prev))
        {
            prev = new ModelPreview
            {
                Viewport = new SubViewport
                {
                    Disable3D = false,
                    TransparentBg = true,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                    Size = new Vector2I(Mathf.Max(1, pixelW), Mathf.Max(1, pixelH)),
                    OwnWorld3D = true,
                },
                ModelRoot = new Node3D(),
                Camera = new Camera3D { Current = true, Fov = 40f, Near = 0.05f, Far = 1000f },
            };
            prev.Viewport.AddChild(prev.ModelRoot);
            prev.Viewport.AddChild(prev.Camera);
            AddChild(prev.Viewport);
            _modelPreviews[mdl] = prev;
        }

        var desiredSize = new Vector2I(Mathf.Max(1, pixelW), Mathf.Max(1, pixelH));
        if (prev.Viewport.Size != desiredSize) prev.Viewport.Size = desiredSize;

        // Rebuild the mesh whenever the Target NodePath changes (including
        // the first time). GetNodeOrNull is relative to mdl; we also fall
        // back to the edited scene root so absolute paths work.
        string targetStr = mdl.Target.ToString();
        if (targetStr != prev.LastTargetPath)
        {
            foreach (var c in prev.ModelRoot.GetChildren()) c.QueueFree();
            Node? tgt = mdl.GetNodeOrNull(mdl.Target);
            if (tgt == null)
            {
                var root = GetTree()?.EditedSceneRoot;
                if (root != null) tgt = root.GetNodeOrNull(mdl.Target);
            }
            if (tgt is Node3D n3d)
            {
                var dup = (Node3D)n3d.Duplicate((int)DuplicateFlags.UseInstantiation);
                dup.Position = Vector3.Zero;
                dup.Rotation = Vector3.Zero;
                dup.Visible = true;
                prev.ModelRoot.AddChild(dup);
            }
            prev.LastTargetPath = targetStr;
        }

        // Orbit: camera sits at yaw(Y) * pitch(X) * (0,0,distance) and
        // looks at the model origin. Mirrors the runtime setup in
        // renderer.cpp:renderUIModels so the preview matches PSX framing.
        float yaw = Mathf.DegToRad(mdl.OrbitYawDegrees);
        float pitch = Mathf.DegToRad(mdl.OrbitPitchDegrees);
        float dist = Mathf.Max(0.01f, mdl.OrbitDistance);
        var orbit = Basis.FromEuler(new Vector3(pitch, yaw, 0f));
        var camPos = orbit * new Vector3(0f, 0f, dist);
        prev.Camera.Transform = new Transform3D(Basis.Identity, camPos).LookingAt(Vector3.Zero, Vector3.Up);
        return prev;
    }

    // Free all preview viewports — called when the selected canvas
    // changes or the dock exits, to avoid leaking SubViewports of models
    // that are no longer visible.
    private void ClearModelPreviews()
    {
        foreach (var kv in _modelPreviews)
        {
            if (GodotObject.IsInstanceValid(kv.Value.Viewport))
                kv.Value.Viewport.QueueFree();
        }
        _modelPreviews.Clear();
    }

    // Flip the mouse delta on any axis whose inset runs opposite the screen
    // direction. Right* anchors: X counts leftward from the right edge, so
    // dragging right (dxPx > 0) needs to DECREASE X. Bottom* anchors: Y
    // counts upward from the bottom edge, so dragging down (dyPx > 0) needs
    // to DECREASE Y.
    private static (int dx, int dy) AnchorAdjustDelta(PS1UIAnchor anchor, int dxPx, int dyPx)
    {
        int ex = dxPx, ey = dyPx;
        if (anchor is PS1UIAnchor.TopRight or PS1UIAnchor.CenterRight or PS1UIAnchor.BottomRight)
            ex = -dxPx;
        if (anchor is PS1UIAnchor.BottomLeft or PS1UIAnchor.BottomCenter or PS1UIAnchor.BottomRight)
            ey = -dyPx;
        return (ex, ey);
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
