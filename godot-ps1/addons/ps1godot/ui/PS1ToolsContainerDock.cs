#if TOOLS
using Godot;
using System.Collections.Generic;

namespace PS1Godot.UI;

// Single-host wrapper that groups several secondary docks behind one
// top-level bottom-panel tab. The plugin's bottom panel grew to ten
// tabs over Phase 3 — well past the "non-intimidating" pillar of the
// UI/UX philosophy — so secondary docks fold into two of these
// containers (PS1 Editors, PS1 Tools), leaving only the primary
// authoring surfaces (Graph, Doctor) at the top level.
//
// Plugin owns the contained docks. This container only borrows them
// for layout; it does NOT QueueFree children on _ExitTree.
[Tool]
public partial class PS1ToolsContainerDock : VBoxContainer
{
    private TabContainer? _tabs;
    private readonly Dictionary<Control, int> _dockToTabIndex = new();

    public PS1ToolsContainerDock(string visibleName)
    {
        Name = visibleName;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        _tabs = new TabContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        AddChild(_tabs);
    }

    // Adds a sub-tab to the internal TabContainer. The control is
    // reparented under _tabs; if it had a previous parent (e.g. was
    // registered at the bottom panel), the caller is responsible for
    // detaching it first.
    public void AddSubTab(string tabTitle, Control dock)
    {
        if (_tabs == null || dock == null) return;
        dock.Name = tabTitle;
        _tabs.AddChild(dock);
        _dockToTabIndex[dock] = _tabs.GetTabCount() - 1;
    }

    // Switches the internal TabContainer to show the given dock.
    // Call MakeBottomPanelItemVisible(this) first so the container
    // itself is the active bottom-panel tab.
    public void RevealSubTab(Control dock)
    {
        if (_tabs == null || dock == null) return;
        if (_dockToTabIndex.TryGetValue(dock, out int idx))
        {
            _tabs.CurrentTab = idx;
        }
    }

    // Detach all sub-tabs without freeing them — plugin keeps
    // references and frees them itself in _ExitTree.
    public void DetachAllSubTabs()
    {
        if (_tabs == null) return;
        foreach (Node child in _tabs.GetChildren())
        {
            _tabs.RemoveChild(child);
        }
        _dockToTabIndex.Clear();
    }
}
#endif
