using WebFormsMigrator.IR.Contracts;

namespace Migrator.Engine.Contracts;

/// <summary>
/// Optional: a source engine reports source constructs it did not understand (CLAUDE.md: never silently drop).
/// Entries end up in project.ir.json <c>Shared.Unsupported</c>. A diagnostic, not a facet: it may re-read the raw source.
/// </summary>
public interface IUnsupportedReporter
{
    IEnumerable<string> FindUnsupported(IrExtractionContext context);
}
