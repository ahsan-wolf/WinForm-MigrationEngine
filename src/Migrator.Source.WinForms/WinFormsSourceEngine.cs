using Migrator.Engine.Contracts;
using Migrator.Engine.Projections;
using Migrator.Engine.Support;
using Migrator.Source.WinForms.Extraction;
using Migrator.Source.WinForms.Projections;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms;

public sealed class WinFormsSourceEngine : IProjectAwareSourceEngine, IIrProjectionProvider, IUnsupportedReporter, ISupportSourceEngine, IResourceSourceEngine, IDependencySourceEngine
{
    public string Id => "winforms";
    public string Technology => "WinForms";
    public Version Version { get; } = new(0, 2, 0);
    public Version SchemaVersion { get; } = Version.Parse(IrSchema.Version);

    public IProjectAnalyzer Analyzer { get; } = new WinFormsProjectAnalyzer();

    /// <summary>Project-independent pipeline (UserControls are left as opaque leaves). Prefer <see cref="CreateExtractors"/>.</summary>
    public IEnumerable<IIrExtractor> Extractors => Pipeline(null);

    /// <summary>Same pipeline, with the project's UserControls inlined into the control tree.</summary>
    public IEnumerable<IIrExtractor> CreateExtractors(ProjectAnalysis analysis)
    {
        // Same class name in two namespaces would collide; the first wins (inlining matches on the simple name anyway).
        var userControls = analysis.UserControls.GroupBy(u => u.Name).Select(g => g.First()).ToDictionary(
            u => u.Name,
            u => new WinFormsSourceText(File.ReadAllText(u.DesignerPath), File.ReadAllText(u.CodeBehindPath), ResxReader.ReadSibling(u.CodeBehindPath)));
        return Pipeline(userControls, new WinFormsTypeResolver(analysis.TypeBases), analysis.TypeFiles);
    }

    /// <summary>The project's own non-UI classes (helpers, data classes, enums), written as support.ir.json.</summary>
    public SupportFileIr ExtractSupport(ProjectAnalysis analysis) => WinFormsSupportExtractor.Extract(analysis);

    /// <summary>The images of the project's Resources.resx that code references, written as resources.ir.json.</summary>
    public ResourceFileIr ExtractResources(ProjectAnalysis analysis) => WinFormsResourceExtractor.Extract(analysis);

    /// <summary>External (binary) assemblies the project files reference, for contract extraction.</summary>
    public IReadOnlyList<ExternalAssemblyIr> FindExternalAssemblies(ProjectAnalysis analysis) => WinFormsDependencyScanner.Find(analysis);

    /// <summary>WinForms-specific facet files on top of the generic ones the host writes (controls, control-properties, code-behind).</summary>
    public IEnumerable<IIrProjection> Projections { get; } = new IIrProjection[] { new WinFormsLayoutProjection() };

    /// <summary>Designer statements the parser did not understand (the parser records them rather than dropping them).</summary>
    public IEnumerable<string> FindUnsupported(IrExtractionContext context) =>
        WinFormsDesignerParser.Parse(context.MarkupContent).Unhandled;

    // Order matters: tree -> members -> semantics -> workflows -> navigation (later facets read the earlier ones from PageIr).
    private static IEnumerable<IIrExtractor> Pipeline(IReadOnlyDictionary<string, WinFormsSourceText>? userControls, WinFormsTypeResolver? resolver = null, IReadOnlyDictionary<string, IReadOnlyList<string>>? typeFiles = null) => new IIrExtractor[]
    {
        new WinFormsControlTreeExtractor(userControls, resolver: resolver),
        new WinFormsMemberExtractor(typeFiles),
        new WinFormsCodeWiringExtractor(),
        new WinFormsSemanticsExtractor(),
        new WinFormsWorkflowExtractor(),
        new WinFormsNavigationExtractor()
    };
}
