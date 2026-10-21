using System.Text.Json;
using Migrator.Engine.Contracts;
using Migrator.Engine.Projections;
using WebFormsMigrator.IR.Models;

namespace Migrator.Engine.Pipeline;

public sealed record IrWriteSummary(string ProjectIrPath, IReadOnlyList<string> Files);

/// <summary>
/// Writes <c>&lt;out&gt;/ir/{Unit}/{Unit}.complete.ir.json</c> (source of truth), one derived file per projection, and
/// <c>&lt;out&gt;/ir/project.ir.json</c>. Every facet is computed from the complete PageIr that is written next to it.
/// </summary>
public static class IrFileWriter
{
    public const string CompleteSuffix = "complete";
    public const string SupportFileName = "support.ir.json";
    public const string ResourcesFileName = "resources.ir.json";
    private const int MaxUnsupportedPerUnit = 50;

    /// <summary>Indented PascalCase, matching the existing IR samples.</summary>
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static IrWriteSummary Write(string outputDir, ISourceEngine engine, ProjectAnalysis analysis, IReadOnlyList<UnitIrResult> results)
    {
        var irRoot = Path.Combine(outputDir, "ir");
        Directory.CreateDirectory(irRoot);

        var projections = new List<IIrProjection> { new ControlsProjection(), new ControlPropertiesProjection(), new CodeBehindProjection() };
        if (engine is IIrProjectionProvider provider) projections.AddRange(provider.Projections);

        var project = new ProjectIr
        {
            SchemaVersion = IrSchema.Version,
            Source = new ProjectSourceIr
            {
                Technology = engine.Technology,
                FrameworkVersion = analysis.FrameworkVersion,
                RootPath = Path.GetFullPath(analysis.RootPath),
                StartupForm = analysis.StartupForm,
                AdditionalRoots = analysis.AdditionalRoots?.Select(Path.GetFullPath).ToList() ?? new List<string>()
            }
        };

        var written = new List<string>();
        foreach (var result in results)
        {
            if (result.Page is null)
            {
                project.Failures.Add(new ProjectFailureIr { Name = result.Unit.Name, Kind = result.Unit.Kind.ToString(), Error = result.Error ?? "" });
                continue;
            }

            var name = result.Unit.Name;
            var unitDir = Path.Combine(irRoot, name);
            Directory.CreateDirectory(unitDir);

            var completeFile = $"{name}.{CompleteSuffix}.ir.json";
            WriteJson(Path.Combine(unitDir, completeFile), result.Page, written);
            foreach (var projection in projections)
                WriteJson(Path.Combine(unitDir, $"{name}.{projection.FileSuffix}.ir.json"), projection.Project(result.Page), written);

            AddUnsupported(project, name, result.Unsupported);

            var entry = new ProjectUnitIr { Name = name, Kind = result.Unit.Kind.ToString(), IrFile = $"{name}/{completeFile}" };
            (result.Unit.Kind == SourceUnitKind.UserControl ? project.UserControls : project.Forms).Add(entry);
        }

        if (engine is ISupportSourceEngine supportEngine && supportEngine.ExtractSupport(analysis) is { Classes.Count: > 0 } support)
            WriteJson(Path.Combine(irRoot, SupportFileName), support, written);

        if (engine is IResourceSourceEngine resourceEngine && resourceEngine.ExtractResources(analysis) is { Images.Count: > 0 } resources)
            WriteJson(Path.Combine(irRoot, ResourcesFileName), resources, written);

        if (engine is IDependencySourceEngine dependencies)
            project.Shared.ExternalAssemblies.AddRange(dependencies.FindExternalAssemblies(analysis));

        var projectPath = Path.Combine(irRoot, "project.ir.json");
        WriteJson(projectPath, project, written);
        return new IrWriteSummary(projectPath, written);
    }

    /// <summary>Listed per unit as "{Unit}: {what}", capped so one noisy form cannot bury the rest.</summary>
    private static void AddUnsupported(ProjectIr project, string unit, IReadOnlyList<string>? items)
    {
        if (items is null) return;
        foreach (var item in items.Take(MaxUnsupportedPerUnit))
            project.Shared.Unsupported.Add($"{unit}: {item}");
        if (items.Count > MaxUnsupportedPerUnit)
            project.Shared.Unsupported.Add($"{unit}: ... and {items.Count - MaxUnsupportedPerUnit} more");
    }

    private static void WriteJson(string path, object value, List<string> written)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, value.GetType(), Json));
        written.Add(path);
    }
}
