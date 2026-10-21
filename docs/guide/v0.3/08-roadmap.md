# 8. Roadmap

Sizes are rough: lines of engine and library code, in the files named. Today's reference: `MethodTranslator` 1,485 lines, `PageGenerator` 1,381, `PageTranslator` 496. The structure of the engine does not change: every slice adds model entries, translator rules and library components behind the existing contracts.

## 8.1 Done in 0.3
Event wiring from code-behind, the grid rows model and `DataSource`, tab pages, controls added in code, image resources (`ImageList` decoding, `Properties.Resources`, `--images`), `Path.*` helpers, routes, `CellFormatting`, `ref` to non-assigning methods, `int.TryParse`, expression lambdas and LINQ, favicon. Decision records D1–D5 and D7 in [07-decisions.md](07-decisions.md) are implemented.

## 8.2 To-do list
- Translator residue: block-bodied lambdas and anonymous methods, `out` and reassigned `ref` arguments, event-args rows (`e.Row`), `this` passed as a value, string resources of `Resources.resx`.
- Tab pages created at run time, `Selecting/Selected/Deselected`, `foreach` over `panel.Controls`, calls on controls created in code.
- Viewer and app shell on path routes instead of hash routes.
- LLM tier for the residue the rules cannot translate; third partial-class files; multi-level workflow steps.
- Angular library and target (needs a shared generation core first); HTML + jQuery only if the plain-HTML library is not enough.
- **Backend gateway (last):** a .NET 10 REST gateway generated from the backend analysis, in front of the running ASMX services (variant A: services stay, library facades are ported only where needed), plus a TypeScript client replacing the mock services. Open questions: variant A or B, service URLs, authentication. The read-only analyzer from 0.1 stays the only backend piece until then.
