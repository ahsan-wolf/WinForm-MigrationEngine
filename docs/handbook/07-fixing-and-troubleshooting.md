# 7. Fixing and troubleshooting

## 7.1 Fix at the right layer
The generated app is output, not source: **never patch it by hand and expect the change to survive a rerun.** Find the layer that caused the problem and fix it there, then regenerate.

| Symptom | Cause | Fix in |
|---|---|---|
| A control, property or event is missing from the IR (`ir/<Unit>/…complete.ir.json`) | The source engine did not extract it | the **extractor** (`Migrator.Source.WinForms`) |
| A statement is left as `// TODO: not translated` and the reason is a missing pattern | No translation rule | a **translator rule** (`Migrator.Target.Core/Translation`; see [../ENGINE_AUTHORING.md](../ENGINE_AUTHORING.md)) |
| A control renders as a FALLBACK or `WfUnsupported` | No component for that WinForms type | add it to **`ui-spec`** and every `ui-*` package, or choose another stand-in in `fallbacks.json` |
| Windows-only code runs through unchanged or is wrongly bypassed | Rules data | `rules/windows-bypass.json` (copy per project) |
| Data-access code appears in the browser app, or logic is wrongly left out | Rules data | `rules/server-bound.json` (copy per project) |
| A unit is classified as the wrong archetype | Name pattern or input threshold | a copy of `rules/control-archetypes.json`, passed with `--archetypes` |
| An application-specific need | The project, not the engine | the project's own rules copy, **outside** the repository; never put application names into the engine |

After a fix: regenerate, check `0 TS error(s) remain`, build and lint ([workflow step 9](04-workflow.md#49-step-9-fix-at-the-right-layer-and-regenerate)).

## 7.2 Common problems
| Symptom | Cause and fix |
|---|---|
| `0 form(s), 0 user control(s)` on an old project whose forms have no `.Designer.cs` | Supported since 0.5: a class that declares `InitializeComponent` itself is found as a unit. If you still get 0 units, check that the source folder is the project folder and that the forms derive from `Form` / `UserControl` (directly or through your own base classes) |
| API: `The ConnectionString property has not been initialized` popup | The server has no connection string: set `AppSettings__<key>` (the key is in `API_REPORT.md`) before `dotnet run` |
| API: `Keyword not supported: provider` | An OLE DB connection string reached SqlClient: `Provider=` keys are stripped automatically; other OLE DB-only keys must be removed from the value |
| API: CORS error in the browser console | Add the app's origin to `Cors:Origins` in `appsettings.json` (or `Cors__Origins__0` in the environment) |
| App cannot reach the API | The app calls `window.__API_BASE__` or `http://localhost:5000`: start the API there or set the variable in `index.html` |
| `engines.json not found` | Run from the repository root or pass `--config <path>` |
| `Target engine 'x' is not loaded or cannot generate a whole project` | The engine is missing or disabled in `engines.json`; run `migrator engines` for diagnostics |
| An engine DLL in `plugins/` is ignored | Only engines listed in `engines.json` load; list it |
| `Cannot find @wfm/ui-react` / blank styles / `@wfm/ui-core` not found | Build the libraries first: `ui-core`, then `ui-react` (and `ui-angular`), see [setup](02-setup.md#22-one-time-build) |
| An npm script fails on a path containing `&` or spaces | npm `.bin` shims break on `&` in a Windows path. Generated scripts call `node ./node_modules/<tool>/…` for that reason; do not replace them with shims. If you put the output under such a path and a tool still fails, move the output |
| The compile gate does nothing ("not run: dependencies are not installed" or "could not run") | Pass `--compile-gate` (it installs) or run `npm install` first; with a relative `--out` older versions could report a false zero, so use full paths |
| `npm install` fails | Network or registry issue; the log is in the UI's Log tab or the terminal. Generated apps depend on the local `ui-core` / `ui-react` folders through `file:` paths: the repository must still be at the path used at generation time |
| Port already in use | `npm run dev` picks the next port and prints it (Vite); the UI starts apps from port 5400 and chooses a free one |
| A page is blank | Hard refresh (Ctrl+Shift+R); open `/<page-name>` to see the render error; check the console. An Angular dev server with a second copy of Angular shows a blank page (NG0203): the generated `angular.json` disables prebundling for that reason, so regenerate instead of editing it |
| `/x` shows the page list | The route is the kebab-case component name without `Page` (`SearchResultsPage.tsx` is `/search-results`); `.code.tsx` files are not pages. Open `/pages` to list them |
| `/modules` is a 404 or missing | The app was generated without `--catalog` / `--units all`, or every unit is opened by code (then no catalog is written) |
| `/modules` lists a unit but it is blank | Its designer is empty and its code adds nothing (often a base class); not an error |
| Wrong columns, nodes or items on an archetype page | The engine inferred them from code literals or used marked generic ones: check the report's *Archetypes* section; if the name is misleading, adjust the rules copy |
| Images show a placeholder | The image could not be extracted (unsupported colour depth, external `.resx` file reference): supply `--images <folder>` with files named by key |
| 404 for `favicon` in the browser console | The entry form has no icon; harmless |
| Output differs between two runs | It should not: compare the IR first (`ir`), then the tool version |
| Migrator UI: *Missing X-Migrator-UI header* (403) | A request was sent from another origin or tool; use the UI itself |
| Migrator UI: *The output folder must be outside this repository* | Choose another output folder |
| Migrator UI: *Dependencies are not installed* | Press **1. Install** before **2. Start** |
| Migrator UI: stopped dev server still answers | Press **Stop** in the *Run the app* tab; the host also kills whatever listens on the app's port. Restarting the host stops everything it started |

## 7.3 FAQ
**Does it translate business logic correctly?** It translates mechanically by rules and checks that the result compiles. It does not verify behaviour: review every marker.

**Can I use the generated app as the new product?** It is a starting point: mock data, no real services or authentication. Plan the backend (the [backend survey](04-workflow.md#44-step-4-optional-backend-inputs) shows what is needed) and review the translated logic.

**Why are some screens missing?** The default scope is the entry form and what it opens directly. Use `--depth all`, and `--units all` for screens loaded dynamically ([step 3](04-workflow.md#43-step-3-choose-the-scope)).

**My project has no `*.Designer.cs` files (the designer code is inside each form's `.cs`). Does it work?** Yes. Such single-file forms, including decompiled sources, are units like any other (`MainMenu` / `MenuItem` menus and the old `DataGrid` are mapped too). Third-party controls are drawn by the closest library component through name patterns (listed as fallbacks in the report); data access with OleDb is left to the backend. Check the *fallback* and *server-only* parts of the report.

**Can I run it on an application without source?** Binary-only input is members-first and lossy, with no event wiring unless wired with `+=` in code; prefer source.

**Is there an LLM mode?** `--translate llm` currently equals `rules`; the model tier is planned. When it exists, its output will be marked `// LLM-translated — verify` and is opt-in.

**Can I add my own controls or rules?** Yes; see [../ENGINE_AUTHORING.md](../ENGINE_AUTHORING.md) and [../guide/v0.5/06-extending.md](../guide/v0.5/06-extending.md). They are outside this handbook.

**Where do secrets go?** Nowhere in browser code. Credentials and connection strings of the application are inputs: keep them out of the repository; contract members that look like secrets are omitted and listed as SERVER-ONLY.

## 7.4 Where to look next
| Need | Document |
|---|---|
| Every option and what it changes | [chapter 5](05-options-reference.md) |
| The architecture as built | [../PROJECT_DOCUMENTATION.md](../PROJECT_DOCUMENTATION.md) |
| Decisions and the roadmap | [../guide/v0.5/07-decisions.md](../guide/v0.5/07-decisions.md), [08-roadmap.md](../guide/v0.5/08-roadmap.md) |
| Which component represents which control | [../UI_CONTROL_MAPPING.md](../UI_CONTROL_MAPPING.md) |
| The Angular target | [../guide/v0.5/09-angular.md](../guide/v0.5/09-angular.md) |
