using WebFormsMigrator.IR.Contracts;

namespace Migrator.Engine.Contracts;

/// <summary>Analyzes a legacy technology and produces IR.</summary>
public interface ISourceEngine : IEngine
{
    /// <summary>Technology name written to ProjectIr.Source.Technology, e.g. "WinForms".</summary>
    string Technology { get; }

    IProjectAnalyzer Analyzer { get; }

    /// <summary>Ordered pipeline: tree -> members -> semantics -> workflows/navigation. Order is declared here, not by the host.</summary>
    IEnumerable<IIrExtractor> Extractors { get; }
}
