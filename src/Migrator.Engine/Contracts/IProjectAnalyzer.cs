namespace Migrator.Engine.Contracts;

public enum SourceUnitKind { Form, UserControl }

/// <summary>One Form/UserControl discovered in a legacy project.</summary>
public sealed record SourceUnit(string Name, SourceUnitKind Kind, string DesignerPath, string CodeBehindPath);

public sealed record ProjectAnalysis(
    string RootPath,
    string? FrameworkVersion,
    string? StartupForm,
    IReadOnlyList<SourceUnit> Forms,
    IReadOnlyList<SourceUnit> UserControls);

/// <summary>Finds Forms/UserControls, the startup form and dependencies in a legacy project.</summary>
public interface IProjectAnalyzer
{
    ProjectAnalysis Analyze(string rootPath);
}
