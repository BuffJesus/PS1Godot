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
//   - LoadingScreen: shown DURING scene loads (between the previous
//     scene's teardown and the new scene's first frame). Exported into
//     a separate `scene_N.loading` LoaderPack file the runtime reads
//     before the main splashpack. Must contain a Progress element
//     named exactly "loading" so the runtime can drive the percentage
//     during file load. Author one canvas per scene at most.
// MVP exporter encodes this in the canvas flags byte upper bits; the
// runtime currently only reads bit 0 (visible). Runtime patch to gate
// font VRAM upload by residency is a follow-up.
public enum PS1UIResidency
{
    Gameplay = 0,
    MenuOnly = 1,
    LoadOnDemand = 2,
    LoadingScreen = 3,
}

[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_ui_canvas.svg")]
public partial class PS1UICanvas : Node
{
    /// <summary>
    /// Lua-callable name. UI.SetVisible("name", true/false), UI.FindElement,
    /// etc. all match by this string. Must be unique within the scene.
    /// </summary>
    [ExportGroup("Identity")]
    [Export] public string CanvasName { get; set; } = "";
    /// <summary>
    /// When this canvas's VRAM lives. Gameplay = always resident
    /// (HUD, health bar). MenuOnly = loaded when a menu opens (inventory).
    /// LoadOnDemand = loaded by Lua via UI.LoadCanvas. LoadingScreen =
    /// shown during scene load — must contain a Progress element named
    /// "loading"; ships in the .loading LoaderPack file.
    /// </summary>
    [Export] public PS1UIResidency Residency { get; set; } = PS1UIResidency.Gameplay;

    /// <summary>
    /// Initial visibility on scene load. Gameplay canvases (HUD) usually
    /// start visible. MenuOnly / LoadOnDemand usually start hidden and Lua
    /// toggles them via UI.SetVisible.
    /// </summary>
    [Export] public bool VisibleOnLoad { get; set; } = true;

    /// <summary>
    /// Draw order — LIFO-inverted: LOWEST sortOrder draws LAST (= ON TOP).
    /// HIGHEST sortOrder draws FIRST (= behind). Use 0–10 for full-screen
    /// overlays (title fades, dialog boxes); use 200+ for backdrops
    /// (pre-rendered BG canvas).
    /// </summary>
    [ExportGroup("Rendering")]
    [Export(PropertyHint.Range, "0,255,1")]
    public int SortOrder { get; set; } = 0;

    /// <summary>
    /// Optional shared palette. When set, child elements with a non-Custom
    /// ThemeSlot read their color from this Theme resource. Leave null to
    /// use authored colors only. Default shipped at
    /// addons/ps1godot/themes/PS1Theme.tres — duplicate + edit to
    /// customize without mutating the plugin copy.
    /// </summary>
    [ExportGroup("Theming")]
    [Export] public PS1Theme? Theme { get; set; }

    public override string[] _GetConfigurationWarnings()
    {
        var w = new System.Collections.Generic.List<string>();
        if (string.IsNullOrEmpty(CanvasName))
            w.Add("CanvasName is empty. Lua calls like UI.SetVisible(name, true) " +
                  "use this name to find the canvas — an empty name silently fails.");
        return w.ToArray();
    }
}
