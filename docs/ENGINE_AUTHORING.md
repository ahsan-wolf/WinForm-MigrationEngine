# Engine authoring (P0)

## Add an engine
1. New project under `src/` referencing `Migrator.Engine` (contracts only) and importing `build/IrReference.props`.
2. Implement `ISourceEngine` or `ITargetEngine` (both extend `IEngine`: `Id`, `Version`, `SchemaVersion`).
3. Add one public `IEngineModule` that registers it. Optional constructor `(EngineOptions options)` receives `options` from `engines.json`.
4. List it in `engines.json`. Nothing in `Migrator.Engine` changes.

```json
{ "id": "react-app", "enabled": true, "assembly": "Migrator.Target.React", "options": { } }
```

## Loading rules
- `engines.json` is an allow-list. A DLL in `plugins/` that isn't listed is reported (warning) and never executed.
- `assembly` is resolved from the app directory first (built-in; referenced by the CLI composition root), then `plugins/<assembly>.dll`, then `plugins/<assembly>/<assembly>.dll`.
- Plugins load in their own `AssemblyLoadContext`; `Migrator.Engine`, the IR package and the framework are shared with the host so contract types unify.
- Each engine loads in isolation: a failure becomes an `[ERROR]` diagnostic and the remaining engines still load.
- Schema check: same major; while major is `0`, the minor must also match. Otherwise the registry refuses the engine.

## Interim IR reference
The shared `WebFormsMigrator.IR` / `.IR.Contracts` packages are not yet available to this repo.
`src/Migrator.IR.Interim` stands in, using the **same namespaces**, and `build/IrReference.props` is the only switch.
When the packages are on a feed: `-p:UseSharedIrPackages=true -p:SharedIrVersion=<x.y.z>` (or flip the default), then delete `src/Migrator.IR.Interim`.
`IIrExtractor` / `IIrGenerator` / `IrExtractionContext` there are **provisional** shapes; reconcile with the real ones at that point.

## Commands
```bash
dotnet build WinFormsMigrator.sln
dotnet run --project src/Migrator.Cli -- engines [--config path/to/engines.json]
```
