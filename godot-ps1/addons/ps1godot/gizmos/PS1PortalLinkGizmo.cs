#if TOOLS
using Godot;

namespace PS1Godot;

// Semi-transparent quad gizmo for PS1PortalLink. Magenta color so it
// pops against cyan rooms and green triggers. Shows the portal opening
// size and facing direction.
public partial class PS1PortalLinkGizmo : EditorNode3DGizmoPlugin
{
    private const string MaterialName = "ps1_portal";

    public PS1PortalLinkGizmo()
    {
        CreateMaterial(MaterialName, new Color(0.85f, 0.35f, 0.85f, 0.7f));
    }

    public override string _GetGizmoName() => "PS1PortalLink";

    public override bool _HasGizmo(Node3D forNode3D) => forNode3D is PS1PortalLink;

    public override void _Redraw(EditorNode3DGizmo gizmo)
    {
        gizmo.Clear();
        if (gizmo.GetNode3D() is not PS1PortalLink portal) return;

        float hw = portal.PortalSize.X * 0.5f;
        float hh = portal.PortalSize.Y * 0.5f;

        // Portal quad in local space: X = width, Y = height, Z = facing.
        Vector3 tl = new(-hw, hh, 0);
        Vector3 tr = new( hw, hh, 0);
        Vector3 bl = new(-hw, -hh, 0);
        Vector3 br = new( hw, -hh, 0);

        // Outline edges.
        Vector3[] lines = new Vector3[]
        {
            tl, tr,  tr, br,  br, bl,  bl, tl,
            // Cross lines to show it's a portal, not just a rectangle.
            tl, br,  tr, bl,
        };

        gizmo.AddLines(lines, GetMaterial(MaterialName, gizmo));
    }
}
#endif
