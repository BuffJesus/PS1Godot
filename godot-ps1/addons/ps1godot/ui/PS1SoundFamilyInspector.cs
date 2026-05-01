#if TOOLS
using Godot;
using System.Collections.Generic;

namespace PS1Godot.UI;

// Custom inspector for PS1SoundFamily. Two distinct surfaces:
//   1) Replace Array<string> AudioClipNames with a multi-pick widget
//      (chips for selected clips + an "+ Add Variant" dropdown sourced
//      from the active scene's PS1Scene.AudioClips).
//   2) Append a "Jitter Preview" panel after the standard properties
//      that visualises the three jitter ranges (pitch, volume, pan).
//
// Subsumes the per-element AudioClipNames[i] dropdown deferred from
// Phase 1 #7 — the multi-pick widget owns the whole array.
public partial class PS1SoundFamilyInspector : EditorInspectorPlugin
{
    public override bool _CanHandle(GodotObject obj) => obj is PS1SoundFamily;

    public override bool _ParseProperty(
        GodotObject @object,
        Variant.Type type,
        string name,
        PropertyHint hintType,
        string hintString,
        PropertyUsageFlags usageFlags,
        bool wide)
    {
        if (@object is not PS1SoundFamily fam) return false;
        if (type != Variant.Type.Array) return false;
        if (name != "AudioClipNames" && name != "audio_clip_names") return false;

        AddPropertyEditor(name, new PS1SoundFamilyVariantList(fam));
        return true;
    }

    public override void _ParseEnd(GodotObject obj)
    {
        if (obj is not PS1SoundFamily fam) return;
        AddCustomControl(new PS1SoundFamilyJitterPreview(fam));
    }
}

// Multi-pick chips for AudioClipNames. Each picked clip shows as a
// labeled badge with a × delete; an OptionButton at the end lets the
// author add another variant from the scene's clip list.
public partial class PS1SoundFamilyVariantList : EditorProperty
{
    private readonly PS1SoundFamily _fam;
    private VBoxContainer _root = null!;
    private FlowContainer _chips = null!;
    private OptionButton _addBtn = null!;
    private Label _emptyHint = null!;
    private bool _suppressUpdate;

    public PS1SoundFamilyVariantList(PS1SoundFamily fam)
    {
        _fam = fam;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    public override void _Ready()
    {
        _root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(_root);
        SetBottomEditor(_root);

        _chips = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _root.AddChild(_chips);

        _emptyHint = new Label
        {
            Text = "(no variants — add one or more from the scene's PS1Scene.AudioClips)",
        };
        _emptyHint.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
        _root.AddChild(_emptyHint);

        var addRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _root.AddChild(addRow);
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        addRow.AddChild(spacer);

        _addBtn = new OptionButton { Text = "+ Add Variant" };
        _addBtn.GetPopup().AboutToPopup += () => RebuildAddMenu();
        _addBtn.ItemSelected += OnAddVariantPicked;
        addRow.AddChild(_addBtn);

        Rebuild();
    }

    public override void _UpdateProperty()
    {
        if (_suppressUpdate) return;
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var c in _chips.GetChildren()) c.QueueFree();
        var names = _fam.AudioClipNames ?? new Godot.Collections.Array<string>();
        _emptyHint.Visible = names.Count == 0;

        var sceneClips = new HashSet<string>(ScanSceneClipNames());
        for (int i = 0; i < names.Count; i++)
        {
            string n = names[i] ?? "";
            bool inScene = !string.IsNullOrEmpty(n) && sceneClips.Contains(n);
            _chips.AddChild(BuildChip(n, i, orphan: !inScene));
        }
    }

    private Control BuildChip(string clipName, int index, bool orphan)
    {
        // Theme: a flat colored panel with the clip name + an × button.
        // Orphan refs (clip removed from scene since this family was edited)
        // get a yellow-ish tint so they're visibly broken.
        var panel = new PanelContainer();
        var sb = new StyleBoxFlat
        {
            BgColor = orphan ? new Color(0.45f, 0.40f, 0.20f, 0.85f)
                             : new Color(0.20f, 0.30f, 0.45f, 0.85f),
            CornerRadiusBottomLeft  = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft     = 6,
            CornerRadiusTopRight    = 6,
            ContentMarginLeft       = 6,
            ContentMarginRight      = 4,
            ContentMarginTop        = 2,
            ContentMarginBottom     = 2,
        };
        panel.AddThemeStyleboxOverride("panel", sb);

        var hbox = new HBoxContainer();
        panel.AddChild(hbox);

        var label = new Label
        {
            Text = orphan ? $"{clipName} (orphan)" : clipName,
        };
        hbox.AddChild(label);

        var del = new Button
        {
            Text = "×",
            Flat = true,
            TooltipText = "Remove this variant",
        };
        del.Pressed += () => RemoveVariant(index);
        hbox.AddChild(del);

        return panel;
    }

    private void RebuildAddMenu()
    {
        _addBtn.Clear();
        _addBtn.AddItem("+ Add Variant"); // header label, disabled
        _addBtn.SetItemDisabled(0, true);

        var existing = new HashSet<string>(_fam.AudioClipNames ?? new Godot.Collections.Array<string>());
        var sceneClips = ScanSceneClipNames();
        int added = 0;
        foreach (var n in sceneClips)
        {
            if (existing.Contains(n)) continue;
            int idx = _addBtn.ItemCount;
            _addBtn.AddItem(n);
            _addBtn.SetItemMetadata(idx, n);
            added++;
        }
        if (added == 0)
        {
            int idx = _addBtn.ItemCount;
            _addBtn.AddItem("(no more clips available — add to PS1Scene.AudioClips)");
            _addBtn.SetItemDisabled(idx, true);
        }
    }

    private void OnAddVariantPicked(long index)
    {
        var meta = _addBtn.GetItemMetadata((int)index);
        string name = meta.AsString() ?? "";
        if (string.IsNullOrEmpty(name)) return;

        var arr = _fam.AudioClipNames ?? new Godot.Collections.Array<string>();
        arr.Add(name);
        _fam.AudioClipNames = arr;
        EmitFamilyChanged(arr);
    }

    private void RemoveVariant(int index)
    {
        var arr = _fam.AudioClipNames;
        if (arr == null || index < 0 || index >= arr.Count) return;
        arr.RemoveAt(index);
        EmitFamilyChanged(arr);
    }

    private void EmitFamilyChanged(Godot.Collections.Array<string> arr)
    {
        _suppressUpdate = true;
        EmitChanged(GetEditedProperty(), arr);
        _suppressUpdate = false;
        _fam.EmitChanged();
        Rebuild();
    }

    private static List<string> ScanSceneClipNames()
    {
        var result = new List<string>();
        var root = EditorInterface.Singleton?.GetEditedSceneRoot();
        if (root == null) return result;
        var ps1Scene = FindPS1Scene(root);
        if (ps1Scene?.AudioClips == null) return result;
        foreach (var clip in ps1Scene.AudioClips)
        {
            string n = ResolveClipName(clip);
            if (!string.IsNullOrEmpty(n)) result.Add(n);
        }
        result.Sort(System.StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static string ResolveClipName(PS1AudioClip? clip)
    {
        if (clip == null) return "";
        if (!string.IsNullOrWhiteSpace(clip.ClipName)) return clip.ClipName;
        if (!string.IsNullOrEmpty(clip.Stream?.ResourcePath))
            return System.IO.Path.GetFileNameWithoutExtension(clip.Stream.ResourcePath);
        return "";
    }

    private static PS1Scene? FindPS1Scene(Node n)
    {
        if (n is PS1Scene s) return s;
        foreach (var c in n.GetChildren())
            if (c is Node child)
            {
                var found = FindPS1Scene(child);
                if (found != null) return found;
            }
        return null;
    }
}

// "Jitter Preview" panel: three horizontal bars showing the value
// range each jitter setting will sample from. Lives after the standard
// inspector fields (added by PS1SoundFamilyInspector._ParseEnd).
public partial class PS1SoundFamilyJitterPreview : VBoxContainer
{
    private readonly PS1SoundFamily _fam;
    private RangeBar _pitchBar = null!;
    private RangeBar _volBar   = null!;
    private RangeBar _panBar   = null!;

    public PS1SoundFamilyJitterPreview(PS1SoundFamily fam)
    {
        _fam = fam;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 4);
    }

    public override void _Ready()
    {
        AddChild(new HSeparator());

        var header = new Label
        {
            Text = "Jitter Preview",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        header.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f));
        AddChild(header);

        _pitchBar = new RangeBar("Pitch", -12, 12, "st");
        AddChild(_pitchBar);
        _volBar = new RangeBar("Volume", 0, 128, "");
        AddChild(_volBar);
        _panBar = new RangeBar("Pan ±jitter", 0, 32, "");
        AddChild(_panBar);

        // Live refresh: PropertyEdited fires whenever the inspector
        // commits a value. We don't need to filter by property name
        // (cheap to redraw three bars) but skip null lookups gracefully.
        var inspector = EditorInterface.Singleton?.GetInspector();
        if (inspector != null) inspector.PropertyEdited += OnAnyPropertyEdited;

        Refresh();
    }

    public override void _ExitTree()
    {
        var inspector = EditorInterface.Singleton?.GetInspector();
        if (inspector != null) inspector.PropertyEdited -= OnAnyPropertyEdited;
    }

    private void OnAnyPropertyEdited(string property) => Refresh();

    private void Refresh()
    {
        _pitchBar.SetValues(_fam.PitchSemitonesMin, _fam.PitchSemitonesMax);
        _volBar.SetValues(_fam.VolumeMin, _fam.VolumeMax);
        _panBar.SetValues(-_fam.PanJitter, _fam.PanJitter);
    }
}

// Single-line "[label] [────████────]  v0..v1 unit" range display.
// Uses _Draw so the bar reflects the actual range against the full
// possible span — visual proportion communicates "how much variation".
public partial class RangeBar : HBoxContainer
{
    private readonly string _label;
    private readonly int _absMin;
    private readonly int _absMax;
    private readonly string _unit;
    private int _v0;
    private int _v1;
    private Label _labelCtl = null!;
    private RangeBarDraw _draw = null!;
    private Label _valueCtl = null!;

    public RangeBar(string label, int absMin, int absMax, string unit)
    {
        _label  = label;
        _absMin = absMin;
        _absMax = absMax;
        _unit   = unit;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);
    }

    public override void _Ready()
    {
        _labelCtl = new Label { Text = _label, CustomMinimumSize = new Vector2(110, 0) };
        AddChild(_labelCtl);

        _draw = new RangeBarDraw(_absMin, _absMax)
        {
            CustomMinimumSize = new Vector2(160, 14),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        AddChild(_draw);

        _valueCtl = new Label { CustomMinimumSize = new Vector2(120, 0) };
        AddChild(_valueCtl);

        UpdateLabel();
    }

    public void SetValues(int v0, int v1)
    {
        // Tolerate inverted authoring (Min > Max) without flipping the
        // saved values — the runtime would treat that as a degenerate
        // range; visualisation still reads as "swap your min/max".
        _v0 = v0; _v1 = v1;
        if (_draw != null)
        {
            _draw.V0 = v0; _draw.V1 = v1;
            _draw.QueueRedraw();
            UpdateLabel();
        }
    }

    private void UpdateLabel()
    {
        string suf = string.IsNullOrEmpty(_unit) ? "" : _unit;
        if (_v0 == -_v1 && _v0 < 0)
            _valueCtl.Text = $"±{_v1}{suf}";
        else
            _valueCtl.Text = $"{_v0}..{_v1}{suf}";
    }
}

public partial class RangeBarDraw : Control
{
    public int V0;
    public int V1;
    private readonly int _absMin;
    private readonly int _absMax;

    public RangeBarDraw(int absMin, int absMax)
    {
        _absMin = absMin;
        _absMax = absMax;
    }

    public override void _Draw()
    {
        var size = Size;
        if (size.X <= 0 || size.Y <= 0) return;

        // Background track.
        DrawRect(new Rect2(0, size.Y * 0.35f, size.X, size.Y * 0.30f),
                 new Color(0.22f, 0.22f, 0.24f));

        float span = _absMax - _absMin;
        if (span <= 0) return;

        // Center marker (e.g. zero for pitch / pan): a thin tick to
        // anchor the eye when the range crosses zero.
        if (_absMin < 0 && _absMax > 0)
        {
            float zeroX = (-_absMin) / span * size.X;
            DrawRect(new Rect2(zeroX - 0.5f, size.Y * 0.20f, 1f, size.Y * 0.60f),
                     new Color(0.45f, 0.45f, 0.50f));
        }

        // Range fill — clamp + tolerate inverted values gracefully.
        int lo = Mathf.Min(V0, V1);
        int hi = Mathf.Max(V0, V1);
        lo = Mathf.Clamp(lo, _absMin, _absMax);
        hi = Mathf.Clamp(hi, _absMin, _absMax);

        float x0 = (lo - _absMin) / span * size.X;
        float x1 = (hi - _absMin) / span * size.X;
        if (x1 - x0 < 1) x1 = x0 + 1; // single-value range stays visible

        var fill = (V0 == 0 && V1 == 0)
            ? new Color(0.4f, 0.4f, 0.4f, 0.6f)        // grey when no jitter
            : new Color(0.30f, 0.65f, 1.0f, 0.85f);    // accent blue otherwise
        DrawRect(new Rect2(x0, size.Y * 0.30f, x1 - x0, size.Y * 0.40f), fill);
    }
}
#endif
