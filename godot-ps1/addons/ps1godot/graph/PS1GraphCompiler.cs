using System.Collections.Generic;
using System.Text;

namespace PS1Godot.Graph;

// PS1Graph → Lua compiler. Slice 4a: supports print / branch / comment
// from the slice-1..3 framework. Output is a flat sequence of Lua
// statements, one per exec root, emitted in node-Id order.
//
// Slot indices below MUST match the per-kind layout in
// PS1GraphEditorDock.BuildVisualBody — they are the contract between
// the visual and the compiler. A future slice may extract the slot
// layout into a shared per-kind table so the duplication can't drift,
// but slice 4a accepts the duplication with explicit comments at each
// hardcoded port reference.
//
// Data-pin resolution: for each data input we look for an incoming
// connection. If present, walk back through the source node's matching
// output (recursive). If absent, fall back to the input node's own
// Payload string (Print's literal value mode). No Bool-producing kinds
// exist yet, so Branch's condition always resolves to the literal
// `false` — a Bool Literal + comparison nodes are slice-5 work.
public static class PS1GraphCompiler
{
    public static string Compile(PS1GraphResource resource)
    {
        if (resource?.Nodes == null) return "";

        var sb = new StringBuilder();
        string pathLabel = string.IsNullOrEmpty(resource.ResourcePath) ? "(unsaved)" : resource.ResourcePath;
        sb.AppendLine($"-- Compiled from {pathLabel}");
        sb.AppendLine($"-- {resource.Nodes.Count} node(s), {resource.Connections.Count} connection(s)");
        sb.AppendLine();

        // Index nodes by Id, build the "has incoming exec edge" set so
        // we can identify roots in one pass.
        var byId = new Dictionary<int, PS1GraphNode>();
        foreach (var n in resource.Nodes) byId[n.Id] = n;

        var hasIncomingExec = new HashSet<int>();
        foreach (var c in resource.Connections)
        {
            if (IsExecPort(byId, c.ToNodeId, c.ToPort, isInput: true))
            {
                hasIncomingExec.Add(c.ToNodeId);
            }
        }

        // Roots = exec-bearing nodes whose exec input is unconnected,
        // in Id order. Id order matches creation order, so successive
        // "+ Print" presses produce output in the order the author saw
        // them appear — predictable enough for slice 4a.
        var roots = new List<int>();
        foreach (var n in resource.Nodes)
        {
            if (!HasExecInput(n.Kind)) continue;
            if (hasIncomingExec.Contains(n.Id)) continue;
            roots.Add(n.Id);
        }
        roots.Sort();

        foreach (var rootId in roots)
        {
            EmitNode(sb, byId, resource.Connections, rootId, indent: 0);
        }

        return sb.ToString();
    }

    // ── Emission ────────────────────────────────────────────────────

    private static void EmitNode(StringBuilder sb, Dictionary<int, PS1GraphNode> byId,
                                  Godot.Collections.Array<PS1GraphConnection> conns, int nodeId, int indent)
    {
        if (!byId.TryGetValue(nodeId, out var n)) return;
        string pad = new string(' ', indent * 4);
        switch (n.Kind)
        {
            case "print":
            {
                // Print: row 0 = Exec in+out (slot 0), row 1 = String
                // in+out (slot 1). Body emits print(<resolved string>).
                string msg = ResolveStringInput(byId, conns, nodeId, slot: 1);
                sb.AppendLine($"{pad}print({msg})");
                EmitExecContinuation(sb, byId, conns, nodeId, slot: 0, indent);
                break;
            }
            case "branch":
            {
                // Branch: row 0 = Exec in + Exec out (true), row 1 =
                // right-only Exec out (false), row 2 = Bool in (left
                // only). Body emits if/then/else.
                string cond = ResolveBoolInput(byId, conns, nodeId, slot: 2);
                sb.AppendLine($"{pad}if {cond} then");
                EmitExecContinuation(sb, byId, conns, nodeId, slot: 0, indent + 1);
                sb.AppendLine($"{pad}else");
                EmitExecContinuation(sb, byId, conns, nodeId, slot: 1, indent + 1);
                sb.AppendLine($"{pad}end");
                break;
            }
            case "comment":
                // Pinless decoration — emit nothing.
                break;
            default:
                sb.AppendLine($"{pad}-- (unknown kind '{n.Kind}')");
                break;
        }
    }

    // Follow the single exec edge originating at (nodeId, slot). Each
    // exec output drives at most one continuation; GraphEdit's pin
    // model permits multiple but slice-4a semantics treat extras as
    // ambiguous (we just take the first match in connection order —
    // future validation slice will warn about multi-outbound exec).
    private static void EmitExecContinuation(StringBuilder sb, Dictionary<int, PS1GraphNode> byId,
                                              Godot.Collections.Array<PS1GraphConnection> conns,
                                              int nodeId, int slot, int indent)
    {
        foreach (var c in conns)
        {
            if (c.FromNodeId == nodeId && c.FromPort == slot)
            {
                EmitNode(sb, byId, conns, c.ToNodeId, indent);
                return;
            }
        }
        // Dangling exec edge — emit nothing. Slice-5 dangling-input
        // warnings would flag this at authoring time.
    }

    // ── Data-pin resolution ─────────────────────────────────────────

    // String input: walk back through any incoming connection, falling
    // back to the consuming node's own Payload literal when the input
    // is unconnected. Tail-recursive through chained String pass-
    // throughs (Print A → Print B → Print C).
    private static string ResolveStringInput(Dictionary<int, PS1GraphNode> byId,
                                              Godot.Collections.Array<PS1GraphConnection> conns,
                                              int nodeId, int slot)
    {
        foreach (var c in conns)
        {
            if (c.ToNodeId == nodeId && c.ToPort == slot)
            {
                return ProduceString(byId, conns, c.FromNodeId, c.FromPort);
            }
        }
        if (byId.TryGetValue(nodeId, out var n))
        {
            return EscapeLuaString(n.Payload);
        }
        return "\"\"";
    }

    // Produce a string value FROM a source node's output port. Each
    // kind that has a String output declares how to compute it here.
    private static string ProduceString(Dictionary<int, PS1GraphNode> byId,
                                         Godot.Collections.Array<PS1GraphConnection> conns,
                                         int srcId, int srcPort)
    {
        if (!byId.TryGetValue(srcId, out var n)) return "\"\"";
        switch (n.Kind)
        {
            case "print":
                // Print's String out (slot 1 right) passes through its
                // resolved String in — same value the node will itself
                // print, available for downstream Prints to forward.
                if (srcPort == 1) return ResolveStringInput(byId, conns, srcId, slot: 1);
                break;
        }
        return "\"\"";
    }

    // Bool input: no Bool-producing kinds exist in slice 4a, so any
    // connection here is currently impossible (GraphEdit's type check
    // would reject it — there's no Bool output to drag from). Default
    // to literal `false`. Slice 5 will add Bool Literal + comparison
    // kinds and switch this to walk-back semantics.
    private static string ResolveBoolInput(Dictionary<int, PS1GraphNode> byId,
                                            Godot.Collections.Array<PS1GraphConnection> conns,
                                            int nodeId, int slot)
    {
        return "false";
    }

    // ── Per-kind slot metadata ──────────────────────────────────────

    private static bool HasExecInput(string kind) => kind switch
    {
        "print"   => true,
        "branch"  => true,
        "comment" => false,
        _         => false,
    };

    // Is the given port on a node an Exec pin? Used to build the
    // "incoming exec" set and (future) relax cycle rejection to
    // exec-only. Slot indices match BuildVisualBody exactly.
    private static bool IsExecPort(Dictionary<int, PS1GraphNode> byId, int nodeId, int port, bool isInput)
    {
        if (!byId.TryGetValue(nodeId, out var n)) return false;
        return n.Kind switch
        {
            "print"  => port == 0,                            // row 0 in+out
            "branch" => port == 0 || (!isInput && port == 1), // row 0 in+out, row 1 out-only
            _        => false,
        };
    }

    // ── Escaping ────────────────────────────────────────────────────

    private static string EscapeLuaString(string s)
    {
        if (s == null) return "\"\"";
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:   sb.Append(c); break;
            }
        }
        sb.Append("\"");
        return sb.ToString();
    }
}
