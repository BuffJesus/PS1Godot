#if TOOLS
using Godot;

namespace PS1Godot;

// Wireframe AABB gizmo for PS1Room volumes. Cyan color distinguishes
// rooms from green trigger boxes. Without this, rooms are invisible
// in the viewport and authors size them by typing numbers blind.
public partial class PS1RoomGizmo : EditorNode3DGizmoPlugin
{
    private const string MaterialName = "ps1_room";

    public PS1RoomGizmo()
    {
        CreateMaterial(MaterialName, new Color(0.35f, 0.85f, 0.95f, 0.8f));
    }

    public override string _GetGizmoName() => "PS1Room";

    public override bool _HasGizmo(Node3D forNode3D) => forNode3D is PS1Room;

    public override void _Redraw(EditorNode3DGizmo gizmo)
    {
        gizmo.Clear();
        if (gizmo.GetNode3D() is not PS1Room room) return;

        Vector3 he = room.VolumeSize * 0.5f;
        Vector3 off = room.VolumeOffset;

        Vector3[] c = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            c[i] = off + new Vector3(
                (i & 1) != 0 ? he.X : -he.X,
                (i & 2) != 0 ? he.Y : -he.Y,
                (i & 4) != 0 ? he.Z : -he.Z);
        }

        Vector3[] lines = new Vector3[]
        {
            c[0], c[1],  c[2], c[3],  c[4], c[5],  c[6], c[7],
            c[0], c[2],  c[1], c[3],  c[4], c[6],  c[5], c[7],
            c[0], c[4],  c[1], c[5],  c[2], c[6],  c[3], c[7],
        };

        gizmo.AddLines(lines, GetMaterial(MaterialName, gizmo));
    }
}
#endif
