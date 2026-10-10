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

## Shared IR reference
`build/IrReference.props` is the only place that decides how projects consume `WebFormsMigrator.IR` and `WebFormsMigrator.IR.Contracts`.
By default it ProjectReferences a sibling checkout of the real WebFormsMigrator repo (`..\..\MIgration Engine 2` relative to this repo);
override with `-p:WebFormsMigratorRoot=<path>`. Nothing is copied. When the packages are on a feed, use
`-p:UseSharedIrPackages=true -p:SharedIrVersion=<x.y.z>`. Our contracts use the real `IIrExtractor` (`Facet`, `Extract(context, PageIr)`)
and `IIrGenerator` (`Target`, `Generate(PageIr, outputDirectory)`).

### Known schema gaps for WinForms (changes belong in the WebFormsMigrator repo, additive only)
- `NavigationIr` is web-shaped. **Resolved without touching the shared IR:** WinForms entries reuse the existing fields (`SourceControlId` = trigger control, `TriggerEvent` = event, `Target` = target form, `TargetType` = `Show|ShowDialog|Close|Hide|Exit`). Create and read them through `Migrator.Engine.Navigation.WinNavigation` (typed `TryGetKind`, `TargetForm`, `FindWorkflow`); don't hand-write the strings. If richer data is ever needed (DialogResult, ctor args), add a WinForms-owned sidecar file keyed by control + event.
- `MemberIr.Kind` / `Visibility` are enums (`Visibility` includes `Protected`/`Internal`), not the ints in the prototype.
- `PageIr.RootControl` is nullable; the prototype assumed non-null.
- The real projects target `net8.0` and `IR.Contracts` pulls AngleSharp + Roslyn 4.8.0 into every consumer.

## WinForms source engine (P1, first slice)
Pipeline (declared by the engine, order matters): `controlTree` -> `members` -> `semantics` -> `workflows` -> `navigation`.
Later facets read earlier ones from the `PageIr` only (event wiring is read back from `Attributes["OnClick"]` etc.).
`IProjectAwareSourceEngine.CreateExtractors(analysis)` lets an engine build extractors that need the whole project
(UserControl inlining); `Migrator.Engine.Pipeline.SourceIrRunner` runs them per unit with per-unit failure isolation.

Known limitations (carry into reports): handlers in a third partial file are not resolved; only direct calls become workflow
steps (not 2+ levels), ordered by call order; a handler name inside an inlined UserControl that equals a form method name
would be attributed to the form; menu/toolbar items, DataGridView columns, TableLayoutPanel cells, DataBindings and `.resx`
are not extracted yet; a custom base class stays as `RootControl.TagName` (e.g. `BaseForm`) rather than resolving to `Form`.

## Commands
```bash
dotnet build WinFormsMigrator.sln
dotnet run --project src/Migrator.Cli -- engines [--config path/to/engines.json]
dotnet run --project src/Migrator.Cli -- ir --source <winforms-folder> --out <dir>   # writes <dir>/ir/<Form>/<Form>.complete.ir.json
```
