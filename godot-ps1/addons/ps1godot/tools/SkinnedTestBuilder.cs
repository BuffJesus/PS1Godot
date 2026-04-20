#if TOOLS
using Godot;
using System;

namespace PS1Godot.Tools;

// One-click test asset for Phase 2 bullet 11 (skinned meshes).
// Drops a self-contained bent-cylinder rig into the currently edited
// scene: 2-bone Skeleton3D, a cylinder mesh with per-vertex weights
// blending smoothly between the two bones, and an AnimationPlayer with
// a "wave" clip that rotates the tip bone 30° on Z back-and-forth.
//
// Wrapping MeshInstance3D is a PS1SkinnedMesh so the exporter picks it
// up through the normal path. Once stage 2 (clip baking) lands, the
// runtime will actually deform the mesh per the animation; today
// stage 1 emits the skin data and renders at bind pose.
public static class SkinnedTestBuilder
{
    private const int    Rings   = 5;     // vertex ring count along the cylinder length
    private const int    Sides   = 8;     // vertices per ring
    private const float  Height  = 2.0f;  // cylinder length (meters)
    private const float  Radius  = 0.25f;

    public static Node3D Build(string nodeName = "SkinnedTest")
    {
        var root = new Node3D { Name = nodeName };

        var skel = BuildSkeleton();
        root.AddChild(skel);

        var meshInst = BuildMesh(skel);
        root.AddChild(meshInst);

        var ap = BuildAnimationPlayer();
        root.AddChild(ap);

        return root;
    }

    private static Skeleton3D BuildSkeleton()
    {
        var skel = new Skeleton3D { Name = "Skeleton3D" };

        // Bone 0: Root — sits at the cylinder base.
        int boneRoot = skel.AddBone("Root");
        skel.SetBoneRest(boneRoot, Transform3D.Identity);
        skel.SetBonePose(boneRoot, Transform3D.Identity);

        // Bone 1: Tip — anchored at cylinder top. Rest is relative to
        // parent, so translating up by Height puts Tip at world Y=Height.
        int boneTip = skel.AddBone("Tip");
        skel.SetBoneParent(boneTip, boneRoot);
        var tipRest = new Transform3D(Basis.Identity, new Vector3(0, Height, 0));
        skel.SetBoneRest(boneTip, tipRest);
        skel.SetBonePose(boneTip, tipRest);

        return skel;
    }

    private static MeshInstance3D BuildMesh(Skeleton3D skel)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Build a cylinder lying along +Y. Each vertex:
        //   - position: (cos θ · r, y, sin θ · r)
        //   - normal: (cos θ, 0, sin θ) — outward radial
        //   - bone weights: linearly blend between Root (y=0) and Tip (y=H)
        //
        // Vertex bones: slot 0 = Root, slot 1 = Tip (other slots unused,
        // weight 0). This keeps bone indices stable regardless of ring.
        for (int r = 0; r < Rings; r++)
        {
            float t = (float)r / (Rings - 1);
            float y = t * Height;
            float weightTip = t;
            float weightRoot = 1f - t;

            for (int s = 0; s < Sides; s++)
            {
                float theta = (float)s / Sides * Mathf.Tau;
                float x = Mathf.Cos(theta) * Radius;
                float z = Mathf.Sin(theta) * Radius;

                st.SetNormal(new Vector3(Mathf.Cos(theta), 0, Mathf.Sin(theta)));
                st.SetUV(new Vector2((float)s / Sides, t));
                st.SetBones(new[] { 0, 1, 0, 0 });
                st.SetWeights(new[] { weightRoot, weightTip, 0f, 0f });
                st.AddVertex(new Vector3(x, y, z));
            }
        }

        // Triangulate: each (r, s) quad → two triangles. Godot uses
        // CCW-from-the-front-face with default back-face culling, so
        // winding matters. From the viewer OUTSIDE the cylinder at
        // side s, the quad corners are (looking inward):
        //   bottom-left  = (s,   r)
        //   bottom-right = (s+1, r)          (+θ points to viewer's right)
        //   top-left     = (s,   r+1)
        //   top-right    = (s+1, r+1)
        // CCW from that POV: bottom-left → bottom-right → top-right → top-left.
        for (int r = 0; r < Rings - 1; r++)
        {
            for (int s = 0; s < Sides; s++)
            {
                int s1 = (s + 1) % Sides;
                int i0 = r * Sides + s;            // bottom-left
                int i1 = r * Sides + s1;           // bottom-right
                int i2 = (r + 1) * Sides + s;      // top-left
                int i3 = (r + 1) * Sides + s1;     // top-right
                // Tri A: BL → BR → TR. Tri B: BL → TR → TL.
                st.AddIndex(i0); st.AddIndex(i1); st.AddIndex(i3);
                st.AddIndex(i0); st.AddIndex(i3); st.AddIndex(i2);
            }
        }

        var mesh = st.Commit();

        // Skin: one bind per bone, inverse of its rest pose so the
        // renderer's vertex-space → bone-space → deformed vertex math
        // reduces to identity when the pose equals the rest.
        var skin = new Skin();
        skin.AddBind(0, Transform3D.Identity);                                           // Root: rest is identity.
        skin.AddBind(1, new Transform3D(Basis.Identity, new Vector3(0, -Height, 0)));    // Tip: inverse of its rest offset.

        // PS1SkinnedMesh inherits from PS1MeshInstance (and thus from
        // MeshInstance3D). New() instantiates the C# type so the
        // exporter's `is PS1SkinnedMesh` check picks it up.
        //
        // MaterialOverride with CullMode=Disabled keeps the whole
        // cylinder visible regardless of which direction the triangle
        // winding considers "front" — this is a test asset, not a
        // shipped mesh, so the 2× fill-rate hit doesn't matter.
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.80f, 0.55f, 0.90f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var meshInst = new PS1SkinnedMesh
        {
            Name = "SkinnedMesh",
            Mesh = mesh,
            Skin = skin,
            MaterialOverride = mat,
            // NodePath is resolved relative to this node once we're in the tree.
            // Setting before AddChild works because Godot stores the path
            // verbatim; resolution happens lazily.
            Skeleton = new NodePath("../Skeleton3D"),
            TargetFps = 15,
            ClipNames = new[] { "wave" },
        };
        return meshInst;
    }

    private static AnimationPlayer BuildAnimationPlayer()
    {
        var anim = new Animation { Length = 1.0f, LoopMode = Animation.LoopModeEnum.Linear };

        // One rotation track on the Tip bone. AnimationPlayer.RootNode
        // defaults to NodePath("..") — the parent, i.e. SkinnedTest —
        // so track paths are resolved from there. Skeleton3D is a
        // sibling of AnimationPlayer under SkinnedTest, so the correct
        // path is just "Skeleton3D:Tip" (no leading `../`).
        // 3 keyframes: rest at 0 s and 1 s, 30° bend at 0.5 s around Z.
        int track = anim.AddTrack(Animation.TrackType.Rotation3D);
        anim.TrackSetPath(track, new NodePath("Skeleton3D:Tip"));
        var bend = new Quaternion(new Vector3(0, 0, 1), Mathf.Pi / 6f);
        anim.RotationTrackInsertKey(track, 0.0, Quaternion.Identity);
        anim.RotationTrackInsertKey(track, 0.5, bend);
        anim.RotationTrackInsertKey(track, 1.0, Quaternion.Identity);

        var lib = new AnimationLibrary();
        lib.AddAnimation("wave", anim);

        var ap = new AnimationPlayer { Name = "AnimationPlayer" };
        ap.AddAnimationLibrary("", lib);

        return ap;
    }
}
#endif
