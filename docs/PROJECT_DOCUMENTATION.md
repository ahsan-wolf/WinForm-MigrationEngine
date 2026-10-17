# WinForms Migration Engine — project documentation (as built)

Status as of 2026-10-12. This is the **as-built** description: what exists, how it fits together and why. Plans and estimates are in `PROJECT_PLAN.md`; rules in `CLAUDE.md`; mappings in `SKILLS.md`; run instructions in `USER_GUIDE.md`; extension conventions in `ENGINE_AUTHORING.md`; the first real application run in `PILOT_FINDINGS.md`.

## 1. Purpose and scope

Convert legacy **.NET WinForms** applications (designer files, code-behind, user controls and the app's own helper classes) into a **minimum working modern web application**: it builds and runs, screens render, controls are wired to real state and translated handlers, navigation works, grids and lists show mock data. Today the target is **React 18 + TypeScript + Vite**; Angular and HTML+jQuery are planned on the same IR. It is explicitly **not** pixel-perfect, not a guarantee of correct business logic, and not a production backend.

Principles: analyze once → serialize a framework-neutral IR → generate from the IR; deterministic first (an LLM only for the residue); every uncertain, mocked or untranslated item carries a marker and a report entry; nothing is silently dropped; secrets never reach browser code.

## 2. Architecture

```
 legacy project (.cs, .Designer.cs, .resx)            backend DLL (metadata only)
        │                                                    │
        ▼                                                    ▼
 Source engine  ───────►  IR folder  ◄───────  migrator contract ──► contract.json
 (Migrator.Source.WinForms)   │  ir/<Form>/*.ir.json, support.ir.json, project.ir.json
                              ▼
                    Target engine (Migrator.Target.React)  ──reads── ui-library/ui-spec (controls.json, data only)
                              │
                              ▼
        react-app/  (pages, support module, services, legacy copies, MIGRATION_REPORT.md)
                              │ imports only
                              ▼
                    @wfm/ui-react  (independent library)
```
Dependency rule: `Cli → Engine → (Source.*, Target.*) → IR`; engines never reference each other and talk only through the IR folder; `ui-library` knows nothing about the engine, the IR or WinForms. Engines are listed in `engines.json` (an allow-list) and loaded by `EngineLoader` (built-ins from the app directory, others from `plugins/` in their own load context). Schema compatibility is checked on registration.

### 2.1 Repository layout
| Path | Contents |
|---|---|
| `src/Migrator.Engine` | Host: contracts (`IEngine`, `ISourceEngine`, `ITargetEngine`, optional capabilities), `EngineLoader`/`EngineRegistry`, `SourceIrRunner` (per-unit failure isolation), `IrFileWriter`, IR projections, `WinNavigation`, assembly metadata reader, support IR model |
| `src/Migrator.Source.WinForms` | Source engine: project analyzer, designer parser (Roslyn), extractors (control tree, members, semantics, workflows, navigation), `.resx` reader, layout projection, support-class extractor |
| `src/Migrator.Target.React` | Target engine: spec catalog, page generator, layout deriver, naming, app writer, compile gate, contract-to-service emitter, rule translator, support-module emitter, report |
| `src/Migrator.Cli` | Thin console (`engines`, `ir`, `contract`, `migrate`) and composition root |
| `ui-library/ui-spec` | `controls.json` (control contract: WinForms types, props, events, a11y, examples) and `tokens.css` |
| `ui-library/ui-react` | `@wfm/ui-react` (22 components, dialog service, `useWfModel`) |
| `build/IrReference.props` | The only place that decides how the shared IR is consumed |
| `docs/`, `CLAUDE.md`, `SKILLS.md` | Documentation (this set) |
| `local-inputs/`, `local-outputs/` | Git-ignored: backend DLLs and derived contracts. Never committed |

## 3. Pipeline

1. **Analyze** — find forms, user controls, the startup form (`Program.cs`), framework version.
2. **Extract IR** — per unit: control tree → members → semantics → workflows → navigation. One extraction, many projections: the other files are derived from `complete.ir.json`.
3. **Extract support classes** — the app's own non-form classes into `support.ir.json`.
4. *(optional)* **Contract** — `migrator contract` reads a backend DLL's public API from metadata only.
5. **Generate** — deterministic: pages, layout, state model, navigation, services, support module, app shell.
6. **Translate** (`--translate rules`) — C# bodies to TypeScript by syntax-directed rules.
7. **Compile gate** (`--compile-gate`) — `tsc` as an oracle; failing methods are demoted to commented C#; regenerate.
8. **Report** — `MIGRATION_REPORT.md`.

## 4. The IR

Same schema as the Web Forms migrator (`PageIr`, schema 0.1.0), consumed from the shared package/repo and never copied or modified here.

- **Per unit** (`ir/<Form>/`): `complete` (source of truth), `controls`, `control-properties`, `code-behind`, `layout` (Dock, Anchor, Location, Size, TabIndex, Margin, Padding, table cells).
- **`project.ir.json`**: source info, forms/user controls index, shared `Unsupported` (unhandled designer statements, never dropped), `Failures`.
- **`support.ir.json`** (WinForms-owned sidecar): classes, structs, enums that are not UI units; members reuse `MemberIr` with modifiers as `mod:*` attributes.
- Designer facts without a property of their own live in dotted pseudo-attributes (`ParentSlot`, `TableLayout.*`, `DataBinding.*`, `Items`, `DefaultCellStyle.*`). Event wiring is `Attributes["OnClick"] = "btnSave_Click"`. Navigation reuses the web-shaped `NavigationIr` through `WinNavigation` (`Show|ShowDialog|Close|Hide|Exit`). Constructors are recorded as `.ctor` members. Fields and properties carry their initializer in `Body`.
- Details and the table of constructs: `ENGINE_AUTHORING.md` and `CLAUDE.md` section 4.

Known extractor limits: handlers in a third partial file, workflow steps deeper than one call level, runtime-created controls, `ImageList` streams and external `.resx` references (placeholder icons), binary-only inputs (lossy).

## 5. The React target in detail

### 5.1 Generation (deterministic)
- **Controls**: `TagName` → `Wf*` component through `controls.json` (`winformsTypes`, `props[].from/transform`, `events[].from`); no match → `WfUnsupported` + TODO + report entry.
- **Layout**: no pixels. Dock/Location → top chrome, free rows (clustered by Y, ordered by X), left/fill/right band, bottom chrome (`wfm-*` flex classes).
- **State**: one `useWfModel` per page — `vm` for control properties (text, enabled, visible, checked, value, readOnly; ListView `items`/`columns`; grid `rows`), `f` for form fields. A deep proxy: reads are immediate, writes batch into one re-render, so imperative WinForms code translates one to one. Declared only when referenced (lint-clean).
- **Navigation**: `ShowDialog` → `await dialog.show(close => <XPage onClose={close}/>)`; `Close/Hide` → `onClose?.(result)`; `Button.DialogResult` closes the dialog after the Click handler with that result.
- **Constructor arguments → props** with fixture defaults for contract data types, and `field = arg;` seeding the field model, so a dialog opens on its own.
- **Fallback controls**: a control without a library component renders as the closest available one (data file `fallbacks.json`: tree/list box -> list, tabs -> panels), marked and reported; **Windows-specific calls** are bypassed with a marker (data file `rules/windows-bypass.json`).
- **Lists**: `WfListView` columns from `ColumnHeader` children; code-created lists get clearly marked sample rows.
- **App shell** (`--appshell`): the same visual-only pages in a WinForms-style host window: hyperlink navigation on the left, the selected unit in a form-style frame on the right (USER_GUIDE 5.2).
- **Component viewer** (`--preview`): a separate generated app with a visual-only page for every form and user control and a sidebar shell that loads the selected one; no logic of any kind (see USER_GUIDE 5.1, ENGINE_AUTHORING).
- **Dev launcher** in `main.tsx`: `?page=<Name>` / `?page=`; render errors shown, not blank.
- **Contracts**: `src/services/<assembly>/{types,fixtures,service}.ts`; secret-looking members omitted/stripped (SERVER-ONLY list in the report).

### 5.2 Translation (rules)
Roslyn syntax-directed C# → TypeScript for **every** method of a page (most handlers are one-line delegations) and for the project's own classes.
- Conventions: `vm.<id>.<prop>`, `f.<field>`, `await dialog.alert/confirm/show`, `await <instance>.<Facade>.<Method>(...)`, `main.X(...)` for project classes, `DateTime` → ISO string, `string.Format` → template literal or `formatString`, `main.WriteErrorLog` → `console.error`.
- **Statement-level fallback**: what cannot be translated faithfully stays a commented original with a TODO; a failed declaration *poisons* its name so dependents fall back too.
- **Dropped with a comment**: appearance-only code and UI-loop calls (Anchor, Font, Cursor, `DoEvents`, Refresh, Focus).
- **async** by call-graph fixpoint across pages and the support module.
- **Per method status**: Complete / Partial / Not translated (typed stub).
- **Lint-clean by construction**: const/`_unused` locals and loop variables, usage-based declarations and imports, notes on uncalled functions, hook-name avoidance.
- **Compile gate**: `tsc --noEmit`; each error is mapped to its method by line range and the method is demoted (up to 5 rounds). An invented or mistyped API cannot ship.
- **ListView** rules (Items, SelectedItems, Columns, `ListViewItem`, SubItems) and **project-class** rules (static/instance members, enums, overload merging, constructors, accessors).

### 5.3 Project classes (`src/support/index.ts`)
One module (no import cycles; classes ordered so static initializers are valid): enums → `export enum`; classes → `export class` with static/instance members; overloads → `Foo`, `Foo_2`; constructors merged into the longest with `null!` defaults; static fields without initializer → fixture row (`// MOCK`) or `new T()`. UI calls inside them and configuration/registry/file access stay TODO.

## 6. UI library

Independent packages with a single contract (`ui-spec`): identical component names (`WfButton`, ...), prop names and canonical events across frameworks; themeable through `--wf-*` CSS variables; accessible by default; versioned and additive. React is built (22 components: form, menu bar/items/separator, toolbar, status bar, panel, group box, split, label, button, text box, combo box, check box, progress, picture box, data grid, list view, context menu, dialog service, `useWfModel`, `WfUnsupported`). Angular and HTML+jQuery libraries are not started. Generated code never imports a third-party widget library directly.

## 7. Quality gates and honesty

- Build and lint of the engine and of every generated app; `tsc` at 0 errors; every generated page rendered in a headless DOM once per slice.
- Markers: `// TODO: not translated`, `// TODO: wire to real API`, `// MOCK`, `// inferred`, `// dropped`, `// LLM-translated — verify`, `<WfUnsupported/>`; each listed in the report.
- No test, demo or Storybook projects in this repo (a separate demo project is planned). Verification is build + lint + smoke run.
- Security: backend DLLs are only parsed (never loaded); secrets omitted from browser code; `local-*` folders and DLLs are never tracked; output derived from a customer app is never committed.

## 8. Pilot results (ECMNavigator)

36 units (35 forms, 1 user control), no third-party controls, 706 page methods in the 17 generated forms, a SOAP-client backend library. After this work: **375 / 706 methods complete (53%)**, 209 partial, 122 not translated; 33 project classes (157 members, 56 complete); `tsc` 0, lint 0, build OK, all pages render. Progress: 321 → 332 (ListView) → 375 (project classes). Detail and blockers: `PILOT_FINDINGS.md` section 5.

## 9. Tooling
- `migrator contract` — managed assembly public API from metadata only → `contract.json`, `data-access.json` (SQL names only).
- IR viewer — a small React page to review IR; lives on branch `feature/win-ir-viewer` (not merged to `main`).

## 10. Decisions on record
Engine on `net10.0` with the shared IR on `net8.0`; the shared IR is referenced through `build/IrReference.props` and never edited from here; WinForms navigation reuses existing IR fields through `WinNavigation`; project classes use a WinForms-owned sidecar instead of changing the shared IR; React model uses a deep proxy instead of per-property `useState` (imperative code maps one to one); `tsc` is the oracle for translated code (invented APIs cannot survive); LLM is a late, opt-in, cached tier because measurements showed phi4-mini ≈ 48 s per 30-line method and phi4 3× slower while inventing helper APIs.

## 11. Roadmap
P4.3 LLM tier for the residue → real data for pages (openers passing constructor arguments, remaining rules) → missing controls (TreeView, list boxes, rich/masked text, month picker) → Angular and HTML+jQuery targets and libraries → separate demo project → full pilot sign-off. See `PROJECT_PLAN.md`.

## 12. Glossary
**IR** — framework-neutral model of the legacy app. **Unit** — one form or user control. **Facet** — a derived IR file. **Project class** — the app's own non-UI class (`main`, `Preferences`). **Contract** — public API of a backend DLL read from metadata. **Compile gate** — `tsc`-based demotion of untranslated code. **vm / f** — the per-page models for controls and fields. **Demoted** — a translated method turned back into commented C# because it did not compile.

## 13. Document map
`CLAUDE.md` (rules, architecture contract, markers, git) · `SKILLS.md` (control/event/pattern mappings, translation cheat sheet, extension skills) · `docs/USER_GUIDE.md` · `docs/UI_CONTROL_MAPPING.md` (which component represents which WinForms control) · `docs/PROJECT_DOCUMENTATION.md` (this file) · `docs/ENGINE_AUTHORING.md` (engine/IR/translator conventions and extension) · `docs/PROJECT_PLAN.md` (status, scope, plan, risks) · `docs/PILOT_FINDINGS.md` · `docs/WINFORMS_MIGRATION_PROMPT.md` (prompt pack for LLM-assisted steps) · `ui-library/README.md`.

Versioned end-to-end guide (UI generation, backend, workflow, project independence): `guide/LATEST.md`.
