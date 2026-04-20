using Godot;

namespace PS1Godot;

// A UI canvas: a layer in screen space that groups UI elements and
// toggles visibility as a unit. Child nodes of type PS1UIElement become
// the canvas's widgets at export time.
//
// Residency (REF-GAP-8 from docs/ps1_large_rpg_optimization_reference.md):
//   - Gameplay: always resident during gameplay in the owning area.
//     Examples: HUD, health bar.
//   - MenuOnly: not in the gameplay VRAM set. Loaded when the menu
//     opens; unloaded on close. Examples: inventory, status screen.
//   - LoadOnDemand: loaded when Lua calls UI.LoadCanvas(name). Catches
//     everything between — rare dialog prompts, end-of-area popups.
// MVP exporter encodes this in the canvas flags byte upper bits; the
// runtime currently only reads bit 0 (visible). Runtime patch to gate
// font VRAM upload by residency is a follow-up.
public enum PS1UIResidency
{
    Gameplay = 0,
    MenuOnly = 1,
    LoadOnDemand = 2,
}

[Tool]
[GlobalClass]
public partial class PS1UICanvas : Node
{
    [Export] public string CanvasName { get; set; } = "";

    [Export] public PS1UIResidency Residency { get; set; } = PS1UIResidency.Gameplay;

    // Initial visibility. Gameplay canvases typically start visible;
    // MenuOnly / LoadOnDemand typically start hidden and Lua toggles them.
    [Export] public bool VisibleOnLoad { get; set; } = true;

    // Back-to-front draw order. Lower sortOrder draws first (behind
    // higher). 8-bit to match the runtime's UICanvas.sortOrder.
    [Export(PropertyHint.Range, "0,255,1")]
    public int SortOrder { get; set; } = 0;

    // Palette source for this canvas's elements. When set, each
    // PS1UIElement child with a non-Custom ThemeSlot pulls its color
    // from the theme. Leave null for "authored colors only" (current
    // behavior). Shipped default is addons/ps1godot/themes/PS1Theme.tres —
    // duplicate + edit to customise without mutating the plugin copy.
    [Export] public PS1Theme? Theme { get; set; }
}
