using Godot;

namespace PS1Godot;

// A PS1MeshInstance that additionally carries skeleton + animation data.
// At export time the mesh's bind-pose is written through the standard
// mesh pipeline; the skin data (per-triangle bone indices) and baked
// animation frames are emitted as a side-table the runtime indexes by
// GameObject. Play clips via `SkinnedAnim.Play("name")` in Lua.
//
// Stage 0 (current): node type + property surface only. Exporter detects
// but does not yet write the skin data block. Stages 1+ land bone
// baking and animation clip baking in separate turns — see
// ROADMAP.md Phase 2 bullet 11.
//
// Authoring: set `Mesh` to a mesh with bone weights (imported from
// FBX/GLTF with a skeleton), point `Skeleton` at the scene's
// `Skeleton3D` (standard Godot behaviour — inherited), then set
// `AnimationPlayer` to the node whose `AnimationLibrary` defines the
// clips you want baked. `TargetFps` controls sampling resolution —
// 15 is plenty for PS1 dialog animations, 30 for combat.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_skinned_mesh.svg")]
public partial class PS1SkinnedMesh : PS1MeshInstance
{
    [ExportGroup("PS1 / Skinning")]
    // Path (relative to this node) to the AnimationPlayer whose
    // AnimationLibraries contain the clips to bake. If empty, exporter
    // walks up to the scene root looking for the first AnimationPlayer.
    [Export] public NodePath AnimationPlayerPath { get; set; } = new NodePath();

    // Sampling rate for baking bone matrices (frames per second).
    // Lower values save memory and splashpack size; 15 is usually
    // sufficient for PS1 character animation.
    [Export(PropertyHint.Range, "1,30,1,suffix:fps")]
    public int TargetFps { get; set; } = 15;

    // Which clips to bake. If empty, every animation in the
    // AnimationPlayer is baked. Authored as clip names so renaming
    // the AnimationPlayer doesn't silently change the export.
    [Export] public string[] ClipNames { get; set; } = System.Array.Empty<string>();

    // Skinned characters use a snap-disabled variant of the PS1 shader.
    // The 320×240 vertex snap collapses bone-driven verts onto the same
    // grid cell at typical editor / gameplay camera distances, fragmenting
    // the body into grey-patched triangles. World geometry keeps snap on
    // (that's where the PS1 wobble belongs); skinned meshes opt out by
    // default. Override this by setting an explicit MaterialOverride
    // (the editor-applied default only kicks in when none is set).
    public override void _EnterTree()
    {
        if (MaterialOverride == null)
        {
            var mat = ResourceLoader.Load<Material>(
                "res://addons/ps1godot/shaders/ps1_skinned.tres");
            if (mat != null) MaterialOverride = mat;
        }
        // base._EnterTree sees MaterialOverride set above, skips its own
        // default-material apply, then runs the AABB padding pass — which
        // skinned meshes still need because bone motion expands the
        // rendered footprint beyond the static mesh AABB.
        base._EnterTree();
    }
}
