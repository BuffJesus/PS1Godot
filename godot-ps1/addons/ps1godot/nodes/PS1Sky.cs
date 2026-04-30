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
[Icon("res://addons/ps1godot/icons/ps1_sky.svg")]
public partial class PS1Sky : Node3D
{
    /// <summary>
    /// Sky texture. Must be saved as a .png/.tres asset (in-memory
    /// textures aren't collected). Drawn full-screen behind 3D geometry.
    /// </summary>
    [Export] public Texture2D? Texture { get; set; }

    /// <summary>
    /// VRAM bit-depth. Starry sky / few-color → 4bpp (cheapest VRAM).
    /// Painted sky with gradients → 8bpp. Photo-style → 16bpp (eats a
    /// full 256-wide atlas page; reserve for cinematics).
    /// </summary>
    [Export] public PSXBPP BitDepth { get; set; } = PSXBPP.TEX_4BIT;

    /// <summary>
    /// Render-time multiplier on the texture. White = untinted. Use to
    /// dim the sky for night/storm without re-authoring the texture.
    /// </summary>
    [Export] public Color Tint { get; set; } = new Color(1f, 1f, 1f, 1f);
}
