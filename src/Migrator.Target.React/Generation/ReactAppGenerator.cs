using System.Text;
using Migrator.Engine.Contracts;
using Migrator.Target.Core.Generation;
using Migrator.Target.Core.Ir;
using Migrator.Target.Core.Spec;
using Migrator.Target.Core.Translation;

namespace Migrator.Target.React.Generation;

/// <summary>The React side of the shared project pipeline: pages, app skeleton and the type checker. The viewer and the app shell are React-only tools.</summary>
public sealed class ReactBackend : ITargetBackend
{
    private readonly ReactTargetOptions _options;

    public ReactBackend(ReactTargetOptions options) => _options = options;

    public string Key => "react-app";
    public string UiSpecPath => _options.UiSpecPath;
    public string UiLibraryPath => _options.UiLibraryPath;
    public string BypassRulesPath => _options.BypassRulesPath;
    public string ServerBoundRulesPath => _options.ServerBoundRulesPath;

    public IPageGenerator CreatePageGenerator(SpecCatalog spec, IReadOnlyDictionary<string, UnitIr> units, IReadOnlyDictionary<string, string> components, ISet<string> generated, TranslationEnv? env) =>
        new PageGenerator(spec, units, components, generated, env);

    public void WritePage(string outputDirectory, GeneratedPage page, GenerationResult result)
    {
        OutputFiles.Put(outputDirectory, $"src/pages/{page.ComponentName}.tsx", page.Source, result.Files);
        if (page.CodeSource.Length > 0) OutputFiles.Put(outputDirectory, $"src/pages/{page.ComponentName}.code.tsx", page.CodeSource, result.Files);
        OutputFiles.Put(outputDirectory, $"src/legacy/{page.ComponentName}.cs.txt", page.LegacySource, result.Files);
        AppWriter.PutAssets(outputDirectory, page, result.Files);
    }

    public string CodeFilePath(GeneratedPage page) => $"src/pages/{page.ComponentName}.code.tsx";

    public void WriteApp(string appDir, string appName, string entryComponent, string uiLibraryDir, List<string> files, string? favicon) =>
        AppWriter.Write(appDir, appName, entryComponent, uiLibraryDir, files, favicon);

    public bool CanRunTypeCheck(string appDir) => CompileGate.CanRun(appDir);
    public bool TryInstall(string appDir, out string error) => CompileGate.TryInstall(appDir, out error);
    public List<TscDiagnostic>? RunTypeCheck(string appDir) => CompileGate.RunTsc(appDir);
}

/// <summary>Whole-app generation from an IR folder. The app itself is the shared <see cref="ProjectGenerator"/>; the component viewer and the app shell (visual-only pages) are React tools.</summary>
public sealed class ReactAppGenerator
{
    private readonly ReactTargetOptions _options;

    public ReactAppGenerator(ReactTargetOptions options) => _options = options;

    public GenerationResult Run(string irFolder, string outputDirectory, GenerationOptions options)
    {
        if (options.Preview || options.AppShell)
        {
            var ir = IrFolder.Load(irFolder);
            return RunPreview(ir, SpecCatalog.Load(_options.UiSpecPath), outputDirectory, options.AppShell);
        }
        return new ProjectGenerator(new ReactBackend(_options)).Run(irFolder, outputDirectory, options);
    }

    /// <summary>
    /// Component viewer: every form and user control becomes a visual-only page, listed in a manifest, plus the viewer shell. No entry form, navigation,
    /// contracts, translation, support module or legacy copies.
    /// </summary>
    private GenerationResult RunPreview(IrFolder ir, SpecCatalog spec, string outputDirectory, bool appShell)
    {
        var result = new GenerationResult { OutputDirectory = outputDirectory };
        var components = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new NameScope(Array.Empty<string>());
        foreach (var name in ir.Units.Keys.OrderBy(n => n, StringComparer.Ordinal)) components[name] = taken.Unique(Naming.Component(name, ir.Units[name].IsUserControl));

        var generator = new PageGenerator(spec, ir.Units, components, new HashSet<string>(ir.Units.Keys), translation: null, preview: true, previewNavigation: appShell);
        var viewerUnits = new List<ViewerUnit>();
        foreach (var (name, unit) in ir.Units.OrderBy(u => u.Key, StringComparer.Ordinal))
        {
            var source = unit.Page.RelativePath ?? "";
            var group = source.Contains('/') ? source[..source.LastIndexOf('/')] : "";
            var rootLayout = unit.Page.RootControl is { } root ? unit.Layout.GetValueOrDefault(root.Id)?.Size : null;
            try
            {
                var page = generator.Generate(unit);
                AppWriter.Put(outputDirectory, $"src/pages/{page.ComponentName}.tsx", page.Source, result.Files);
                AppWriter.PutAssets(outputDirectory, page, result.Files);
                result.GeneratedUnits.Add(name);
                result.Issues.AddRange(page.Issues);
                result.Issues.AddRange(page.NonVisual.Select(n => new GenerationIssue(name, "non-visual", n)));
                viewerUnits.Add(new ViewerUnit(name, page.ComponentName, page.ComponentName, unit.IsUserControl ? "control" : "form", unit.Title, group, source, page.Controls,
                    page.Issues.Where(i => i.Category == "fallback-control").Select(i => i.Message).ToList(),
                    page.Issues.Where(i => i.Category == "unsupported-control").Select(i => i.Message).ToList(),
                    rootLayout?.Width, rootLayout?.Height, null));
            }
            catch (Exception ex)
            {
                // Failure isolation: the unit stays in the list with its error, and the viewer shows it.
                result.SkippedUnits.Add(name);
                result.Issues.Add(new GenerationIssue(name, "generation-failed", ex.Message));
                viewerUnits.Add(new ViewerUnit(name, components[name], components[name], unit.IsUserControl ? "control" : "form", unit.Title, group, source, 0,
                    new List<string>(), new List<string>(), null, null, ex.Message));
            }
        }

        var projectName = Path.GetFileName((ir.Project.Source.RootPath ?? "").TrimEnd('/', '\\'));
        ViewerShell.Write(outputDirectory, (appShell ? "winform-appshell" : "viewer-" + Naming.Camel(Naming.StripPrefix(ir.Project.Source.Technology)).ToLowerInvariant()),
            Path.GetFullPath(_options.UiLibraryPath), viewerUnits, projectName.Length > 0 ? projectName : "Application", appShell, result.Files);
        AppWriter.Put(outputDirectory, "VIEWER_REPORT.md", ViewerReport(viewerUnits, result), result.Files);
        result.Stats["viewer.units"] = viewerUnits.Count;
        return result;
    }

    private static string ViewerReport(List<ViewerUnit> units, GenerationResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Viewer report");
        sb.AppendLine();
        sb.AppendLine($"- {units.Count(u => u.Error is null)} of {units.Count} units rendered ({units.Count(u => u.Kind == "form")} forms, {units.Count(u => u.Kind == "control")} user controls). Pages are visual-only: controls and layout as designed, placeholder data, no handlers or logic.");
        var withFallbacks = units.Where(u => u.Fallbacks.Count > 0).ToList();
        sb.AppendLine($"- {withFallbacks.Sum(u => u.Fallbacks.Count)} control(s) in {withFallbacks.Count} unit(s) are drawn by the closest library component (fallback).");
        var withUnsupported = units.Where(u => u.Unsupported.Count > 0).ToList();
        sb.AppendLine($"- {withUnsupported.Sum(u => u.Unsupported.Count)} control(s) in {withUnsupported.Count} unit(s) have no component at all (placeholder).");
        if (units.Any(u => u.Error is not null))
        {
            sb.AppendLine();
            sb.AppendLine("## Units that could not be generated");
            foreach (var u in units.Where(u => u.Error is not null)) sb.AppendLine($"- **{u.Id}**: {u.Error}");
        }
        if (withFallbacks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Fallback controls");
            foreach (var u in withFallbacks) { sb.AppendLine($"- **{u.Id}**"); foreach (var f in u.Fallbacks) sb.AppendLine($"  - {f}"); }
        }
        if (withUnsupported.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Controls without a component");
            foreach (var u in withUnsupported) { sb.AppendLine($"- **{u.Id}**"); foreach (var f in u.Unsupported) sb.AppendLine($"  - {f}"); }
        }
        return sb.ToString();
    }
}
