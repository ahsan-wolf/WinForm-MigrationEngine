using Migrator.Engine.Contracts;
using Migrator.Engine.Projections;
using Migrator.Source.WinForms.Extraction;
using Migrator.Source.WinForms.Projections;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms;

public sealed class WinFormsSourceEngine : IProjectAwareSourceEngine, IIrProjectionProvider
{
    public string Id => "winforms";
    public string Technology => "WinForms";
    public Version Version { get; } = new(0, 1, 0);
    public Version SchemaVersion { get; } = Version.Parse(IrSchema.Version);

    public IProjectAnalyzer Analyzer { get; } = new WinFormsProjectAnalyzer();

    /// <summary>Project-independent pipeline (UserControls are left as opaque leaves). Prefer <see cref="CreateExtractors"/>.</summary>
    public IEnumerable<IIrExtractor> Extractors => Pipeline(null);

    /// <summary>Same pipeline, with the project's UserControls inlined into the control tree.</summary>
    public IEnumerable<IIrExtractor> CreateExtractors(ProjectAnalysis analysis)
    {
        var userControls = analysis.UserControls.ToDictionary(
            u => u.Name,
            u => new WinFormsSourceText(File.ReadAllText(u.DesignerPath), File.ReadAllText(u.CodeBehindPath)));
        return Pipeline(userControls);
    }

    /// <summary>WinForms-specific facet files on top of the generic ones the host writes (controls, control-properties, code-behind).</summary>
    public IEnumerable<IIrProjection> Projections { get; } = new IIrProjection[] { new WinFormsLayoutProjection() };

    // Order matters: tree -> members -> semantics -> workflows -> navigation (later facets read the earlier ones from PageIr).
    private static IEnumerable<IIrExtractor> Pipeline(IReadOnlyDictionary<string, WinFormsSourceText>? userControls) => new IIrExtractor[]
    {
        new WinFormsControlTreeExtractor(userControls),
        new WinFormsMemberExtractor(),
        new WinFormsSemanticsExtractor(),
        new WinFormsWorkflowExtractor(),
        new WinFormsNavigationExtractor()
    };
}
