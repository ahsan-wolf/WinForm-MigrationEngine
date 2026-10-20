using System.Text;
using System.Text.RegularExpressions;

namespace Migrator.Target.React.Generation;

/// <summary>CLAUDE.md §8: strip Hungarian prefixes; btnSave -> handleSaveClick; txtFirstName -> state firstName.</summary>
public static partial class Naming
{
    private static readonly HashSet<string> HungarianPrefixes = new(StringComparer.Ordinal)
    {
        "btn", "txt", "lbl", "cmb", "chk", "rdo", "dgv", "lst", "lv", "tvw", "tv", "pnl", "grp", "tab", "dtp", "nud", "pic",
        "mnu", "tsm", "tss", "tsb", "tsl", "tsc", "tmr", "cmd", "pb", "frm", "uc", "ctx", "cms", "gb", "rb", "cb", "lnk", "img", "sc", "ofd", "sfd", "fbd"
    };

    [GeneratedRegex(@"^([a-z]{1,4})(?=[A-Z0-9_])")]
    private static partial Regex PrefixRegex();

    [GeneratedRegex(@"[^A-Za-z0-9_]")]
    private static partial Regex NonIdentifierRegex();

    public static string StripPrefix(string id)
    {
        var m = PrefixRegex().Match(id);
        var stripped = m.Success && HungarianPrefixes.Contains(m.Value) ? id[m.Length..].TrimStart('_') : id;
        return stripped.Length == 0 ? id : stripped;
    }

    public static string Pascal(string text)
    {
        var clean = NonIdentifierRegex().Replace(text, "_");
        if (clean.Length == 0) return "X";
        var sb = new StringBuilder();
        foreach (var part in clean.Split('_', StringSplitOptions.RemoveEmptyEntries))
            sb.Append(char.ToUpperInvariant(part[0])).Append(part.AsSpan(1));
        var result = sb.Length == 0 ? "X" : sb.ToString();
        return char.IsDigit(result[0]) ? "N" + result : result;
    }

    public static string Camel(string text)
    {
        var p = Pascal(text);
        return char.ToLowerInvariant(p[0]) + p[1..];
    }

    /// <summary>frmOrders -> OrdersPage; ucFieldEditor -> FieldEditorControl.</summary>
    public static string Component(string unitName, bool userControl) => Pascal(StripPrefix(unitName)) + (userControl ? "Control" : "Page");

    public static string StateName(string controlId) => Camel(StripPrefix(controlId));

    /// <summary>cmdBrowse + Click -> handleBrowseClick; the form itself + Load -> handleLoad.</summary>
    public static string HandlerName(string controlId, string eventName, bool isRoot) =>
        "handle" + (isRoot ? "" : Pascal(StripPrefix(controlId))) + Pascal(eventName);
}

/// <summary>Hands out identifiers that are unique within one generated file.</summary>
public sealed class NameScope
{
    private readonly HashSet<string> _used;

    public NameScope(IEnumerable<string> reserved) => _used = new HashSet<string>(reserved, StringComparer.Ordinal);

    public string Unique(string name)
    {
        var candidate = name;
        for (var i = 2; !_used.Add(candidate); i++) candidate = name + i;
        return candidate;
    }
}
