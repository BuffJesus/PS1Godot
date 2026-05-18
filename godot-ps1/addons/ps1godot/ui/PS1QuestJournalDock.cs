#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;
using PS1Godot.Graph;

namespace PS1Godot.UI;

// PS1 Quest Journal — in-editor quest simulator.
//
// Quest authoring round-trip without this dock: edit .tres → Save →
// F5 → wait for splashpack export + ISO build + PCSX-Redux boot →
// trigger objective completions in-game → observe outcome. ~30
// seconds per iteration, and you have to script the completion
// triggers in Lua first because you can't yet click an objective
// from gameplay.
//
// This dock collapses that loop to: load .tres → click "Complete"
// on any active objective → see active set + outcome update live.
// All in the editor, no PSX cycle.
//
// Slice 1 (D2-4) MVP: load a quest .tres via file dialog, render
// the objectives list with click-to-complete buttons + the current
// outcome (if any). Re-uses `PS1GraphCompiler.RobustLoad` so the
// dock survives the Godot 4.7-dev5 binding quirk that bit PS1Graph.
//
// Future slices: hook the SceneTree picker so the dock surfaces
// every quest .tres in the project (no manual load); show the
// raw compiled .lua side-by-side for debugging; live-reload when
// the .tres saves elsewhere.
public partial class PS1QuestJournalDock : VBoxContainer
{
    private Label?         _pathLabel;
    private VBoxContainer? _objectivesList;
    private Label?         _outcomeLabel;
    private EditorFileDialog? _openDialog;

    // Quest state mirrors the runtime Quest.new helper:
    private PS1GraphResource? _quest;
    private readonly HashSet<string> _completed = new();
    private readonly HashSet<string> _active    = new();

    public PS1QuestJournalDock()
    {
        Name = "PS1 Quest Journal";
        SizeFlagsVertical   = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        BuildUi();
    }

    private void BuildUi()
    {
        AddThemeConstantOverride("separation", 6);

        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 6);
        AddChild(toolbar);

        var loadBtn = new Button { Text = "Load Quest…" };
        loadBtn.Pressed += OnLoadPressed;
        toolbar.AddChild(loadBtn);

        var resetBtn = new Button { Text = "Reset" };
        resetBtn.Pressed += () => { ResetState(); Rebuild(); };
        toolbar.AddChild(resetBtn);

        _pathLabel = new Label
        {
            Text = "(no quest loaded)",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toolbar.AddChild(_pathLabel);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        AddChild(scroll);

        _objectivesList = new VBoxContainer
        {
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _objectivesList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_objectivesList);

        _outcomeLabel = new Label
        {
            Text = "Outcome: —",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        AddChild(_outcomeLabel);
    }

    private void OnLoadPressed()
    {
        if (_openDialog == null)
        {
            _openDialog = new EditorFileDialog
            {
                FileMode = EditorFileDialog.FileModeEnum.OpenFile,
                Access   = EditorFileDialog.AccessEnum.Resources,
            };
            _openDialog.AddFilter("*.tres", "PS1Graph (.tres)");
            _openDialog.FileSelected += OnLoadPathChosen;
            AddChild(_openDialog);
        }
        _openDialog.PopupCentered(new Vector2I(800, 600));
    }

    private void OnLoadPathChosen(string path)
    {
        var graph = PS1GraphCompiler.RobustLoad(path);
        if (graph == null)
        {
            GD.PushError($"[PS1Godot] PS1QuestJournal: failed to load '{path}'.");
            return;
        }
        if (graph.Kind != "quest")
        {
            GD.PushWarning($"[PS1Godot] PS1QuestJournal: '{path}' has Kind='{graph.Kind}', expected 'quest'. Loading anyway — non-quest nodes will show as Other.");
        }
        _quest = graph;
        if (_pathLabel != null) _pathLabel.Text = path;
        ResetState();
        Rebuild();
    }

    private void ResetState()
    {
        _completed.Clear();
        _active.Clear();
        if (_quest == null) return;

        // Seed active from objectives with no incoming objective edge
        // — mirrors PS1GraphCompiler.CompileQuest's "initial" rule.
        foreach (var n in _quest.Nodes)
        {
            if (n.Kind != "objective") continue;
            string id = n.GetPayload(0);
            if (string.IsNullOrEmpty(id)) continue;
            if (HasIncomingObjectiveEdge(n.Id)) continue;
            _active.Add(id);
        }
    }

    private void Rebuild()
    {
        if (_objectivesList == null || _outcomeLabel == null) return;
        foreach (var child in _objectivesList.GetChildren())
        {
            _objectivesList.RemoveChild(child);
            child.QueueFree();
        }

        if (_quest == null)
        {
            _outcomeLabel.Text = "Outcome: (no quest loaded)";
            return;
        }

        // Header counters so the author has totals at a glance.
        var counters = new Label
        {
            Text = $"{_active.Count} active  •  {_completed.Count} complete  •  {CountTotalObjectives()} total",
        };
        counters.AddThemeColorOverride("font_color", new Color(0.75f, 0.85f, 1.00f));
        _objectivesList.AddChild(counters);

        // Render every objective with state badge + (when active)
        // a Complete button that mutates _completed and re-renders.
        foreach (var n in _quest.Nodes.OrderBy(o => o.Id))
        {
            if (n.Kind != "objective") continue;
            string id = n.GetPayload(0);
            if (string.IsNullOrEmpty(id)) continue;
            _objectivesList.AddChild(BuildObjectiveRow(n));
        }

        // Outcome line — first outcome whose prereqs are all complete.
        string? outcome = FindReadyOutcome();
        _outcomeLabel.Text = outcome != null
            ? $"Outcome: {outcome}"
            : "Outcome: —";
        _outcomeLabel.AddThemeColorOverride("font_color",
            outcome != null
                ? new Color(0.55f, 0.85f, 0.55f)
                : new Color(0.70f, 0.70f, 0.70f));
    }

    private Control BuildObjectiveRow(PS1GraphNode obj)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        string id    = obj.GetPayload(0);
        string title = obj.GetPayload(1);

        string state;
        Color  tone;
        if (_completed.Contains(id))
        {
            state = "✓";
            tone  = new Color(0.55f, 0.85f, 0.55f);
        }
        else if (_active.Contains(id))
        {
            state = "●";
            tone  = new Color(0.95f, 0.80f, 0.35f);
        }
        else
        {
            state = "·";
            tone  = new Color(0.50f, 0.50f, 0.50f);
        }
        var badge = new Label { Text = state, CustomMinimumSize = new Vector2(16, 0) };
        badge.AddThemeColorOverride("font_color", tone);
        row.AddChild(badge);

        var label = new Label
        {
            Text = string.IsNullOrEmpty(title) ? id : $"{id}  —  {title}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        label.AddThemeColorOverride("font_color", tone);
        row.AddChild(label);

        if (_active.Contains(id))
        {
            var btn = new Button { Text = "Complete" };
            btn.Pressed += () => { Complete(id); Rebuild(); };
            row.AddChild(btn);
        }
        return row;
    }

    private void Complete(string id)
    {
        if (_quest == null) return;
        if (!_active.Contains(id)) return;
        _completed.Add(id);
        _active.Remove(id);
        // Recompute active set: any objective whose prereqs are now
        // all-complete moves from inactive to active. Mirrors the
        // Quest.new helper's recomputeActive.
        foreach (var n in _quest.Nodes)
        {
            if (n.Kind != "objective") continue;
            string objId = n.GetPayload(0);
            if (string.IsNullOrEmpty(objId)) continue;
            if (_completed.Contains(objId) || _active.Contains(objId)) continue;
            if (PrereqsMet(n.Id)) _active.Add(objId);
        }
    }

    // ── Graph walking helpers — mirror CompileQuest's resolution
    // logic without depending on the .lua text being generated yet.

    private bool HasIncomingObjectiveEdge(int nodeId)
    {
        if (_quest == null) return false;
        foreach (var c in _quest.Connections)
        {
            if (c.ToNodeId != nodeId) continue;
            if (c.ToPort != 0) continue;
            var src = NodeById(c.FromNodeId);
            if (src != null && src.Kind == "objective") return true;
        }
        return false;
    }

    private bool PrereqsMet(int nodeId)
    {
        if (_quest == null) return true;
        foreach (var c in _quest.Connections)
        {
            if (c.ToNodeId != nodeId) continue;
            if (c.ToPort != 0) continue;
            var src = NodeById(c.FromNodeId);
            if (src == null || src.Kind != "objective") continue;
            string id = src.GetPayload(0);
            if (string.IsNullOrEmpty(id)) continue;
            if (!_completed.Contains(id)) return false;
        }
        return true;
    }

    private string? FindReadyOutcome()
    {
        if (_quest == null) return null;
        foreach (var n in _quest.Nodes.OrderBy(o => o.Id))
        {
            if (n.Kind != "outcome") continue;
            string id = n.GetPayload(0);
            if (string.IsNullOrEmpty(id)) continue;
            if (PrereqsMet(n.Id)) return id;
        }
        return null;
    }

    private PS1GraphNode? NodeById(int id)
    {
        if (_quest == null) return null;
        foreach (var n in _quest.Nodes)
        {
            if (n.Id == id) return n;
        }
        return null;
    }

    private int CountTotalObjectives()
    {
        if (_quest == null) return 0;
        int c = 0;
        foreach (var n in _quest.Nodes)
        {
            if (n.Kind != "objective") continue;
            if (string.IsNullOrEmpty(n.GetPayload(0))) continue;
            c++;
        }
        return c;
    }
}
#endif
