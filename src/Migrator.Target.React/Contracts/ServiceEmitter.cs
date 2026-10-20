using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Engine.Assemblies;
using Migrator.Engine.Contracts;
using Migrator.Target.React.Generation;

namespace Migrator.Target.React.Contracts;

public sealed class ContractEmitResult
{
    public string Assembly { get; init; } = "";
    public int DataTypes { get; set; }
    public int Enums { get; set; }
    public int Methods { get; set; }
    public int CallSites { get; set; }
    public List<string> OmittedMembers { get; } = new();
    public List<string> OmittedMethods { get; } = new();
    public List<string> CredentialParametersStripped { get; } = new();
    public List<string> Notes { get; } = new();
    public string Scope { get; init; } = "used";

    /// <summary>First namespace segments of the contract's types (e.g. the assembly's root namespace): code like `Root.Facade.Method(...)` names a type by namespace.</summary>
    public HashSet<string> NamespaceRoots { get; } = new(StringComparer.Ordinal);

    // What the translator needs to call the generated service and use its types.
    public string InstanceName { get; set; } = "";
    public string ServiceImport { get; set; } = "";
    public string TypesImport { get; set; } = "";
    public TsTypeMapper Mapper { get; set; } = null!;

    /// <summary>facade type -> method -> overloads (original parameter count and the indexes kept after credential stripping).</summary>
    public Dictionary<string, Dictionary<string, List<CallSignature>>> Facades { get; } = new(StringComparer.Ordinal);

    /// <summary>Simple C# type name -> TypeScript name, for the data types and enums that were actually emitted.</summary>
    public Dictionary<string, string> TypeNameBySimple { get; } = new(StringComparer.Ordinal);
}

public sealed record CallSignature(int ParamCount, int[] Keep);

/// <summary>
/// Emits typed TypeScript from an assembly contract: DTO interfaces, enums, a Promise-based service interface (one method per used
/// static facade method) and a mock with deterministic fixtures. Names mirror the C# API so translated code maps 1:1. Secrets are
/// omitted (see <see cref="SecretFilter"/>). Nothing here references the assembly itself.
/// </summary>
public static partial class ServiceEmitter
{
    private sealed record Overload(MethodContract Method, List<string> ParamNames, List<TsType> Params, TsType Return, int OriginalCount, int[] Keep);

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "enum", "export", "extends", "false",
        "finally", "for", "function", "if", "import", "in", "instanceof", "new", "null", "return", "super", "switch", "this", "throw", "true", "try",
        "typeof", "var", "void", "while", "with", "yield", "let", "static", "await", "async", "of", "type", "interface", "package", "private",
        "protected", "public", "implements"
    };

    private static readonly JsonSerializerOptions JsonIn = new() { PropertyNameCaseInsensitive = true };

    public static ContractEmitResult Emit(string contractFile, IReadOnlyList<UnitIr> units, bool all, string appDir, List<string> files,
        IEnumerable<WebFormsMigrator.IR.Models.MemberIr>? extraMembers = null)
    {
        var contract = JsonSerializer.Deserialize<AssemblyContract>(File.ReadAllText(contractFile), JsonIn)
                       ?? throw new InvalidDataException($"'{contractFile}' is empty.");
        var byFull = contract.Types.ToDictionary(t => t.FullName, StringComparer.Ordinal);
        var mapper = new TsTypeMapper(contract.Types);
        var usage = UsageScanner.Scan(contract, units, all, extraMembers);
        var result = new ContractEmitResult { Assembly = contract.Name, CallSites = usage.CallSites, Scope = all ? "all" : "used" };
        result.NamespaceRoots.Add(contract.Name);
        foreach (var t in contract.Types) if (t.FullName.Contains('.')) result.NamespaceRoots.Add(t.FullName[..t.FullName.IndexOf('.')]);

        // 1. Facade methods first: their signatures pull in the data types they need.
        var facades = new List<(string Name, SortedDictionary<string, List<Overload>> Methods)>();
        foreach (var (full, names) in usage.Calls)
        {
            if (!byFull.TryGetValue(full, out var type)) continue;
            var methods = new SortedDictionary<string, List<Overload>>(StringComparer.Ordinal);
            foreach (var m in type.Methods.Where(m => m.IsStatic && !m.IsConstructor && names.Contains(m.Name)))
            {
                if (SecretFilter.IsSecretMethod(m)) { result.OmittedMethods.Add($"{type.Name}.{m.Name}"); continue; }
                var keep = m.Parameters.Select((p, i) => (p, i)).Where(x => !SecretFilter.IsCredentialObject(x.p)).ToList();
                var kept = keep.Select(x => x.p).ToList();
                if (kept.Count != m.Parameters.Count) result.CredentialParametersStripped.Add($"{type.Name}.{m.Name}");
                if (!methods.TryGetValue(m.Name, out var list)) methods[m.Name] = list = new();
                list.Add(new Overload(m, kept.Select(p => p.Name).ToList(), kept.Select(p => mapper.Map(p.Type)).ToList(), mapper.Map(m.ReturnType),
                    m.Parameters.Count, keep.Select(x => x.i).ToArray()));
            }
            if (methods.Count > 0) facades.Add((mapper.NameOf(full), methods));
        }

        // 2. Types the forms mention directly (data classes and enums only; facades and SOAP proxies are not browser data).
        foreach (var full in usage.TypeRefs)
            if (byFull.TryGetValue(full, out var t) && (t.Kind == "enum" || (t.Kind is "class" or "struct" && t.Properties.Count > 0 && !UsageScanner.IsProxy(t))))
                mapper.Map(full);

        // 3. Closure over property types (secret-looking properties do not pull anything in).
        var processed = new HashSet<string>(StringComparer.Ordinal);
        for (var pending = mapper.Referenced.Where(r => !processed.Contains(r)).ToList(); pending.Count > 0; pending = mapper.Referenced.Where(r => !processed.Contains(r)).ToList())
            foreach (var full in pending)
            {
                processed.Add(full);
                foreach (var p in byFull[full].Properties.Where(p => !p.IsStatic && !SecretFilter.IsSecretProperty(p))) mapper.Map(p.Type);
            }

        var enums = mapper.Referenced.Where(r => byFull[r].Kind == "enum").OrderBy(mapper.NameOf, StringComparer.Ordinal).ToList();
        var dtos = mapper.Referenced.Where(r => byFull[r].Kind != "enum").OrderBy(mapper.NameOf, StringComparer.Ordinal).ToList();
        result.Mapper = mapper;
        foreach (var f in facades)
            result.Facades[f.Name] = f.Methods.ToDictionary(kv => kv.Key, kv => kv.Value.Select(o => new CallSignature(o.OriginalCount, o.Keep)).ToList(), StringComparer.Ordinal);
        foreach (var group in enums.Concat(dtos).GroupBy(full => byFull[full].Name).Where(g => g.Count() == 1))
            result.TypeNameBySimple[group.Key] = mapper.NameOf(group.Single());
        result.Enums = enums.Count;
        result.DataTypes = dtos.Count;
        result.Methods = facades.Sum(f => f.Methods.Values.Sum(v => v.Count));

        var dir = "src/services/" + contract.Name.ToLowerInvariant();
        var header = $"// MOCK — generated from the {contract.Name} contract (metadata only; the assembly is not referenced). Names mirror the C# API so translated code maps 1:1.";

        // types.ts
        var types = new StringBuilder(header + "\n// Replace with the real API types once a backend exists. inferred — check against backend contract.\n\n");
        foreach (var full in enums)
        {
            var members = byFull[full].EnumMembers.Select(m => $"'{m.Name}'").ToList();
            types.AppendLine($"export type {mapper.NameOf(full)} = {(members.Count > 0 ? string.Join(" | ", members) : "string")};");
        }
        if (enums.Count > 0) types.AppendLine();
        foreach (var full in dtos)
        {
            types.AppendLine($"export interface {mapper.NameOf(full)} {{");
            foreach (var p in byFull[full].Properties.Where(p => !p.IsStatic))
            {
                if (SecretFilter.IsSecretProperty(p)) { result.OmittedMembers.Add($"{byFull[full].Name}.{p.Name}"); continue; }
                types.AppendLine($"  {Prop(p.Name)}: {mapper.Map(p.Type).Text};");
            }
            types.AppendLine("}");
            types.AppendLine();
        }
        AppWriter.Put(appDir, $"{dir}/types.ts", types.ToString(), files);

        // fixtures.ts
        var fixtures = new StringBuilder(header + "\n// Deterministic sample rows (shape only; values are fake).\n\n");
        if (dtos.Count > 0)
        {
            fixtures.AppendLine($"import type {{ {string.Join(", ", dtos.Select(mapper.NameOf))} }} from './types';");
            fixtures.AppendLine();
            fixtures.AppendLine($"export const fixtures: {{ {string.Join("; ", dtos.Select(d => $"{mapper.NameOf(d)}: {mapper.NameOf(d)}[]"))} }} = {{");
            foreach (var full in dtos)
            {
                fixtures.AppendLine($"  {mapper.NameOf(full)}: [");
                for (var n = 1; n <= 3; n++)
                {
                    var props = byFull[full].Properties.Where(p => !p.IsStatic && !SecretFilter.IsSecretProperty(p))
                        .Select(p => $"{Prop(p.Name)}: {FixtureValue(mapper.Map(p.Type), p.Name, n, byFull, mapper)}");
                    fixtures.AppendLine($"    {{ {string.Join(", ", props)} }},");
                }
                fixtures.AppendLine("  ],");
            }
            fixtures.AppendLine("};");
        }
        else fixtures.AppendLine("export const fixtures = {};");
        AppWriter.Put(appDir, $"{dir}/fixtures.ts", fixtures.ToString(), files);

        // service.ts
        var iface = new StringBuilder();
        var mock = new StringBuilder();
        foreach (var (facade, methods) in facades)
        {
            iface.AppendLine($"  {facade}: {{");
            mock.AppendLine($"  {facade}: {{");
            foreach (var (name, overloads) in methods)
            {
                foreach (var o in overloads)
                    iface.AppendLine($"    {name}({Params(o.ParamNames, o.Params)}): Promise<{o.Return.Text}>;");
                mock.AppendLine($"    {name}: async (..._args: unknown[]) => {MockValue(overloads[0].Return, byFull, mapper)},");
            }
            iface.AppendLine("  };");
            mock.AppendLine("  },");
        }

        var ident = Naming.Pascal(contract.Name);
        result.InstanceName = LowerLeadingWord(ident);
        result.ServiceImport = $"../services/{contract.Name.ToLowerInvariant()}/service";
        result.TypesImport = $"../services/{contract.Name.ToLowerInvariant()}/types";
        var service = new StringBuilder(header + "\n// Every call is async here even where the C# API was synchronous. A SERVER-ONLY list (secret-looking members) is in MIGRATION_REPORT.md.\n\n");
        // A data type can share its name with a method or facade (e.g. type CheckInList and method CheckInList): only type positions count.
        var typePositions = Regex.Replace(iface.ToString(), @"^\s+\w+(?=\(|: \{)", "", RegexOptions.Multiline);
        var used = dtos.Concat(enums).Select(mapper.NameOf).Where(n => WordRegex(n).IsMatch(typePositions)).ToList();
        if (used.Count > 0) service.AppendLine($"import type {{ {string.Join(", ", used)} }} from './types';");
        if (mock.ToString().Contains("fixtures.")) service.AppendLine("import { fixtures } from './fixtures';");
        service.AppendLine();
        service.AppendLine($"export interface {ident}Service {{");
        service.Append(iface);
        service.AppendLine("}");
        service.AppendLine();
        service.AppendLine("// MOCK — replace with real implementation (an API client behind the same interface).");
        service.AppendLine($"export const mock{ident}Service = {{");
        service.Append(mock);
        service.AppendLine($"}} as unknown as {ident}Service;");
        service.AppendLine();
        service.AppendLine($"export const {LowerLeadingWord(ident)}: {ident}Service = mock{ident}Service;");
        AppWriter.Put(appDir, $"{dir}/service.ts", service.ToString(), files);

        result.Notes.AddRange(mapper.Notes);
        return result;
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private static string Prop(string name) => Regex.IsMatch(name, @"^[A-Za-z_$][\w$]*$") ? name : JsonSerializer.Serialize(name);

    private static string Params(List<string> names, List<TsType> types)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var parts = new List<string>();
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (Reserved.Contains(name) || !Regex.IsMatch(name, @"^[A-Za-z_$][\w$]*$")) name += "_";
            for (var n = 2; !used.Add(name); n++) name = names[i] + n;
            parts.Add($"{name}: {types[i].Text}");
        }
        return string.Join(", ", parts);
    }

    private static string FixtureValue(TsType t, string prop, int n, IReadOnlyDictionary<string, TypeContract> types, TsTypeMapper mapper) => t.Kind switch
    {
        TsKind.String => JsonSerializer.Serialize($"{prop} {n}"),
        TsKind.Guid => $"\"00000000-0000-4000-8000-{n:D12}\"",
        TsKind.Date => $"\"2026-01-{n:D2}T09:00:00.000Z\"",
        TsKind.Number => n.ToString(),
        TsKind.Boolean => n % 2 == 1 ? "true" : "false",
        TsKind.Enum => EnumFirst(t, types),
        TsKind.Array => "[]",
        TsKind.Record => "{}",
        TsKind.Dto => $"{{}} as {t.Text}",
        _ => "null"
    };

    private static string MockValue(TsType t, IReadOnlyDictionary<string, TypeContract> types, TsTypeMapper mapper) => t.Kind switch
    {
        TsKind.Void => "undefined",
        TsKind.String => "\"\"",
        TsKind.Guid => "\"00000000-0000-4000-8000-000000000001\"",
        TsKind.Date => "\"2026-01-01T00:00:00.000Z\"",
        TsKind.Number => "0",
        TsKind.Boolean => "false",
        TsKind.Enum => EnumFirst(t, types),
        TsKind.Dto => $"fixtures.{t.Text}[0]",
        TsKind.Array when t.Element is { Kind: TsKind.Dto } e => $"fixtures.{e.Text}",
        TsKind.Array => "[]",
        TsKind.Record => "{}",
        _ => "null"
    };

    private static string EnumFirst(TsType t, IReadOnlyDictionary<string, TypeContract> types) =>
        t.ContractType is { } full && types.TryGetValue(full, out var e) && e.EnumMembers.Count > 0 ? $"'{e.EnumMembers[0].Name}'" : "\"\"";

    /// <summary>VendorApi -> vendorApi (lower the leading acronym, keep the last capital for the next word).</summary>
    private static string LowerLeadingWord(string ident)
    {
        var run = 0;
        while (run < ident.Length && char.IsUpper(ident[run])) run++;
        if (run <= 1) return char.ToLowerInvariant(ident[0]) + ident[1..];
        if (run == ident.Length) return ident.ToLowerInvariant();
        return ident[..(run - 1)].ToLowerInvariant() + ident[(run - 1)..];
    }

    private static Regex WordRegex(string word) => new($@"\b{Regex.Escape(word)}\b");
}
