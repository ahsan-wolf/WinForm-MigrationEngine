using Migrator.Engine.Support;

namespace Migrator.Engine.Contracts;

/// <summary>
/// Optional capability of a source engine: it also extracts the project's non-UI classes (helpers, data classes, enums).
/// The host writes the result next to project.ir.json as <c>support.ir.json</c>.
/// </summary>
public interface ISupportSourceEngine
{
    SupportFileIr ExtractSupport(ProjectAnalysis analysis);
}
