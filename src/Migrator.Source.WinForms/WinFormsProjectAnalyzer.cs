using Migrator.Engine.Contracts;

namespace Migrator.Source.WinForms;

/// <summary>Placeholder until P1 (find Designer/code-behind pairs, UserControls, startup form).</summary>
public sealed class WinFormsProjectAnalyzer : IProjectAnalyzer
{
    public ProjectAnalysis Analyze(string rootPath) =>
        throw new NotImplementedException("WinForms project analysis is implemented in phase P1.");
}
