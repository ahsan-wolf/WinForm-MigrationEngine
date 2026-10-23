# 8. Roadmap

Sizes are rough: lines of engine and library code, in the files named. Today's reference: `MethodTranslator` 1,485 lines, `PageGenerator` 1,381, `PageTranslator` 496. The structure of the engine does not change: every slice adds model entries, translator rules and library components behind the existing contracts.

## 8.1 Done in 0.3
Event wiring from code-behind, the grid rows model and `DataSource`, tab pages, controls added in code, image resources (`ImageList` decoding, `Properties.Resources`, `--images`), `Path.*` helpers, routes, `CellFormatting`, `ref` to non-assigning methods, `int.TryParse`, expression lambdas and LINQ, favicon. Decision records D1–D5 and D7 in [07-decisions.md](07-decisions.md) are implemented.

## 8.1b Done in 0.4
The Angular target and library (`angular-app`, `@wfm/ui-angular`), `ui-core`, the shared target core.

## 8.1c Done in 0.5
Archetype controls (grid, tree, menu, toolbar, list) classified from the designer and the name, inferred from code literals, and the `/modules` catalog ([10-archetypes.md](10-archetypes.md)).

## 8.2 To-do list
- Angular: viewer and app shell (visual-only pages exist in the generator; the shell templates are React), tree-view "Loading…" row for async expand, `--base-href` option.
- Translator residue (both targets): block-bodied lambdas and anonymous methods, `out` and reassigned `ref` arguments, event-args rows (`e.Row`), `this` passed as a value, string resources of `Resources.resx`.
- Tab pages created at run time, `Selecting/Selected/Deselected`, `foreach` over `panel.Controls`, calls on controls created in code.
- Archetypes: nested menu structure and items built from data; the classification could also steer the viewer and app shell; a permission-driven filter on the catalog.
- Viewer and app shell on path routes instead of hash routes (React).
- LLM tier for the residue the rules cannot translate; third partial-class files; multi-level workflow steps.
- HTML + jQuery only if the plain-HTML library is not enough; Vue/Blazor would add a backend, an assembler and a library package.
- **Backend gateway (last):** a .NET 10 REST gateway generated from the backend analysis, in front of the running ASMX services (variant A: services stay, library facades are ported only where needed), plus a TypeScript client replacing the mock services. Open questions: variant A or B, service URLs, authentication. The read-only analyzer from 0.1 stays the only backend piece until then.
