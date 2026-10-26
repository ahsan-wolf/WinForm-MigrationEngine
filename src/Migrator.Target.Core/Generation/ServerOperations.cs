using System.Text.RegularExpressions;
using Migrator.Engine.Support;
using Migrator.Target.Core.Translation;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.Core.Generation;

public sealed record ServerParameter(string Name, string Type, bool ByRef);

/// <summary>A public method of a server-bound class: one REST operation.</summary>
public sealed record ServerMethod(string Name, IReadOnlyList<ServerParameter> Parameters, string ReturnType, bool IsStatic, bool IsAsync, string Route = "")
{
    public bool HasByRefParameters => Parameters.Any(p => p.ByRef);
    public bool ReturnsValue => !ReturnType.Trim().Equals("void", StringComparison.Ordinal) && !ReturnType.Trim().Equals("Task", StringComparison.Ordinal);
}

/// <summary>A public instance field or property of a server-bound class whose value is plain data: it travels with every call (the record the page filled in).</summary>
public sealed record ServerStateMember(string Name, string Type);

/// <summary>A class whose methods talk to a database, the file system or configuration (see `rules/server-bound.json`): it runs on the server, the page calls it.</summary>
public sealed record ServerClass(string Name, bool IsStatic, string Marker, IReadOnlyList<ServerStateMember> State, IReadOnlyList<ServerMethod> Methods);

/// <summary>
/// Finds the server-bound classes of the project's support code and their public operations. The same rule that leaves such classes out of the browser app
/// (a class where at least `classThreshold` of its methods use a server-side marker) decides here, so the browser client and the generated API always agree.
/// </summary>
public static class ServerOperations
{
    private static readonly HashSet<string> DataTypes = new(StringComparer.Ordinal)
    {
        "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "decimal", "string", "char", "object",
        "Boolean", "Byte", "Int16", "Int32", "Int64", "Single", "Double", "Decimal", "String", "Char", "DateTime", "TimeSpan", "Guid", "DateTimeOffset"
    };

    public static IReadOnlyList<ServerClass> Find(SupportFileIr? support, ServerBoundRules rules)
    {
        if (support is null || rules.IsEmpty) return Array.Empty<ServerClass>();
        var own = support.Classes.Where(c => !c.External).ToList();
        var known = new HashSet<string>(own.Select(c => c.Name), StringComparer.Ordinal);
        var result = new List<ServerClass>();

        foreach (var cls in own.Where(c => c.Kind == "class"))
        {
            var methods = cls.Members.Where(m => m.Kind == MemberKind.Method && m.Name != ".ctor" && m.Body != null).ToList();
            var marked = methods.Select(m => rules.Match(m.Body)).Where(x => x is not null).ToList();
            if (methods.Count < 2 || marked.Count < methods.Count * rules.ClassThreshold) continue;

            var operations = methods.Where(m => m.Visibility == Visibility.Public)
                .Select(m => new ServerMethod(m.Name,
                    m.Parameters.Select(p => new ServerParameter(p.Name, StripRef(p.Type), IsByRef(p.Type))).ToList(),
                    m.Type, m.IsStatic, m.Attributes.Contains("mod:async") || m.Type.StartsWith("Task", StringComparison.Ordinal)))
                .ToList();
            // A name used by several overloads is told apart by its parameter count (then by its position): the API route and the browser client agree on it.
            operations = operations.Select((m, i) => m with { Route = operations.Count(o => o.Name == m.Name) == 1 ? m.Name
                : operations.Count(o => o.Name == m.Name && o.Parameters.Count == m.Parameters.Count) == 1 ? $"{m.Name}-{m.Parameters.Count}" : $"{m.Name}-{m.Parameters.Count}-{i}" }).ToList();
            var state = cls.Members
                .Where(m => m.Kind is MemberKind.Field or MemberKind.Property && m.Visibility == Visibility.Public && !m.IsStatic
                            && !m.Attributes.Contains("mod:const") && !m.Attributes.Contains("mod:readonly") && IsDataType(m.Type, known))
                .Select(m => new ServerStateMember(m.Name, m.Type))
                .ToList();
            result.Add(new ServerClass(cls.Name, cls.IsStatic, marked[0]!, state, operations));
        }
        return result;
    }

    /// <summary>True for plain data: primitives, strings, dates, enums and classes of the project, and arrays / lists / dictionaries of them (a DataSet or a connection is not).</summary>
    public static bool IsDataType(string type, ISet<string> projectTypes)
    {
        type = type.Trim().TrimEnd('?');
        if (type.EndsWith("[]", StringComparison.Ordinal)) return IsDataType(type[..^2], projectTypes);
        var generic = Regex.Match(type, @"^(?:\w+\.)*(List|IList|IEnumerable|ICollection|HashSet|Dictionary|IDictionary|KeyValuePair|Nullable|ArrayList)<(.+)>$");
        if (generic.Success) return SplitArguments(generic.Groups[2].Value).All(a => IsDataType(a, projectTypes));
        if (type == "ArrayList") return true;
        var simple = type[(type.LastIndexOf('.') + 1)..];
        return DataTypes.Contains(simple) || projectTypes.Contains(simple);
    }

    private static IEnumerable<string> SplitArguments(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '<') depth++;
            else if (text[i] == '>') depth--;
            else if (text[i] == ',' && depth == 0) { yield return text[start..i]; start = i + 1; }
        }
        yield return text[start..];
    }

    private static bool IsByRef(string type) => type.StartsWith("ref ", StringComparison.Ordinal) || type.StartsWith("out ", StringComparison.Ordinal);

    private static string StripRef(string type) => IsByRef(type) ? type[4..] : type;
}
