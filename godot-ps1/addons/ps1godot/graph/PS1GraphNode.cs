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
    // Slice D1 added Payloads[] below for multi-string kinds (Line,
    // Choice). Payload stays as the single-string fallback for kinds
    // that only need one (Print message, Branch "true"/"false", Bool
    // Literal "true"/"false", Comment text).
    [Export] public string Payload { get; set; } = "";

    // Multi-string payload — appended at slice D1a for kinds that
    // need more than one literal value. Line uses
    // [0]=text, [1]=speaker. Choice uses [0..n-1]=option texts.
    // Append-only: existing graphs with Payloads = [] still load
    // and resolve their single-string Payload via the same kind-
    // specific accessor that reads Payloads first, then falls back
    // to Payload when Payloads is empty.
    [Export] public Godot.Collections.Array<string> Payloads { get; set; } = new();

    // Read the i-th payload string, falling back to the legacy single
    // Payload field for index 0 when Payloads is empty. Use this from
    // BuildVisualBody / compiler instead of indexing Payloads directly
    // so legacy .tres files keep loading correctly.
    public string GetPayload(int i)
    {
        if (Payloads != null && i < Payloads.Count) return Payloads[i] ?? "";
        if (i == 0) return Payload ?? "";
        return "";
    }

    // Write the i-th payload, growing Payloads as needed. Index 0
    // mirrors to the legacy Payload field too so the resource keeps
    // round-tripping cleanly even for single-string kinds — useful
    // because Godot's resource inspector still shows the Payload
    // field independently.
    public void SetPayload(int i, string value)
    {
        if (Payloads == null) Payloads = new Godot.Collections.Array<string>();
        while (Payloads.Count <= i) Payloads.Add("");
        Payloads[i] = value ?? "";
        if (i == 0) Payload = value ?? "";
    }

    // Enabled tri-state (UE port-plan pick #2, mirrors UE's
    // ENodeEnabledState). Default Enabled keeps every existing .tres
    // unchanged. Disabled excludes the node from the compiled output —
    // the compiler chases past it through linear `next` chains so the
    // surrounding graph still flows. DevelopmentOnly emits normally
    // today; a future slice can add a build-time strip toggle.
    public enum NodeEnabledState
    {
        Enabled = 0,
        Disabled = 1,
        DevelopmentOnly = 2,
    }

    [Export] public NodeEnabledState EnabledState { get; set; } = NodeEnabledState.Enabled;

    public bool IsDisabled        => EnabledState == NodeEnabledState.Disabled;
    public bool IsDevelopmentOnly => EnabledState == NodeEnabledState.DevelopmentOnly;
}
