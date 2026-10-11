using System.Text;
using Migrator.Engine.Contracts;
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
        var generator = new PageGenerator(spec, ir.Units, components, new HashSet<string>(scope.Keys));
        var pages = new List<(string Unit, int Depth, GeneratedPage Page)>();

        foreach (var (name, depth) in scope)
        {
            try
            {
                var page = generator.Generate(ir.Units[name]);
                AppWriter.Put(outputDirectory, $"src/pages/{page.ComponentName}.tsx", page.Source, result.Files);
                AppWriter.Put(outputDirectory, $"src/legacy/{page.ComponentName}.cs.txt", page.LegacySource, result.Files);
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

        AppWriter.Put(outputDirectory, "MIGRATION_REPORT.md", Report(ir, entry, options, spec, pages, result, skippedOutOfScope), result.Files);
        return result;
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
        List<(string Unit, int Depth, GeneratedPage Page)> pages, GenerationResult result, List<string> outOfScope)
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
            ("non-visual", "Non-visual components (not rendered)")
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
        sb.AppendLine("- Visibility toggled by code, data loading, validation and `ECMLibrary`-style backend calls are not wired.");
        sb.AppendLine("- ListView, TreeView, tabs, radio groups, date pickers and group boxes have no library component yet.");
        return sb.ToString();
    }
}
