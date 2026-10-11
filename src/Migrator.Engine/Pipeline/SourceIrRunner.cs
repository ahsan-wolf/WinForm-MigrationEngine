using Migrator.Engine.Contracts;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Engine.Pipeline;

/// <summary>Outcome for one Form/UserControl. Exactly one of <see cref="Page"/> / <see cref="Error"/> is set.</summary>
public sealed record UnitIrResult(SourceUnit Unit, PageIr? Page, string? Error, IReadOnlyList<string>? Unsupported = null);

/// <summary>Runs a source engine's extractor pipeline over every unit of an analyzed project.</summary>
public static class SourceIrRunner
{
    /// <summary>UserControls first, then Forms. A failure on one unit is recorded and never aborts the run.</summary>
    public static IReadOnlyList<UnitIrResult> Run(ISourceEngine engine, ProjectAnalysis analysis)
    {
        var extractors = engine is IProjectAwareSourceEngine aware ? aware.CreateExtractors(analysis) : engine.Extractors;
        var registry = new IrPipelineRegistry();
        foreach (var extractor in extractors) registry.AddExtractor(extractor);

        var results = new List<UnitIrResult>();
        foreach (var unit in analysis.UserControls.Concat(analysis.Forms))
        {
            try
            {
                var context = new IrExtractionContext
                {
                    FilePath = unit.CodeBehindPath,
                    MarkupContent = File.ReadAllText(unit.DesignerPath),
                    CodeBehindContent = File.ReadAllText(unit.CodeBehindPath)
                };
                var page = registry.BuildIr(context, unit.Name);
                page.RelativePath = Path.GetRelativePath(analysis.RootPath, unit.CodeBehindPath).Replace('\\', '/');
                var unsupported = engine is IUnsupportedReporter reporter ? reporter.FindUnsupported(context).ToList() : null;
                results.Add(new UnitIrResult(unit, page, null, unsupported));
            }
            catch (Exception ex)
            {
                results.Add(new UnitIrResult(unit, null, ex.Message));
            }
        }
        return results;
    }
}
