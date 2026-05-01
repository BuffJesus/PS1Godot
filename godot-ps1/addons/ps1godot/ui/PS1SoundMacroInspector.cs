#if TOOLS
using Godot;
using System.Collections.Generic;

namespace PS1Godot.UI;

// Custom inspector for PS1SoundMacro. Replaces the default
// Array<PS1SoundMacroEvent> editor with a per-event row timeline:
// frame spinbox, scene-aware clip dropdown, volume / pan / pitch
// controls, delete button. Header carries [+ Add Event] and a
// [▶ Audition] button that previews the macro through Godot's
// AudioStreamPlayer at the authored timing/volume/pitch.
//
// Why a custom inspector: Godot's stock array editor surfaces each
// event as a foldout containing the 5 fields. For a 6-event drum
// macro that's 30 collapsed property slots — unscannable. A flat
// row layout fits the whole macro on screen and lets the author
// reason about timing at a glance.
//
// Subsumes the per-element clip-name dropdown that #7 deferred for
// PS1SoundMacroEvent (the row's clip dropdown reuses the same
// ScanSceneClipNames logic).
public partial class PS1SoundMacroInspector : EditorInspectorPlugin
{
    public override bool _CanHandle(GodotObject obj) => obj is PS1SoundMacro;

    public override bool _ParseProperty(
        GodotObject @object,
        Variant.Type type,
        string name,
        PropertyHint hintType,
        string hintString,
        PropertyUsageFlags usageFlags,
        bool wide)
    {
        if (@object is not PS1SoundMacro macro) return false;
        if (type != Variant.Type.Array) return false;
        if (name != "Events" && name != "events") return false;

        // SetBottomEditor inside the EditorProperty's _Ready hands the
        // inner Control the full inspector width so the row layout has
        // room for all five fields without column collapse.
        AddPropertyEditor(name, new PS1SoundMacroEventList(macro));
        return true; // suppress the default array editor
    }
}

public partial class PS1SoundMacroEventList : EditorProperty
{
    private readonly PS1SoundMacro _macro;
    private VBoxContainer _root = null!;
    private Label _countLabel = null!;
    private VBoxContainer _rows = null!;
    private bool _suppressUpdate;

    public PS1SoundMacroEventList(PS1SoundMacro macro)
    {
        _macro = macro;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    // ─── .wav drop target ───────────────────────────────────────────────
    // Drop a .wav from the FileSystem dock anywhere on the events widget
    // to spawn a new event referencing it. Multi-drop creates one event
    // per file at staggered frames so the timeline doesn't pile up at 0.

    public override bool _CanDropData(Vector2 atPosition, Variant data)
        => PS1WavDropHelper.IsWavDrop(data);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var events = _macro.Events ?? new Godot.Collections.Array<PS1SoundMacroEvent>();
        int nextFrame = 0;
        foreach (var e in events)
            if (e != null && e.Frame >= nextFrame) nextFrame = e.Frame + 5;

        bool changed = false;
        foreach (var path in PS1WavDropHelper.ExtractWavPaths(data))
        {
            string name = PS1WavDropHelper.EnsureClipInScene(path);
            if (string.IsNullOrEmpty(name)) continue;
            events.Add(new PS1SoundMacroEvent { Frame = nextFrame, AudioClipName = name });
            nextFrame += 5;
            changed = true;
        }
        if (changed)
        {
            _macro.Events = events;
            _suppressUpdate = true;
            EmitChanged(GetEditedProperty(), events);
            _suppressUpdate = false;
            Rebuild();
        }
    }

    public override void _Ready()
    {
        _root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(_root);
        SetBottomEditor(_root); // give the editor the full inspector width

        // Header bar: count label + add + audition.
        var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _root.AddChild(header);

        _countLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(_countLabel);

        var addBtn = new Button { Text = "+ Add Event" };
        addBtn.Pressed += OnAddEvent;
        header.AddChild(addBtn);

        var auditionBtn = new Button { Text = "▶ Audition" };
        auditionBtn.TooltipText = "Plays the macro through Godot's AudioStreamPlayer at the authored " +
                                   "timing, volume, and pitch. Pan is not auditioned (AudioStreamPlayer " +
                                   "is mono in the editor). Only works for clips already added to the " +
                                   "active PS1Scene.AudioClips.";
        auditionBtn.Pressed += OnAudition;
        header.AddChild(auditionBtn);

        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _root.AddChild(_rows);

        Rebuild();
    }

    public override void _UpdateProperty()
    {
        if (_suppressUpdate) return;
        Rebuild();
    }

    private void Rebuild()
    {
        // Tear down + repopulate. Per-event mutations re-emit the array
        // and trigger _UpdateProperty, so a fresh build stays in sync
        // with whatever Godot's undo/redo or external edits left behind.
        foreach (var c in _rows.GetChildren()) c.QueueFree();

        var events = _macro.Events ?? new Godot.Collections.Array<PS1SoundMacroEvent>();
        _countLabel.Text = $"Events ({events.Count})";

        if (events.Count == 0)
        {
            var hint = new Label
            {
                Text = "(no events — click + Add Event to start)",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
            _rows.AddChild(hint);
            return;
        }

        for (int i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            // Tolerate nulls (Godot inserts a fresh slot as null until the
            // user picks a Resource). Auto-construct on first inspect so
            // the row has something to bind to.
            if (ev == null)
            {
                ev = new PS1SoundMacroEvent();
                events[i] = ev;
            }
            _rows.AddChild(BuildRow(ev, i));
        }
    }

    private Control BuildRow(PS1SoundMacroEvent ev, int index)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

        // Frame
        var frame = new SpinBox
        {
            MinValue = 0,
            MaxValue = 3600,
            Step = 1,
            Value = ev.Frame,
            Suffix = "f",
            CustomMinimumSize = new Vector2(80, 0),
        };
        frame.ValueChanged += v => { ev.Frame = (int)v; NotifyEdited(ev); };
        row.AddChild(frame);

        // Clip dropdown
        var clipBtn = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        BuildClipMenu(clipBtn, ev.AudioClipName);
        clipBtn.GetPopup().AboutToPopup += () => BuildClipMenu(clipBtn, ev.AudioClipName);
        clipBtn.ItemSelected += idx =>
        {
            string val = clipBtn.GetItemMetadata((int)idx).AsString() ?? "";
            ev.AudioClipName = val;
            NotifyEdited(ev);
        };
        row.AddChild(clipBtn);

        // Volume
        var vol = new SpinBox
        {
            MinValue = 0,
            MaxValue = 128,
            Step = 1,
            Value = ev.Volume,
            Prefix = "vol",
            CustomMinimumSize = new Vector2(90, 0),
        };
        vol.ValueChanged += v => { ev.Volume = (int)v; NotifyEdited(ev); };
        row.AddChild(vol);

        // Pan
        var pan = new SpinBox
        {
            MinValue = 0,
            MaxValue = 127,
            Step = 1,
            Value = ev.Pan,
            Prefix = "pan",
            CustomMinimumSize = new Vector2(90, 0),
        };
        pan.ValueChanged += v => { ev.Pan = (int)v; NotifyEdited(ev); };
        row.AddChild(pan);

        // Pitch (semitones)
        var pitch = new SpinBox
        {
            MinValue = -24,
            MaxValue = 24,
            Step = 1,
            Value = ev.PitchOffset,
            Suffix = "st",
            CustomMinimumSize = new Vector2(80, 0),
        };
        pitch.ValueChanged += v => { ev.PitchOffset = (int)v; NotifyEdited(ev); };
        row.AddChild(pitch);

        // Delete
        var del = new Button { Text = "×", TooltipText = "Remove this event" };
        del.Pressed += () => OnRemoveEvent(index);
        row.AddChild(del);

        return row;
    }

    private void BuildClipMenu(OptionButton btn, string current)
    {
        btn.Clear();
        var names = ScanSceneClipNames();

        btn.AddItem("(none)");
        btn.SetItemMetadata(0, "");
        int matchIdx = string.IsNullOrEmpty(current) ? 0 : -1;
        foreach (var n in names)
        {
            int idx = btn.ItemCount;
            btn.AddItem(n);
            btn.SetItemMetadata(idx, n);
            if (n == current) matchIdx = idx;
        }
        if (matchIdx < 0 && !string.IsNullOrEmpty(current))
        {
            int idx = btn.ItemCount;
            btn.AddItem($"{current}  (not in scene)");
            btn.SetItemMetadata(idx, current);
            matchIdx = idx;
        }
        btn.Selected = matchIdx >= 0 ? matchIdx : 0;
    }

    private void OnAddEvent()
    {
        var events = _macro.Events ?? new Godot.Collections.Array<PS1SoundMacroEvent>();
        // Spawn after the highest existing frame so the new slot is
        // visually-distinct and obviously the "next" event.
        int nextFrame = 0;
        foreach (var e in events)
            if (e != null && e.Frame >= nextFrame) nextFrame = e.Frame + 5;

        events.Add(new PS1SoundMacroEvent { Frame = nextFrame });
        _macro.Events = events;
        // EmitChanged tells Godot to mark the resource dirty + re-fire
        // the inspector pipeline. Suppress the immediate _UpdateProperty
        // bounce so we don't lose focus while typing.
        _suppressUpdate = true;
        EmitChanged(GetEditedProperty(), events);
        _suppressUpdate = false;
        Rebuild();
    }

    private void OnRemoveEvent(int index)
    {
        var events = _macro.Events;
        if (events == null || index < 0 || index >= events.Count) return;
        events.RemoveAt(index);
        _suppressUpdate = true;
        EmitChanged(GetEditedProperty(), events);
        _suppressUpdate = false;
        Rebuild();
    }

    // Per-event field changes mutate the resource in place (Godot Array
    // holds Resource references, not values). Notify the inspector +
    // mark the macro dirty so the .tres is saved on Ctrl+S.
    private void NotifyEdited(PS1SoundMacroEvent ev)
    {
        ev.EmitChanged();
        _macro.EmitChanged();
        _countLabel.Text = $"Events ({_macro.Events.Count})";
    }

    // ─── Audition ────────────────────────────────────────────────────────

    private List<AudioStreamPlayer>? _activeAudition;

    private void OnAudition()
    {
        var events = _macro.Events;
        if (events == null || events.Count == 0)
        {
            GD.Print($"[PS1Godot] Audition '{_macro.MacroName}': no events to play.");
            return;
        }

        var clipMap = ScanSceneClipMap();
        if (clipMap.Count == 0)
        {
            GD.PushWarning("[PS1Godot] Audition: active scene has no PS1Scene.AudioClips. " +
                           "Add a PS1AudioClip to the scene's AudioClips array first.");
            return;
        }

        // Stop any in-flight audition so a quick re-press doesn't stack.
        if (_activeAudition != null)
        {
            foreach (var p in _activeAudition)
                if (IsInstanceValid(p)) p.QueueFree();
        }
        _activeAudition = new List<AudioStreamPlayer>();

        // Schedule each event off frame 0 (PS1 fixed 30 Hz clock).
        foreach (var ev in events)
        {
            if (ev == null) continue;
            if (string.IsNullOrEmpty(ev.AudioClipName)) continue;
            if (!clipMap.TryGetValue(ev.AudioClipName, out var clip) || clip?.Stream == null)
            {
                GD.PushWarning($"[PS1Godot] Audition: event @frame {ev.Frame} clip '{ev.AudioClipName}' " +
                               "not found in scene AudioClips — skipped.");
                continue;
            }

            var player = new AudioStreamPlayer
            {
                Stream      = clip.Stream,
                VolumeDb    = LinearToDb(ev.Volume / 128f),
                PitchScale  = Mathf.Pow(2f, ev.PitchOffset / 12f),
            };
            AddChild(player); // EditorProperty is a Node; safe to parent
            _activeAudition.Add(player);

            float delaySec = ev.Frame / 30f;
            // Capture-locally to avoid the foreach-variable capture trap.
            var capturedPlayer = player;
            GetTree().CreateTimer(delaySec).Timeout += () =>
            {
                if (!IsInstanceValid(capturedPlayer)) return;
                capturedPlayer.Play();
                // Free a generous time after start so the sample finishes.
                // 5 s covers most macro samples; long bed clips would need
                // a Finished hook but those don't belong in macros anyway.
                GetTree().CreateTimer(5.0).Timeout += () =>
                {
                    if (IsInstanceValid(capturedPlayer)) capturedPlayer.QueueFree();
                };
            };
        }
        GD.Print($"[PS1Godot] Audition '{_macro.MacroName}': scheduled {_activeAudition.Count} event(s).");
    }

    private static float LinearToDb(float linear)
    {
        if (linear <= 0.0001f) return -80f; // effectively silent
        return Mathf.LinearToDb(linear);
    }

    // ─── Scene helpers (mirror PS1ClipNameInspector) ────────────────────

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

    private static Dictionary<string, PS1AudioClip> ScanSceneClipMap()
    {
        var result = new Dictionary<string, PS1AudioClip>();
        var root = EditorInterface.Singleton?.GetEditedSceneRoot();
        if (root == null) return result;
        var ps1Scene = FindPS1Scene(root);
        if (ps1Scene?.AudioClips == null) return result;
        foreach (var clip in ps1Scene.AudioClips)
        {
            string n = ResolveClipName(clip);
            if (!string.IsNullOrEmpty(n)) result[n] = clip;
        }
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
