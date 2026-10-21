using Migrator.Engine.Support;

namespace Migrator.Engine.Contracts;

/// <summary>
/// Optional capability of a source engine: it also extracts the images of the project's own resource file that its code references.
/// The host writes the result next to project.ir.json as <c>resources.ir.json</c>.
/// </summary>
public interface IResourceSourceEngine
{
    ResourceFileIr ExtractResources(ProjectAnalysis analysis);
}
