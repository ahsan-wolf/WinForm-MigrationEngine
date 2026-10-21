# 9. The Angular target (`angular-app`)

Same pipeline, a second framework. `migrate --target angular-app` generates an Angular 21 app (standalone components, router, zone.js change detection) that uses only `@wfm/ui-angular`.

## 9.1 Run it
```bash
cd ui-library/ui-core && npm ci && npm run build          # shared helpers (once)
cd ../ui-angular && npm ci && npm run build && npm run lint   # type-checks the components and their templates (ngc, strict templates)
dotnet run --project src/Migrator.Cli -- migrate --target angular-app --source <path> --out <dir> --translate rules --compile-gate [--depth all]
cd <dir>/angular-app && npm start                         # or: npm run build, npm run lint, npm run check
```
Angular 21 needs Node 20.19, 22.12 or newer (Angular 22 needs 22.22+; the generated app pins `^21.2`). The viewer (`--preview`) and the app shell (`--appshell`) exist for `react-app` only.

## 9.2 How it works
| Piece | Where | Role |
|---|---|---|
| `ProjectGenerator`, `PageGenerator`, translator, contracts, support module, report | `Migrator.Target.Core` | Everything that is not framework syntax: scope, model, handlers, C# -> TypeScript, the compile gate loop, `MIGRATION_REPORT.md`. React and Angular run the same code, so translation numbers are identical |
| `IPageAssembler` / `PageAssemblyInput` | Core | The generator hands a framework everything it decided (neutral JSX-like markup, model, handlers, wiring, imports); the framework writes files |
| `AngularPageAssembler`, `JsxToAngular` | `Migrator.Target.Angular` | Turns the neutral markup into an Angular template (`<wf-button [text]="..." (wfClick)="...">`, `@if`), lifts inline handlers into the code-behind, writes the three files of a page |
| `AngularAppWriter`, `AngularBackend` | `Migrator.Target.Angular` | Workspace (`angular.json`, tsconfig with path mappings, `main.ts`, routes), page files, `ngc` as the type checker |
| `@wfm/ui-angular` | `ui-library/ui-angular` | Standalone components; consumed as TypeScript source through `tsconfig` paths, so the app and the library share one Angular |
| `@wfm/ui-core` | `ui-library/ui-core` | Framework-free helpers and types (grid model, image list, tree nodes, list items, mask) and the shared stylesheet `wf.css` |

## 9.3 Generated files
- `src/pages/<page>.page.ts`: the component. It creates the code-behind and exposes only what the template uses (getters).
- `src/pages/<page>.page.html`: the template (the designer).
- `src/pages/<page>.page.code.ts`: the code-behind as a factory `create<Page>Code(dialog, props)` holding the model (`vm`, `f` as plain objects), handlers, translated methods and the exported `wiring` table. The translated function bodies are the ones the React hook gets.
- `src/app/*`: `app.config.ts` (zone.js, router), `app.routes.ts`, `pages.component.ts` (`/pages`), `app.component.ts` (dialog root close goes home).
- Routes: `/` entry form, `/<page-name>` one page, `/pages` list. Constructor arguments of the original form are component inputs (`input()`), the code-behind is created in `ngOnInit`.

## 9.4 Conventions
- Component names are the React ones in kebab-case (`WfButton` is `<wf-button>`); props keep their names (`text`, `disabled`, `selectedIndex`); an element id is `controlId`.
- Events are `wf` + the canonical event (`wfClick`, `wfValueChange`, `wfSelectionChange`, `wfCellClick`); events with two values emit a tuple `[a, b]` (list item check, tree node check, checked list item).
- Dialogs: `dialog.show(PageClass, { title })`, `dialog.close(result)`, `alert` / `confirm` / `message`; native `<dialog>` as in the React library.
- A control with no Angular component yet becomes `<wf-unsupported>` and an `unsupported-control` entry in the report (none of the 36 mapped controls today).
- Zone.js re-renders after handlers, so writes to `vm` made in handlers (also after `await`) show without a proxy; the event handling coalesces to one render per frame.

## 9.5 Verification (0.4)
- All ten sample apps generate, pass `ngc` with 0 errors, `ng build` and lint; seven behaviour suites run in Edge (wiring, popups and routes, std controls, tabs and controls added in code, image list, grid events, grid rows model).
- Pilot 0: 30 units, 955 methods, 502 complete / 321 partial / 132 not translated (the same as React), 0 TypeScript errors, `ng build` and lint clean; every page opens on its route and 111 clicks run without a console error.
- Pilot 1: 159 units, 4,123 methods, 1,893 / 1,343 / 887 (the same as React), 0 errors, `ng build` and lint clean.

## 9.6 Limits
- Viewer and app shell: React only.
- The tree's lazy loading shows the children when the handler fills them (no "Loading…" row); the grid keeps a local copy of an edited cell and also emits `wfCellEdit`.
- `base href` is `/`; to host under a sub-path build with `ng build --base-href /app/`.
- Same translator residue as React (see the roadmap).
