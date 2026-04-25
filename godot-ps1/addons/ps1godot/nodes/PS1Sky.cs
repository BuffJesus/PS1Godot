using Godot;
using PS1Godot.Exporter;

namespace PS1Godot;

// Scene-level skybox. Exactly one PS1Sky per scene; place it as a
// direct child of the scene root. The exporter writes a 16-byte sky
// struct into the splashpack header (v24+); the runtime renders the
// texture as a full-screen quad BEFORE the main scene OT, so 3D
// geometry naturally over-draws it where present (Crash/Spyro/MediEvil
// pattern). Where geometry doesn't cover (open windows, missing
// walls), the sky shows through.
//
// Authentic PS1 2D sky — no actual cubemap, no real depth. For
// fixed-camera scenes (Final Fantasy VII, Resident Evil) this is
// indistinguishable from a "real" skybox; for free-cam scenes the
// lack of parallax is noticeable but acceptable.
//
// Multiple PS1Sky nodes in one scene = the exporter takes the first
// it finds and warns about the rest. The format only carries one.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_scene.svg")]
public partial class PS1Sky : Node3D
{
    // Sky texture. Must be saved as an asset (resource path required —
    // in-memory textures fail collection the same way they do for
    // PS1MeshInstance albedos and PS1UIElement images).
    [Export] public Texture2D? Texture { get; set; }

    // PSX bit-depth for the sky's VRAM atlas slot. A starry sky needs
    // very few colors (mostly black + a few star highlights) so 4bpp
    // is usually enough; richer painted skies want 8bpp. 16bpp burns
    // an entire 256-wide atlas page on one texture — reserved for
    // photo-style skies that genuinely need the full 32k palette.
    [Export] public PSXBPP BitDepth { get; set; } = PSXBPP.TEX_4BIT;

    // Multiplied with the texture sample at render time. White = no
    // tint (texture appears as authored). Useful for dimming the sky
    // without re-authoring the texture (e.g., cloudy night vs clear
    // night via tint alone).
    [Export] public Color Tint { get; set; } = new Color(1f, 1f, 1f, 1f);
}
