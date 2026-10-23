# 1. Summary

## 1.1 What it is
The WinForms Migration Engine converts a legacy **.NET WinForms** application (`*.Designer.cs` + code-behind, plus user-control libraries) into a **minimum working web application**. It is a standalone, modular tool: analyse once, write a framework-neutral **IR** (intermediate representation) to disk, then generate an app from that IR.

```
WinForms source ──► Source engine ──► IR folder ──► Target engine ──► app + report
 (.cs, .resx)       (analyze, extract)  (JSON files)   (React or Angular)   (MIGRATION_REPORT.md)
                                              │
                                              └── same IR also feeds the app shell and viewer
```

## 1.2 What you get
| Output | Where | What it is |
|---|---|---|
| **IR** | `<out>/ir/` | Per form / user control: `complete`, `controls`, `control-properties`, `code-behind`, `layout` files; plus `project.ir.json` (index, failures, unsupported constructs), `support.ir.json` (the app's own non-UI classes), `resources.ir.json` (referenced images) |
| **Working app** | `<out>/react-app/` or `<out>/angular-app/` | One page per form and user control, translated code-behind, routes, a dialog service for `ShowDialog`, mock data, the app's own classes, typed mock services from backend contracts |
| **App shell** | `<out>/react-appshell/` | A WinForms-style host window: navigation links on the left, the selected form on the right; visual only, dialogs open and close |
| **Component viewer** | `<out>/react-viewer/` | A visual-only page for every form and user control with a sidebar and filter |
| **Report** | `<app>/MIGRATION_REPORT.md` | What was translated, what is TODO and why, archetypes, notes on secrets, controls with no equivalent |
| **Backend survey** | `<out>/backend/BACKEND_ANALYSIS.md` | (optional) read-only description of the legacy service layer |

## 1.3 What "minimum working" means
- **It does:** build and run; render every visible control through the UI library; wire controls to real state and **translated handlers**; open dialogs and routes; show typed **mock** data in grids and lists; list everything that needs a person.
- **It does not:** guarantee that translated business logic is correct, reproduce pixels, call real services or authenticate, or translate GDI+/P/Invoke/COM. Windows-only APIs are bypassed with a marker, and database, file and configuration code is left to the backend and listed in the report.

## 1.4 The parts
| Part | Role |
|---|---|
| **Source engine** (`Migrator.Source.WinForms`) | Reads the project, parses designer files, extracts controls, members, event wiring, navigation, resources |
| **Shared target core** (`Migrator.Target.Core`) | Everything not framework-specific: scope, page model, C# → TypeScript translation, compile-gate loop, report |
| **Target engines** (React, Angular) | Page files, app skeleton, type checker (`tsc` / `ngc`) |
| **UI library** (`@wfm/ui-react`, `@wfm/ui-angular`, `@wfm/ui-core`, `ui-spec`) | The controls, independent of the engine; generated pages import only `Wf*` components |
| **Rules** (`rules/*.json`, `ui-spec/fallbacks.json`) | Data you can extend without rebuilding: Windows APIs to bypass, server-only markers, control archetype names, fallbacks |
| **Migrator UI** (`tools/migrator-ui`) | A local window that builds the command line, runs it, shows the log and report and starts the generated app |

## 1.5 Key behaviours worth knowing
- **Deterministic first.** Layout, routing, models and library wiring come from templates; C# method bodies are translated by rules and then **checked by the compiler** (the compile gate): a method that does not compile goes back to commented C# with a TODO, so the app always builds.
- **Nothing is silently dropped.** Untranslatable code stays as a commented original plus a marker, and appears in the report; the full original code-behind of every form is kept in `src/legacy/`.
- **Archetype controls.** User controls that code never opens (loaded by menu data, permissions or a database row) are classified as grid, tree, menu, toolbar or list controls from their designer (name as a hint), built from what the designer and the code reveal, and listed on a generated `/modules` page.
- **Project independence.** The engine and its rules contain no application names. Project-specific needs live in a copy of a rules file outside the repository.
- **Two targets, one translation.** React and Angular show the same translation numbers for the same input; only the file layout and syntax differ.

## 1.6 Limits (read before promising anything)
- Translated logic is unverified; review every `TODO`, `MOCK`, `BYPASSED`, `FALLBACK` and `inferred` marker.
- Layout comes from Dock / Anchor / table facts, never from pixels.
- Handlers in a third partial-class file, items added by helper loops, and layout computed at run time are not seen.
- App shell and component viewer exist for React only; HTML + jQuery is planned, not built.
- The LLM translation tier is planned; `--translate llm` currently behaves like `rules`.
- The REST gateway for the backend is on the to-do list; generated services are mocks.

More: [../guide/v0.5/01-overview.md](../guide/v0.5/01-overview.md), [../PROJECT_DOCUMENTATION.md](../PROJECT_DOCUMENTATION.md).
