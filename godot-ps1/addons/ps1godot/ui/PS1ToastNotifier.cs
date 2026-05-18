#if TOOLS
using Godot;

namespace PS1Godot.UI;

// Toast notifier — UE editor port plan pick #3. Transient status
// pops in the bottom-right of the editor window, fades after a few
// seconds. PS1GodotPlugin calls Show(..) after every Run-on-PSX
// outcome (success/halt/launch) so the headline never gets buried
// in the collapsed Output panel.
//
// Implementation: a CanvasLayer parented to the editor's base
// Control with a single Label child anchored bottom-right. Each
// Show(..) cancels the previous fade, sets the text + colour,
// then runs a 0.2s fade-in → hold → 0.4s fade-out Tween.
//
// Stays a Control + CanvasLayer pair (Godot-native UI primitives);
// no SubViewport / extra Window / SlateNotificationManager-style
// reinvention.
public partial class PS1ToastNotifier : CanvasLayer
{
    public enum Severity
    {
        Info,     // green-ish — success / "Launching PCSX-Redux…"
        Warning,  // amber — non-blocking warnings ("VRAM over budget")
        Error,    // red — hard fail ("Run on PSX HALTED: …")
    }

    private Label?  _label;
    private Tween?  _tween;
    private Panel?  _bg;

    public PS1ToastNotifier()
    {
        // Layer high enough to sit over the rest of the editor.
        Layer = 128;
    }

    public override void _Ready()
    {
        // Container so we can give the toast a translucent panel
        // background that's distinct from whatever's behind it.
        _bg = new Panel
        {
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = -380, OffsetTop = -80, OffsetRight = -16, OffsetBottom = -16,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        var style = new StyleBoxFlat
        {
            BgColor      = new Color(0.08f, 0.09f, 0.12f, 0.92f),
            BorderWidthLeft = 4, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            BorderColor  = new Color(0.55f, 0.55f, 0.55f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        };
        _bg.AddThemeStyleboxOverride("panel", style);
        AddChild(_bg);

        _label = new Label
        {
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0, AnchorBottom = 1,
            OffsetLeft = 12, OffsetTop = 8, OffsetRight = -12, OffsetBottom = -8,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bg.AddChild(_label);
    }

    // Show a toast. duration < 0 → infinite (until next Show()
    // replaces it). Set durations large for Error so the user has
    // time to read; Info/Warning auto-clear quickly.
    public void Show(string message, Severity severity = Severity.Info, double durationSec = 4.0)
    {
        if (_label == null || _bg == null) return;

        _tween?.Kill();

        _label.Text = message;
        Color tier = severity switch
        {
            Severity.Error   => new Color(0.95f, 0.45f, 0.45f),
            Severity.Warning => new Color(0.95f, 0.80f, 0.35f),
            _                => new Color(0.55f, 0.85f, 0.55f),
        };
        _label.AddThemeColorOverride("font_color", tier);
        // Re-style the left border accent to match severity for at-a-
        // glance categorisation.
        if (_bg.GetThemeStylebox("panel") is StyleBoxFlat sb)
        {
            sb.BorderColor = tier;
        }

        // Fade in to fully opaque, hold, fade out. Skip the fade-out
        // when durationSec < 0 (caller wants it sticky).
        _bg.Modulate = new Color(1, 1, 1, 0);
        _tween = CreateTween();
        _tween.TweenProperty(_bg, "modulate:a", 1.0f, 0.2f);
        if (durationSec >= 0)
        {
            _tween.TweenInterval(durationSec);
            _tween.TweenProperty(_bg, "modulate:a", 0.0f, 0.4f);
        }
    }
}
#endif
