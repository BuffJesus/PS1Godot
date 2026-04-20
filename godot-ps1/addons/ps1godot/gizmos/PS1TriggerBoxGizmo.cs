#if TOOLS
using Godot;

namespace PS1Godot;

// Editor gizmo that draws the PS1TriggerBox's world AABB as a green
// wireframe in the 3D viewport. Without this, a trigger is just a bare
// origin gizmo and sizing it blind is painful.
//
// Registered from PS1GodotPlugin._EnterTree via AddNode3DGizmoPlugin.
public partial class PS1TriggerBoxGizmo : EditorNode3DGizmoPlugin
{
    private const string MaterialName = "ps1_trigger";

    public PS1TriggerBoxGizmo()
    {
        // Bright green so it reads as "volume the player triggers" at a
        // glance. Partly transparent so it doesn't overpower neighbouring
        // geometry.
        CreateMaterial(MaterialName, new Color(0.25f, 1.0f, 0.35f, 0.9f));
    }

    public override string _GetGizmoName() => "PS1TriggerBox";

    public override bool _HasGizmo(Node3D forNode3D) => forNode3D is PS1TriggerBox;

    public override void _Redraw(EditorNode3DGizmo gizmo)
    {
        gizmo.Clear();
        if (gizmo.GetNode3D() is not PS1TriggerBox tb) return;

        Vector3 he = tb.HalfExtents;
        // 8 corners of the AABB in local space (node rotation/scale applied
        // by the viewport as usual since we push local-space lines).
        Vector3[] c = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            c[i] = new Vector3(
                (i & 1) != 0 ? he.X : -he.X,
                (i & 2) != 0 ? he.Y : -he.Y,
                (i & 4) != 0 ? he.Z : -he.Z);
        }

        // 12 cube edges as pairs of vertices. Bits: 1=X, 2=Y, 4=Z. Each
        // pair differs in exactly one of those bits — one edge per axis
        // direction per face.
        Vector3[] lines = new Vector3[]
        {
            c[0], c[1],  c[2], c[3],  c[4], c[5],  c[6], c[7], // X edges
            c[0], c[2],  c[1], c[3],  c[4], c[6],  c[5], c[7], // Y edges
            c[0], c[4],  c[1], c[5],  c[2], c[6],  c[3], c[7], // Z edges
        };

        gizmo.AddLines(lines, GetMaterial(MaterialName, gizmo));
    }
}
#endif
