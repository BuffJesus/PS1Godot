using Godot;

namespace PS1Godot;

// Composite "stat bar" node — RFC §L4 (combat-framework), Phase 4.5.
//
// Replaces the 3-element authoring (bg Box + fill Box + optional Text
// label as siblings under a PS1UICanvas) with a single inspector
// surface. At export the layout resolver lowers this into 2-3
// synthetic PS1UIElement records inside the parent canvas, using the
// same `<elementName>_bg` / `<elementName>_fill` naming convention
// the hand-authored bars use (so the existing geometric + readability
// lints don't false-positive on composite-emitted bars).
//
// The `TrackedEntity` / `TrackedStat` pair is editor-only metadata
// the Doctor reads — there's no auto-emit of per-frame
// `UI.UpdateStatBar` calls in this slice. Authors still write the
// imperative call in their entity's `onUpdate`. The composite node
// reduces the *visual* authoring from three nodes to one; the bar
// update wiring stays in Lua. RFC §L3's declarative
// `UI.BindStatBars` form is "v2" per the RFC itself and waits on
// either an engine pre-update Lua hook or export-time source
// rewriting infrastructure — neither of which we ship in this slice.
//
// Conservative scope cuts vs the RFC table:
// - `LowThreshold` / `LowFillColor` — would need
//   `UI.UpdateStatBar` to support color swap, which Phase 1 didn't
//   ship. Adding the inspector fields without runtime support would
//   be dead authoring surface.
// - `Interpolated` — explicitly tagged v2 in the RFC.
// - `CanvasName` — redundant since PS1StatBar must be a child of
//   PS1UICanvas (the layout resolver only sees it from there).
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_player.svg")]
public partial class PS1StatBar : Node
{
    /// <summary>
    /// Base name for the generated child elements. Two PS1UIElement
    /// records emit at export: "ElementName_bg" (the dark backing
    /// panel) and "ElementName_fill" (the live-driven gauge). The
    /// optional Label emits as "ElementName_label". Names follow
    /// the same suffix convention as hand-authored bars, so the
    /// `Bar fill exceeds BG` + `Paired bars near-black` lints fire
    /// on composite-emitted bars too.
    /// </summary>
    [ExportGroup("Identity")]
    [Export] public string ElementName { get; set; } = "stat";

    /// <summary>
    /// Top-left X of the FILL rect in PSX pixels (0..319). The bg
    /// rect insets to (X - Padding, Y - Padding) with W/H grown by
    /// 2*Padding so the bg reads as a frame around the fill.
    /// </summary>
    [ExportGroup("Geometry")]
    [Export(PropertyHint.Range, "0,319,1,suffix:px")]
    public int X { get; set; } = 16;

    /// <summary>
    /// Top-left Y of the FILL rect in PSX pixels (0..239).
    /// </summary>
    [Export(PropertyHint.Range, "0,239,1,suffix:px")]
    public int Y { get; set; } = 200;

    /// <summary>
    /// Fill rect width at full gauge. Author the maximum; the runtime
    /// shrinks it proportionally via UI.UpdateStatBar.
    /// </summary>
    [Export(PropertyHint.Range, "1,319,1,suffix:px")]
    public int Width { get; set; } = 100;

    /// <summary>
    /// Fill rect height. PSX bars typically 4-8 px tall.
    /// </summary>
    [Export(PropertyHint.Range, "1,32,1,suffix:px")]
    public int Height { get; set; } = 4;

    /// <summary>
    /// Pixels of bg padding around the fill on each side. Bg extends
    /// from (X - Padding, Y - Padding) to (X + W + Padding, Y + H +
    /// Padding). Default 2 matches the boss_smoke hand-rolled bars.
    /// </summary>
    [Export(PropertyHint.Range, "0,16,1,suffix:px")]
    public int Padding { get; set; } = 2;

    /// <summary>
    /// Fill color at full gauge. Will be drawn over the bg as the
    /// gauge resizes per stat ratio.
    /// </summary>
    [ExportGroup("Appearance")]
    [Export] public Color FillColor { get; set; } = new Color(0.24f, 0.78f, 0.24f, 1f);

    /// <summary>
    /// Dark backing panel color. Stays constant as the fill shrinks.
    /// </summary>
    [Export] public Color BGColor { get; set; } = new Color(0.08f, 0.08f, 0.08f, 1f);

    /// <summary>
    /// Optional text overlay (e.g. "HP" / "BOSS"). Drawn at the same
    /// (X, Y) as the fill so labels overlay the gauge. Empty string
    /// skips the label element entirely.
    /// </summary>
    [ExportGroup("Label")]
    [Export(PropertyHint.MultilineText)] public string Label { get; set; } = "";

    [Export] public Color LabelColor { get; set; } = new Color(0.94f, 0.86f, 0.86f, 1f);

    /// <summary>
    /// Entity whose PS1Stats drives the fill — read by the Doctor's
    /// `Stats without HUD` lint to verify every stat-bearing entity
    /// has at least one bar pointing at it. Editor-only metadata; not
    /// emitted into the splashpack and not read by the runtime.
    /// Author still calls UI.UpdateStatBar manually from the entity's
    /// onUpdate to drive the actual per-frame size change.
    /// </summary>
    [ExportGroup("Binding")]
    [Export] public NodePath TrackedEntity { get; set; } = new NodePath("");

    /// <summary>
    /// Which stat from the tracked entity's PS1Stats this bar
    /// represents. One of "hp" / "stamina" / "mana". Used by the
    /// `Stats without HUD` lint to match per-stat coverage.
    /// </summary>
    [Export(PropertyHint.Enum, "hp,stamina,mana")]
    public string TrackedStat { get; set; } = "hp";

    public override string[] _GetConfigurationWarnings()
    {
        var w = new System.Collections.Generic.List<string>();
        if (GetParent() is not PS1UICanvas)
        {
            w.Add("PS1StatBar must be a direct child of a PS1UICanvas. " +
                  "The layout resolver only sees stat-bar children from a canvas.");
        }
        if (string.IsNullOrWhiteSpace(ElementName))
        {
            w.Add("ElementName is empty. Child elements name as <ElementName>_bg / _fill — " +
                  "without a base name, multiple bars in one canvas collide.");
        }
        if (TrackedEntity != null && !TrackedEntity.IsEmpty)
        {
            var node = GetNodeOrNull(TrackedEntity);
            if (node == null)
            {
                w.Add($"TrackedEntity NodePath '{TrackedEntity}' doesn't resolve.");
            }
        }
        return w.ToArray();
    }
}
