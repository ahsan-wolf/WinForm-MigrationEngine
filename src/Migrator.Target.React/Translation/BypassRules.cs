using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Migrator.Target.React.Translation;

/// <summary>
/// Windows-specific APIs a browser app cannot call (registry, clipboard, directory services, process start, ...), loaded from a data file
/// (<c>rules/windows-bypass.json</c>; extend per project without a rebuild). A simple statement that uses one is bypassed: kept as a
/// `// BYPASSED` comment and listed in the report, instead of becoming a TODO that blocks the method.
/// </summary>
public sealed class BypassRules
{
    public static BypassRules Empty { get; } = new();

    private readonly Dictionary<string, HashSet<string>?> _types = new(StringComparer.Ordinal);
    private readonly List<string> _namespaces = new();

    public int Count => _types.Count + _namespaces.Count;

    public static BypassRules Load(string? path)
    {
        if (path is null || !File.Exists(path)) return Empty;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var rules = new BypassRules();
        if (doc.RootElement.TryGetProperty("namespaces", out var namespaces))
            foreach (var ns in namespaces.EnumerateArray()) rules._namespaces.Add(ns.GetString()!);
        if (doc.RootElement.TryGetProperty("types", out var types))
            foreach (var t in types.EnumerateArray())
            {
                var name = t.GetProperty("name").GetString()!;
                rules._types[name] = t.TryGetProperty("members", out var members)
                    ? members.EnumerateArray().Select(m => m.GetString()!).ToHashSet(StringComparer.Ordinal)
                    : null;
            }
        return rules;
    }

    /// <summary>The first Windows-specific API the statement uses (e.g. <c>Registry</c>, <c>Process.Start</c>), or null. A name the code declares itself (local, field, control) never matches.</summary>
    public string? Match(SyntaxNode statement, Func<string, bool> isDeclaredName)
    {
        if (Count == 0) return null;

        foreach (var node in statement.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case IdentifierNameSyntax id when _types.TryGetValue(id.Identifier.ValueText, out var members) && !isDeclaredName(id.Identifier.ValueText):
                    if (members is null) return id.Identifier.ValueText;
                    if (id.Parent is MemberAccessExpressionSyntax ma && ma.Expression == id && members.Contains(ma.Name.Identifier.ValueText))
                        return $"{id.Identifier.ValueText}.{ma.Name.Identifier.ValueText}";
                    break;

                case MemberAccessExpressionSyntax or QualifiedNameSyntax when node.Parent is not (MemberAccessExpressionSyntax or QualifiedNameSyntax):
                    var text = node.ToString();
                    foreach (var ns in _namespaces)
                        if (text.StartsWith(ns + ".", StringComparison.Ordinal)) return ns;
                    break;
            }
        }
        return null;
    }
}
