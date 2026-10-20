using Migrator.Engine.Assemblies;

namespace Migrator.Target.React.Contracts;

public enum TsKind { String, Guid, Date, Number, Boolean, Dto, Enum, Array, Record, Void, Unknown }

/// <summary>A mapped TypeScript type plus enough structure for mock values and fixtures.</summary>
public sealed record TsType(string Text, TsKind Kind, string? ContractType = null, TsType? Element = null);

/// <summary>CLAUDE.md §8 type mapping from the C# type strings in contract.json to TypeScript. Records every contract type it touches.</summary>
public sealed class TsTypeMapper
{
    private static readonly HashSet<string> ListLike = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.List", "System.Collections.Generic.IList", "System.Collections.Generic.IEnumerable",
        "System.Collections.Generic.ICollection", "System.Collections.Generic.IReadOnlyList", "System.Collections.Generic.IReadOnlyCollection",
        "System.Collections.Generic.HashSet", "System.Collections.Generic.Queue", "System.Collections.Generic.Stack",
        "System.Collections.ObjectModel.Collection", "System.Collections.ObjectModel.ObservableCollection"
    };

    private static readonly HashSet<string> DictionaryLike = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.Dictionary", "System.Collections.Generic.IDictionary", "System.Collections.Generic.SortedDictionary",
        "System.Collections.Generic.IReadOnlyDictionary"
    };

    private readonly IReadOnlyDictionary<string, TypeContract> _types;
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);
    private readonly HashSet<string> _noted = new(StringComparer.Ordinal);

    public TsTypeMapper(IEnumerable<TypeContract> types) => _types = types.ToDictionary(t => t.FullName, StringComparer.Ordinal);

    /// <summary>Full names of every data type (DTO or enum) mapped so far.</summary>
    public SortedSet<string> Referenced { get; } = new(StringComparer.Ordinal);

    /// <summary>Unmapped / inferred types, one line each (feeds the migration report).</summary>
    public List<string> Notes { get; } = new();

    /// <summary>Flattened, collision-free TypeScript name: Vendor.Api+StatusKind -> ApiStatusKind.</summary>
    public string NameOf(string fullName)
    {
        if (_names.TryGetValue(fullName, out var existing)) return existing;
        var simple = fullName[(fullName.LastIndexOf('.') + 1)..].Replace("+", "");
        var candidate = simple;
        if (_taken.Contains(candidate))
        {
            var ns = fullName.Contains('.') ? fullName[..fullName.LastIndexOf('.')] : "";
            candidate = Generation.Naming.Pascal(ns[(ns.LastIndexOf('.') + 1)..]) + simple;
        }
        for (var i = 2; !_taken.Add(candidate); i++) candidate = simple + i;
        _names[fullName] = candidate;
        return candidate;
    }

    public TsType Map(string csType)
    {
        var cs = csType.Trim();

        if (cs == "byte[]") return new TsType("string", TsKind.String); // base64
        if (cs.EndsWith("[]", StringComparison.Ordinal))
        {
            var element = Map(cs[..^2]);
            return new TsType(Wrap(element.Text) + "[]", TsKind.Array, null, element);
        }
        if (cs.EndsWith(']')) return Unknown(cs, "multi-dimensional array");

        var lt = cs.IndexOf('<');
        if (lt > 0 && cs.EndsWith('>'))
        {
            var name = cs[..lt];
            var args = SplitTopLevel(cs[(lt + 1)..^1]);
            if (name == "System.Nullable" && args.Count == 1)
            {
                var inner = Map(args[0]);
                return inner with { Text = inner.Text + " | null" };
            }
            if (ListLike.Contains(name) && args.Count == 1)
            {
                var element = Map(args[0]);
                return new TsType(Wrap(element.Text) + "[]", TsKind.Array, null, element);
            }
            if (DictionaryLike.Contains(name) && args.Count == 2)
            {
                var key = Map(args[0]);
                var value = Map(args[1]);
                return new TsType($"Record<{(key.Kind == TsKind.Number ? "number" : "string")}, {value.Text}>", TsKind.Record, null, value);
            }
            return Unknown(cs, "generic type without a mapping");
        }

        switch (cs)
        {
            case "string": case "System.String": case "char": case "System.Char": return new TsType("string", TsKind.String);
            case "bool": case "System.Boolean": return new TsType("boolean", TsKind.Boolean);
            case "byte": case "sbyte": case "short": case "ushort": case "int": case "uint": case "long": case "ulong":
            case "float": case "double": case "System.Decimal": case "System.Single": case "System.Double":
            case "System.Int16": case "System.Int32": case "System.Int64": case "System.Byte": case "System.UInt32": case "System.UInt64":
                return new TsType("number", TsKind.Number);
            case "System.Guid": return new TsType("string", TsKind.Guid);
            case "System.DateTime": case "System.DateTimeOffset": return new TsType("string", TsKind.Date); // ISO 8601
            case "System.TimeSpan": return new TsType("string", TsKind.String);
            case "void": return new TsType("void", TsKind.Void);
            case "object": case "System.Object": return new TsType("unknown", TsKind.Unknown);
            case "System.Data.DataTable": case "System.Data.DataSet": case "System.Data.DataView":
                Note(cs, "mapped to Row[] (inferred — check against backend contract)");
                return new TsType("Record<string, unknown>[]", TsKind.Array, null, new TsType("Record<string, unknown>", TsKind.Record));
        }

        if (_types.TryGetValue(cs, out var type))
        {
            if (type.Kind == "enum") { Referenced.Add(cs); return new TsType(NameOf(cs), TsKind.Enum, cs); }
            if ((type.Kind is "class" or "struct") && type.Properties.Count > 0) { Referenced.Add(cs); return new TsType(NameOf(cs), TsKind.Dto, cs); }
            return Unknown(cs, $"{type.Kind} without data members");
        }

        return Unknown(cs, "external type not in the contract");
    }

    private TsType Unknown(string cs, string why)
    {
        Note(cs, why);
        return new TsType("unknown", TsKind.Unknown);
    }

    private void Note(string cs, string why)
    {
        if (_noted.Add(cs)) Notes.Add($"{cs}: {why}");
    }

    private static string Wrap(string text) => text.Contains(' ') ? $"({text})" : text;

    /// <summary>Splits "A, B<C, D>" at top-level commas.</summary>
    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '<') depth++;
            else if (text[i] == '>') depth--;
            else if (text[i] == ',' && depth == 0) { parts.Add(text[start..i].Trim()); start = i + 1; }
        }
        parts.Add(text[start..].Trim());
        return parts;
    }
}
