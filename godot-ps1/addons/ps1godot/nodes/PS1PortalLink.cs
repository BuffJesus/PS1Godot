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
[Icon("res://addons/ps1godot/icons/ps1_portal_link.svg")]
public partial class PS1PortalLink : Node3D
{
    /// <summary>
    /// Path to the PS1Room on the "near" side. Must be set, must differ
    /// from RoomB, and both targets must be PS1Room nodes in the same
    /// scene — the exporter warns and skips otherwise.
    /// </summary>
    [ExportGroup("Rooms")]
    [Export] public NodePath RoomA { get; set; } = new NodePath();

    /// <summary>
    /// Path to the PS1Room on the "far" side.
    /// </summary>
    [Export] public NodePath RoomB { get; set; } = new NodePath();

    /// <summary>
    /// Portal opening size in world units (X = width, Y = height). The
    /// rendered portal quad is centered on the node and spans this size
    /// along the node's local +X and +Y axes.
    /// </summary>
    [ExportGroup("Opening")]
    [Export] public Vector2 PortalSize { get; set; } = new Vector2(1.5f, 2.0f);

    public override string[] _GetConfigurationWarnings()
    {
        var w = new System.Collections.Generic.List<string>();
        if (RoomA == null || RoomA.IsEmpty)
            w.Add("RoomA is not set. Point it at a PS1Room node on one side of this portal.");
        if (RoomB == null || RoomB.IsEmpty)
            w.Add("RoomB is not set. Point it at a PS1Room node on the other side.");
        if (RoomA != null && RoomB != null && !RoomA.IsEmpty && !RoomB.IsEmpty && RoomA == RoomB)
            w.Add("RoomA and RoomB point at the same node. A portal must connect two different rooms.");
        if (PortalSize.X <= 0 || PortalSize.Y <= 0)
            w.Add($"PortalSize ({PortalSize}) has a non-positive dimension. Both X and Y must be > 0.");
        return w.ToArray();
    }
}
