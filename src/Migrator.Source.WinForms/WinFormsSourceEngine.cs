using Migrator.Engine.Contracts;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms;

public sealed class WinFormsSourceEngine : ISourceEngine
{
    public string Id => "winforms";
    public string Technology => "WinForms";
    public Version Version { get; } = new(0, 1, 0);
    public Version SchemaVersion { get; } = Version.Parse(IrSchema.Version);

    public IProjectAnalyzer Analyzer { get; } = new WinFormsProjectAnalyzer();

    // P1: designer parser -> members -> semantics -> workflows -> navigation (ported from reference/winforms-prototype).
    public IEnumerable<IIrExtractor> Extractors { get; } = Array.Empty<IIrExtractor>();
}
