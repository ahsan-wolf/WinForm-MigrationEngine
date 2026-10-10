namespace Migrator.Engine.Contracts;

/// <summary>What a module sees: a write-only surface to contribute engines.</summary>
public interface IEngineRegistry
{
    void RegisterSource(ISourceEngine engine);
    void RegisterTarget(ITargetEngine engine);
}
