# User guide — running the WinForms Migration Engine

This guide is for the person who runs a migration: you point the tool at a legacy WinForms project and get a React app, an intermediate model (the IR) and a report of what still needs manual work.
For architecture see `PROJECT_DOCUMENTATION.md`; for rules and git workflow see `CLAUDE.md`; for control mappings see `SKILLS.md`.

## 1. What you get

| Output | Where | What it is |
|---|---|---|
| **IR** | `<out>/ir/` | A framework-neutral model of the app: per form/user control a `complete` file plus `controls`, `control-properties`, `code-behind`, `layout`; `support.ir.json` (the app's own non-form classes); `project.ir.json` (index, failures, unsupported constructs) |
| **React app** | `<out>/react-app/` | Vite + React 18 + TypeScript app that imports only `@wfm/ui-react`: one page per form, a dialog service for `ShowDialog`, translated code-behind, mock data, `src/support/` for the app's own classes |
| **Report** | `<out>/react-app/MIGRATION_REPORT.md` | Counts, what was translated, what is TODO and why, contract notes (omitted secrets), controls with no equivalent |

It produces a **minimum working application**: it builds, screens render, controls are wired to real state and translated handlers, data is **mock** data. It does **not** guarantee that translated business logic is correct — review everything marked `TODO`, `MOCK` or `LLM-translated`.

## 2. Prerequisites

- **.NET SDK 10** (the engine targets `net10.0`) and a current **Node.js LTS** with npm.
- A sibling checkout of the shared IR repository (`..\MIgration Engine 2` relative to this repo). The engine only *references* it and never edits it. Another location: `-p:WebFormsMigratorRoot=<path>` on `dotnet build`.
- The legacy project's **source** (`*.cs`, `*.Designer.cs`, `.resx`). Binary-only inputs are possible but lossy.
- Optional: the legacy app's backend library (a managed DLL) if you want typed mock services (section 7).
- Not needed now: Ollama (planned LLM tier).

## 3. One-time setup

```bash
dotnet build WinFormsMigrator.sln
cd ui-library/ui-react && npm ci && npm run build && cd ../..
```
`engines.json` lists the enabled engines (`winforms` source, `react-app` target) and where the `ui-spec` and the library live. Only listed engines load.

## 4. Quick start

```bash
# IR only (inspect what was understood)
dotnet run --project src/Migrator.Cli -- ir --source "<winforms project folder>" --out "<output folder>"

# Whole migration: IR + React app, rule-based translation, compile check
dotnet run --project src/Migrator.Cli -- migrate --source "<winforms project folder>" --out "<output folder>" \
    --translate rules --compile-gate --depth all [--contract <contract.json>]

# Run the generated app
cd "<output folder>/react-app"
npm install          # first time (the compile gate already did it if you passed --compile-gate)
npm run dev          # http://localhost:5173
```
Use an output folder **outside the repository**. Never commit generated output derived from a customer application.

## 5. Command reference

| Command | Purpose |
|---|---|
| `engines [--config <engines.json>]` | List the engines that load (diagnostics for the rest) |
| `ir --source <dir> --out <dir> [--engine <id>]` | Extract the IR only |
| `migrate --source <dir> --out <dir> [options]` | IR step, then generate the app into `<out>/react-app` |
| `contract --assembly <dll> --out <dir>` | Read a managed DLL's public API from metadata only (never loaded or run) |
| `backend --source <dir>... [--wsdl <dir>...] --out <dir> [--rules <file>]` | Read-only survey of .NET Framework class libraries / ASMX services and WSDLs (services and operations, SOAP proxies, facade thickness, Windows-specific calls, static mutable state, config key names, data access). Writes `BACKEND_ANALYSIS.md` + `backend-analysis.json`; use a git-ignored `--out`. Rules: `rules/backend-patterns.json` |

`migrate` options:

| Option | Meaning |
|---|---|
| `--target react-app` | Target engine (only React exists today; Angular and HTML+jQuery are planned) |
| `--entry <form>` | Entry form (default: the startup form from `Program.cs`) |
| `--no-external-projects` | Do not follow `ProjectReference`s that point outside the source folder (by default their classes are included as typed API shapes) |
| `--units all` | Generate every form and user control, not only the ones reachable from the entry form (use for shells that open screens from menu data) |
| `--depth <n\|all>` | How many `Show/ShowDialog` hops from the entry form to generate (default 1; `all` = everything reachable) |
| `--translate none\|rules\|llm` | `none`: handlers stay commented C#. `rules`: deterministic C# -> TypeScript. `llm` currently behaves like `rules` (LLM tier planned) |
| `--compile-gate` | Run `npm install` if needed, then `tsc`; methods that do not compile are demoted back to commented C# and the page is regenerated (up to 5 rounds). Without the flag the gate runs only if dependencies already exist; `--no-compile-gate` skips it |
| `--contract <file>` (repeatable) | Turn a backend contract into typed services, fixtures and types in the app |
| `--images <folder>` | Image files named by key (`Name.png`, `Name.ico`, ...) for `Properties.Resources.Name` and image-list entries the engine could not extract |
| `--contract-scope used\|all` | Emit only what the generated code uses (default) or everything |

The console ends with counts: units extracted, pages generated, methods complete/partial/not translated, support classes, remaining TypeScript errors.

### 5.1 Component viewer (`--preview`)
`migrate --source <dir> --out <dir> --preview` writes a **separate app** `<out>/react-viewer/` next to the IR: every form and every user control of the project (including the ones normally inlined into other screens) as a **visual-only page**:
controls, layout, designer text and fallback controls as designed, with placeholder data in grids, lists and combo boxes, and **no** handlers, dialogs, translated code, services, support classes or props. Entry form, navigation between forms, contracts and translation are ignored.

```bash
cd "<output folder>/react-viewer" && npm install && npm run dev
```
The viewer has a sidebar (forms and user controls, grouped by source folder, with counts), a filter box, and loads only the selected unit; the selection is in the URL (`#/<UnitName>`), so back/forward and links work. Arrow keys or Previous/Next step through the list;
"design size" frames a unit at its designed width/height; a background switch helps with dark or light surfaces. A unit that could not be generated stays listed with its error. `VIEWER_REPORT.md` lists units, fallback controls and controls without a component.
Use it to review what the engine understood of each screen before looking at behaviour (the functional app is in `react-app`).

### 5.2 App shell (`--appshell`)
`migrate --source <dir> --out <dir> --appshell` writes `<out>/react-appshell/`: the same visual-only pages as the viewer, hosted by a **WinForms-style window**. Left: a bordered "Navigation" pane with a filter box and **hyperlinks** (LinkLabel look: blue, underlined on hover,
bold with a marker when active) grouped by kind (Forms / User controls) and source folder; a draggable (or arrow-key) splitter separates it from the right-hand **main container**, which loads the clicked unit inside a **form-style frame** (title bar with the unit title and source, border, client area).
A title bar names the project, a status bar shows the unit count and the selected unit; Previous/Next, arrow keys and a "design size" switch are available. Links are real anchors (`#/<UnitName>`), so back/forward, deep links and open-in-new-tab work.

```bash
cd "<output folder>/react-appshell" && npm install && npm run dev
```
Colours and fonts are CSS variables at the top of `src/appshell.css` (system-style defaults; they follow the UI library tokens when present), so a project can restyle the shell without touching the pages. **Dialogs:** a button or menu item that opens a form in the original (`ShowDialog`) opens that form's visual-only page as a popup, and Close / OK / Cancel buttons, the title-bar ✕ and Escape close it, so the original window flow can be walked through. This is the only behaviour wired in the shell: nothing else runs, and the plain `--preview` viewer has no popups. Use `--preview` instead for the plainer developer viewer.

## 6. Reading the results

### 6.1 The report (`MIGRATION_REPORT.md`)
- **Translation table** — per form: methods, complete, partial, not translated. *Complete* = every statement translated. *Partial* = some statements stayed as commented C# with a TODO. *Not translated* = the whole method stays commented (a typed stub keeps callers compiling).
- **Most common reasons** — the fastest way to see what blocks more automation (for example `ref`/`out` arguments, `ConfigurationManager`).
- **Project classes** — the same for `src/support/index.ts`.
- **Manual work by category** — controls with no library component, navigation that needs a decision, grids whose columns are created in code, non-visual components, contract notes, compile errors outside translated methods.

### 6.2 Markers in generated code
| Marker | Meaning |
|---|---|
| `// TODO: not translated — <reason>` + commented C# | The rules could not translate this statement faithfully; the original is kept |
| `// TODO: wire to real API — X not resolvable from IR` | A call or type the IR/contract does not describe |
| `// MOCK — ...` | Sample or fake data (fixture rows, sample list rows, constructor-argument defaults) |
| `// inferred — check against backend contract` | A type guessed from columns or formats |
| `// dropped (no browser equivalent: ...)` | Appearance-only or UI-loop code (Cursor, `DoEvents`, Refresh) that has no behaviour in a browser |
| `// LLM-translated — verify` | Reserved for the LLM tier (not generated yet) |
| `/* FALLBACK: DateTimePicker */` | A control with no library component of its own, rendered by the closest one (here a text box); behaviour may differ, see the report |
| `// wired in the markup (btn.Click): btn.Click += handler;` | A subscription in the code-behind that the extractor turned into event wiring: the markup binds the handler (`wiring` table, `source: 'code'`); the original statement is kept as this comment |
| `// BYPASSED — Windows-specific API (...)` | A statement using a Windows-only API (registry, clipboard, `Process.Start`, directory services, ...) was left out; the original is kept next to it |
| `<WfUnsupported .../>` | A control that could not even be given a fallback (rare) |

### 6.3 What the generated app looks like
- `src/pages/<Form>Page.tsx` + `src/pages/<Form>Page.code.tsx` — **two files per form, like the WinForms original**: the `.tsx` holds only the markup (the "Designer": `Wf*` controls bound by name), the `.code.tsx` is the **code-behind**, a hook `use<Form>PageCode()` with the model, handlers and translated methods, plus an exported `wiring` table (`control`, `event`, `handler`, `method`, `source`) that lists every event wiring of the original form for tracing. Control properties live in `vm` (`vm.txtSearch.text`) and form fields in `f`, both from `useWfModel` (reads are immediate, writes re-render once, so translated WinForms code maps one to one).
- `src/support/index.ts` — the app's own classes (`main`, `Preferences`, enums, data classes), called as in the original.
- `src/services/<assembly>/` — typed mock service when a contract was given.
- `src/legacy/*.cs.txt` — the original code-behind of every generated form, so nothing is lost.

## 7. Backend libraries (contracts)

Many WinForms apps call a backend client DLL. To get typed mock services instead of guesses:

1. Put the DLL in the git-ignored `local-inputs/` and run `contract --assembly local-inputs/<name>.dll --out local-outputs` (it reads metadata only; the DLL is never loaded or executed).
2. Pass `local-outputs/<name>/contract.json` to `migrate --contract`.
3. Members that look like secrets (password, key, license, connection string, credentials, token) are **omitted** or stripped and listed in the report as SERVER-ONLY. Secrets are never emitted to browser code.

Never commit a DLL, a contract derived from a customer assembly, or any generated output.

## 8. Running and checking the generated app

```bash
npm run dev       # development server
npm run build     # production build (must pass)
npm run lint      # must report 0 problems
```
**Dev launcher.** `http://localhost:5173/` opens the entry form. `/<page-name>` (for example `http://localhost:5173/search-results`, the kebab-case component name without `Page`) opens one generated page on its own with mock data; `/pages` lists all pages. Routes are standard History API paths relative to the app base (`<base href>`, Vite `base: './'`), so the app also runs under a sub-path; a production host must serve `index.html` for unknown paths. Old `?page=<Name>` links are rewritten to routes. A page that throws while rendering shows its error text instead of a blank screen. Dialogs that the original opened with constructor arguments take them as optional props and fall back to fixture data, so they work standalone.

What to click through: the menu and toolbar, each dialog from the entry form, lists and grids (their rows are mock data), `Close`/`OK`/`Cancel` buttons, and the pages in the launcher index.

## 9. A typical working loop

1. `ir` first on a new application: read `project.ir.json` (`Failures`, `Unsupported`) and open a few `*.complete.ir.json` to confirm the control tree and handlers are right. (A small React IR viewer exists on branch `feature/win-ir-viewer`; it is not merged to `main` yet.)
2. `migrate --translate rules --compile-gate --depth all` and read the report's top reasons.
3. Fix the cause at the right layer: **extractor** (something missing from the IR), **translator** (a missing rule, see `ENGINE_AUTHORING.md`), **UI library** (a missing control: add it to `ui-spec` and every `ui-*` package). Never patch generated output by hand and expect it to survive a rerun.
4. Re-run; `tsc` and lint must stay at 0.
5. Review the TODOs that remain with the people who know the application.

## 10. Known limitations (read before promising anything)

- Business logic is translated deterministically but **not verified**. Review async behaviour, mock data assumptions and every TODO.
- Data is mock. The opener passing real arguments to dialogs, real services and authentication are not part of the generated app yet.
- No pixel fidelity: layout is derived from Dock/Anchor/Location facts into flex/grid.
- Windows-only APIs are bypassed, not translated (`rules/windows-bypass.json`; the report lists each).
- Classes from referenced projects outside the source folder are included as API shapes only (typed fields, stubbed methods); data access, file system and configuration code is left out and listed in the report as backend work.
- Event subscriptions written in the code-behind (`btn.Click += handler`, `new EventHandler(m)`, lambdas, anonymous methods) are read as event wiring and bound in the markup (`source: 'code'` in the `wiring` table); only subscriptions on controls created at run time stay TODO.
- Not translated today: `ref`/`out`, `string.Format` format specifiers, `ConfigurationManager`/registry/file access, GDI+/P/Invoke/COM, UI calls inside non-form classes, LINQ query syntax, lambdas, `dgv.Rows` and `pnl.Controls` members, runtime-created controls.
- Controls without a library component yet (TreeView, list box, rich/masked text, month calendar, ...) are rendered by the closest component (`ui-spec/fallbacks.json`): a date picker is a text box, a tree a flat list, tabs stacked panels. They work, but look and behave simply.
- Only the React target exists; Angular and HTML+jQuery are planned. See `PROJECT_PLAN.md` for the roadmap.

## 11. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `engines.json not found` | Run from the repo root or pass `--config <path>` |
| Plugin DLL ignored | A DLL in `plugins/` that is not listed in `engines.json` is reported and never executed — list it |
| `Cannot find @wfm/ui-react` or blank styles | Build the library first: `cd ui-library/ui-react && npm ci && npm run build` |
| `npm run` fails on a path containing `&` or spaces | The generated scripts call `node ./node_modules/<tool>/bin/...` for that reason; do not replace them with `.bin` shims |
| Compile gate does nothing | Dependencies are not installed: pass `--compile-gate` (installs) or run `npm install` first |
| Page is blank in the browser | Hard refresh; open `/<page-name>` to see the render error; check the console |
| Two copies of React / hooks error | The generated Vite config dedupes React; re-generate rather than editing it |
| `/x` shows the page list | The route is the kebab-case name of a markup file in `src/pages` without `Page` (`SearchResultsPage.tsx` is `/search-results`; the `.code.tsx` files are not pages); open `/pages` to list them |
| Tool output differs between runs | It should not: the generators are deterministic. Compare IR first (`ir` step), then the translator version |

## 12. Git workflow (when changing the tool itself)

Branch from the latest `main` (`feature/…`, `fix/…`, `docs/…`), commit in small logical commits, **never push or open PRs from the tool/agent**; the maintainer pushes. The exact command block is in `CLAUDE.md` section 0.2. Do not add test projects, Storybook or demo apps to this repo.
