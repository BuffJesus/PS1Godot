using Godot;

namespace PS1Godot;

// Wraps a subtree of high-detail MeshInstance3D nodes (typically an
// imported FBX prop) intended ONLY for the prerendered-background bake
// — never rendered at runtime. The PSX runtime gets axis-aligned
// collision boxes per inner MeshInstance3D so the player still bumps
// into the visible silhouette of the prop, but pays zero render cost.
//
// Why a wrapper: SceneCollector's auto-export branch picks up any raw
// MeshInstance3D not already under a PS1Player and emits it as a normal
// GameObject — fine for low-poly authored geometry, terrible for a
// 2,000-tri Unreal-grade table. Wrapping the FBX in PS1Backdrop tells
// the exporter "skip render-side emission entirely; just produce
// collision."
//
// Authoring:
//   PS1Backdrop
//   └── tavern_table.fbx (instanced PackedScene)
//       └── SM_PROP_table_01 (MeshInstance3D — emits as collision only)
//
// Use cases: Resident Evil / FFVII fixed-camera scenes where every prop
// in view is in the BG image, and only the player avatar (+ a few
// dynamic actors) is rendered live.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_backdrop.svg")]
public partial class PS1Backdrop : Node3D
{
    /// <summary>
    /// When true (default), each inner MeshInstance3D contributes a
    /// world-AABB collider so the player can't walk through visible
    /// props. Turn off for purely decorative props above eye-line
    /// (chandeliers, ceiling beams) where collision would just be a
    /// nuisance.
    /// </summary>
    [Export] public bool EmitCollision { get; set; } = true;
}
