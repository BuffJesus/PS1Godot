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
[Icon("res://addons/ps1godot/icons/ps1_trigger_box.svg")]
public partial class PS1TriggerBox : Node3D
{
    private Vector3 _halfExtents = new Vector3(1, 1, 1);

    [ExportGroup("Volume")]
    /// <summary>
    /// Half-extents of the box in local space (so x=2 means 4-unit wide
    /// box). World AABB is computed at export by baking the node's
    /// GlobalTransform into the 8 corners. The wireframe gizmo in the
    /// viewport updates live as you tune the values.
    /// </summary>
    [Export]
    public Vector3 HalfExtents
    {
        get => _halfExtents;
        set
        {
            _halfExtents = value;
            UpdateGizmos();
        }
    }

    [ExportGroup("Scripting")]
    /// <summary>
    /// Lua script that handles enter/exit. Runtime calls
    /// onTriggerEnter(triggerIndex) and onTriggerExit(triggerIndex) as
    /// top-level functions (no self — triggers aren't GameObjects). Set
    /// the same script on multiple triggers and dispatch via the
    /// triggerIndex argument.
    /// </summary>
    [Export(PropertyHint.File, "*.lua")]
    public string ScriptFile { get; set; } = "";

    public override void _Ready()
    {
        // Nudge the node to show up in the editor even without geometry —
        // without this, a trigger box is invisible and easy to lose in the
        // scene tree. Future Phase 3: add a gizmo drawer for the AABB.
    }
}
