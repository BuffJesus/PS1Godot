#if TOOLS
using Godot;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace PS1Godot.UI;

// Godot 4.7-dev's mono integration has CSharpScript::get_documentation()
// returning an empty Vector — see modules/mono/csharp_script.h:244 (TODO).
// Result: the XML doc-comments we emit via <GenerateDocumentationFile> never
// reach the inspector as field tooltips, no matter how well the comments are
// formatted.
//
// Workaround: parse PS1Godot.xml ourselves and apply tooltips by walking the
// EditorInspector tree, finding each EditorProperty by GetEditedProperty(),
// and setting TooltipText. Fires on every ParseBegin (= each time the
// inspector rebuilds for a selected node), with a deferred call so the
// EditorProperty controls have actually been added by the time we look.
[Tool]
public partial class PS1InspectorTooltips : EditorInspectorPlugin
{
    // (className, propertyName) → tooltip text. Class name is the C# leaf
    // name (e.g. "PS1MeshInstance"), not the full namespace-qualified one,
    // because that's what GodotObject.GetType().Name returns.
    private static readonly Dictionary<string, Dictionary<string, string>> s_docs
        = LoadDocsFromXml();

    public override bool _CanHandle(GodotObject @object)
    {
        if (@object == null) return false;
        var type = @object.GetType();
        // Also handle Resource-typed PS1 things (PS1SoundMacro, PS1Theme,
        // PS1AudioClip, etc.). The leaf class name is the dictionary key.
        return s_docs.ContainsKey(type.Name);
    }

    public override void _ParseBegin(GodotObject @object)
    {
        // _ParseBegin runs once per inspector rebuild, before properties
        // are added. Defer the tooltip sweep so the EditorProperty nodes
        // exist by the time we walk the tree.
        var typeName = @object.GetType().Name;
        Callable.From(() => ApplyTooltipsFor(typeName)).CallDeferred();
    }

    private static void ApplyTooltipsFor(string typeName)
    {
        if (!s_docs.TryGetValue(typeName, out var props)) return;
        var inspector = EditorInterface.Singleton?.GetInspector();
        if (inspector == null) return;
        ApplyRecursive(inspector, props);
    }

    private static void ApplyRecursive(Node n, Dictionary<string, string> props)
    {
        if (n is EditorProperty ep)
        {
            string propName = ep.GetEditedProperty();
            if (props.TryGetValue(propName, out string? tooltip))
            {
                ep.TooltipText = tooltip;
                // Also propagate to the property's label child so hovering
                // the property name (not just the editor) shows the tooltip.
                foreach (var c in ep.GetChildren())
                {
                    if (c is Control ctl) ctl.TooltipText = tooltip;
                }
            }
        }
        foreach (var c in n.GetChildren())
            if (c is Node child) ApplyRecursive(child, props);
    }

    // ─── XML loader ──────────────────────────────────────────────────────

    private static Dictionary<string, Dictionary<string, string>> LoadDocsFromXml()
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        string xmlPath = FindXmlSidecar();
        if (xmlPath == null || !File.Exists(xmlPath)) return result;

        try
        {
            using var reader = XmlReader.Create(xmlPath);
            string? lastMemberName = null;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "member")
                {
                    lastMemberName = reader.GetAttribute("name");
                }
                else if (reader.NodeType == XmlNodeType.Element && reader.Name == "summary"
                         && lastMemberName != null)
                {
                    string summary = reader.ReadElementContentAsString();
                    StoreSummary(result, lastMemberName, summary);
                    lastMemberName = null;
                }
            }
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"[PS1Godot] Failed to parse PS1Godot.xml for inspector tooltips: {e.Message}");
        }
        return result;
    }

    private static void StoreSummary(
        Dictionary<string, Dictionary<string, string>> result,
        string memberName, string summary)
    {
        // memberName format: "P:PS1Godot.PS1MeshInstance.BitDepth" for
        // properties, "F:..." for fields, "T:..." for types, "M:..." for
        // methods. Inspector tooltips only need P: and F: entries.
        if (memberName.Length < 3) return;
        char kind = memberName[0];
        if (kind != 'P' && kind != 'F') return;
        if (memberName[1] != ':') return;

        string fqn = memberName.Substring(2);
        int lastDot = fqn.LastIndexOf('.');
        if (lastDot < 0) return;
        string typeFqn = fqn.Substring(0, lastDot);
        string propName = fqn.Substring(lastDot + 1);

        // Skip Godot's auto-generated MethodName / PropertyName / SignalName
        // nested types — they're internal scaffolding, not user-facing props.
        if (typeFqn.EndsWith(".MethodName") ||
            typeFqn.EndsWith(".PropertyName") ||
            typeFqn.EndsWith(".SignalName"))
            return;

        // Use the leaf class name as the dict key. GodotObject.GetType().Name
        // returns just "PS1MeshInstance", not the namespace-qualified form.
        int leafDot = typeFqn.LastIndexOf('.');
        string leafName = leafDot >= 0 ? typeFqn.Substring(leafDot + 1) : typeFqn;

        if (!result.TryGetValue(leafName, out var props))
        {
            props = new Dictionary<string, string>();
            result[leafName] = props;
        }
        props[propName] = NormalizeSummary(summary);
    }

    // XML doc summaries arrive with leading whitespace + line breaks from
    // the original /// comment block. Collapse to a single paragraph for
    // the tooltip popup.
    private static string NormalizeSummary(string raw)
    {
        // Trim, collapse runs of whitespace + newlines to single spaces.
        string s = raw.Trim();
        s = Regex.Replace(s, @"\s+", " ");
        return s;
    }

    private static string? FindXmlSidecar()
    {
        // The XML sidecar is emitted next to the compiled DLL by the C#
        // compiler when GenerateDocumentationFile=true. Locate the running
        // assembly and look for "<assemblyName>.xml" in the same directory.
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            string? loc = asm.Location;
            if (string.IsNullOrEmpty(loc)) return null;
            string dir = Path.GetDirectoryName(loc) ?? "";
            string xmlName = Path.GetFileNameWithoutExtension(loc) + ".xml";
            string full = Path.Combine(dir, xmlName);
            return File.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }
}
#endif
