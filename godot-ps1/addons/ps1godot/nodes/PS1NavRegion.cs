using Godot;

namespace PS1Godot;

public enum PS1NavSurfaceType : byte
{
    Flat = 0,
    Ramp = 1,
    Stairs = 2,
}

// Author-drawn convex walkable polygon. At export the verts are transformed
// to world space and fit to a plane (Y = A·X + B·Z + D) so a single region
// can describe ramps and angled floors, not just flat slabs.
//
// Winding: verts must form a convex polygon in CCW order on the XZ plane
// when viewed from above (+Y looking down in Godot). Up to 8 verts per
// region — the runtime's NavRegion struct is fixed-size.
//
// Portal stitching is automatic: any two regions whose edges share near-
// coincident endpoints (within a small world-space epsilon) get a portal
// connecting them, so the player can walk across the boundary. Regions
// without shared edges behave as islands.
//
// For flat floors you can also rely on the auto-region emitted from
// flat-ish Static PS1MeshInstance AABBs — this node is for ramps,
// non-rectangular shapes, or cases where the mesh and walkable area
// don't line up.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_nav_region.svg")]
public partial class PS1NavRegion : Node3D
{
    // Default to a 2×2 square at origin so the node is immediately usable
    // once dropped into a scene — authors can reshape from there.
    private Vector3[] _verts =
    {
        new(-1, 0, -1),
        new( 1, 0, -1),
        new( 1, 0,  1),
        new(-1, 0,  1),
    };

    /// <summary>
    /// Convex polygon in local space (CCW from above). X/Z = outline,
    /// Y = floor height at that vertex. Equal Ys = flat region; three
    /// non-collinear Ys = ramp. Max 8 verts (runtime's fixed-size struct).
    /// </summary>
    [Export]
    public Vector3[] Verts
    {
        get => _verts;
        set
        {
            _verts = value ?? System.Array.Empty<Vector3>();
            UpdateGizmos();
        }
    }

    /// <summary>
    /// Override the auto-inferred surface type. Flat = floor, Ramp =
    /// smooth slope, Stairs = step-up snap. Leave at Flat to let the
    /// exporter pick from the fitted plane's slope.
    /// </summary>
    [Export] public PS1NavSurfaceType SurfaceType { get; set; } = PS1NavSurfaceType.Flat;

    /// <summary>
    /// PS1Room index this region belongs to. 0xFF (default) = exterior /
    /// unknown — fine for outdoor scenes. Interior scenes set this to
    /// match the PS1Room.RoomIndex so portal culling rejects out-of-room
    /// regions correctly.
    /// </summary>
    [Export(PropertyHint.Range, "0,255,1")]
    public int RoomIndex { get; set; } = 0xFF;

    /// <summary>
    /// When true, ALL boundary edges without a stitched portal let the
    /// player walk off (platforms, ledges, balconies). When false
    /// (default), boundaries act as walls and clamp the player.
    /// </summary>
    [Export] public bool Platform { get; set; } = false;
}
