using Godot;

namespace PS1Godot;

// Central palette for PS1 UI elements. Each element can opt into a
// theme slot via PS1UIElement.ThemeSlot; at export time, that slot's
// color replaces the element's Color field. Change the theme once,
// every opted-in element restyles.
//
// Author workflow:
//   1. Create a PS1UICanvas (or drop one of the templates).
//   2. Assign a PS1Theme resource to the canvas's Theme property.
//      The shipped default lives at addons/ps1godot/themes/PS1Theme.tres;
//      right-click → Duplicate to customise.
//   3. On each PS1UIElement, pick its ThemeSlot (Text / Accent / Bg / …)
//      to pull from the theme instead of the authored Color. Elements
//      with ThemeSlot = Custom keep their authored Color as-is.
//
// No runtime format change: resolution happens at export time inside
// SceneCollector, so the splashpack binary looks the same as if the
// author typed each color by hand.
[Tool]
[GlobalClass]
public partial class PS1Theme : Resource
{
    // Body text (dialog lines, HUD labels, menu items).
    [Export] public Color TextColor { get; set; } = new Color(1.0f, 1.0f, 1.0f);

    // Headings, selection cursors, narrator name tags —
    // anything that should feel like a brand-colored accent.
    [Export] public Color AccentColor { get; set; } = new Color(1.0f, 0.85f, 0.30f);

    // Dialog / menu background fill. Usually a saturated dark blue.
    [Export] public Color BgColor { get; set; } = new Color(0.0f, 0.0f, 0.4f);

    // Optional border stroke around background panels.
    [Export] public Color BgBorderColor { get; set; } = new Color(1.0f, 1.0f, 1.0f);

    // "Healthy" / success / full-bar fill. HP bars when > 50 %.
    [Export] public Color HighlightColor { get; set; } = new Color(0.20f, 0.80f, 0.30f);

    // Caution tone — amber HP bar, "quest pending" hint.
    [Export] public Color WarningColor { get; set; } = new Color(0.95f, 0.75f, 0.25f);

    // Critical tone — low-HP red, error states, danger zone.
    [Export] public Color DangerColor { get; set; } = new Color(0.90f, 0.25f, 0.25f);

    // Neutral dark fill for non-decorative elements (bar background,
    // dim separator lines).
    [Export] public Color NeutralColor { get; set; } = new Color(0.10f, 0.10f, 0.10f);
}
