using Migrator.Engine.Contracts;
using Migrator.Target.Angular.Generation;
using Migrator.Target.Core.Generation;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.Angular;

/// <summary>Bound from the engine's "options" in engines.json; relative paths are resolved against engines.json by the module.</summary>
public sealed class AngularTargetOptions
{
    public string UiSpecPath { get; set; } = "ui-library/ui-spec/controls.json";
    public string UiLibraryPath { get; set; } = "ui-library/ui-angular";

    /// <summary>Windows-specific APIs to bypass during translation (data file; copy and extend it per project).</summary>
    public string BypassRulesPath { get; set; } = "rules/windows-bypass.json";

    /// <summary>Markers of server-side code (data access, file system, configuration) that is left out of the browser app (data file).</summary>
    public string ServerBoundRulesPath { get; set; } = "rules/server-bound.json";

    /// <summary>Name patterns that classify user controls the designer cannot: grid, tree, menu, toolbar, list (data file).</summary>
    public string ArchetypeRulesPath { get; set; } = "rules/control-archetypes.json";
}

public sealed class AngularTargetModule : IEngineModule
{
    private readonly AngularTargetOptions _options;

    public AngularTargetModule(EngineOptions options)
    {
        var bound = options.Bind<AngularTargetOptions>();
        _options = new AngularTargetOptions { UiSpecPath = options.ResolvePath(bound.UiSpecPath), UiLibraryPath = options.ResolvePath(bound.UiLibraryPath), BypassRulesPath = options.ResolvePath(bound.BypassRulesPath), ServerBoundRulesPath = options.ResolvePath(bound.ServerBoundRulesPath), ArchetypeRulesPath = options.ResolvePath(bound.ArchetypeRulesPath) };
    }

    public void Register(IEngineRegistry registry) => registry.RegisterTarget(new AngularTargetEngine(_options));
}

public sealed class AngularTargetEngine : IProjectTargetEngine
{
    private readonly AngularTargetOptions _options;

    public AngularTargetEngine(AngularTargetOptions options) => _options = options;

    public string Id => "angular-app";
    public Version Version { get; } = new(0, 5, 0);
    public Version SchemaVersion { get; } = Version.Parse(IrSchema.Version);
    public string UiLibraryPackage => "@wfm/ui-angular";
    public IReadOnlyList<string> RequiredSpecVersion { get; } = new[] { "^0.4" };

    /// <summary>The page-level generator is not offered: a page needs the project context (routes, shared support code). Use <see cref="GenerateProject"/>.</summary>
    public IIrGenerator Generator => throw new NotSupportedException("angular-app generates whole projects; use GenerateProject.");

    public GenerationResult GenerateProject(string irFolder, string outputDirectory, GenerationOptions options)
    {
        if (options.Preview || options.AppShell)
            throw new InvalidOperationException("The component viewer and the app shell are not available for angular-app yet (use react-app for them).");
        return new ProjectGenerator(new AngularBackend(_options)).Run(irFolder, outputDirectory, options);
    }
}
