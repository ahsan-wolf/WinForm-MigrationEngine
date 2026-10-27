using Migrator.Engine.Contracts;
using Migrator.Target.Api.Generation;
using Migrator.Target.Core.Generation;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Target.Api;

/// <summary>Bound from the engine's "options" in engines.json; relative paths are resolved against engines.json by the module.</summary>
public sealed class ApiTargetOptions
{
    /// <summary>Markers of server-side code: the classes that carry them become the API (data file, shared with the web targets).</summary>
    public string ServerBoundRulesPath { get; set; } = "rules/server-bound.json";

    /// <summary>How server-bound classes are ported: usings, providers and aliases (data file; copy and extend it per project).</summary>
    public string ApiPortRulesPath { get; set; } = "rules/api-port.json";

    /// <summary>How the database structure is inferred from the SQL in the data-access code: type hints and ignored names (data file; copy and extend it per project).</summary>
    public string SchemaInferRulesPath { get; set; } = "rules/schema-infer.json";
}

public sealed class ApiTargetModule : IEngineModule
{
    private readonly ApiTargetOptions _options;

    public ApiTargetModule(EngineOptions options)
    {
        var bound = options.Bind<ApiTargetOptions>();
        _options = new ApiTargetOptions { ServerBoundRulesPath = options.ResolvePath(bound.ServerBoundRulesPath), ApiPortRulesPath = options.ResolvePath(bound.ApiPortRulesPath), SchemaInferRulesPath = options.ResolvePath(bound.SchemaInferRulesPath) };
    }

    public void Register(IEngineRegistry registry) => registry.RegisterTarget(new ApiTargetEngine(_options));
}

/// <summary>Writes the server side of a migration: a .NET 10 minimal API that hosts the project's data-access classes and exposes their methods as operations.</summary>
public sealed class ApiTargetEngine : IProjectTargetEngine
{
    private readonly ApiTargetOptions _options;

    public ApiTargetEngine(ApiTargetOptions options) => _options = options;

    public string Id => "dotnet-api";
    public Version Version { get; } = new(0, 5, 0);
    public Version SchemaVersion { get; } = Version.Parse(IrSchema.Version);
    public string UiLibraryPackage => "";
    public IReadOnlyList<string> RequiredSpecVersion { get; } = Array.Empty<string>();

    public IIrGenerator Generator => throw new NotSupportedException("dotnet-api generates a whole project; use GenerateProject.");

    public GenerationResult GenerateProject(string irFolder, string outputDirectory, GenerationOptions options)
    {
        if (options.Preview || options.AppShell) throw new InvalidOperationException("The component viewer and the app shell are web targets; dotnet-api generates the server side only.");
        return new ApiProjectGenerator(_options).Run(irFolder, outputDirectory, options);
    }
}
