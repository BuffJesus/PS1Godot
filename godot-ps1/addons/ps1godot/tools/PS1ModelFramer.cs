using Godot;

namespace PS1Godot.Tools;

// Author-side helper for framing a 3D model at a specific apparent size on
// the PSX 320×240 screen. Given the bounding sphere of a selected Node3D
// and a desired apparent width in PSX pixels, computes:
//   - Camera distance (along the current camera's forward axis)
//   - Projection H (the PSX H register — higher = narrower FOV)
//   - A ready-to-paste Lua snippet the author can drop into a scene script
//
// Output:
//   - Prints the computed values + Lua snippet to the Godot Output panel.
//   - If the scene has exactly one Camera3D, moves it into position so the
//     author sees the framing in the Godot viewport immediately.
//   - If there are zero or multiple Camera3Ds, only prints (no clobbering).
//
// Math reference:
//   PSX projection: screen_x = H * cam_x / cam_z  (plus 160 offset)
//   Apparent radius = H * R / D, where R = sphere radius, D = distance.
//   Given desired apparent diameter = apparentPx, solve for D:
//     D = H * R / (apparentPx / 2) = 2 * H * R / apparentPx
public static class PS1ModelFramer
{
    // Default H used for generic framing. Godot's camera FOV default is
    // 75° — with H=240 the PSX approximates a similarly narrow view.
    public const int DefaultProjectionH = 240;

    public readonly struct Result
    {
        public Result(Vector3 camPos, Vector3 camRotRadians, int projectionH, float distance, float radius)
        {
            CameraPosition = camPos;
            CameraRotationRadians = camRotRadians;
            ProjectionH = projectionH;
            Distance = distance;
            Radius = radius;
        }
        public Vector3 CameraPosition { get; }
        public Vector3 CameraRotationRadians { get; }
        public int ProjectionH { get; }
        public float Distance { get; }
        public float Radius { get; }
    }

    // Compute camera position + rotation that puts `model`'s bounding sphere
    // at the desired apparent width in PSX pixels. The camera is placed along
    // +Z behind the model looking toward -Z (Godot convention; the exporter
    // later flips Y+Z for PSX). Author can rotate around the model afterward.
    public static Result Compute(Node3D model, int apparentWidthPx, int projectionH = DefaultProjectionH)
    {
        if (model == null)
        {
            return new Result(Vector3.Zero, Vector3.Zero, projectionH, 0f, 0f);
        }

        Aabb aabb = ComputeWorldAabb(model);
        Vector3 center = aabb.Position + aabb.Size * 0.5f;
        float radius = aabb.Size.Length() * 0.5f;
        if (radius < 0.01f) radius = 0.5f;  // sanity floor for point-like objects

        float apparent = Mathf.Max(1, apparentWidthPx);
        float distance = 2f * projectionH * radius / apparent;

        // Godot camera looks along -Z; place camera at (center.x, center.y, center.z + D)
        // and leave rotation zero — so the model sits centered in Godot's viewport.
        var camPos = new Vector3(center.X, center.Y, center.Z + distance);
        var camRot = Vector3.Zero;

        return new Result(camPos, camRot, projectionH, distance, radius);
    }

    // Recursively gathers world-space AABBs from all MeshInstance3D
    // descendants under `root`. Returns an AABB covering everything, or a
    // zero-sized AABB at the node's origin if no meshes found.
    public static Aabb ComputeWorldAabb(Node3D root)
    {
        bool found = false;
        var combined = new Aabb(root.GlobalPosition, Vector3.Zero);
        WalkForAabb(root, ref combined, ref found);
        return combined;
    }

    private static void WalkForAabb(Node node, ref Aabb combined, ref bool found)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            Aabb local = mi.Mesh.GetAabb();
            // Transform the 8 corners into world space.
            Transform3D xform = mi.GlobalTransform;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) != 0 ? local.End.X : local.Position.X,
                    (i & 2) != 0 ? local.End.Y : local.Position.Y,
                    (i & 4) != 0 ? local.End.Z : local.Position.Z);
                Vector3 world = xform * corner;
                if (!found)
                {
                    combined = new Aabb(world, Vector3.Zero);
                    found = true;
                }
                else
                {
                    combined = combined.Expand(world);
                }
            }
        }
        foreach (Node c in node.GetChildren())
        {
            WalkForAabb(c, ref combined, ref found);
        }
    }

    // Builds a Lua snippet the author can paste into a scene script to
    // reproduce this framing at runtime. Uses the existing Camera API —
    // rotation is in "pi fractions" (1.0 = π radians = 180°), matching
    // Camera.SetRotation's convention.
    public static string BuildLuaSnippet(Result r)
    {
        // Godot → PSX: reflect Y and Z (matches the exporter convention).
        float lx = r.CameraPosition.X;
        float ly = -r.CameraPosition.Y;
        float lz = -r.CameraPosition.Z;
        // Rotation → pi-fraction. Zero rotation stays zero; author-adjusted
        // rotations emerge as r.CameraRotationRadians / π.
        float ryPi = r.CameraRotationRadians.Y / Mathf.Pi;
        float rxPi = r.CameraRotationRadians.X / Mathf.Pi;
        float rzPi = r.CameraRotationRadians.Z / Mathf.Pi;
        return
            "-- Framed via PS1Godot: Frame Selected Model in Viewport\n" +
            $"Camera.SetPosition({lx:0.###}, {ly:0.###}, {lz:0.###})\n" +
            $"Camera.SetRotation({rxPi:0.###}, {ryPi:0.###}, {rzPi:0.###})\n" +
            $"Camera.SetH({r.ProjectionH})\n";
    }
}
