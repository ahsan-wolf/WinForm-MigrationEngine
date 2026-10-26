using System.Text;
using Migrator.Engine.Support;
using Migrator.Target.Core.Generation;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.Api.Generation;

/// <summary>One ported class as a C# source file, with the line range of every member (so a compiler error can be traced back to the member that caused it).</summary>
public sealed record PortedFile(string ClassName, string Path, string Source, IReadOnlyList<PortedMember> Members);

/// <summary>`Key` is `Class#index` (the member's position in the class); lines are 1-based and inclusive.</summary>
public sealed record PortedMember(string Key, string Display, int StartLine, int EndLine);

/// <summary>
/// Re-emits a support class of the IR as C# for the API project. Method bodies are the original text; what the original used (`MessageBox`, `ConfigurationManager`,
/// `OleDb*`) compiles against the shims and the provider aliases of the project, so the logic itself is not rewritten. Members the compiler rejects are demoted
/// to a `NotSupportedException` with the original kept as a comment.
/// </summary>
public static class ClassPorter
{
    public static PortedFile Port(SupportClassIr cls, ApiPortRules rules, ProviderRules provider, ISet<string> includedClasses, ISet<string> projectTypes, ISet<string> demoted, ISet<string> demotedReasons)
    {
        var lines = new List<string>
        {
            "// Ported from the original project by Migrator.Target.Api. Generated: re-run the migration instead of editing.",
        };
        foreach (var u in rules.Usings) lines.Add($"using {u};");
        lines.Add($"using {provider.Namespace};");
        foreach (var (from, to) in provider.Aliases) lines.Add($"using {from} = {(to.Contains('.') ? to : provider.Namespace + "." + to)};");
        lines.Add("");
        lines.Add("namespace Generated.Api;");
        lines.Add("");

        var members = new List<PortedMember>();
        if (cls.Kind == "enum")
        {
            lines.Add($"public enum {cls.Name}");
            lines.Add("{");
            lines.AddRange(cls.EnumValues.Select(v => $"    {v.Name}{(v.Value is null ? "" : " = " + v.Value)},"));
            lines.Add("}");
            return new PortedFile(cls.Name, $"Ported/{cls.Name}.cs", string.Join("\n", lines) + "\n", members);
        }

        var baseType = cls.BaseType is { } b && includedClasses.Contains(Simple(b)) ? " : " + Simple(b) : "";
        lines.Add($"public {(cls.IsStatic ? "static " : "")}{(cls.Kind == "struct" ? "struct" : "class")} {cls.Name}{baseType}");
        lines.Add("{");

        for (var index = 0; index < cls.Members.Count; index++)
        {
            var m = cls.Members[index];
            var key = $"{cls.Name}#{index}";
            var start = lines.Count + 1;
            var down = demoted.Contains(key);
            var emitted = Emit(cls, m, key, down, projectTypes, demotedReasons);
            if (emitted.Count == 0) continue;
            lines.AddRange(emitted.SelectMany(e => e.Split((char)10)));
            members.Add(new PortedMember(key, $"{cls.Name}.{m.Name}", start, lines.Count));
        }
        lines.Add("}");
        return new PortedFile(cls.Name, $"Ported/{cls.Name}.cs", string.Join("\n", lines) + "\n", members);
    }

    private static List<string> Emit(SupportClassIr cls, MemberIr m, string key, bool demoted, ISet<string> projectTypes, ISet<string> reasons)
    {
        var visibility = m.Visibility switch { Visibility.Public => "public", Visibility.Protected => "protected", Visibility.Internal => "internal", _ => "private" };
        var isConst = m.Attributes.Contains("mod:const");
        var isReadonly = m.Attributes.Contains("mod:readonly");
        var staticWord = (m.IsStatic || cls.IsStatic) && !isConst ? "static " : "";
        var lines = new List<string>();
        var ignore = m.Visibility == Visibility.Public && m.Kind is MemberKind.Field or MemberKind.Property && !ServerOperations.IsDataType(m.Type, projectTypes)
            ? "    [System.Text.Json.Serialization.JsonIgnore]" : null;

        switch (m.Kind)
        {
            case MemberKind.Field:
            {
                if (ignore is not null) lines.Add(ignore);
                var init = demoted || string.IsNullOrWhiteSpace(m.Body) ? "" : " = " + m.Body;
                if (demoted && !string.IsNullOrWhiteSpace(m.Body)) lines.Add($"    // not ported ({Reason(reasons, key)}): initializer {Collapse(m.Body!)}");
                lines.Add($"    {visibility} {(isConst ? "const " : staticWord)}{(isReadonly && !isConst ? "readonly " : "")}{m.Type} {m.Name}{init};");
                break;
            }
            case MemberKind.Property:
            {
                if (ignore is not null) lines.Add(ignore);
                var body = m.Body;
                if (m.Attributes.Contains("mod:accessors") && !demoted && !string.IsNullOrWhiteSpace(body))
                    lines.Add($"    {visibility} {staticWord}{m.Type} {m.Name} {Indent(body!)}");
                else
                {
                    if (demoted) lines.Add($"    // not ported ({Reason(reasons, key)}): accessors {Collapse(body ?? "")}");
                    var init = !demoted && m.Attributes.Contains("mod:auto") && !string.IsNullOrWhiteSpace(body) ? " = " + body + ";" : "";
                    lines.Add($"    {visibility} {staticWord}{m.Type} {m.Name} {{ get; {(isReadonly ? "" : "set; ")}}}{init}");
                }
                break;
            }
            case MemberKind.Method when m.Name == ".ctor":
            {
                var parameters = Parameters(m);
                if (demoted)
                {
                    lines.Add($"    // not ported ({Reason(reasons, key)}): constructor body");
                    foreach (var l in (m.Body ?? "").Split('\n')) lines.Add("    // " + l.TrimEnd('\r'));
                    lines.Add($"    {visibility} {cls.Name}({parameters}) {{ }}");
                }
                else lines.Add($"    {visibility} {cls.Name}({parameters}) {Indent(m.Body ?? "{ }")}");
                break;
            }
            case MemberKind.Method:
            {
                var asyncWord = m.Attributes.Contains("mod:async") ? "async " : "";
                var header = $"    {visibility} {staticWord}{asyncWord}{m.Type} {m.Name}({Parameters(m)})";
                if (demoted || m.Body is null)
                {
                    lines.Add($"    // not ported ({Reason(reasons, key)}): original body below");
                    foreach (var l in (m.Body ?? "").Split('\n')) lines.Add("    // " + l.TrimEnd('\r'));
                    lines.Add($"{header.Replace("async ", "")} {{ throw new NotSupportedException(\"{cls.Name}.{m.Name} was not ported to the API: see API_REPORT.md\"); }}");
                }
                else lines.Add($"{header} {Indent(m.Body)}");
                break;
            }
        }
        return lines;
    }

    private static string Parameters(MemberIr m) =>
        string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}{(p.DefaultValue is null ? "" : " = " + p.DefaultValue)}"));

    /// <summary>Method text starts at its own `{`; the following lines already carry the original indentation, so only the first line needs the member indent.</summary>
    private static string Indent(string body) => body.Replace("\r", "");

    private static string Reason(ISet<string> reasons, string key) => reasons.FirstOrDefault(r => r.StartsWith(key + "|", StringComparison.Ordinal))?[(key.Length + 1)..] ?? "does not compile on .NET 10";

    private static string Collapse(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim() is { Length: > 100 } t ? t[..100] + "..." : System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    private static string Simple(string type)
    {
        var generic = type.IndexOf('<');
        if (generic >= 0) type = type[..generic];
        return type[(type.LastIndexOf('.') + 1)..].Trim();
    }
}
