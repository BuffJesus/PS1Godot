#if TOOLS
using Godot;
using System;

namespace PS1Godot.Tools;

// One-click test asset for Phase 2 bullet 11 (skinned meshes).
// Drops a self-contained bent-cylinder rig into the currently edited
// scene: a chain of Rings bones along the cylinder's length, a
// cylinder mesh whose vertex rings are 1:1 rigidly weighted to their
// matching bone, and an AnimationPlayer with a "wave" clip that
// rotates each non-root bone a small amount on Z. Rotations stack
// along the chain, so bone N's global rotation is N × per-bone-delta
// — a five-bone chain at 7.5° per bone approximates a 30° bow at the
// tip with four visible inflection points along the way.
//
// Why a chain instead of a smooth weighted rig: PSX runtime does
// per-vertex RIGID skinning (one bone per vertex, no weight blend),
// so Godot's smooth bow curve can't be reproduced with 2 bones —
// you'd see a single rigid hinge at the bone boundary. More bones =
// more discrete steps = closer to smooth.
//
// Wrapping MeshInstance3D is a PS1SkinnedMesh so the exporter picks
// it up through the normal path.
public static class SkinnedTestBuilder
{
    private const int    Rings              = 5;     // vertex ring count along the cylinder length
    private const int    Sides              = 8;     // vertices per ring
    private const float  Height             = 2.0f;  // cylinder length (meters)
    private const float  Radius             = 0.25f;
    // Bone N's local rotation at the wave peak. Stacked along the
    // chain, ring 4's global rotation is 4 × PeakDeltaDeg = 30°.
    private const float  PeakDeltaDeg       = 7.5f;
    private const int    BoneCount          = Rings; // one bone per ring

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

        // Chain of BoneCount bones up the cylinder. Bone 0 is the root
        // at the base; bones 1..N-1 are each parented to the previous
        // with a local offset of one "ring spacing" upward. When bone K
        // rotates locally, rotation propagates to every descendant —
        // this is what stacks the per-bone delta into a bow curve.
        float ringSpacing = Height / (Rings - 1);
        int prev = skel.AddBone("Ring0");
        skel.SetBoneRest(prev, Transform3D.Identity);
        skel.SetBonePose(prev, Transform3D.Identity);
        for (int b = 1; b < BoneCount; b++)
        {
            int bone = skel.AddBone($"Ring{b}");
            skel.SetBoneParent(bone, prev);
            // Local rest: just an offset along +Y, no rotation.
            var rest = new Transform3D(Basis.Identity, new Vector3(0, ringSpacing, 0));
            skel.SetBoneRest(bone, rest);
            skel.SetBonePose(bone, rest);
            prev = bone;
        }
        return skel;
    }

    private static MeshInstance3D BuildMesh(Skeleton3D skel)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Build a cylinder lying along +Y. Each vertex ring is pinned
        // 100 % to its matching bone — ring 0 → bone 0, ring 1 → bone 1,
        // etc. On PSX that means each ring is rigid around its own bone
        // pivot; because the bones are a CHAIN with incremental
        // rotations, the overall mesh still forms a smooth bow.
        for (int r = 0; r < Rings; r++)
        {
            float t = (float)r / (Rings - 1);
            float y = t * Height;

            for (int s = 0; s < Sides; s++)
            {
                float theta = (float)s / Sides * Mathf.Tau;
                float x = Mathf.Cos(theta) * Radius;
                float z = Mathf.Sin(theta) * Radius;

                st.SetNormal(new Vector3(Mathf.Cos(theta), 0, Mathf.Sin(theta)));
                st.SetUV(new Vector2((float)s / Sides, t));
                // Slot 0 = this ring's bone, weight 1; other slots unused.
                st.SetBones(new[] { r, 0, 0, 0 });
                st.SetWeights(new[] { 1f, 0f, 0f, 0f });
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

        // Skin: one bind per bone, inverse of that bone's rest-world
        // transform. Because the chain is purely Y-translations, bone K's
        // rest world position is (0, K × ringSpacing, 0); bind-inverse
        // is translate(0, -K × ringSpacing, 0). At bind pose,
        // GetBoneGlobalPose(K) * GetBindPose(K) == Identity, so the
        // exporter bake produces zero-motion matrices at the rest frame.
        float ringSpacing = Height / (Rings - 1);
        var skin = new Skin();
        for (int b = 0; b < BoneCount; b++)
        {
            skin.AddBind(b, new Transform3D(Basis.Identity, new Vector3(0, -b * ringSpacing, 0)));
        }

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

        // One rotation track per non-root bone. Each rotates by the same
        // local delta, and because the bones are a chain the cumulative
        // global rotation grows along the cylinder — ring N's world
        // rotation is N × PeakDeltaDeg at t = 0.5, then back to identity
        // at t = 1.0. AnimationPlayer.RootNode defaults to NodePath(".."),
        // which is the SkinnedTest node, so bone track paths look like
        // "Skeleton3D:Ring1", "Skeleton3D:Ring2", etc.
        float peakRad = Mathf.DegToRad(PeakDeltaDeg);
        var bend = new Quaternion(new Vector3(0, 0, 1), peakRad);
        for (int b = 1; b < BoneCount; b++)
        {
            int track = anim.AddTrack(Animation.TrackType.Rotation3D);
            anim.TrackSetPath(track, new NodePath($"Skeleton3D:Ring{b}"));
            anim.RotationTrackInsertKey(track, 0.0, Quaternion.Identity);
            anim.RotationTrackInsertKey(track, 0.5, bend);
            anim.RotationTrackInsertKey(track, 1.0, Quaternion.Identity);
        }

        var lib = new AnimationLibrary();
        lib.AddAnimation("wave", anim);

        var ap = new AnimationPlayer { Name = "AnimationPlayer" };
        ap.AddAnimationLibrary("", lib);

        return ap;
    }
}
#endif
