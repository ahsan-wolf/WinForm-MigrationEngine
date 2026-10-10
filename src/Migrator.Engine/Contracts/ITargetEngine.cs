using WebFormsMigrator.IR.Contracts;

namespace Migrator.Engine.Contracts;

/// <summary>Consumes IR and generates an app.</summary>
public interface ITargetEngine : IEngine
{
    IIrGenerator Generator { get; }

    /// <summary>UI library package generated apps depend on, e.g. "@wfm/ui-react".</summary>
    string UiLibraryPackage { get; }

    /// <summary>Compatible ui-spec version range(s), e.g. "^1.0".</summary>
    IReadOnlyList<string> RequiredSpecVersion { get; }
}
