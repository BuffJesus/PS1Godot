#if TOOLS
using Godot;

namespace PS1Godot;

// Polygon gizmo for PS1NavRegion. Translucent green fill + bright-green
// outline so authors can see the walkable area at a glance, Unreal-style.
// Convex polygon is fan-triangulated from vertex 0 — fine because nav
// regions are required to be convex by NavRegionSystem.
public partial class PS1NavRegionGizmo : EditorNode3DGizmoPlugin
{
    private const string EdgeMaterialName = "ps1_navregion_edge";
    private const string FillMaterialName = "ps1_navregion_fill";

    public PS1NavRegionGizmo()
    {
        // Bright green outline + matching translucent fill. Alpha 0.25 keeps
        // overlapping floor geometry readable underneath.
        CreateMaterial(EdgeMaterialName, new Color(0.30f, 1.00f, 0.40f, 0.95f));
        CreateMaterial(FillMaterialName, new Color(0.30f, 1.00f, 0.40f, 0.25f));
    }

    public override string _GetGizmoName() => "PS1NavRegion";

    public override bool _HasGizmo(Node3D forNode3D) => forNode3D is PS1NavRegion;

    public override void _Redraw(EditorNode3DGizmo gizmo)
    {
        gizmo.Clear();
        if (gizmo.GetNode3D() is not PS1NavRegion region) return;

        var verts = region.Verts;
        if (verts == null || verts.Length < 3) return;

        // Outline: closed polygon edges.
        var lines = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < verts.Length; i++)
        {
            int next = (i + 1) % verts.Length;
            lines.Add(verts[i]);
            lines.Add(verts[next]);
        }
        gizmo.AddLines(lines.ToArray(), GetMaterial(EdgeMaterialName, gizmo));

        // Fill: fan triangulation from vert 0. Emit each tri TWICE with
        // opposite winding so the overlay reads from above and below
        // without depending on cull mode (CreateMaterial doesn't expose it).
        var fillVerts = new System.Collections.Generic.List<Vector3>();
        for (int i = 1; i < verts.Length - 1; i++)
        {
            fillVerts.Add(verts[0]);
            fillVerts.Add(verts[i]);
            fillVerts.Add(verts[i + 1]);
            fillVerts.Add(verts[0]);
            fillVerts.Add(verts[i + 1]);
            fillVerts.Add(verts[i]);
        }

        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = fillVerts.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        gizmo.AddMesh(mesh, GetMaterial(FillMaterialName, gizmo));
    }
}
#endif
