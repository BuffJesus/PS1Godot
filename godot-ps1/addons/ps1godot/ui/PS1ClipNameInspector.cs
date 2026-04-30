#if TOOLS
using Godot;
using System.Collections.Generic;

namespace PS1Godot.UI;

// Replaces the bare LineEdit on a few PS1AudioClip-name string slots with
// an OptionButton populated from the active scene's PS1Scene.AudioClips.
// Mistyped names were a common authoring footgun (the exporter reports
// the unknown clip, but only after F5) — picking from a list closes that
// loop at edit time.
//
// Hooks: PS1MusicChannel.AudioClipName, PS1SampleRegion.AudioClipName,
// PS1SoundMacroEvent.AudioClipName.
//
// Array-of-string targets (PS1SoundFamily.AudioClipNames,
// PS1DrumKit.AudioClipNames) are deferred to the dedicated custom
// inspectors that own the whole resource (Phase 2 #15 / #16). Hooking
// individual array elements via EditorInspectorPlugin requires an
// element-aware path that the bigger custom inspector subsumes anyway.
public partial class PS1ClipNameInspector : EditorInspectorPlugin
{
    public override bool _CanHandle(GodotObject obj)
    {
        return obj is PS1MusicChannel or PS1SampleRegion or PS1SoundMacroEvent;
    }

    public override bool _ParseProperty(
        GodotObject @object,
        Variant.Type type,
        string name,
        PropertyHint hintType,
        string hintString,
        PropertyUsageFlags usageFlags,
        bool wide)
    {
        if (type != Variant.Type.String) return false;

        // Godot 4 .NET converts C# PascalCase to snake_case in the property
        // table for the inspector. Accept both forms so the hook is robust
        // against any future tweak in how Mono surfaces names.
        if (name != "AudioClipName" && name != "audio_clip_name") return false;

        AddPropertyEditor(name, new PS1ClipNameEditor());
        return true; // suppress the default LineEdit
    }
}

// Custom EditorProperty rendered in place of the LineEdit. Owns an
// OptionButton populated from the scene's clip names (with a "(none)"
// slot, an "(not in scene)" annotation when the current value is an
// orphan, and a "Custom…" item that drops to a free-text field for
// authors who want to type a name that isn't yet in the scene's
// AudioClips).
public partial class PS1ClipNameEditor : EditorProperty
{
    private const string EmptyLabel  = "(none)";
    private const string CustomLabel = "Custom…";  // "Custom…"
    private const string CustomTag   = "__custom__";

    private OptionButton _option = null!;
    private LineEdit     _custom = null!;
    private bool         _suppressUpdate;

    public PS1ClipNameEditor()
    {
        var hbox = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(hbox);

        _option = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _option.ItemSelected += OnItemSelected;
        // Refresh the menu just before it pops so a clip added to the
        // scene since the inspector first rendered shows up without
        // requiring a re-select.
        _option.GetPopup().AboutToPopup += () =>
        {
            string current = ReadCurrent();
            RebuildOptions(current);
        };
        hbox.AddChild(_option);

        _custom = new LineEdit
        {
            PlaceholderText      = "Type a clip name…",
            SizeFlagsHorizontal  = SizeFlags.ExpandFill,
            Visible              = false,
        };
        _custom.TextSubmitted += OnCustomSubmitted;
        _custom.FocusExited   += () => OnCustomSubmitted(_custom.Text);
        hbox.AddChild(_custom);

        AddFocusable(_option);
    }

    public override void _UpdateProperty()
    {
        if (_suppressUpdate) return;
        RebuildOptions(ReadCurrent());
    }

    private string ReadCurrent()
    {
        var v = GetEditedObject().Get(GetEditedProperty());
        return v.AsString() ?? "";
    }

    private void RebuildOptions(string current)
    {
        _option.Clear();

        var names = ScanSceneClipNames();

        // Slot 0 — clear the field. "(none)" maps to empty string.
        _option.AddItem(EmptyLabel);
        _option.SetItemMetadata(0, "");

        int matchIdx = string.IsNullOrEmpty(current) ? 0 : -1;

        foreach (var n in names)
        {
            int idx = _option.ItemCount;
            _option.AddItem(n);
            _option.SetItemMetadata(idx, n);
            if (n == current) matchIdx = idx;
        }

        // Orphan annotation: current value isn't in the scene's clips.
        // Common when the clip was renamed/deleted; surface it visibly so
        // the author sees the bad reference at edit time, not export time.
        if (matchIdx < 0 && !string.IsNullOrEmpty(current))
        {
            int idx = _option.ItemCount;
            _option.AddItem($"{current}  (not in scene)");
            _option.SetItemMetadata(idx, current);
            matchIdx = idx;
        }

        _option.AddSeparator();
        int customIdx = _option.ItemCount;
        _option.AddItem(CustomLabel);
        _option.SetItemMetadata(customIdx, CustomTag);

        _option.Selected = matchIdx >= 0 ? matchIdx : 0;
        _custom.Visible  = false;
    }

    private void OnItemSelected(long index)
    {
        var meta = _option.GetItemMetadata((int)index);
        string val = meta.AsString() ?? "";
        if (val == CustomTag)
        {
            // Drop to free-text. _UpdateProperty fires after EmitChanged
            // and would clobber the half-typed text — guard with the flag.
            _suppressUpdate = true;
            _custom.Text    = ReadCurrent();
            _custom.Visible = true;
            _custom.GrabFocus();
            _custom.SelectAll();
            _suppressUpdate = false;
            return;
        }
        EmitChanged(GetEditedProperty(), val);
    }

    private void OnCustomSubmitted(string text)
    {
        _custom.Visible = false;
        EmitChanged(GetEditedProperty(), text ?? "");
    }

    // Walk the active scene tree to find the PS1Scene root, then collect
    // each clip's effective name (mirrors the exporter's resolution at
    // SceneCollector.cs:2001 — authored ClipName wins, otherwise fall back
    // to the AudioStream's resource basename).
    private static List<string> ScanSceneClipNames()
    {
        var result = new List<string>();
        var root = EditorInterface.Singleton?.GetEditedSceneRoot();
        if (root == null) return result;

        var ps1Scene = FindPS1Scene(root);
        if (ps1Scene?.AudioClips == null) return result;

        foreach (var clip in ps1Scene.AudioClips)
        {
            if (clip == null) continue;
            string n = !string.IsNullOrWhiteSpace(clip.ClipName)
                ? clip.ClipName
                : (!string.IsNullOrEmpty(clip.Stream?.ResourcePath)
                    ? System.IO.Path.GetFileNameWithoutExtension(clip.Stream.ResourcePath)
                    : "");
            if (!string.IsNullOrEmpty(n)) result.Add(n);
        }
        result.Sort(System.StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static PS1Scene? FindPS1Scene(Node n)
    {
        if (n is PS1Scene s) return s;
        foreach (var c in n.GetChildren())
        {
            if (c is Node child)
            {
                var found = FindPS1Scene(child);
                if (found != null) return found;
            }
        }
        return null;
    }
}
#endif
