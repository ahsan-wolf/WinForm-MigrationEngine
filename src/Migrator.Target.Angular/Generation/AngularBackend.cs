using Migrator.Engine.Contracts;
using Migrator.Target.Core.Generation;
using Migrator.Target.Core.Ir;
using Migrator.Target.Core.Spec;
using Migrator.Target.Core.Translation;

namespace Migrator.Target.Angular.Generation;

/// <summary>The Angular side of the shared project pipeline: pages as standalone components, the workspace skeleton, and `ngc` as the type checker.</summary>
public sealed class AngularBackend : ITargetBackend
{
    private readonly AngularTargetOptions _options;
    private readonly Dictionary<string, PageEntry> _pages = new(StringComparer.Ordinal);

    public AngularBackend(AngularTargetOptions options) => _options = options;

    public string Key => "angular-app";
    public string UiSpecPath => _options.UiSpecPath;
    public string UiLibraryPath => _options.UiLibraryPath;
    public string UiLibraryPackage => "@wfm/ui-angular";
    public string BypassRulesPath => _options.BypassRulesPath;
    public string ServerBoundRulesPath => _options.ServerBoundRulesPath;
    public string ArchetypeRulesPath => _options.ArchetypeRulesPath;

    public IPageGenerator CreatePageGenerator(SpecCatalog spec, IReadOnlyDictionary<string, UnitIr> units, IReadOnlyDictionary<string, string> components, ISet<string> generated, TranslationEnv? env) =>
        new PageGenerator(spec, units, components, generated, env, assembler: new AngularPageAssembler());

    public void WritePage(string appDir, GeneratedPage page, GenerationResult result)
    {
        var fileBase = AngularPageAssembler.FileBase(page.ComponentName);
        _pages[page.ComponentName] = new PageEntry(page.ComponentName, fileBase);
        OutputFiles.Put(appDir, $"src/pages/{fileBase}.page.html", page.Source, result.Files);
        OutputFiles.Put(appDir, $"src/pages/{fileBase}.page.code.ts", page.CodeSource, result.Files);
        OutputFiles.Put(appDir, $"src/pages/{fileBase}.page.ts", page.ComponentSource, result.Files);
        OutputFiles.Put(appDir, $"src/legacy/{fileBase}.cs.txt", page.LegacySource, result.Files);
        foreach (var (path, data) in page.Assets) OutputFiles.PutBytes(appDir, path, data, result.Files);
    }

    public string CodeFilePath(GeneratedPage page) => $"src/pages/{AngularPageAssembler.FileBase(page.ComponentName)}.page.code.ts";

    public void WriteApp(string appDir, string appName, string entryComponent, string uiLibraryDir, List<string> files, string? favicon, IReadOnlyList<CatalogEntry> catalog) =>
        AngularAppWriter.Write(appDir, appName, entryComponent, uiLibraryDir, _pages.Values.ToList(), files, favicon, catalog);

    private static string NgcPath(string appDir) => Path.Combine(appDir, "node_modules", "@angular", "compiler-cli", "bundles", "src", "bin", "ngc.js");

    public bool CanRunTypeCheck(string appDir) => File.Exists(NgcPath(appDir));
    public bool TryInstall(string appDir, out string error) => CompileGate.TryInstall(appDir, out error) || CanRunTypeCheck(appDir);

    /// <summary>`ngc --noEmit`: the TypeScript check plus the template type check (strict templates).</summary>
    public List<TscDiagnostic>? RunTypeCheck(string appDir) =>
        CompileGate.RunChecker(appDir, NgcPath(appDir), "-p", "tsconfig.app.json", "--noEmit", "--pretty", "false");
}
