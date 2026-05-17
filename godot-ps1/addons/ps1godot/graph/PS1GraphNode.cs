using Godot;

namespace PS1Godot.Graph;

// One node in a PS1Graph. Identity is the int Id (stable within the
// owning PS1GraphResource); Kind picks the visual + future compiler
// behavior; Payload is a generic per-node string (the "Print" node
// carries its message here in slice 1).
//
// Walking-skeleton fields only. Subsequent slices will add typed pins
// (each pin's value type + connection rules), per-Kind structured
// data, and visual styling overrides. Keep additions append-only —
// existing graphs in user repos must keep loading.
[GlobalClass]
public partial class PS1GraphNode : Resource
{
    [Export] public int Id { get; set; } = -1;

    // Identifies the node type within the graph kind. The dock uses
    // this to pick the visual + LineEdit hookups. The compiler (later
    // slice) will dispatch per-Kind to emit Lua. Examples for slice 1:
    // "print". Examples for future kinds: "dialogue_line",
    // "dialogue_choice", "quest_objective", "fsm_state".
    [Export] public string Kind { get; set; } = "";

    // Canvas position in the GraphEdit. Persisted so layouts survive
    // load/save cycles; the dock reads this when materializing the
    // visual GraphNode and writes it back before save.
    [Export] public Vector2 Position { get; set; } = Vector2.Zero;

    // Free-form payload. Slice 1 stores the Print node's message here.
    // Subsequent slices will replace this with per-Kind structured data
    // (dialogue text + speaker, quest objective ID, etc.), but the
    // single-string fallback is a useful safety hatch for prototyping
    // new kinds without bumping the resource schema each time.
    [Export] public string Payload { get; set; } = "";
}
