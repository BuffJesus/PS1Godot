using Godot;

namespace PS1Godot;

// Connects two PS1Room volumes with a portal quad. Place it at the opening
// (doorway / archway / window) and point it at whichever room lies on the
// far side of it. The portal's transform describes the opening:
//   - position = portal center
//   - forward  = portal facing direction (normal)
//   - right    = portal local +X axis (half of PortalSize.X to each side)
//   - up       = portal local +Y axis (half of PortalSize.Y to each side)
//
// At export time the exporter auto-corrects the normal so it points from
// RoomA → RoomB regardless of how the author rotated the node. Portals are
// single-sided at render: the runtime culls portals whose normal faces
// away from the camera, so the auto-correction matters.
[Tool]
[GlobalClass]
public partial class PS1PortalLink : Node3D
{
    // Path to the PS1Room on the "near" side. Must be set, must differ
    // from RoomB, and both targets must be PS1Room nodes in the same
    // scene — the exporter warns and skips otherwise.
    [Export] public NodePath RoomA { get; set; } = new NodePath();

    // Path to the PS1Room on the "far" side.
    [Export] public NodePath RoomB { get; set; } = new NodePath();

    // Portal opening size in world units (X = width, Y = height). The
    // rendered portal quad is centered on the node and spans this size
    // along the node's local +X and +Y axes.
    [Export] public Vector2 PortalSize { get; set; } = new Vector2(1.5f, 2.0f);
}
