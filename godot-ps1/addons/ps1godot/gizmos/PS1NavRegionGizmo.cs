#if TOOLS
using Godot;

namespace PS1Godot;

// Polygon edge gizmo for PS1NavRegion. Yellow wireframe shows the
// convex walkable area so authors aren't drawing nav regions blind.
// Edges connect consecutive vertices; the last vertex wraps to the first.
public partial class PS1NavRegionGizmo : EditorNode3DGizmoPlugin
{
    private const string MaterialName = "ps1_navregion";

    public PS1NavRegionGizmo()
    {
        CreateMaterial(MaterialName, new Color(0.95f, 0.85f, 0.25f, 0.9f));
    }

    public override string _GetGizmoName() => "PS1NavRegion";

    public override bool _HasGizmo(Node3D forNode3D) => forNode3D is PS1NavRegion;

    public override void _Redraw(EditorNode3DGizmo gizmo)
    {
        gizmo.Clear();
        if (gizmo.GetNode3D() is not PS1NavRegion region) return;

        var verts = region.Verts;
        if (verts == null || verts.Length < 3) return;

        // Draw edges around the polygon + close the loop.
        var lines = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < verts.Length; i++)
        {
            int next = (i + 1) % verts.Length;
            lines.Add(verts[i]);
            lines.Add(verts[next]);
        }

        gizmo.AddLines(lines.ToArray(), GetMaterial(MaterialName, gizmo));
    }
}
#endif
