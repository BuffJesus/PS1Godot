using Godot;

namespace PS1Godot.Graph;

// PinType doubles as both the semantic label ("this pin carries an
// exec signal / a string / …") and the int that GraphEdit hands to
// SetSlot's type-compatibility check. Same-int pins connect; different-
// int pins are silently rejected by GraphEdit unless an explicit
// AddValidConnectionType bridges them.
//
// Append only — the int values are persisted indirectly via Connection
// FromPort/ToPort slot indices and via Node kinds that bake the pin
// layout in. Renumbering would silently rewire saved graphs.
public enum PinType
{
    Exec       = 0,  // white  — control-flow edge (Blueprint-style)
    String     = 1,  // yellow — strings, names, dialogue lines
    Number     = 2,  // green  — int or fp12 (kept unified for slice 2;
                     //          split if/when we need stricter typing)
    Bool       = 3,  // red    — true/false
    Vec3       = 4,  // blue   — positions, directions, colours
    EntityRef  = 5,  // purple — handles to runtime GameObjects / NPCs
}

// A single edge between two PS1GraphNodes, addressed by Id + port index
// on each side. Stored as a Resource so it survives .tres round-trips
// without custom (de)serialization. Port indices match the slot index
// passed to GraphNode.SetSlot — they identify which row's left/right
// pin is connected. Pin TYPE is determined by the node Kind's slot
// layout (looked up at materialise time), not stored here.
[GlobalClass]
public partial class PS1GraphConnection : Resource
{
    [Export] public int FromNodeId { get; set; } = -1;
    [Export] public int FromPort   { get; set; } = 0;
    [Export] public int ToNodeId   { get; set; } = -1;
    [Export] public int ToPort     { get; set; } = 0;
}
