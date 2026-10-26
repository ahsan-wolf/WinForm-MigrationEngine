using Migrator.Engine.Projections;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Migrator.Target.Core.Ir;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.Core.Translation;

/// <summary>What translation may reference outside the page itself.</summary>
public sealed class TranslationEnv
{
    public ContractVocabulary Vocabulary { get; init; } = ContractVocabulary.Empty;
    public IReadOnlyDictionary<string, string> Components { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, UnitIr> Units { get; init; } = new Dictionary<string, UnitIr>();
    public ISet<string> Generated { get; init; } = new HashSet<string>();
    public SupportRegistry? Support { get; init; }
    public BypassRules Bypass { get; init; } = BypassRules.Empty;
    public ServerBoundRules ServerBound { get; init; } = ServerBoundRules.Empty;

    /// <summary>Images by name (`Properties.Resources.X`): the project's resource file and the `--images` folder.</summary>
    public ResourceImages Resources { get; init; } = ResourceImages.Empty;

    /// <summary>The UI library package the generated code imports library types and helpers from (`@wfm/ui-react`, `@wfm/ui-angular`).</summary>
    public string UiLibraryPackage { get; init; } = "@wfm/ui-react";
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
            Support = env.Support?.Classes ?? new Dictionary<string, SupportClassInfo>(),
            Resources = env.Resources,
            Bypass = env.Bypass,
            Output = output
        };
        foreach (var node in Walk(root).Skip(1))
        {
            scope.Controls.Add(node.Id);
            if (node.Effective() == "System.Windows.Forms.ListView") scope.ListViews.Add(node.Id);
            if (node.Effective() == "System.Windows.Forms.TabControl") scope.TabControls.Add(node.Id);
            if (node.Effective() == ContainerUsage.ImageListType) scope.ImageLists.Add(node.Id);
            if (node.Effective() is "System.Windows.Forms.ComboBox" or "System.Windows.Forms.ToolStripComboBox" or "System.Windows.Forms.ListBox"
                or "System.Windows.Forms.CheckedListBox" or "System.Windows.Forms.DomainUpDown") scope.Combos.Add(node.Id);
            if (node.Effective() == "System.Windows.Forms.CheckedListBox") scope.CheckedLists.Add(node.Id);
            foreach (var (key, value) in node.Attributes)
                if (key.StartsWith("Wired.", StringComparison.Ordinal) && value == "code") scope.CodeWired.Add($"{node.Id}.{key[6..]}");
            if (node.Effective() == "System.Windows.Forms.DataGridView") scope.Grids.Add(node.Id);
            if (node.Effective() == "System.Windows.Forms.TreeView") scope.TreeViews.Add(node.Id);
            if (node.Effective() == "System.Windows.Forms.MonthCalendar") scope.Calendars.Add(node.Id);
            foreach (var (key, value) in node.Attributes.Where(a => a.Key.StartsWith("Fallback.Alias.", StringComparison.Ordinal)))
            {
                if (!scope.Aliases.TryGetValue(node.Id, out var map)) scope.Aliases[node.Id] = map = new Dictionary<string, string>(StringComparer.Ordinal);
                map[key["Fallback.Alias.".Length..]] = value;
            }
        }

        foreach (var (tab, pages) in ContainerUsage.TabPages(root))
        {
            scope.TabPages[tab] = pages;
            foreach (var page in pages) scope.TabPageParent[page] = tab;
        }
        foreach (var host in ContainerUsage.Hosts(unit)) scope.DynamicHosts.Add(host);

        foreach (var m in unit.Page.Members.Where(m => m.Kind is MemberKind.Field or MemberKind.Property))
            scope.Fields.TryAdd(m.Name, m);

        // 1. Names. A wired handler method keeps the handler name derived from its first wiring (the wiring code uses the same name).
        var methods = unit.Page.Members.Where(m => m.Kind == MemberKind.Method && m.Name != ".ctor" && m.Body != null).ToList();
        var wiredMember = methods.GroupBy(m => m.Name)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault(m => m.Parameters.Count == 2) ?? g.First());
        var translations = new List<MethodTranslation>();
        foreach (var m in methods)
        {
            var isWired = wired.TryGetValue(m.Name, out var w) && ReferenceEquals(wiredMember[m.Name], m);
            var tsName = names.Unique(isWired ? Naming.HandlerName(w!.ControlId, w.Event, w.IsRoot) : SafeName(Naming.Camel(m.Name)));
            var fn = new MemberFunction
            {
                Member = m, TsName = tsName, IgnoresArgs = m.Parameters.Count == 2 && m.Parameters[0].Type == "object",
                GridEvent = m.Parameters.Count == 2 && wired.TryGetValue(m.Name, out var gridWire) && scope.Grids.Contains(gridWire.ControlId) && gridWire.Event.StartsWith("Cell", StringComparison.Ordinal)
            };
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
            TranslateMethod(scope, t, fn, demoted.Contains(t.TsName), wired, LineDemotion.For(demoted, t.TsName));
            output.Methods.Add(t);
            output.ByMember[t.Member] = t;
        }

        // 4. Field model.
        foreach (var field in scope.Fields.Values)
            output.FieldInitials[field.Name] = FieldInitial(scope, field);

        // 5. Constructor arguments -> props.
        var ctor = unit.Page.Members.Where(m => m.Kind == MemberKind.Method && m.Name == ".ctor" && m.Parameters.Count > 0)
            .OrderByDescending(m => m.Parameters.Count).FirstOrDefault();
        if (ctor is not null) BuildCtorProps(scope, output, names, ctor);

        return output;
    }

    /// <summary>
    /// The form's constructor arguments become optional props. Mock defaults (a contract data type -> its fixture row) let the page open on its own;
    /// `field = argument;` statements seed the field model from the prop. Anything else the constructor does is kept as a note.
    /// </summary>
    private static void BuildCtorProps(PageScope scope, PageTranslation output, NameScope names, MemberIr ctor)
    {
        output.FixturesImport = scope.Vocabulary.FixturesImport;
        foreach (var p in ctor.Parameters)
        {
            var type = scope.Vocabulary.MapType(p.Type, output.TypeImports);
            var local = names.Unique(SafeName(Naming.Camel(p.Name)));
            string? def = null;
            if (p.DefaultValue is not null)
            {
                try { def = new MethodTranslator(scope, null, constantContext: true).TranslateConstant(p.DefaultValue); }
                catch (Untranslatable) { }
                if (def == "null") def = null;
            }
            else if (type != "unknown")
                def = MockDefault(scope, p.Type, type, output.FixturesImport.Length > 0);
            output.CtorProps.Add(new CtorProp(p.Name, local, type, def));
        }

        var tree = CSharpSyntaxTree.ParseText("class __W { void __M() " + ctor.Body + " }");
        var statements = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First().Body?.Statements ?? default;
        foreach (var s in statements)
        {
            if (s is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "InitializeComponent" } } }) continue;
            if (s is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } a }
                && FieldName(a.Left) is { } fieldName && scope.Fields.TryGetValue(fieldName, out var field)
                && a.Right is IdentifierNameSyntax arg && output.CtorProps.FirstOrDefault(cp => cp.Key == arg.Identifier.ValueText) is { Default: not null } prop
                && prop.Type != "unknown" && scope.Vocabulary.MapType(field.Type, new HashSet<string>()) == prop.Type)
            {
                output.FieldInitials[fieldName] = prop.Local;
                continue;
            }
            output.CtorNotes.Add(Regex.Replace(s.ToString(), @"\s+", " ").Trim());
        }
    }

    private static string? FieldName(ExpressionSyntax e) => e switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } m => m.Name.Identifier.ValueText,
        _ => null
    };

    /// <summary>A sample value for a constructor argument of a contract data type (first fixture row, or the fixture list), or a neutral primitive.</summary>
    internal static string? MockDefault(PageScope scope, string sourceType, string tsType, bool haveFixtures)
    {
        var parsed = SyntaxFactory.ParseTypeName(sourceType);
        if (parsed is ArrayTypeSyntax { RankSpecifiers.Count: 1 } array && haveFixtures && scope.Vocabulary.DtoTypeName(Simple(array.ElementType)) is { } elementDto)
            return $"fixtures.{elementDto}";
        if (parsed is not ArrayTypeSyntax && haveFixtures && scope.Vocabulary.DtoTypeName(Simple(parsed)) is { } dto)
            return $"fixtures.{dto}[0]";
        return tsType switch { "string" => "\"\"", "number" => "0", "boolean" => "false", _ => null };
    }

    // --------------------------------------------------------------------------------------------------- async

    internal static void ColorAsync(PageScope scope, List<MemberIr> methods)
    {
        var direct = new HashSet<string>(StringComparer.Ordinal);
        var calls = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var m in methods)
        {
            var tree = CSharpSyntaxTree.ParseText("class __W { void __M() " + m.Body + " }");
            var root = tree.GetRoot();
            var formLocals = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Initializer?.Value is ObjectCreationExpressionSyntax oc && scope.Units.ContainsKey(Simple(oc.Type)))
                .Select(v => v.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);

            if (!calls.TryGetValue(m.Name, out var called)) calls[m.Name] = called = new HashSet<string>(StringComparer.Ordinal);
            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                switch (inv.Expression)
                {
                    case IdentifierNameSyntax id when scope.Functions.ContainsKey(id.Identifier.ValueText):
                        called.Add(id.Identifier.ValueText);
                        break;
                    case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } t when scope.Functions.ContainsKey(t.Name.Identifier.ValueText):
                        called.Add(t.Name.Identifier.ValueText);
                        break;
                    case MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax sid } sma
                        when scope.Support.TryGetValue(sid.Identifier.ValueText, out var supportClass) && !scope.Fields.ContainsKey(sid.Identifier.ValueText)
                             && supportClass.Functions.TryGetValue(sma.Name.Identifier.ValueText, out var supportFns):
                        if (supportFns.Any(fn => fn.IsAsync)) direct.Add(m.Name);
                        break;
                    case MemberAccessExpressionSyntax ma:
                        var recv = ma.Expression.ToString();
                        var method = ma.Name.Identifier.ValueText;
                        var shown = ma.Expression;
                        while (shown is ParenthesizedExpressionSyntax parenthesized) shown = parenthesized.Expression;
                        if (recv is "MessageBox" or "System.Windows.Forms.MessageBox" && method == "Show") direct.Add(m.Name);
                        else if (method is "ShowDialog" or "Show" && (shown is ObjectCreationExpressionSyntax oc2 && scope.Units.ContainsKey(Simple(oc2.Type))
                                 || shown is IdentifierNameSyntax fid && formLocals.Contains(fid.Identifier.ValueText))) direct.Add(m.Name);
                        else if (FacadeName(ma.Expression, scope.Vocabulary) is { } facade && scope.Vocabulary.IsFacade(facade) && !scope.Fields.ContainsKey(facade) && !scope.Controls.Contains(facade))
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

    internal static string? FacadeName(ExpressionSyntax e, ContractVocabulary vocabulary) => e switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax ns } m when vocabulary.IsNamespaceRoot(ns.Identifier.ValueText) => m.Name.Identifier.ValueText,
        _ => null
    };

    private static string Simple(TypeSyntax t) => t switch
    {
        QualifiedNameSyntax q => q.Right.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        IdentifierNameSyntax i => i.Identifier.ValueText,
        _ => t.ToString()
    };

    // -------------------------------------------------------------------------------------------------- methods

    private static void TranslateMethod(PageScope scope, MethodTranslation t, MemberFunction fn, bool demoted, IReadOnlyDictionary<string, WiredHandler> wired, IReadOnlySet<int> demotedLines)
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
            // Addresses of demoted statements are indices into the body as the first emission produced it (after local clean-up), so demote first and only rename afterwards.
            body = RenameUnusedLocals(body);
            body = LineDemotion.Apply(body, demotedLines, out var demotedCount);
            if (demotedCount > 0) { t.Reasons.Add("statement fails the TypeScript check"); body = RenameUnusedLocals(body, merge: false); }
            t.Todos = translator.Todos + demotedCount;
            t.Reasons.AddRange(translator.Reasons);
            t.Bypasses.AddRange(translator.Bypasses);
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
            foreach (var l in body) lines.Add(l.Length == 0 ? l : "  " + l);
        }
        lines.Add("}");
        lines.Add("");
        t.Lines = lines;
    }

    internal static string Params(PageScope scope, MemberIr m, MemberFunction fn, string bodyText)
    {
        var parts = new List<string>();
        foreach (var p in m.Parameters)
        {
            var used = Regex.IsMatch(bodyText, $@"\b{Regex.Escape(p.Name)}\b");
            if (fn.IgnoresArgs && !used) continue; // handler-shaped (sender, e) that the body never reads
            var name = (JsReserved.Contains(p.Name) ? p.Name + "_" : p.Name);
            if (!used) name = "_" + name;

            var type = scope.Vocabulary.MapType(p.Type, scope.Output.TypeImports);
            if (fn.GridEvent && m.Parameters.IndexOf(p) == 1) { parts.Add($"{name}: WfGridCellEvent"); continue; }
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
    internal static string? DefaultReturn(PageScope scope, MemberIr m)
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

    internal static string FieldInitial(PageScope scope, MemberIr field)
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
            _ when scope.Support.TryGetValue(ts, out var supportType) => SupportDefault(supportType, ts),
            _ => $"null as unknown as {ts}"
        };
    }

    /// <summary>Default for a field of a support type: the first enum value, or a new instance when the class needs no constructor arguments.</summary>
    internal static string SupportDefault(SupportClassInfo type, string ts)
    {
        if (type.IsEnum) return type.Ir.EnumValues.Count > 0 ? $"{ts}.{type.Ir.EnumValues[0].Name}" : $"0 as {ts}";
        if (type.Ir.IsStatic) return $"null as unknown as {ts}";
        var ctors = type.Constructors.ToList();
        return ctors.Count == 0 || ctors.Any(c => c.Parameters.All(p => p.DefaultValue != null)) ? $"new {ts}()" : $"Object.create({ts}.prototype) as {ts}"; // needs constructor arguments: an instance without running it, so member reads do not throw
    }

    /// <summary>Reserved words get a suffix; `useX` would be mistaken for a React hook by the lint rules, so it becomes `doUseX`.</summary>
    internal static string SafeName(string name) =>
        JsReserved.Contains(name) ? name + "_" : Regex.IsMatch(name, "^use[A-Z]") ? "do" + char.ToUpperInvariant(name[0]) + name[1..] : name;

    /// <summary>
    /// Final-text clean-up that keeps lint quiet without changing behaviour: (1) a local nothing READS (unused in the original, only assigned,
    /// or used only by statements that fell back) becomes `_name`; (2) a `let x = init` that no emitted line reassigns becomes `const`.
    /// Decided on the emitted lines, so it always matches what ships.
    /// </summary>
    /// <param name="merge">False keeps the line count (used after statement-level demotion, whose addresses are line indices): only names and const/let change.</param>
    internal static List<string> RenameUnusedLocals(List<string> body, bool merge = true)
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
                result[i] = renamed; // every declaration of the name: nothing reads it anywhere in the method
            }
        }

        // A loop variable the loop body never reads (the body may be commented out, or the name is reused elsewhere in the method).
        for (var i = 0; i < result.Count; i++)
        {
            var loop = Regex.Match(result[i], @"^(\s*)for \(const (\w+) of ");
            if (!loop.Success || result[i].TrimStart().StartsWith("//", StringComparison.Ordinal) || loop.Groups[2].Value.StartsWith('_')) continue;
            var closing = loop.Groups[1].Value + "}";
            var close = result.FindIndex(i + 1, l => l == closing);
            if (close < 0) continue;
            var inner = StripStrings(string.Join("\n", result.Skip(i + 1).Take(close - i - 1).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))));
            if (!Regex.IsMatch(inner, $@"(?<![\w.]){Regex.Escape(loop.Groups[2].Value)}\b"))
                result[i] = Regex.Replace(result[i], @"for \(const (\w+) of ", "for (const _$1 of ");
        }

        // A declaration whose own block never reads it (the same name is read in other blocks, or its readers were commented out by statement-level demotion):
        // renamed with its writes inside that block, so it is not reported as unused.
        for (var i = 0; i < result.Count; i++)
        {
            var decl = Regex.Match(result[i], @"^(\s*)(?:const|let) (\w+)\b");
            if (!decl.Success || result[i].TrimStart().StartsWith("//", StringComparison.Ordinal) || decl.Groups[2].Value.StartsWith('_')) continue;

            var indent = decl.Groups[1].Length;
            var end = result.Count;
            for (var k = i + 1; k < result.Count; k++)
                if (result[k].Trim().Length > 0 && result[k].Length - result[k].TrimStart().Length < indent && result[k].TrimStart().StartsWith('}')) { end = k; break; }

            var name = decl.Groups[2].Value;
            var block = string.Join("\n", result.Skip(i + 1).Take(end - i - 1).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            if (CountOccurrences(block, name) - CountWrites(block, name) > 0) continue;

            result[i] = Regex.Replace(result[i], $@"^(\s*(?:const|let) ){Regex.Escape(name)}\b", $"$1_{name}");
            for (var k = i + 1; k < end; k++)
                if (!result[k].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    result[k] = Regex.Replace(result[k], $@"(?<![\w.]){Regex.Escape(name)}\b", "_" + name);
        }

        // `let x: T;` immediately followed by `x = v;` and never written again is a const declaration.
        for (var i = 0; merge && i + 1 < result.Count; i++)
        {
            var decl = Regex.Match(result[i], @"^(\s*)let (\w+)(: [^=]+)?;$");
            if (!decl.Success || result[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
            var assign = Regex.Match(result[i + 1], $@"^{Regex.Escape(decl.Groups[1].Value)}{Regex.Escape(decl.Groups[2].Value)} = (.*);$");
            if (!assign.Success || CountWrites(Code(), decl.Groups[2].Value) != 1) continue;
            result[i] = $"{decl.Groups[1].Value}const {decl.Groups[2].Value}{decl.Groups[3].Value} = {assign.Groups[1].Value};";
            result.RemoveAt(i + 1);
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
