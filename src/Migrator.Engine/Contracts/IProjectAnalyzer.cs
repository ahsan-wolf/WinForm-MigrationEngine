namespace Migrator.Engine.Contracts;

public enum SourceUnitKind { Form, UserControl }

/// <summary>One Form/UserControl discovered in a legacy project.</summary>
public sealed record SourceUnit(string Name, SourceUnitKind Kind, string DesignerPath, string CodeBehindPath);

/// <param name="TypeFiles">Simple class name -> source files declaring it (partial classes list several), for every class of the project.</param>
/// <param name="TypeBases">Simple class name -> declared base type text, for every class of the project (custom controls resolve through it).</param>
public sealed record ProjectAnalysis(
    string RootPath,
    string? FrameworkVersion,
    string? StartupForm,
    IReadOnlyList<SourceUnit> Forms,
    IReadOnlyList<SourceUnit> UserControls,
    IReadOnlyDictionary<string, string>? TypeBases = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? TypeFiles = null);

/// <summary>Finds Forms/UserControls, the startup form and dependencies in a legacy project.</summary>
public interface IProjectAnalyzer
{
    ProjectAnalysis Analyze(string rootPath);
}
