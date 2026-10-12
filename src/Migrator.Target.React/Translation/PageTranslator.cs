using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Migrator.Target.React.Generation;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.React.Translation;

/// <summary>What translation may reference outside the page itself.</summary>
public sealed class TranslationEnv
{
    public ContractVocabulary Vocabulary { get; init; } = ContractVocabulary.Empty;
    public IReadOnlyDictionary<string, string> Components { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, UnitIr> Units { get; init; } = new Dictionary<string, UnitIr>();
    public ISet<string> Generated { get; init; } = new HashSet<string>();
}

/// <summary>Where an event handler method is wired: first control + event that uses it.</summary>
public sealed record WiredHandler(string ControlId, string Event, bool IsRoot);

/// <summary>
/// Translates every method of one page: assigns TypeScript names, colors the call graph async, translates bodies (rules), classifies
/// each result Complete / Partial / NotTranslated, and builds the initial values of the page's fields.
/// </summary>
public static class PageTranslator
{
    private static readonly HashSet<string> JsReserved = new(StringComparer.Ordinal)
    {
        "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "enum", "export", "extends", "false",
        "finally", "for", "function", "if", "import", "in", "instanceof", "new", "null", "return", "super", "switch", "this", "throw", "true", "try",
        "typeof", "var", "void", "while", "with", "yield", "let", "static", "await", "async", "of"
    };

    public static PageTranslation Translate(UnitIr unit, NameScope names, TranslationEnv env, IReadOnlyDictionary<string, WiredHandler> wired, ISet<string> demoted)
    {
        var root = unit.Page.RootControl!;
        var output = new PageTranslation();
        var scope = new PageScope
        {
            RootId = root.Id,
            Vocabulary = env.Vocabulary,
            Components = env.Components,
            Units = env.Units,
            Generated = env.Generated,
            Output = output
        };
        foreach (var node in Walk(root).Skip(1)) scope.Controls.Add(node.Id);

        foreach (var m in unit.Page.Members.Where(m => m.Kind is MemberKind.Field or MemberKind.Property))
            scope.Fields.TryAdd(m.Name, m);

        // 1. Names. A wired handler method keeps the handler name derived from its first wiring (the wiring code uses the same name).
        var methods = unit.Page.Members.Where(m => m.Kind == MemberKind.Method && m.Body != null).ToList();
        var wiredMember = methods.GroupBy(m => m.Name)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault(m => m.Parameters.Count == 2) ?? g.First());
        var translations = new List<MethodTranslation>();
        foreach (var m in methods)
        {
            var isWired = wired.TryGetValue(m.Name, out var w) && ReferenceEquals(wiredMember[m.Name], m);
            var tsName = names.Unique(isWired ? Naming.HandlerName(w!.ControlId, w.Event, w.IsRoot) : SafeName(Naming.Camel(m.Name)));
            var fn = new MemberFunction { Member = m, TsName = tsName, IgnoresArgs = m.Parameters.Count == 2 && m.Parameters[0].Type == "object" };
            if (!scope.Functions.TryGetValue(m.Name, out var list)) scope.Functions[m.Name] = list = new List<MemberFunction>();
            list.Add(fn);
            translations.Add(new MethodTranslation { Member = m, TsName = tsName, IsWiredHandler = isWired });
        }

        // 2. Async coloring over the call graph.
        ColorAsync(scope, methods);
        foreach (var t in translations) t.IsAsync = scope.Functions[t.Member.Name].First(f => f.Member == t.Member).IsAsync;

        // 3. Bodies.
        foreach (var t in translations)
        {
            var fn = scope.Functions[t.Member.Name].First(f => f.Member == t.Member);
            TranslateMethod(scope, t, fn, demoted.Contains(t.TsName), wired);
            output.Methods.Add(t);
            output.ByMember[t.Member] = t;
        }

        // 4. Field model.
        foreach (var field in scope.Fields.Values)
            output.FieldInitials[field.Name] = FieldInitial(scope, field);

        return output;
    }

    // --------------------------------------------------------------------------------------------------- async

    private static void ColorAsync(PageScope scope, List<MemberIr> methods)
    {
        var direct = new HashSet<string>(StringComparer.Ordinal);
        var calls = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var m in methods)
        {
            var tree = CSharpSyntaxTree.ParseText("class __W { void __M() " + m.Body + " }");
            var root = tree.GetRoot();
            var formLocals = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Initializer?.Value is ObjectCreationExpressionSyntax oc && scope.Units.ContainsKey(Simple(oc.Type)))
                .Select(v => v.Identifier.Text).ToHashSet(StringComparer.Ordinal);

            if (!calls.TryGetValue(m.Name, out var called)) calls[m.Name] = called = new HashSet<string>(StringComparer.Ordinal);
            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                switch (inv.Expression)
                {
                    case IdentifierNameSyntax id when scope.Functions.ContainsKey(id.Identifier.Text):
                        called.Add(id.Identifier.Text);
                        break;
                    case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } t when scope.Functions.ContainsKey(t.Name.Identifier.Text):
                        called.Add(t.Name.Identifier.Text);
                        break;
                    case MemberAccessExpressionSyntax ma:
                        var recv = ma.Expression.ToString();
                        var method = ma.Name.Identifier.Text;
                        if (recv is "MessageBox" or "System.Windows.Forms.MessageBox" && method == "Show") direct.Add(m.Name);
                        else if (method is "ShowDialog" or "Show" && (ma.Expression is ObjectCreationExpressionSyntax oc2 && scope.Units.ContainsKey(Simple(oc2.Type))
                                 || ma.Expression is IdentifierNameSyntax fid && formLocals.Contains(fid.Identifier.Text))) direct.Add(m.Name);
                        else if (FacadeName(ma.Expression) is { } facade && scope.Vocabulary.IsFacade(facade) && !scope.Fields.ContainsKey(facade) && !scope.Controls.Contains(facade))
                        {
                            if (Enumerable.Range(0, 16).Any(n => scope.Vocabulary.FindCall(facade, method, n) is not null)) direct.Add(m.Name);
                        }
                        break;
                }
            }
        }

        var asyncNames = new HashSet<string>(direct, StringComparer.Ordinal);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (name, called) in calls)
                if (!asyncNames.Contains(name) && called.Any(asyncNames.Contains)) { asyncNames.Add(name); changed = true; }
        }
        foreach (var overloads in scope.Functions.Values)
            foreach (var fn in overloads) fn.IsAsync = asyncNames.Contains(fn.Member.Name);
    }

    private static string? FacadeName(ExpressionSyntax e) => e switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax ns } m when ns.Identifier.Text is "ECMLibrary" or "System" => m.Name.Identifier.Text,
        _ => null
    };

    private static string Simple(TypeSyntax t) => t switch
    {
        QualifiedNameSyntax q => q.Right.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        IdentifierNameSyntax i => i.Identifier.Text,
        _ => t.ToString()
    };

    // -------------------------------------------------------------------------------------------------- methods

    private static void TranslateMethod(PageScope scope, MethodTranslation t, MemberFunction fn, bool demoted, IReadOnlyDictionary<string, WiredHandler> wired)
    {
        var m = t.Member;
        var translator = new MethodTranslator(scope, m);
        var body = new List<string>();
        int top = 0, fallbacks = 0;
        if (demoted)
        {
            t.Reasons.Add("demoted by the TypeScript compile gate");
            t.Todos = 1;
        }
        else
        {
            body = translator.TranslateBody(out top, out fallbacks);
            t.Todos = translator.Todos;
            t.Reasons.AddRange(translator.Reasons);
        }

        var allFellBack = demoted || (top > 0 && fallbacks == top && body.All(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        t.Status = allFellBack ? TranslationStatus.NotTranslated : t.Todos > 0 ? TranslationStatus.Partial : TranslationStatus.Complete;

        // Wired handlers that could not be translated keep the old stub path (navigation lines + commented C#), built by the generator.
        if (t.Status == TranslationStatus.NotTranslated && t.IsWiredHandler) return;

        var lines = new List<string>();
        if (t.IsWiredHandler && wired.TryGetValue(m.Name, out var w)) lines.Add($"// {w.ControlId}.{w.Event} -> {m.Name}");

        var bodyText = string.Join("\n", body.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        lines.Add($"{(fn.IsAsync ? "async " : "")}function {t.TsName}({Params(scope, m, fn, bodyText)}) {{");
        if (t.Status == TranslationStatus.NotTranslated)
        {
            lines.Add($"  // TODO: not translated — original C# is in src/legacy and below");
            foreach (var l in (m.Body ?? "").Split('\n')) lines.Add("  // " + l.TrimEnd('\r'));
            var fallback = DefaultReturn(scope, m);
            if (fallback is not null) lines.Add("  " + fallback);
        }
        else
        {
            if (t.Status == TranslationStatus.Partial) lines.Add($"  // TODO: partially translated — {t.Todos} statement(s) need manual work");
            foreach (var l in RenameUnusedLocals(body)) lines.Add(l.Length == 0 ? l : "  " + l);
        }
        lines.Add("}");
        lines.Add("");
        t.Lines = lines;
    }

    private static string Params(PageScope scope, MemberIr m, MemberFunction fn, string bodyText)
    {
        var parts = new List<string>();
        foreach (var p in m.Parameters)
        {
            var used = Regex.IsMatch(bodyText, $@"\b{Regex.Escape(p.Name)}\b");
            if (fn.IgnoresArgs && !used) continue; // handler-shaped (sender, e) that the body never reads
            var name = (JsReserved.Contains(p.Name) ? p.Name + "_" : p.Name);
            if (!used) name = "_" + name;

            var type = scope.Vocabulary.MapType(p.Type, scope.Output.TypeImports);
            if (fn.IgnoresArgs) { parts.Add($"{name}?: unknown"); continue; }

            if (p.DefaultValue is null) { parts.Add($"{name}: {type}"); continue; }
            try
            {
                var constant = new MethodTranslator(scope, null, constantContext: true).TranslateConstant(p.DefaultValue);
                parts.Add(constant == "null" ? $"{name}?: {type}" : $"{name}: {type} = {constant}");
            }
            catch (Untranslatable)
            {
                parts.Add($"{name}?: {type}");
            }
        }
        return string.Join(", ", parts);
    }

    /// <summary>A neutral return value for a stubbed function so its callers still type-check.</summary>
    private static string? DefaultReturn(PageScope scope, MemberIr m)
    {
        var ts = scope.Vocabulary.MapType(m.Type, scope.Output.TypeImports);
        return m.Type == "void" ? null : ts switch
        {
            "string" => "return \"\";",
            "number" => "return 0;",
            "boolean" => "return false;",
            _ when ts.EndsWith("[]") => $"return [] as {ts};",
            _ => $"return undefined as unknown as {ts};"
        };
    }

    private static string FieldInitial(PageScope scope, MemberIr field)
    {
        if (!string.IsNullOrWhiteSpace(field.Body))
        {
            try
            {
                var constant = new MethodTranslator(scope, null, constantContext: true).TranslateConstant(field.Body!);
                var ts0 = scope.Vocabulary.MapType(field.Type, scope.Output.TypeImports);
                // `[]` / `{}` / `null` need the declared type or later pushes/assignments will not type-check.
                if (constant is "[]" or "{}" && !ts0.Contains("unknown")) return $"{constant} as {ts0}";
                if (constant == "null") return ts0 == "unknown" ? "undefined as unknown" : $"null as unknown as {ts0}";
                return constant;
            }
            catch (Untranslatable)
            {
                // fall through to the default for the declared type
            }
        }

        var ts = scope.Vocabulary.MapType(field.Type, scope.Output.TypeImports);
        return ts switch
        {
            "string" => "\"\"",
            "number" => "0",
            "boolean" => "false",
            _ when ts.EndsWith("[]") => $"[] as {ts}",
            _ when ts.StartsWith("Record<") => $"{{}} as {ts}",
            "unknown" => "undefined as unknown",
            _ => $"null as unknown as {ts}"
        };
    }

    /// <summary>Reserved words get a suffix; `useX` would be mistaken for a React hook by the lint rules, so it becomes `doUseX`.</summary>
    private static string SafeName(string name) =>
        JsReserved.Contains(name) ? name + "_" : Regex.IsMatch(name, "^use[A-Z]") ? "do" + char.ToUpperInvariant(name[0]) + name[1..] : name;

    /// <summary>
    /// Final-text clean-up that keeps lint quiet without changing behaviour: (1) a local nothing READS (unused in the original, only assigned,
    /// or used only by statements that fell back) becomes `_name`; (2) a `let x = init` that no emitted line reassigns becomes `const`.
    /// Decided on the emitted lines, so it always matches what ships.
    /// </summary>
    private static List<string> RenameUnusedLocals(List<string> body)
    {
        var result = new List<string>(body);
        string Code() => string.Join("\n", result.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        const string DeclarationPattern = @"^\s*(?:const|let) (\w+)\b|^\s*for \((?:const|let) (\w+) of|catch \((\w+)\)";
        var declared = result.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Select(l => Regex.Match(l, DeclarationPattern)).Where(m => m.Success)
            .Select(m => m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value).Distinct().ToList();

        foreach (var name in declared)
        {
            var text = Code();
            // reads = every occurrence except the declaration itself and plain writes (`x = ...`, `x += ...`, `x++`)
            var reads = CountOccurrences(text, name) - 1 - CountWrites(text, name);
            if (name.StartsWith('_') || reads > 0) continue;

            var e = Regex.Escape(name);
            for (var i = 0; i < result.Count; i++)
            {
                if (result[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                var renamed = Regex.Replace(result[i], $@"^(\s*(?:const|let) |\s*for \((?:const|let) )({e})\b|(catch \()({e})\)",
                    m => m.Groups[2].Success ? $"{m.Groups[1].Value}_{name}" : $"{m.Groups[3].Value}_{name})");
                if (renamed == result[i]) continue;
                result[i] = renamed;
                break;
            }
        }

        for (var i = 0; i < result.Count; i++)
        {
            var m = Regex.Match(result[i], @"^(\s*)let (\w+)((?::[^=\n]+)? = .*)$");
            if (!m.Success || result[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
            if (CountWrites(Code(), m.Groups[2].Value) == 0)
                result[i] = $"{m.Groups[1].Value}const {m.Groups[2].Value}{m.Groups[3].Value}";
        }
        return result;
    }

    private static int CountOccurrences(string text, string name) => Regex.Matches(StripStrings(text), $@"(?<![\w.]){Regex.Escape(name)}\b").Count;

    /// <summary>Removes string/template literal contents so names inside labels ("dgvMousedown") are not mistaken for uses.</summary>
    public static string StripStrings(string text) =>
        Regex.Replace(text, @"""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)*'|`(?:[^`\\]|\\.)*`", "\"\"");

    /// <summary>Assignments to a local after its declaration: `x = v`, `x op= v`, `x++`, `++x`. Declaration lines are skipped.</summary>
    private static int CountWrites(string text, string name)
    {
        var e = Regex.Escape(name);
        var declaration = new Regex($@"^\s*(?:const|let) {e}\b|^\s*for \((?:const|let) {e} of|catch \({e}\)");
        text = StripStrings(text);
        var assignment = new Regex($@"(?<![\w.]){e}\s*(?:[-+*/%|&^]|\?\?|<<|>>)?=(?![=>])|(?<![\w.]){e}(?:\+\+|--)|(?:\+\+|--){e}\b");
        return text.Split('\n').Where(l => !declaration.IsMatch(l)).Sum(l => assignment.Matches(l).Count);
    }

    private static IEnumerable<ControlNodeIr> Walk(ControlNodeIr node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in Walk(child))
                yield return n;
    }
}
