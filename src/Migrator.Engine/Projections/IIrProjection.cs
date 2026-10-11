using WebFormsMigrator.IR.Models;

namespace Migrator.Engine.Projections;

/// <summary>
/// A facet file derived from a finished <see cref="PageIr"/> (never re-extracted from source, so facets cannot diverge
/// from complete.ir.json). Written as <c>{Form}.{FileSuffix}.ir.json</c>.
/// </summary>
public interface IIrProjection
{
    /// <summary>e.g. "controls" -> {Form}.controls.ir.json.</summary>
    string FileSuffix { get; }

    object Project(PageIr page);
}

/// <summary>Optional: a source engine contributes technology-specific projections (WinForms: layout) on top of the generic ones.</summary>
public interface IIrProjectionProvider
{
    IEnumerable<IIrProjection> Projections { get; }
}
