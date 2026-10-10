using Migrator.Engine.Contracts;

namespace Migrator.Engine.Hosting;

/// <summary>Holds registered engines; rejects duplicates and engines whose schema doesn't match the host.</summary>
public sealed class EngineRegistry : IEngineRegistry
{
    private readonly Dictionary<string, ISourceEngine> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ITargetEngine> _targets = new(StringComparer.OrdinalIgnoreCase);

    public EngineRegistry(Version hostSchemaVersion) => HostSchemaVersion = hostSchemaVersion;

    public Version HostSchemaVersion { get; }

    public IReadOnlyCollection<ISourceEngine> Sources => _sources.Values;
    public IReadOnlyCollection<ITargetEngine> Targets => _targets.Values;

    public ISourceEngine? FindSource(string id) => _sources.GetValueOrDefault(id);
    public ITargetEngine? FindTarget(string id) => _targets.GetValueOrDefault(id);

    public void RegisterSource(ISourceEngine engine)
    {
        Validate(engine);
        if (!_sources.TryAdd(engine.Id, engine))
            throw new InvalidOperationException($"Source engine '{engine.Id}' is already registered.");
    }

    public void RegisterTarget(ITargetEngine engine)
    {
        Validate(engine);
        if (!_targets.TryAdd(engine.Id, engine))
            throw new InvalidOperationException($"Target engine '{engine.Id}' is already registered.");
    }

    private void Validate(IEngine engine)
    {
        if (string.IsNullOrWhiteSpace(engine.Id))
            throw new InvalidOperationException($"Engine {engine.GetType().FullName} has an empty Id.");
        if (!SchemaCompatibility.IsCompatible(HostSchemaVersion, engine.SchemaVersion))
            throw new InvalidOperationException(
                $"Engine '{engine.Id}' targets IR schema {engine.SchemaVersion}, host is {HostSchemaVersion}; refusing to register.");
    }
}
