using Migrator.Engine.Contracts;

namespace Migrator.Source.WinForms;

public sealed class WinFormsEngineModule : IEngineModule
{
    public void Register(IEngineRegistry registry) => registry.RegisterSource(new WinFormsSourceEngine());
}
