using WebFormsMigrator.IR.Contracts;

namespace Migrator.Engine.Contracts;

/// <summary>
/// Optional extension of <see cref="ISourceEngine"/> for engines whose extractors need project-wide knowledge
/// (e.g. WinForms inlining UserControls). The host prefers this over <see cref="ISourceEngine.Extractors"/> when available.
/// </summary>
public interface IProjectAwareSourceEngine : ISourceEngine
{
    IEnumerable<IIrExtractor> CreateExtractors(ProjectAnalysis analysis);
}
