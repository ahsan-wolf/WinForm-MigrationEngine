using System.Text.RegularExpressions;
using Migrator.Engine.Assemblies;
using Migrator.Target.Core.Ir;

namespace Migrator.Target.Core.Contracts;

/// <summary>What the generated forms actually use from a contract: data types they mention and static facade methods they call.</summary>
public sealed class ContractUsage
{
    /// <summary>Full names of types referenced by name in members (signatures and bodies).</summary>
    public SortedSet<string> TypeRefs { get; } = new(StringComparer.Ordinal);

    /// <summary>Facade type full name -> static method names called as Type.Method(...).</summary>
    public SortedDictionary<string, SortedSet<string>> Calls { get; } = new(StringComparer.Ordinal);

    public int CallSites { get; set; }
}

public static partial class UsageScanner
{
    [GeneratedRegex(@"\b[A-Za-z_]\w*\b")]
    private static partial Regex WordRegex();

    /// <summary>
    /// Regex scan over member types and bodies. Matches simple type names that are unique in the contract. Misses calls through aliases or
    /// `using static`; a miss only means a method is not emitted.
    /// </summary>
    public static ContractUsage Scan(AssemblyContract contract, IEnumerable<UnitIr> units, bool all, IEnumerable<WebFormsMigrator.IR.Models.MemberIr>? extraMembers = null)
    {
        var usage = new ContractUsage();
        var bySimple = contract.Types.Where(t => !t.FullName.Contains('+'))
            .GroupBy(t => t.Name).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);

        if (all)
        {
            foreach (var t in contract.Types.Where(IsPlainDataOrFacade)) usage.TypeRefs.Add(t.FullName);
            foreach (var t in contract.Types.Where(t => t.Methods.Any(m => m.IsStatic && !m.IsConstructor)))
                usage.Calls[t.FullName] = new SortedSet<string>(t.Methods.Where(m => m.IsStatic && !m.IsConstructor).Select(m => m.Name), StringComparer.Ordinal);
            return usage;
        }

        var texts = units.SelectMany(u => u.Page.Members).Concat(extraMembers ?? Enumerable.Empty<WebFormsMigrator.IR.Models.MemberIr>())
            .Select(m => string.Join(' ', new[] { m.Type }.Concat(m.Parameters.Select(p => p.Type)).Append(m.Body ?? ""))).ToList();

        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
            foreach (Match w in WordRegex().Matches(text)) words.Add(w.Value);

        foreach (var name in words.Where(bySimple.ContainsKey))
        {
            var type = bySimple[name];
            usage.TypeRefs.Add(type.FullName);

            var staticMethods = type.Methods.Where(m => m.IsStatic && !m.IsConstructor).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            if (staticMethods.Count == 0) continue;

            var call = new Regex($@"\b{Regex.Escape(name)}\s*\.\s*(\w+)\s*\(");
            foreach (var text in texts)
                foreach (Match m in call.Matches(text))
                    if (staticMethods.Contains(m.Groups[1].Value))
                    {
                        if (!usage.Calls.TryGetValue(type.FullName, out var set)) usage.Calls[type.FullName] = set = new SortedSet<string>(StringComparer.Ordinal);
                        set.Add(m.Groups[1].Value);
                        usage.CallSites++;
                    }
        }
        return usage;
    }

    /// <summary>Data classes/enums, not generated service proxies (SOAP/WCF clients are not browser code).</summary>
    private static bool IsPlainDataOrFacade(TypeContract t) =>
        t.Kind == "enum" || (t.Kind is "class" or "struct" && t.Properties.Count > 0 && !IsProxy(t));

    public static bool IsProxy(TypeContract t) =>
        (t.BaseType?.Contains("SoapHttpClientProtocol") ?? false) || (t.BaseType?.Contains("ClientBase") ?? false) ||
        (t.BaseType?.Contains("System.Web.Services") ?? false);
}
