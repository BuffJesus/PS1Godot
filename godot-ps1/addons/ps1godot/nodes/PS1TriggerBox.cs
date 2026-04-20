using Godot;

namespace PS1Godot;

// World-space axis-aligned trigger volume. When the player's AABB overlaps
// this volume, the runtime fires onTriggerEnter(triggerIndex) on the Lua
// script pointed at by ScriptFile. onTriggerExit fires a few frames after
// the overlap ends (debounce handled runtime-side).
//
// Authored as a plain Node3D whose world transform (position + rotation
// collapsed to AABB corners like our colliders) defines the box in world
// space. Size is local half-extents in Godot units so scaling feels right
// in the editor.
[Tool]
[GlobalClass]
public partial class PS1TriggerBox : Node3D
{
    // Local half-extents. World AABB is computed at export by baking the
    // node's GlobalTransform into the 8 corners and taking the axis-aligned
    // extent — same approach colliders use.
    [Export] public Vector3 HalfExtents { get; set; } = new Vector3(1, 1, 1);

    // Trigger-level Lua script. The runtime calls onTriggerEnter(index) /
    // onTriggerExit(index) as top-level functions in this script (no self,
    // since trigger boxes aren't GameObjects).
    [Export(PropertyHint.File, "*.lua")]
    public string ScriptFile { get; set; } = "";

    public override void _Ready()
    {
        // Nudge the node to show up in the editor even without geometry —
        // without this, a trigger box is invisible and easy to lose in the
        // scene tree. Future Phase 3: add a gizmo drawer for the AABB.
    }
}
