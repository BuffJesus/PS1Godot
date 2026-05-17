using Godot;

namespace PS1Godot.Graph;

// A single edge between two PS1GraphNodes, addressed by Id + port index
// on each side. Stored as a Resource so it survives .tres round-trips
// without custom (de)serialization. The walking skeleton uses one port
// per side per node; the typed-pin model (D0 follow-up) will widen Port
// indices but keep the same Connection shape.
[GlobalClass]
public partial class PS1GraphConnection : Resource
{
    [Export] public int FromNodeId { get; set; } = -1;
    [Export] public int FromPort   { get; set; } = 0;
    [Export] public int ToNodeId   { get; set; } = -1;
    [Export] public int ToPort     { get; set; } = 0;
}
