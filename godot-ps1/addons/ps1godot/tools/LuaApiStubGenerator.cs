#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Godot;

namespace PS1Godot.Tools;

// Parses psxsplash-main/src/luaapi.hh for the structured `// Namespace.Method(args)
// -> retval` comment blocks that already live next to each bind, and emits an
// EmmyLua annotation file the Rider/VS Code Lua plugins pick up for completion
// and signature hints.
//
// Run via Project > Tools > PS1Godot: Regenerate Lua API stubs. Output is a
// single file `godot-ps1/demo/scripts/_ps1api.lua` — `---@meta` marks it as
// annotations-only so it doesn't execute.
//
// The header's comments look like:
//     // Entity.Spawn(tag, {x,y,z} [, rotY]) -> object or nil
//     // Finds the first INACTIVE GameObject whose tag matches...
//     static int Entity_Spawn(lua_State* L);
//
// The parser pulls the signature line, accumulates contiguous `//`-prefixed
// lines as docstring, and inspects argument and return-type phrasing to map
// them to EmmyLua types. Type inference is deliberately shallow — the
// original doc comment always appears above the stub, so authors get the
// real semantics even when the inferred type is `any`.
public static class LuaApiStubGenerator
{
    private const string LuaApiRelPath = "psxsplash-main/src/luaapi.hh";
    private const string OutputRelPath = "godot-ps1/demo/scripts/_ps1api.lua";

    // Matches `// Namespace.Method(args) [-> retval]`. Namespace + method are
    // captured separately; args/retval stay as raw strings for the inferrer.
    private static readonly Regex SigLine = new(
        @"^\s*//\s*(?<ns>[A-Z][A-Za-z0-9]+)\.(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<args>[^)]*)\)(?:\s*->\s*(?<ret>.+?))?\s*$",
        RegexOptions.Compiled);

    public static void Run()
    {
        string projectRoot = ResolveProjectRoot();
        string headerPath = Path.Combine(projectRoot, LuaApiRelPath);
        if (!File.Exists(headerPath))
        {
            GD.PushError($"[PS1Godot] LuaApiStubGenerator: header not found at {headerPath}");
            return;
        }

        string[] lines = File.ReadAllLines(headerPath);
        var binds = Parse(lines);
        string output = Emit(binds, LuaApiRelPath);

        string outPath = Path.Combine(projectRoot, OutputRelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, output);
        GD.Print($"[PS1Godot] Wrote {binds.Count} API stubs to {OutputRelPath}");
    }

    private sealed class Bind
    {
        public string Namespace = "";
        public string Name = "";
        public string RawArgs = "";
        public string RawReturn = "";
        public List<string> Doc = new();
    }

    private static List<Bind> Parse(string[] lines)
    {
        // Convention in luaapi.hh: structured signature comment FIRST,
        // then any number of `//` description lines, then the
        // `static int Foo_Bar(lua_State* L);` declaration. e.g.
        //
        //     // Entity.Destroy(object) -> nil
        //     // Deactivates the object (fires onDisable). Pool re-uses it.
        //     static int Entity_Destroy(lua_State* L);
        //
        // Earlier versions of this parser accumulated `prevDoc` BEFORE
        // the signature, which produced empty docs everywhere — the
        // first bug discovered when wiring up Godot script-editor
        // hover tooltips on 2026-04-27.
        var result = new List<Bind>();
        Bind? cur = null;

        void Finalize()
        {
            if (cur != null)
            {
                result.Add(cur);
                cur = null;
            }
        }

        foreach (string line in lines)
        {
            var m = SigLine.Match(line);
            if (m.Success)
            {
                Finalize();
                cur = new Bind
                {
                    Namespace = m.Groups["ns"].Value,
                    Name = m.Groups["name"].Value,
                    RawArgs = m.Groups["args"].Value.Trim(),
                    RawReturn = m.Groups["ret"].Success ? m.Groups["ret"].Value.Trim() : "",
                    Doc = new List<string>(),
                };
                continue;
            }

            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//") && cur != null)
            {
                string body = trimmed.Length > 2 ? trimmed.Substring(2).TrimStart() : "";
                cur.Doc.Add(body);
            }
            else
            {
                // Non-comment line — finalize the current entry. The
                // static declaration that lives below the doc block is
                // the typical trigger.
                Finalize();
            }
        }

        Finalize();
        return result;
    }

    private static string Emit(List<Bind> binds, string sourceRelPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---@meta");
        sb.AppendLine();
        sb.AppendLine("-- Auto-generated by LuaApiStubGenerator from " + sourceRelPath);
        sb.AppendLine("-- DO NOT EDIT BY HAND — regenerate via Project > Tools >");
        sb.AppendLine("-- PS1Godot: Regenerate Lua API stubs.");
        sb.AppendLine();

        // Helper type aliases — EmmyLua picks these up and binds operator
        // overloads / field access on returned values.
        sb.AppendLine("---@class GameObject");
        sb.AppendLine("---@class FixedPoint");
        sb.AppendLine("---@field _raw integer");
        sb.AppendLine();
        sb.AppendLine("---@class Vec3");
        sb.AppendLine("---@field x FixedPoint");
        sb.AppendLine("---@field y FixedPoint");
        sb.AppendLine("---@field z FixedPoint");
        sb.AppendLine();

        // Group binds by namespace so the output is readable.
        var byNs = new SortedDictionary<string, List<Bind>>(StringComparer.Ordinal);
        foreach (var bind in binds)
        {
            if (!byNs.TryGetValue(bind.Namespace, out var list))
            {
                list = new List<Bind>();
                byNs[bind.Namespace] = list;
            }
            list.Add(bind);
        }

        foreach (var (ns, list) in byNs)
        {
            sb.AppendLine($"---@class {ns}Module");
            sb.AppendLine($"{ns} = {{}}");
            sb.AppendLine();
            foreach (var bind in list) EmitOne(sb, bind);
        }

        return sb.ToString();
    }

    private static void EmitOne(StringBuilder sb, Bind bind)
    {
        // Original docstring → `---` comments so hover shows them.
        foreach (string doc in bind.Doc)
        {
            if (string.IsNullOrWhiteSpace(doc)) continue;
            sb.Append("--- ").AppendLine(doc);
        }

        var (paramNames, paramTypes) = ParseArgs(bind.RawArgs);
        string retType = InferReturnType(bind.RawReturn);

        for (int i = 0; i < paramNames.Count; i++)
        {
            sb.AppendLine($"---@param {paramNames[i]} {paramTypes[i]}");
        }
        if (!string.IsNullOrEmpty(retType))
        {
            sb.AppendLine($"---@return {retType}");
        }

        sb.Append($"function {bind.Namespace}.{bind.Name}(");
        for (int i = 0; i < paramNames.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(paramNames[i]);
        }
        sb.AppendLine(") end");
        sb.AppendLine();
    }

    // Parse the raw args string from the comment into parallel name/type
    // arrays. Comment form is Lua-ish:
    //   ""                                  -> no params
    //   "tag, {x,y,z} [, rotY]"             -> 3 params (brackets stripped,
    //                                          optional marker dropped — Lua
    //                                          ignores trailing nils anyway)
    //   "object, active"                    -> 2 params
    //   "callback"                          -> 1 param
    private static (List<string> names, List<string> types) ParseArgs(string raw)
    {
        var names = new List<string>();
        var types = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return (names, types);

        // Drop `[` and `]` — they're optional-marker syntax in the doc
        // comments but don't affect the emitted stubs. Preserve commas
        // that sit between brackets so arg ordering survives.
        string stripped = raw.Replace("[", "").Replace("]", "");

        // Split by comma at top level only — table literals `{x, y, z}` must
        // stay as a single arg token.
        var tokens = SplitTopLevel(stripped);
        for (int i = 0; i < tokens.Count; i++)
        {
            string tok = tokens[i].Trim();
            if (tok.Length == 0) continue;
            names.Add(NormalizeName(tok, i));
            types.Add(InferParamType(tok));
        }
        return (names, types);
    }

    private static List<string> SplitTopLevel(string s)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '{' || c == '(') depth++;
            else if (c == '}' || c == ')') depth = Math.Max(0, depth - 1);
            else if (c == ',' && depth == 0)
            {
                parts.Add(s.Substring(start, i - start));
                start = i + 1;
            }
        }
        if (start < s.Length) parts.Add(s.Substring(start));
        return parts;
    }

    private static string NormalizeName(string tok, int fallbackIdx)
    {
        string cleaned = tok.Trim().Trim('[', ']').Trim();
        if (cleaned.StartsWith("{"))
        {
            // `{x,y,z}` positional arg — use "pos" for the common case.
            return "pos";
        }
        // Strip type-hint parens like "angle (radians)"
        int parenIdx = cleaned.IndexOf('(');
        if (parenIdx >= 0) cleaned = cleaned.Substring(0, parenIdx).Trim();
        // Keep only the first identifier-like token.
        var ident = new StringBuilder();
        foreach (char c in cleaned)
        {
            if (char.IsLetterOrDigit(c) || c == '_') ident.Append(c);
            else if (ident.Length > 0) break;
        }
        string name = ident.ToString();
        if (string.IsNullOrEmpty(name)) return $"arg{fallbackIdx}";
        // Lua keywords to avoid as param names.
        return name switch
        {
            "end" or "function" or "local" or "if" or "then" or "else"
                or "return" or "true" or "false" or "nil" or "and"
                or "or" or "not" or "for" or "while" or "do" or "repeat"
                or "until" or "in" or "break" => name + "_",
            _ => name,
        };
    }

    private static string InferParamType(string tok)
    {
        string t = tok.Trim().Trim('[', ']').Trim().ToLowerInvariant();
        if (t.StartsWith("{")) return "Vec3";
        // Tokens like "pos", "position", "target", "origin", "min", "max", "from", "to"
        if (t.Contains("pos") || t.Contains("origin") ||
            t.StartsWith("min") || t.StartsWith("max") ||
            t == "from" || t == "to" || t == "dir" || t == "direction" ||
            t == "vec" || t.Contains("point"))
            return "Vec3";
        if (t == "object" || t == "target" || t == "other" || t == "self" || t == "trigger" ||
            t.StartsWith("go") || t == "handle" || t == "victim" || t == "bullet" || t == "enemy")
            return "GameObject";
        if (t == "name" || t == "clip" || t == "tagname" || t == "filename" ||
            t == "sequencename" || t == "text" || t == "canvasname" || t == "elementname" ||
            t == "s" || t == "msg" || t == "message")
            return "string";
        if (t == "active" || t == "enabled" || t == "visible" || t == "loop" ||
            t.StartsWith("is"))
            return "boolean";
        if (t == "callback" || t.EndsWith("fn") || t.Contains("func"))
            return "fun(...): any";
        if (t == "tag" || t == "index" || t == "count" || t == "frames" ||
            t == "volume" || t == "pan" || t == "pitch" ||
            t == "channel" || t == "midichannel" || t == "slot" ||
            t == "idx" || t == "i" || t == "n" || t == "button" ||
            t == "hitcount" || t == "raw" || t == "ttl" || t == "frame" ||
            t == "priority" || t == "font")
            return "integer";
        if (t == "angle" || t == "intensity" || t == "distance" || t == "speed" ||
            t == "rotx" || t == "roty" || t == "rotz" ||
            t == "shake" || t == "duration" || t == "rad" || t == "radians")
            return "FixedPoint|number";
        return "any";
    }

    private static string InferReturnType(string ret)
    {
        if (string.IsNullOrWhiteSpace(ret)) return "";
        string t = ret.Trim();
        string lower = t.ToLowerInvariant();
        if (lower == "nil") return "";
        if (lower.Contains("object") && lower.Contains("nil")) return "GameObject|nil";
        if (lower == "object") return "GameObject";
        if (lower == "boolean") return "boolean";
        if (lower.StartsWith("{") || lower.Contains("{x")) return "Vec3";
        if (lower == "number" || lower == "integer") return "number";
        if (lower == "string") return "string";
        if (lower.Contains("array") || lower.StartsWith("{") || lower.Contains("table"))
            return "table";
        return "any";
    }

    private static string ResolveProjectRoot()
    {
        // User override: PS1GODOT_ROOT env var, for CI / split checkouts.
        string? envRoot = System.Environment.GetEnvironmentVariable("PS1GODOT_ROOT");
        if (!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot)) return envRoot!;

        // Walk up from the Godot project dir (res://) looking for a folder
        // containing both godot-ps1/ and psxsplash-main/.
        string probe = ProjectSettings.GlobalizePath("res://");
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(probe); i++)
        {
            string? parent = Directory.GetParent(probe.TrimEnd('/', '\\'))?.FullName;
            if (parent == null) break;
            if (Directory.Exists(Path.Combine(parent, "psxsplash-main")) &&
                Directory.Exists(Path.Combine(parent, "godot-ps1")))
                return parent;
            probe = parent;
        }
        // Fall back to res://.. which is usually right when godot-ps1/ is the
        // inner Godot project.
        return ProjectSettings.GlobalizePath("res://../");
    }
}
#endif
