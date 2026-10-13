using System.Text;
using System.Text.Json;
using Migrator.Engine.Contracts;
using Migrator.Target.React.Contracts;
using Migrator.Target.React.Translation;
using Migrator.Target.React.Spec;

namespace Migrator.Target.React.Generation;

/// <summary>Whole-app generation from an IR folder: scope -> pages -> app shell -> MIGRATION_REPORT.md.</summary>
public sealed class ReactAppGenerator
{
    private readonly ReactTargetOptions _options;

    public ReactAppGenerator(ReactTargetOptions options) => _options = options;

    public GenerationResult Run(string irFolder, string outputDirectory, GenerationOptions options)
    {
        var ir = IrFolder.Load(irFolder);
        var spec = SpecCatalog.Load(_options.UiSpecPath);

        var entry = options.EntryForm ?? ir.Project.Source.StartupForm ?? ir.Project.Forms.FirstOrDefault()?.Name
                    ?? throw new InvalidOperationException("No entry form: the project has no forms and none was given.");
        if (!ir.Units.ContainsKey(entry)) throw new InvalidOperationException($"Entry form '{entry}' is not in project.ir.json.");

        var scope = Reachable(ir, entry, options.Depth);
        var components = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new NameScope(Array.Empty<string>());
        foreach (var name in scope.Keys) components[name] = taken.Unique(Naming.Component(name, ir.Units[name].IsUserControl));

        var result = new GenerationResult { OutputDirectory = outputDirectory };
        var scopeUnits = scope.Keys.Select(n => ir.Units[n]).ToList();

        // External contracts first: the translator needs their vocabulary (facade methods, type names) before any page is generated.
        var contracts = new List<ContractEmitResult>();
        foreach (var file in options.ContractFiles)
        {
            try
            {
                var emitted = ServiceEmitter.Emit(file, scopeUnits, options.ContractScope.Equals("all", StringComparison.OrdinalIgnoreCase), outputDirectory, result.Files,
                    ir.Support?.Classes.SelectMany(c => c.Members));
                contracts.Add(emitted);
                result.Issues.AddRange(emitted.OmittedMembers.Concat(emitted.OmittedMethods).Select(m => new GenerationIssue(emitted.Assembly, "contract-secret-omitted", m)));
                result.Issues.AddRange(emitted.CredentialParametersStripped.Distinct().Select(m => new GenerationIssue(emitted.Assembly, "contract-credentials-stripped", $"{m}: credentials parameter removed; the server supplies it")));
                result.Issues.AddRange(emitted.Notes.Select(n => new GenerationIssue(emitted.Assembly, "contract-unmapped", n)));
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                result.Issues.Add(new GenerationIssue(Path.GetFileName(file), "generation-failed", $"contract could not be used: {ex.Message}"));
            }
        }

        // "llm" runs the rules first; the model tier itself is a later phase, so today it behaves like "rules".
        var translate = options.Translate.Equals("rules", StringComparison.OrdinalIgnoreCase) || options.Translate.Equals("llm", StringComparison.OrdinalIgnoreCase);
        var generated = new HashSet<string>(scope.Keys);
        // The project's own non-UI classes become src/support/index.ts; their names are valid types in every translated page.
        var vocabulary = new ContractVocabulary(contracts.FirstOrDefault());
        SupportRegistry? supportRegistry = null;
        if (translate && ir.Support is { Classes.Count: > 0 } supportFile)
        {
            supportRegistry = SupportRegistry.Build(supportFile);
            foreach (var name in supportRegistry.Classes.Keys) vocabulary.SupportTypes.Add(name);
            supportRegistry.ColorAsync(vocabulary);
        }
        var env = translate
            ? new TranslationEnv { Vocabulary = vocabulary, Components = components, Units = ir.Units, Generated = generated, Support = supportRegistry }
            : null;
        SupportBuild? support = null;
        if (env is not null && supportRegistry is not null)
        {
            support = new SupportBuild(supportRegistry, env, SupportTranslator.Emit(supportRegistry, env, new HashSet<string>(StringComparer.Ordinal)));
            AppWriter.Put(outputDirectory, SupportTranslator.ModulePath, support.Module.Source, result.Files);
        }
        var generator = new PageGenerator(spec, ir.Units, components, generated, env);
        var pages = new List<(string Unit, int Depth, GeneratedPage Page)>();

        foreach (var (name, depth) in scope)
        {
            try
            {
                var page = generator.Generate(ir.Units[name]);
                WritePage(outputDirectory, page, result);
                pages.Add((name, depth, page));
                result.GeneratedUnits.Add(name);
                result.Issues.AddRange(page.Issues);
                result.Issues.AddRange(page.NonVisual.Select(n => new GenerationIssue(name, "non-visual", n)));
            }
            catch (Exception ex)
            {
                // Failure isolation: one broken form is reported and never aborts the run.
                result.SkippedUnits.Add(name);
                result.Issues.Add(new GenerationIssue(name, "generation-failed", ex.Message));
            }
        }

        var skippedOutOfScope = ir.Units.Keys.Where(n => !scope.ContainsKey(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        result.SkippedUnits.AddRange(skippedOutOfScope);

        if (components.TryGetValue(entry, out var entryComponent) && result.GeneratedUnits.Contains(entry))
            AppWriter.Write(outputDirectory, "migrated-" + Naming.Camel(Naming.StripPrefix(entry)).ToLowerInvariant(), entryComponent,
                Path.GetFullPath(_options.UiLibraryPath), result.Files);

        var gate = translate ? RunCompileGate(outputDirectory, options, generator, ir, pages, result, support) : new GateSummary();

        if (translate)
        {
            // Translation issues are listed per method in the report; counts also go to the console summary.
            result.Stats["methods"] = pages.Sum(p => p.Page.MethodsTotal);
            result.Stats["methods.complete"] = pages.Sum(p => p.Page.MethodsComplete);
            result.Stats["methods.partial"] = pages.Sum(p => p.Page.MethodsPartial);
            result.Stats["methods.not-translated"] = pages.Sum(p => p.Page.MethodsNotTranslated);
            result.Stats["methods.demoted-by-gate"] = pages.Sum(p => p.Page.MethodsDemoted);
            result.Stats["gate.remaining-errors"] = gate.RemainingErrors;
            if (support is not null)
            {
                result.Stats["support.classes"] = support.Module.Classes;
                result.Stats["support.methods"] = support.Module.Methods.Count;
                result.Stats["support.complete"] = support.Module.Methods.Count(m => m.Status == TranslationStatus.Complete);
                result.Stats["support.partial"] = support.Module.Methods.Count(m => m.Status == TranslationStatus.Partial);
                result.Stats["support.not-translated"] = support.Module.Methods.Count(m => m.Status == TranslationStatus.NotTranslated);
            }
        }

        AppWriter.Put(outputDirectory, "MIGRATION_REPORT.md", Report(ir, entry, options, spec, pages, result, skippedOutOfScope, contracts, translate, gate, support?.Module), result.Files);
        return result;
    }

    private static void WritePage(string outputDirectory, GeneratedPage page, GenerationResult result)
    {
        AppWriter.Put(outputDirectory, $"src/pages/{page.ComponentName}.tsx", page.Source, result.Files);
        AppWriter.Put(outputDirectory, $"src/legacy/{page.ComponentName}.cs.txt", page.LegacySource, result.Files);
    }

    /// <summary>The support module with what is needed to regenerate it when the compile gate demotes one of its methods.</summary>
    private sealed class SupportBuild
    {
        public SupportBuild(SupportRegistry registry, TranslationEnv env, SupportModule module) { Registry = registry; Env = env; Module = module; }
        public SupportRegistry Registry { get; }
        public TranslationEnv Env { get; }
        public SupportModule Module { get; set; }
        public HashSet<string> Demoted { get; } = new(StringComparer.Ordinal);
    }

    private sealed class GateSummary
    {
        public bool Ran { get; set; }
        public string Note { get; set; } = "";
        public int Iterations { get; set; }
        public int Demoted { get; set; }
        public int RemainingErrors { get; set; }
    }

    /// <summary>
    /// Runs tsc on the generated app; every translated method that contains an error is demoted to commented C# and its page regenerated.
    /// Repeats a few times (demoting a callee can break its callers). Errors outside translated methods are reported, not fixed.
    /// </summary>
    private static GateSummary RunCompileGate(string appDir, GenerationOptions options, PageGenerator generator, IrFolder ir,
        List<(string Unit, int Depth, GeneratedPage Page)> pages, GenerationResult result, SupportBuild? support)
    {
        var gate = new GateSummary();
        if (options.NoCompileGate)
        {
            gate.Note = "skipped (--no-compile-gate)";
            return gate;
        }
        if (!CompileGate.CanRun(appDir))
        {
            if (!options.CompileGate)
            {
                gate.Note = "not run: dependencies are not installed (use --compile-gate to install them and run it)";
                return gate;
            }
            if (!CompileGate.TryInstall(appDir, out var installError))
            {
                gate.Note = "not run: npm install failed: " + installError;
                return gate;
            }
        }

        var demoted = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        const int maxIterations = 5;
        List<TscDiagnostic>? diagnostics = null;
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            diagnostics = CompileGate.RunTsc(appDir);
            if (diagnostics is null) { gate.Note = "not run: tsc could not be started"; return gate; }
            gate.Ran = true;
            if (diagnostics.Count == 0) break;

            var touched = new HashSet<string>(StringComparer.Ordinal);
            var supportTouched = false;
            foreach (var d in diagnostics)
            {
                if (support is not null && d.File == SupportTranslator.ModulePath)
                {
                    var supportRange = support.Module.Ranges.FirstOrDefault(r => r.Start <= d.Line && d.Line <= r.End);
                    if (supportRange.Key is not null && support.Demoted.Add(supportRange.Key)) supportTouched = true;
                    continue;
                }

                var index = pages.FindIndex(p => d.File == $"src/pages/{p.Page.ComponentName}.tsx");
                if (index < 0) continue;
                var range = pages[index].Page.MethodRanges.FirstOrDefault(r => r.Start <= d.Line && d.Line <= r.End);
                if (range.TsName is null) continue;
                if (!demoted.TryGetValue(pages[index].Unit, out var set)) demoted[pages[index].Unit] = set = new HashSet<string>(StringComparer.Ordinal);
                if (set.Add(range.TsName)) touched.Add(pages[index].Unit);
            }
            if (touched.Count == 0 && !supportTouched) break;

            if (supportTouched)
            {
                support!.Module = SupportTranslator.Emit(support.Registry, support.Env, support.Demoted);
                AppWriter.Put(appDir, SupportTranslator.ModulePath, support.Module.Source, result.Files);
            }

            foreach (var unit in touched)
            {
                var index = pages.FindIndex(p => p.Unit == unit);
                var page = generator.Generate(ir.Units[unit], demoted[unit]);
                pages[index] = (unit, pages[index].Depth, page);
                WritePage(appDir, page, result);
            }
            gate.Iterations++;
        }

        gate.Demoted = demoted.Values.Sum(s => s.Count) + (support?.Demoted.Count ?? 0);
        gate.RemainingErrors = diagnostics?.Count ?? 0;
        foreach (var d in (diagnostics ?? new()).Take(40))
            result.Issues.Add(new GenerationIssue(Path.GetFileNameWithoutExtension(d.File), "compile-error", $"{d.File}({d.Line}): {d.Code} {d.Message}"));
        return gate;
    }

    /// <summary>Breadth-first over Show/ShowDialog navigation, bounded by depth (null = all reachable).</summary>
    private static Dictionary<string, int> Reachable(IrFolder ir, string entry, int? maxDepth)
    {
        var scope = new Dictionary<string, int>(StringComparer.Ordinal) { [entry] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(entry);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (maxDepth is { } limit && scope[current] >= limit) continue;
            foreach (var nav in ir.Units[current].Page.Navigations.Where(n => n.TargetType is "Show" or "ShowDialog"))
                if (ir.Units.ContainsKey(nav.Target) && scope.TryAdd(nav.Target, scope[current] + 1))
                    queue.Enqueue(nav.Target);
        }
        return scope;
    }

    private static string Report(IrFolder ir, string entry, GenerationOptions options, SpecCatalog spec,
        List<(string Unit, int Depth, GeneratedPage Page)> pages, GenerationResult result, List<string> outOfScope, List<ContractEmitResult> contracts, bool translate, GateSummary gate, SupportModule? supportModule)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration report — react-app");
        sb.AppendLine();
        sb.AppendLine($"- Source: {ir.Project.Source.Technology} ({ir.Project.Source.FrameworkVersion ?? "framework unknown"}), IR schema {ir.Project.SchemaVersion}");
        sb.AppendLine($"- Entry form: **{entry}**; depth: {(options.Depth?.ToString() ?? "all reachable")} navigation hop(s); ui-spec {spec.SpecVersion}");
        sb.AppendLine($"- Generated **{pages.Count}** of {ir.Units.Count} units; not generated: {outOfScope.Count + result.SkippedUnits.Count(s => !outOfScope.Contains(s))}");
        sb.AppendLine();
        sb.AppendLine("This is a **minimum working app, not a finished migration**. Layout comes from Dock/Location facts (no pixels). Handler logic is **not translated**:");
        sb.AppendLine("every handler keeps its original C# as comments plus a TODO, and the full untranslated code-behind of each page is in `src/legacy/`.");
        sb.AppendLine("Navigation that the IR proves (ShowDialog / Close) is wired to the dialog service. Mock data is marked `// MOCK`.");
        sb.AppendLine();
        sb.AppendLine("## Generated pages");
        sb.AppendLine();
        sb.AppendLine("| Form | Component | Hop | Controls | Handlers | Navigation wired | Issues |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|");
        foreach (var (unit, depth, page) in pages)
            sb.AppendLine($"| {unit} | `{page.ComponentName}` | {depth} | {page.Controls} | {page.Handlers} | {page.NavigationHandlers} | {page.Issues.Count + page.NonVisual.Count} |");

        if (translate)
        {
            var total = pages.Sum(p => p.Page.MethodsTotal);
            var complete = pages.Sum(p => p.Page.MethodsComplete);
            sb.AppendLine();
            sb.AppendLine("## Translation (rules)");
            sb.AppendLine();
            sb.AppendLine($"{total} methods: **{complete} complete** ({(total == 0 ? 0 : 100 * complete / total)}%), {pages.Sum(p => p.Page.MethodsPartial)} partial (TODOs inside), " +
                          $"{pages.Sum(p => p.Page.MethodsNotTranslated)} not translated (kept as commented C#; {pages.Sum(p => p.Page.MethodsDemoted)} of them demoted by the compile gate).");
            sb.AppendLine(gate.Ran
                ? $"Compile gate: ran ({gate.Iterations} regeneration round(s)); {gate.RemainingErrors} TypeScript error(s) remain."
                : $"Compile gate: {(gate.Note.Length > 0 ? gate.Note : "not run")}.");
            sb.AppendLine("Rule-translated code is deterministic but unreviewed: check business logic, async behaviour and everything marked TODO. Original C# is always kept (comments and `src/legacy/`).");
            sb.AppendLine();
            sb.AppendLine("| Form | Methods | Complete | Partial | Not translated |");
            sb.AppendLine("|---|---:|---:|---:|---:|");
            foreach (var (unit, _, page) in pages)
                sb.AppendLine($"| {unit} | {page.MethodsTotal} | {page.MethodsComplete} | {page.MethodsPartial} | {page.MethodsNotTranslated} |");

            var reasons = pages.SelectMany(p => p.Page.TodoReasons).Select(r => r[(r.IndexOf(": ", StringComparison.Ordinal) + 2)..])
                .Select(r => System.Text.RegularExpressions.Regex.Replace(r, @"'[^']*'", "'…'")).GroupBy(r => r).OrderByDescending(g => g.Count()).Take(20).ToList();
            if (reasons.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Most common reasons a statement stayed commented:");
                foreach (var g in reasons) sb.AppendLine($"- {g.Count()}× {g.Key}");
            }

            if (supportModule is not null)
            {
                var sm = supportModule.Methods;
                sb.AppendLine();
                sb.AppendLine("### Project classes (`src/support/index.ts`)");
                sb.AppendLine();
                sb.AppendLine($"{supportModule.Classes} classes/enums from the project's own non-form code (static helper classes, data classes, enums); {sm.Count} methods/accessors/constructors: "
                              + $"**{sm.Count(m => m.Status == TranslationStatus.Complete)} complete**, {sm.Count(m => m.Status == TranslationStatus.Partial)} partial, "
                              + $"{sm.Count(m => m.Status == TranslationStatus.NotTranslated)} not translated (typed stubs; original C# kept as comments).");
                var supportReasons = sm.SelectMany(m => m.Reasons).Select(r => System.Text.RegularExpressions.Regex.Replace(r, @"'[^']*'", "'…'"))
                    .GroupBy(r => r).OrderByDescending(g => g.Count()).Take(10).ToList();
                if (supportReasons.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Most common reasons in project classes:");
                    foreach (var g in supportReasons) sb.AppendLine($"- {g.Count()}× {g.Key}");
                }
                foreach (var note in supportModule.Notes) sb.AppendLine($"- {note}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Manual work by category");
        var order = new[]
        {
            ("generation-failed", "Generation failed (page skipped)"),
            ("unsupported-control", "Controls without a library component (`WfUnsupported` placeholder + TODO)"),
            ("nav-multi-target", "Handlers that can open several forms (only the first is wired)"),
            ("nav-out-of-scope", "Navigation to forms that were not generated"),
            ("grid-no-columns", "Grids whose columns are created in code"),
            ("event-not-mapped", "WinForms events with no ui-spec event"),
            ("user-control-inlined", "User controls rendered as panels"),
            ("image-resource", "Designer images not extracted"),
            ("non-visual", "Non-visual components (not rendered)"),
            ("compile-error", "TypeScript errors that remain after the compile gate (not inside translated methods)"),
            ("contract-secret-omitted", "Secret-looking members omitted from generated services (SERVER-ONLY)"),
            ("contract-credentials-stripped", "Service methods kept with their credentials parameter removed (server-side concern)"),
            ("contract-unmapped", "Contract types without a TypeScript mapping (`unknown`)")
        };
        foreach (var (category, title) in order)
        {
            var items = result.Issues.Where(i => i.Category == category).ToList();
            if (items.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"### {title} — {items.Count}");
            foreach (var group in items.GroupBy(i => i.Unit).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"- **{group.Key}**");
                foreach (var item in group.Take(25)) sb.AppendLine($"  - {item.Message}");
                if (group.Count() > 25) sb.AppendLine($"  - … and {group.Count() - 25} more");
            }
        }

        foreach (var c in contracts)
        {
            sb.AppendLine();
            sb.AppendLine($"## External contract: {c.Assembly}");
            sb.AppendLine();
            sb.AppendLine($"Typed **mock** service generated into `src/services/{c.Assembly.ToLowerInvariant()}/` from the assembly's public API (scope `{c.Scope}`): " +
                          $"{c.DataTypes} data type(s), {c.Enums} enum(s), {c.Methods} service method(s) ({c.CallSites} call site(s) in the generated forms).");
            sb.AppendLine("Calls are async even where the C# API was synchronous. Fixtures are fake and prove shapes only. Pages do not call the service yet (handler translation is a later phase).");
            if (c.OmittedMethods.Count + c.OmittedMembers.Count > 0)
                sb.AppendLine($"Omitted as secret-looking (never shipped to the browser; belong on a server): {c.OmittedMethods.Count} method(s), {c.OmittedMembers.Count} member(s) — listed above.");
        }

        if (outOfScope.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Not generated ({outOfScope.Count})");
            sb.AppendLine();
            sb.AppendLine("Outside the requested navigation depth (or not reachable from the entry form): " + string.Join(", ", outOfScope) + ".");
        }

        sb.AppendLine();
        sb.AppendLine("## Known limitations of this generator");
        sb.AppendLine("- Handler bodies are not translated (planned LLM phase). Helper methods exist only in `src/legacy/*.cs.txt`.");
        sb.AppendLine("- `Show()` (modeless) is opened as a dialog; there is no router yet.");
        sb.AppendLine("- Visibility toggled by code, data loading, validation and backend (contract) calls are not wired.");
        sb.AppendLine("- ListView, TreeView, tabs, radio groups, date pickers and group boxes have no library component yet.");
        return sb.ToString();
    }
}
