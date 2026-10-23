# 4. Workflow — from a new application to a checked app

Ten steps. Steps 1–3 and 5–7 are the core; 4 and 8 are used when the application needs them. Commands use `migrator` for `dotnet run --project src/Migrator.Cli --`; every step has an equivalent in the [Migrator UI](03-ways-to-run.md#31-the-migrator-ui) (the mode and options are named in each step).

## 4.0 The quick path
For a first look, three runs are enough:

| Run | Command | UI |
|---|---|---|
| 1. What did the engine understand? | `migrator ir --source <src> --out <out>` | mode *IR only* |
| 2. Look at every screen | `migrator migrate --source <src> --out <out> --appshell` | mode *App shell* |
| 3. A working app | `migrator migrate --source <src> --out <out> --translate rules --compile-gate --depth all` | mode *Working app*, Translate `rules`, Compile gate, Depth `all` |

Then `cd <out>/react-app && npm install && npm run dev`. The rest of this chapter explains each step and how to judge it.

## 4.1 Step 1: prepare
1. **Source.** A folder with the WinForms project (`.csproj`/`.sln`, `*.cs`, `*.Designer.cs`, `*.resx`). The engine only reads it. Projects referenced from outside the folder are followed (as typed API shapes) unless you pass `--no-external-projects`.
2. **Output.** A folder outside this repository, for example `D:\out\MyApp`. Reuse the same folder for all runs: later runs overwrite the app and the IR.
3. **Setup done.** [Chapter 2](02-setup.md): the solution and `ui-core` / `ui-react` (and `ui-angular`) built; `migrator engines` lists the engines.
4. **Know the entry point.** The engine reads the startup form from `Program.cs` (`Application.Run(new Main())`). Override with `--entry <FormName>` when it is wrong or when you want to start from another form.

## 4.2 Step 2: extract and inspect the IR
```bash
migrator ir --source "C:\src\MyApp" --out "D:\out\MyApp"
```
Expected console: the number of forms and user controls, `<n> unit(s) extracted; <m> IR file(s) written`, and the path of `project.ir.json`.

What to check in `D:\out\MyApp\ir\`:

| Look at | Check |
|---|---|
| `project.ir.json` → `Failures` | Should be empty. A failure is one unit that could not be extracted; the rest are fine |
| `project.ir.json` → `Shared.Unsupported` | Designer statements the parser did not understand (`"Unit: statement"`, up to 50 per unit). Never silently dropped; a long list means a missing extractor feature |
| `<Unit>/<Unit>.complete.ir.json` | The control tree (`RootControl`), `Members` (fields, properties, methods with bodies), `Navigations`, `Workflows`. Open two or three screens and confirm the controls and handlers are the ones you expect |
| `<Unit>/<Unit>.layout.ir.json` | Dock, Anchor, TabIndex, table cells: the facts layout is derived from |
| `support.ir.json` | The application's own non-UI classes (helpers, data classes, enums) |
| `resources.ir.json` | Images from `Resources.resx` that the code references (only when there are any) |

Per unit there are five files (`complete`, `controls`, `control-properties`, `code-behind`, `layout`); the other four are derived from `complete`. If something is missing in the IR, the **extractor** is the place to fix it, not the generated app.

## 4.3 Step 3: choose the scope
By default the app contains the entry form and the forms it opens (`--depth 1`). Decide how much you want:

| You want | Use | Notes |
|---|---|---|
| Entry form and what it opens directly | default (`--depth 1`) | quickest |
| Everything reachable by `Show` / `ShowDialog` | `--depth all` | follows navigation to the end |
| User controls created in code (`panel.Controls.Add(new X(...))`) | automatic | they join the scope of the page that creates them |
| **Screens that the original loads dynamically** (menu data, permissions, `Activator.CreateInstance`, a database row) | `--units all` or `--catalog` | generates every form and user control and writes the `/modules` page |
| One form only, as the start page | `--entry <Form>` | combine with depth |

Dynamic loading is invisible to static navigation analysis, so such forms and controls are simply not reached without `--units all`/`--catalog`. With them, every generated unit that no code opens is classified as an **archetype** (grid, tree, menu, toolbar, list or form) and listed on `/modules` ([chapter 6.4](06-reading-results.md#64-archetypes-and-the-modules-catalog)). To teach the classifier your project's naming, pass `--archetypes <copy of rules/control-archetypes.json>`.

## 4.4 Step 4 (optional): backend inputs
Many WinForms applications call a service layer. Two optional inputs improve the result:

1. **Survey** (read-only, no code is run):
   ```bash
   migrator backend --source "C:\src\MyApp.Library" --source "C:\src\MyApp.Services" --wsdl "C:\src\wsdl" --out "D:\out\MyApp\backend"
   ```
   Read `BACKEND_ANALYSIS.md`: services and operations, SOAP proxies, how thick the facades are, Windows-specific calls, static mutable state, configuration key names, data access. It tells you what a REST layer would have to provide.
2. **Contract** from a client DLL (metadata only; the DLL is never loaded or executed):
   ```bash
   migrator contract --assembly "C:\src\bin\MyApp.Client.dll" --out "D:\out\MyApp\contracts"
   ```
   Pass the resulting `contract.json` to the migration with `--contract` (repeatable). The app then gets typed mock services, fixtures and types. Members that look like secrets (password, key, license, connection string, credentials, token) are omitted or stripped and listed in the report as SERVER-ONLY; secrets never reach browser code.

Keep DLLs and WSDLs in a git-ignored folder (`local-inputs/`) or outside the repository.

## 4.5 Step 5: generate the working app
```bash
migrator migrate --source "C:\src\MyApp" --out "D:\out\MyApp" \
    --translate rules --compile-gate --depth all [--units all] [--contract "D:\out\MyApp\contracts\...\contract.json"] [--images "C:\src\images"]
```
(Add `--target angular-app` for Angular. UI: mode *Working app*.)

What happens: IR step → scope → pages generated (markup + code-behind) → **compile gate**: `npm install` if needed, then `tsc` (React) or `ngc` (Angular); each translated method that fails to compile is demoted back to commented C# with a TODO and the page is regenerated, up to several rounds → `MIGRATION_REPORT.md`.

Expected console ending (numbers vary):

```
react-app: generated 5 unit(s): ...
  translation: 120 method(s): 60 complete, 40 partial, 20 not translated (6 demoted by the compile gate); 0 TS error(s) remain
  report: D:\out\MyApp\react-app\MIGRATION_REPORT.md
```
**Checkpoint:** `0 TS error(s) remain`. Anything else means the gate could not run (dependencies missing: use `--compile-gate`) or an error sits outside translated methods (the report lists it).

`--translate none` leaves every handler as commented C# (a fast structure-only app); `rules` is the normal choice; `llm` currently behaves like `rules`.

## 4.6 Step 6: read the report
Open `<app>/MIGRATION_REPORT.md` (the UI shows it on the Report tab). In this order:

1. **Header counts**: units generated of units found; units not generated and why.
2. **Generated pages**: per unit controls, handlers, navigation wired, issues.
3. **Archetypes**: classification, what was inferred, catalog count ([6.4](06-reading-results.md#64-archetypes-and-the-modules-catalog)).
4. **Translation (rules)**: methods complete / partial / not translated, the gate's demotions, and **most common reasons**: the fastest way to see what blocks more automation.
5. **Project classes**: the same numbers for the application's own classes.
6. **Manual work by category**: controls with no library component, navigation that needs a decision, grids whose columns are created in code, non-visual components, server-only code, contract notes.

Full explanation: [chapter 6](06-reading-results.md).

## 4.7 Step 7: install, run and review the app
```bash
cd "D:\out\MyApp\react-app"          # or angular-app
npm install                          # skip if the gate already installed
npm run build && npm run lint        # both must be clean
npm run dev                          # React; Angular: npm start
```
In the Migrator UI: *Run the app* tab → Install → Start → Open in browser.

Review checklist in the browser (hard refresh after regenerating):

| Check | How |
|---|---|
| Entry form renders | open `/` |
| Every page opens on its own | `/pages` lists them; open each, or click through |
| Menu and toolbar | click every item that should open a form; dialogs open as popups and close with Close, OK, Cancel, the ✕ and Escape |
| Grids and lists | they show typed **mock** rows (marked `// MOCK` in the code-behind) |
| Dynamic screens | `/modules`: open each unit; confirm columns, nodes, menu items and buttons against the original screen |
| Console | no errors; a page that throws shows its error text instead of a blank screen |

Items that do nothing are usually file or collection operations or untranslated handlers; the report says why.

## 4.8 Step 8: walk the original window flow (app shell and viewer)
Two visual-only React apps show what the engine understood of each screen, without logic or data:

```bash
migrator migrate --source "C:\src\MyApp" --out "D:\out\MyApp" --appshell     # -> D:\out\MyApp\react-appshell
migrator migrate --source "C:\src\MyApp" --out "D:\out\MyApp" --preview      # -> D:\out\MyApp\react-viewer
cd "D:\out\MyApp\react-appshell" && npm install && npm run dev
```
- **App shell**: a WinForms-style window; hyperlinks on the left, the clicked form in a form-style frame on the right; buttons that opened a form with `ShowDialog` open its visual page as a popup, so the window flow can be walked through.
- **Viewer**: a sidebar and filter, one unit at a time; `VIEWER_REPORT.md` lists fallbacks and controls without a component.

Use them **before** judging behaviour, and to show stakeholders the screens early. They ignore entry form, depth, contracts and translation, and are not available for Angular.

## 4.9 Step 9: fix at the right layer and regenerate
Never edit generated output and expect it to survive: regenerate instead.

| Symptom | Fix in |
|---|---|
| A control or an event is missing from the IR | the **extractor** (source engine) |
| A statement pattern is not translated | a **translator rule** |
| A control has no component | **`ui-spec`** and every `ui-*` package, or a different stand-in in `fallbacks.json` |
| Windows-only or server-only code | the **rules data** (`windows-bypass.json`, `server-bound.json`) |
| A unit is classified as the wrong archetype | a copy of `control-archetypes.json` (`--archetypes`) |
| An application-specific need | the project's own copy of a rules file, **outside** the engine |

Then re-run steps 5–7: `tsc`/`ngc` and lint must stay at 0. More: [chapter 7](07-fixing-and-troubleshooting.md).

## 4.10 Step 10: definition of done
- IR files emitted and consistent, no `Failures`.
- The app builds, lint is clean, `0 TS error(s) remain`.
- Every unit renders; every navigation is a route or a dialog; grids and lists show typed mock data.
- Every inferred, mock, fallback, bypassed or untranslated item is **marked** in the code and **listed** in the report, and reviewed with the people who know the application.
- No application names in the engine, rules, `ui-spec` or docs; nothing generated or taken from the application is committed.

## 4.11 Angular differences
Same steps, `--target angular-app` (UI: Target `angular-app`):

| | React | Angular |
|---|---|---|
| Output folder | `react-app` | `angular-app` |
| Run | `npm run dev` | `npm start` (`ng serve`, no prebundling so the library and the app share one Angular) |
| Type check | `tsc` | `ngc` with strict templates (also checks the templates) |
| Page files | `X.tsx` + `X.code.tsx` | `x.page.ts` (component) + `x.page.html` (template) + `x.page.code.ts` (code-behind) |
| App shell / viewer | yes | no |
| Node | LTS | 20.19, 22.12 or newer |

The translation numbers for the same input are identical in both targets. Details: [../guide/v0.5/09-angular.md](../guide/v0.5/09-angular.md).

## 4.12 Worked example (synthetic sample)
A tiny application with a host form that loads a module by name, and four user controls that no code opens: a grid control whose columns are added in code, a tree control whose nodes are added in code, a menu control whose items are added in code, and a toolbar control with two designer buttons.

```bash
migrator migrate --source "C:\samples\Archetypes" --out "D:\out\Archetypes" --translate rules --compile-gate --catalog
cd "D:\out\Archetypes\react-app" && npm run dev
```
What to expect:

| Where | Result |
|---|---|
| Console | `generated 5 unit(s)`, `0 TS error(s) remain` |
| Report → Archetypes | four non-form rows: the grid and the menu control decided by **name**, the tree and the toolbar control by **designer + name**; inferred lines such as *3 column(s) named in the code*, *3 node(s) named in the code*, *2 item(s) named in the code*; *4 unit(s) are never opened by code … listed at `/modules`* |
| `/modules` | a left list *Grid (1) / Tree (1) / Menu (1) / Toolbar (1)*; clicking a unit shows the grid with columns *Item name, Qty, Unit price* and marked sample rows, the tree with *Reports, Settings, Help*, the menu with *File, Edit*, the toolbar with *New, Save* |
| `/` | the host form (its loading logic stays commented C# in its code-behind: not translated) |

The same run with `--target angular-app` gives the same listing under `http://localhost:4200/modules`.
