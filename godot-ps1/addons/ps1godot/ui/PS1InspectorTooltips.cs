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

    private static bool s_didDiagFirstHandle = false;

    public override bool _CanHandle(GodotObject @object)
    {
        if (@object == null) return false;
        var type = @object.GetType();
        // Also handle Resource-typed PS1 things (PS1SoundMacro, PS1Theme,
        // PS1AudioClip, etc.). The leaf class name is the dictionary key.
        bool ok = s_docs.ContainsKey(type.Name);
        if (!s_didDiagFirstHandle)
        {
            s_didDiagFirstHandle = true;
            GD.Print($"[PS1Godot] Inspector tooltips: first _CanHandle for type={type.Name} → {ok} " +
                     $"(docs has {s_docs.Count} known types)");
        }
        return ok;
    }

    public override void _ParseBegin(GodotObject @object)
    {
        // _ParseBegin runs once per inspector rebuild, before properties
        // are added. Defer the tooltip sweep so the EditorProperty nodes
        // exist by the time we walk the tree.
        var typeName = @object.GetType().Name;
        Callable.From(() => ApplyTooltipsFor(typeName)).CallDeferred();
    }

    private static int s_diagApplyBudget = 3;

    private static void ApplyTooltipsFor(string typeName)
    {
        if (!s_docs.TryGetValue(typeName, out var props)) return;
        var inspector = EditorInterface.Singleton?.GetInspector();
        if (inspector == null) return;
        int applied = ApplyRecursive(inspector, props);
        if (s_diagApplyBudget > 0)
        {
            s_diagApplyBudget--;
            GD.Print($"[PS1Godot] Inspector tooltips: applied {applied} of {props.Count} for {typeName}");
        }
    }

    private static int ApplyRecursive(Node n, Dictionary<string, string> props)
    {
        int count = 0;
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
                count++;
            }
        }
        foreach (var c in n.GetChildren())
            if (c is Node child) count += ApplyRecursive(child, props);
        return count;
    }

    // ─── XML loader ──────────────────────────────────────────────────────

    private static Dictionary<string, Dictionary<string, string>> LoadDocsFromXml()
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        string? xmlPath = FindXmlSidecar();
        if (xmlPath == null || !File.Exists(xmlPath))
        {
            GD.PushWarning("[PS1Godot] PS1Godot.xml not found for inspector tooltips. " +
                           $"Searched: {DescribeSearchPaths()}. " +
                           "Inspector field hover descriptions will be unavailable until the XML is located.");
            return result;
        }

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
            return result;
        }

        int totalProps = 0;
        foreach (var kvp in result) totalProps += kvp.Value.Count;
        GD.Print($"[PS1Godot] Inspector tooltips loaded: {result.Count} types, {totalProps} properties from {xmlPath}");
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

    // Possible locations for PS1Godot.xml. Godot's mono runtime sometimes
    // returns an empty Assembly.Location (loaded from memory), so fall back
    // to known build-output paths under res://.godot/mono/temp/bin/<config>.
    private static string[] CandidateXmlPaths()
    {
        var paths = new System.Collections.Generic.List<string>();
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            string? loc = asm.Location;
            if (!string.IsNullOrEmpty(loc))
            {
                string dir = Path.GetDirectoryName(loc) ?? "";
                string xmlName = Path.GetFileNameWithoutExtension(loc) + ".xml";
                paths.Add(Path.Combine(dir, xmlName));
            }
        }
        catch { /* fall through to project-relative paths */ }

        // Project-relative fallbacks. ProjectSettings.GlobalizePath turns
        // res:// into a real OS path that File.Exists can stat.
        foreach (string config in new[] { "Debug", "ExportDebug", "Release" })
        {
            string p = ProjectSettings.GlobalizePath($"res://.godot/mono/temp/bin/{config}/PS1Godot.xml");
            if (!paths.Contains(p)) paths.Add(p);
        }
        return paths.ToArray();
    }

    private static string DescribeSearchPaths()
        => string.Join(", ", CandidateXmlPaths());

    private static string? FindXmlSidecar()
    {
        try
        {
            foreach (string p in CandidateXmlPaths())
            {
                if (File.Exists(p)) return p;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
#endif
