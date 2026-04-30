using Godot;

namespace PS1Godot;

// A convex room volume for the interior portal/room occlusion system. Place
// one per logical room (each dungeon chamber, each house interior, etc.);
// at export, every triangle is assigned to whichever room's AABB contains
// the majority of its vertices (ties broken by centroid distance). The
// runtime then walks only rooms reachable through portals from the camera.
//
// Independent of PS1NavRegion — nav is how the PLAYER moves, rooms are how
// the RENDERER decides what to draw. You usually want one per-room of each.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_room.svg")]
public partial class PS1Room : Node3D
{
    /// <summary>
    /// Displayed in debug prints; purely cosmetic for now.
    /// </summary>
    [ExportGroup("Identity")]
    [Export] public string RoomName { get; set; } = "";

    /// <summary>
    /// Local volume size. Exporter transforms the 8 corners by the node's
    /// GlobalTransform to get a world AABB, so rotating / scaling the room
    /// node reshapes the volume (then AABBs it again). Matches the
    /// SplashEdit convention.
    /// </summary>
    [ExportGroup("Volume")]
    [Export] public Vector3 VolumeSize { get; set; } = new Vector3(4, 3, 4);

    /// <summary>
    /// Offset of the volume center in local space — lets you keep the node
    /// pivot at the room's door / anchor while the AABB covers the whole
    /// interior.
    /// </summary>
    [Export] public Vector3 VolumeOffset { get; set; } = Vector3.Zero;
}
