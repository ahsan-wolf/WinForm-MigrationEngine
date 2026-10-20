using System.Text.RegularExpressions;
using Migrator.Engine.Projections;
using Migrator.Target.React.Generation;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.React.Translation;

/// <summary>
/// Which containers of a page the code-behind adds controls to at run time (`panel.Controls.Add(...)`) and which tab controls it drives
/// (`tab.TabPages`, `tab.SelectedTab`, `tab.TabCount`). The generator (model, markup) and the translator (statements) must agree on these sets,
/// so both ask this one class.
/// </summary>
internal static partial class ContainerUsage
{
    private static readonly HashSet<string> HostTypes = new(StringComparer.Ordinal)
    {
        "System.Windows.Forms.Panel", "System.Windows.Forms.FlowLayoutPanel", "System.Windows.Forms.GroupBox", "System.Windows.Forms.TabPage"
    };

    public const string TabPageType = "System.Windows.Forms.TabPage";
    public const string TabControlType = "System.Windows.Forms.TabControl";

    public static IEnumerable<ControlNodeIr> Walk(ControlNodeIr node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var d in Walk(child)) yield return d;
    }

    /// <summary>The text of every method with a body: what usage is looked up in.</summary>
    public static string CodeOf(UnitIr unit) =>
        string.Join("\n", unit.Page.Members.Where(m => m.Kind == MemberKind.Method && m.Body != null).Select(m => m.Body));

    /// <summary>Panels, group boxes and tab pages (not the form itself) that the code calls `.Controls` on.</summary>
    public static HashSet<string> Hosts(UnitIr unit)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (unit.Page.RootControl is not { } root) return result;
        var code = CodeOf(unit);
        var mentioned = ControlsRegex().Matches(code).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        foreach (var node in Walk(root).Skip(1))
            if (mentioned.Contains(node.Id) && HostTypes.Contains(node.Effective())) result.Add(node.Id);
        return result;
    }

    /// <summary>Tab control id -> ids of its pages in designer order.</summary>
    public static Dictionary<string, List<string>> TabPages(ControlNodeIr root)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in Walk(root).Skip(1).Where(n => n.Effective() == TabControlType))
            map[node.Id] = node.Children.Where(ch => ch.Effective() == TabPageType).Select(ch => ch.Id).ToList();
        return map;
    }

    /// <summary>Tab control ids the code uses through `TabPages`, `TabCount` or `SelectedTab`, and whether `SelectedTab` (its Name/Text) is among them.</summary>
    public static Dictionary<string, bool> TabUsage(UnitIr unit)
    {
        var usage = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (Match m in TabMemberRegex().Matches(CodeOf(unit)))
            usage[m.Groups[1].Value] = usage.GetValueOrDefault(m.Groups[1].Value) || m.Groups[2].Value == "SelectedTab";
        return usage;
    }

    [GeneratedRegex(@"\b(\w+)\s*\.\s*Controls\b")]
    private static partial Regex ControlsRegex();

    [GeneratedRegex(@"\b(\w+)\s*\.\s*(TabPages|TabCount|SelectedTab)\b")]
    private static partial Regex TabMemberRegex();
}
