using Migrator.Engine.Projections;

namespace Migrator.Engine.Contracts;

/// <summary>
/// Optional capability of a source engine: it lists the external (binary) assemblies the project references, so a target can read their public API
/// (`migrator contract`, metadata only) and resolve the types and calls the project's code uses from them. Written to project.ir.json
/// <c>Shared.ExternalAssemblies</c>. Project references (their source is part of the analysed folder) and framework assemblies are not listed.
/// </summary>
public interface IDependencySourceEngine
{
    IReadOnlyList<ExternalAssemblyIr> FindExternalAssemblies(ProjectAnalysis analysis);
}
