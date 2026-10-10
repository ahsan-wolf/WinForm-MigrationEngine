namespace Migrator.Engine.Contracts;

/// <summary>
/// Entry point of every engine assembly (built-in or plugin). The host instantiates it
/// (optionally with <see cref="EngineOptions"/> bound from engines.json) and calls <see cref="Register"/>.
/// </summary>
public interface IEngineModule
{
    void Register(IEngineRegistry registry);
}
