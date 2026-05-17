using Godot;
using Godot.Collections;

namespace PS1Godot.Graph;

// Top-level .tres for one authored graph. Holds the node + connection
// lists plus a monotonic Id counter so freshly-added nodes get unique
// Ids even after deletions. Saved with ResourceSaver; reloaded with
// ResourceLoader, no custom serialization.
//
// Walking skeleton: one graph kind ("untyped") backed by a single
// generic node type ("print"). The Kind field on the resource is here
// so future graph kinds (dialogue, quest, fsm, script) can register
// against the same framework without a second resource type — the dock
// flips palette + compiler dispatch off this field.
[GlobalClass]
public partial class PS1GraphResource : Resource
{
    // Graph-kind tag. Default empty for the slice-1 placeholder kind;
    // D1 onwards will set this to "dialogue", "quest", "fsm", "script".
    [Export] public string Kind { get; set; } = "";

    [Export] public Array<PS1GraphNode>       Nodes       { get; set; } = new();
    [Export] public Array<PS1GraphConnection> Connections { get; set; } = new();

    // Monotonic counter for unique node Ids. Never reused — deleting
    // a node leaves a hole in the Id sequence, which is fine because
    // Connections address nodes by Id, not by list index. Stable Ids
    // also make Undo/Redo (future slice) cheap.
    [Export] public int NextNodeId { get; set; } = 0;

    // Allocate a fresh Id for a new node. Caller is responsible for
    // appending the node to Nodes once it has the Id stamped.
    public int AllocateId()
    {
        int id = NextNodeId;
        NextNodeId++;
        return id;
    }
}
