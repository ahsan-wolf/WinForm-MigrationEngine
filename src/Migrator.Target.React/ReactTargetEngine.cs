using Migrator.Engine.Contracts;
using Migrator.Engine.Projections;
using Migrator.Target.React.Generation;
using Migrator.Target.React.Spec;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.React;

/// <summary>Bound from the engine's "options" in engines.json; relative paths are resolved against engines.json by the module.</summary>
public sealed class ReactTargetOptions
{
    public string UiSpecPath { get; set; } = "ui-library/ui-spec/controls.json";
    public string UiLibraryPath { get; set; } = "ui-library/ui-react";
}

public sealed class ReactTargetModule : IEngineModule
{
    private readonly ReactTargetOptions _options;

    public ReactTargetModule(EngineOptions options)
    {
        var bound = options.Bind<ReactTargetOptions>();
        _options = new ReactTargetOptions { UiSpecPath = options.ResolvePath(bound.UiSpecPath), UiLibraryPath = options.ResolvePath(bound.UiLibraryPath) };
    }

    public void Register(IEngineRegistry registry) => registry.RegisterTarget(new ReactTargetEngine(_options));
}

public sealed class ReactTargetEngine : IProjectTargetEngine
{
    private readonly ReactTargetOptions _options;

    public ReactTargetEngine(ReactTargetOptions options) => _options = options;

    public string Id => "react-app";
    public Version Version { get; } = new(0, 1, 0);
    public Version SchemaVersion { get; } = Version.Parse(IrSchema.Version);
    public string UiLibraryPackage => "@wfm/ui-react";
    public IReadOnlyList<string> RequiredSpecVersion { get; } = new[] { "^0.1" };

    /// <summary>Page-level generator (one component, no project context). Prefer <see cref="GenerateProject"/>.</summary>
    public IIrGenerator Generator => new ReactPageIrGenerator(_options);

    public GenerationResult GenerateProject(string irFolder, string outputDirectory, GenerationOptions options) =>
        new ReactAppGenerator(_options).Run(irFolder, outputDirectory, options);
}

/// <summary>Single-page generation through the shared <see cref="IIrGenerator"/> contract (layout facts are not available at this level).</summary>
public sealed class ReactPageIrGenerator : IIrGenerator
{
    private readonly ReactTargetOptions _options;

    public ReactPageIrGenerator(ReactTargetOptions options) => _options = options;

    public string Target => "react-app";

    public void Generate(PageIr source, string outputDirectory)
    {
        var spec = SpecCatalog.Load(_options.UiSpecPath);
        var unit = new UnitIr(source.Name, "Form", source, new Dictionary<string, ControlLayout>());
        var components = new Dictionary<string, string> { [source.Name] = Naming.Component(source.Name, userControl: false) };

        var page = new PageGenerator(spec, new Dictionary<string, UnitIr> { [source.Name] = unit }, components, new HashSet<string> { source.Name }).Generate(unit);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, page.ComponentName + ".tsx"), page.Source);
        File.WriteAllText(Path.Combine(outputDirectory, page.ComponentName + ".cs.txt"), page.LegacySource);
    }
}
