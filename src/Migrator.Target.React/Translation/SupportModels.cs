using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Migrator.Engine.Support;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.React.Translation;

/// <summary>One extracted support class and what pages/other classes may reference on it.</summary>
public sealed class SupportClassInfo
{
    public SupportClassIr Ir { get; init; } = null!;
    public string Name => Ir.Name;
    public bool IsEnum => Ir.Kind == "enum";

    /// <summary>Fields and properties by name (static and instance).</summary>
    public Dictionary<string, MemberIr> Fields { get; } = new(StringComparer.Ordinal);

    /// <summary>Methods by C# name; overloads get distinct TypeScript names (<c>Foo</c>, <c>Foo_2</c>, ...). Constructors are not listed.</summary>
    public Dictionary<string, List<MemberFunction>> Functions { get; } = new(StringComparer.Ordinal);

    public IEnumerable<MemberIr> Constructors => Ir.Members.Where(m => m.Kind == MemberKind.Method && m.Name == ".ctor");

    public MemberFunction? Resolve(string name, int argCount)
    {
        if (!Functions.TryGetValue(name, out var overloads)) return null;
        return overloads.FirstOrDefault(o => o.Member.Parameters.Count == argCount)
               ?? overloads.FirstOrDefault(o => o.Member.Parameters.Count(p => p.DefaultValue == null) <= argCount && argCount <= o.Member.Parameters.Count)
               ?? (overloads.Count == 1 ? overloads[0] : null);
    }
}

/// <summary>All extracted support classes of a project, addressed by simple name.</summary>
public sealed class SupportRegistry
{
    public IReadOnlyDictionary<string, SupportClassInfo> Classes { get; private init; } = new Dictionary<string, SupportClassInfo>();

    public static SupportRegistry Build(SupportFileIr file)
    {
        var classes = new Dictionary<string, SupportClassInfo>(StringComparer.Ordinal);
        foreach (var ir in file.Classes)
        {
            var info = new SupportClassInfo { Ir = ir };
            foreach (var m in ir.Members)
            {
                if (m.Kind is MemberKind.Field or MemberKind.Property) info.Fields.TryAdd(m.Name, m);
                else if (m.Kind == MemberKind.Method && m.Name != ".ctor")
                {
                    if (!info.Functions.TryGetValue(m.Name, out var list)) info.Functions[m.Name] = list = new List<MemberFunction>();
                    var tsName = list.Count == 0 ? m.Name : $"{m.Name}_{list.Count + 1}";
                    list.Add(new MemberFunction { Member = m, TsName = tsName, Prefix = (m.IsStatic ? ir.Name : "this") + "." });
                }
            }
            classes[ir.Name] = info;
        }
        return new SupportRegistry { Classes = classes };
    }

    /// <summary>
    /// A support method is async when it (transitively) calls an async contract facade method or another async support method.
    /// Constructors and property accessors are never async: code that needs it there falls back.
    /// </summary>
    public void ColorAsync(ContractVocabulary vocabulary)
    {
        var direct = new HashSet<(string, string)>();
        var calls = new Dictionary<(string, string), HashSet<(string, string)>>();

        foreach (var cls in Classes.Values)
        {
            foreach (var (name, overloads) in cls.Functions)
            {
                var key = (cls.Name, name);
                var called = calls[key] = new HashSet<(string, string)>();
                foreach (var m in overloads.Select(o => o.Member).Where(m => m.Body != null))
                {
                    var root = CSharpSyntaxTree.ParseText("class __W { void __M() " + m.Body + " }").GetRoot();
                    foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        switch (inv.Expression)
                        {
                            case IdentifierNameSyntax id when cls.Functions.ContainsKey(id.Identifier.ValueText):
                                called.Add((cls.Name, id.Identifier.ValueText));
                                break;
                            case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } t when cls.Functions.ContainsKey(t.Name.Identifier.ValueText):
                                called.Add((cls.Name, t.Name.Identifier.ValueText));
                                break;
                            case MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target } ma
                                when Classes.TryGetValue(target.Identifier.ValueText, out var other) && other.Functions.ContainsKey(ma.Name.Identifier.ValueText):
                                called.Add((other.Name, ma.Name.Identifier.ValueText));
                                break;
                            case MemberAccessExpressionSyntax ma:
                                var method = ma.Name.Identifier.ValueText;
                                if (PageTranslator.FacadeName(ma.Expression, vocabulary) is { } facade && vocabulary.IsFacade(facade) && !cls.Fields.ContainsKey(facade)
                                    && Enumerable.Range(0, 16).Any(n => vocabulary.FindCall(facade, method, n) is not null))
                                    direct.Add(key);
                                break;
                        }
                    }
                }
            }
        }

        var asyncKeys = new HashSet<(string, string)>(direct);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (key, called) in calls)
                if (!asyncKeys.Contains(key) && called.Any(asyncKeys.Contains)) { asyncKeys.Add(key); changed = true; }
        }
        foreach (var cls in Classes.Values)
            foreach (var (name, overloads) in cls.Functions)
                foreach (var fn in overloads) fn.IsAsync = asyncKeys.Contains((cls.Name, name));
    }
}

/// <summary>The generated support module (<c>src/support/index.ts</c>) and what the compile gate needs to map errors back to methods.</summary>
public sealed class SupportModule
{
    public string Source { get; set; } = "";
    public List<MethodTranslation> Methods { get; } = new();

    /// <summary>1-based inclusive line ranges of each method; key is <c>Class.tsName</c>.</summary>
    public List<(string Key, int Start, int End)> Ranges { get; } = new();

    public int Classes { get; set; }
    public List<string> Notes { get; } = new();
}

/// <summary>
/// Keeps only the support classes the generated pages use, directly or through each other. A project's business layer (data access,
/// domain, services) is not front-end code: emitting all of it would bloat the app and fail to translate. Structural, name-agnostic.
/// </summary>
public static class SupportPruner
{
    public static SupportFileIr Reachable(SupportFileIr all, IEnumerable<Migrator.Target.React.Generation.UnitIr> units)
    {
        var byName = all.Classes.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<SupportClassIr>();

        void Mention(string text)
        {
            foreach (System.Text.RegularExpressions.Match w in System.Text.RegularExpressions.Regex.Matches(text, @"\b[A-Za-z_]\w*\b"))
                if (byName.TryGetValue(w.Value, out var cls) && reached.Add(cls.Name)) queue.Enqueue(cls);
        }

        foreach (var unit in units)
            foreach (var m in unit.Page.Members)
                Mention(string.Join(' ', new[] { m.Type }.Concat(m.Parameters.Select(p => p.Type)).Append(m.Body ?? "")));

        while (queue.Count > 0)
        {
            var cls = queue.Dequeue();
            if (cls.BaseType is { } baseType) Mention(baseType);
            foreach (var m in cls.Members)
                Mention(string.Join(' ', new[] { m.Type }.Concat(m.Parameters.Select(p => p.Type)).Append(m.Body ?? "")));
            foreach (var v in cls.EnumValues) if (v.Value is not null) Mention(v.Value);
        }

        return new SupportFileIr { SchemaVersion = all.SchemaVersion, Classes = all.Classes.Where(c => reached.Contains(c.Name)).ToList() };
    }
}
