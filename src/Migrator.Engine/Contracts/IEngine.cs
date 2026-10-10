namespace Migrator.Engine.Contracts;

/// <summary>Identity every engine (source or target) exposes so the host can validate it.</summary>
public interface IEngine
{
    /// <summary>Stable key, e.g. "winforms" or "react-app".</summary>
    string Id { get; }

    /// <summary>The engine's own version.</summary>
    Version Version { get; }

    /// <summary>IR schema version the engine was built against. The host refuses incompatible engines.</summary>
    Version SchemaVersion { get; }
}
