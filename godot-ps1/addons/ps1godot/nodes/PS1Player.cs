using Godot;

namespace PS1Godot;

// Camera rig style — how the runtime positions the camera relative to
// the player during gameplay. Authoring today; runtime support is a
// Phase 2.5 item (Camera.SetMode). The exporter stamps the chosen mode
// so the runtime picks it up once wired.
public enum PS1CameraMode
{
    ThirdPerson = 0,  // Camera trails behind + above the player (default).
    FirstPerson = 1,  // Camera locked at player head height, player mesh hidden.
    Orbit       = 2,  // Right stick orbits the camera around the player.
}

// Spawn point for the PS1 player. Place one in each scene where you
// want the player to appear; the exporter reads this node's world
// transform into the splashpack's playerStart fields.
//
// If the PS1Player has a Camera3D child, its local transform defines
// the initial camera rig offset (behind/above for 3rd-person, at head
// for 1st-person). No Camera3D child → runtime uses a default offset.
//
// If no PS1Player is in the scene, the exporter falls back to the first
// Camera3D it finds — preserves older demo scenes that were authored
// before this node existed.
//
// Player physics (height, radius, speeds, gravity) still live on
// PS1Scene — they're scene-global, not per-player. Future: per-
// character stats move onto the Phase 2.6 RPG toolkit's AttributeSet.
[Tool]
[GlobalClass]
public partial class PS1Player : Node3D
{
    [Export] public PS1CameraMode CameraMode { get; set; } = PS1CameraMode.ThirdPerson;
}
