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

    // Verts in local space. X and Z define the polygon outline; Y is the
    // floor height at that vertex. Give three non-collinear Ys to author a
    // ramp; give equal Ys for a flat region.
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

    // Leave at Flat to let the exporter infer from the fitted plane's
    // slope. Override when you want the runtime to treat a gentle ramp as
    // stairs (step-up snap) or vice versa.
    [Export] public PS1NavSurfaceType SurfaceType { get; set; } = PS1NavSurfaceType.Flat;

    // 0xFF = exterior / unknown. Set to match your PS1Room index when
    // authoring interior scenes so the portal rendering system culls the
    // right rooms. Interior scenes are Phase 2 bullet 12; leaving at 0xFF
    // is fine until that lands.
    [Export(PropertyHint.Range, "0,255,1")]
    public int RoomIndex { get; set; } = 0xFF;

    // If true, ALL boundary edges (edges without a stitched portal neighbour)
    // let the player walk off the region — use for platforms / ledges you
    // want to fall off. Default is false: walls.
    [Export] public bool Platform { get; set; } = false;
}
