# 3. Ways to run

There are three ways to work with the engine. They produce the same results; choose by convenience.

| Way | Best for | Start |
|---|---|---|
| **Migrator UI** | Trying things, choosing options from a form, watching the log, starting the generated app | `pwsh tools/migrator-ui/start.ps1` |
| **Command line** | Repeatable runs, scripts, CI-like use | `dotnet run --project src/Migrator.Cli -- <command> ...` |
| **The generated app** | Reviewing the result | `npm run dev` (React) / `npm start` (Angular) in the app folder |

## 3.1 The Migrator UI
Start it:

```bash
pwsh tools/migrator-ui/start.ps1               # builds the web part the first time, opens http://127.0.0.1:5280
# or by hand:
cd tools/migrator-ui/web && npm install && npm run build
cd ../host && dotnet run
```
The UI is a local web application (an ASP.NET host plus a React UI built with `@wfm/ui-react`). It listens on `127.0.0.1` only.

### Screen layout
**Left: the form.** **Right: the result** (Log, Report, Run the app, Runs).

| Group | Fields | Same as CLI |
|---|---|---|
| Source and output | WinForms source (Browse…), Output folder (Browse…; must be outside the repository) | `--source`, `--out` |
| What to generate | **IR only** · **Working app** · **App shell** · **Component viewer**; Target `react-app` / `angular-app` | `ir` / `migrate`, `--appshell`, `--preview`, `--target` |
| Scope | Entry form, Depth (0–3, all), *Every form and user control*, */modules catalog*, *Do not follow projects outside the source folder* | `--entry`, `--depth`, `--units all`, `--catalog`, `--no-external-projects` |
| Translation and inputs (working app only) | Translate (none / rules / llm), Contract scope, Contract files (one path per line), *Compile gate* | `--translate`, `--contract-scope`, `--contract`, `--compile-gate` |
| Rules and images | Images folder, Archetype rules file | `--images`, `--archetypes` |

Under the form the **command line** that the choices stand for is shown; copy it to run the same thing in a terminal. The form is remembered in the browser.

### Running
1. Fill in source and output, choose the mode, press **Run**. The Log tab streams the migrator's output; **Cancel** stops it.
2. When it finishes, the header shows *finished* (or *failed (exit n)*). In Working-app mode the **Report** tab shows `MIGRATION_REPORT.md`.
3. Open **Run the app**: it shows the app folder and the manual commands, with buttons **Copy command**, **1. Install** (`npm install`), **2. Start**, **Stop**, **Open in browser**. Start picks a free port from 5400 (`npm run dev` for React, `ng serve` for Angular) and enables Open in browser when the port answers.
4. **Runs** lists every run of the session; click one to see its log.

Notes: App shell and Component viewer are React only (the option is disabled for Angular). The IR-only, app shell and viewer modes write no migration report. Dev servers are stopped when the host stops. Details: [../../tools/migrator-ui/README.md](../../tools/migrator-ui/README.md), [../guide/v0.5/11-ui.md](../guide/v0.5/11-ui.md).

## 3.2 The command line
Run from the repository root. `migrator` below stands for `dotnet run --project src/Migrator.Cli --` (or the built `migrator.exe`).

| Command | Purpose |
|---|---|
| `migrator engines` | List the engines that load |
| `migrator ir --source <src> --out <out>` | Extract the IR only |
| `migrator migrate --source <src> --out <out> [options]` | IR step, then generate the app (or app shell / viewer) |
| `migrator contract --assembly <dll> --out <dir>` | Public API of a managed DLL from metadata (never executed) |
| `migrator backend --source <dir>... [--wsdl <dir>...] --out <dir>` | Read-only survey of the legacy service layer |

Quick forms:

```bash
# IR only: inspect what was understood
migrator ir --source "C:\src\MyApp" --out "D:\out\MyApp"

# Working React app, translated and compile-checked
migrator migrate --source "C:\src\MyApp" --out "D:\out\MyApp" --translate rules --compile-gate --depth all

# Working Angular app
migrator migrate --source "C:\src\MyApp" --out "D:\out\MyApp" --target angular-app --translate rules --compile-gate --depth all

# Everything, including screens that are loaded dynamically, with the /modules catalog
migrator migrate --source "C:\src\MyApp" --out "D:\out\MyApp" --units all --translate rules --compile-gate
```
Every option is described in [chapter 5](05-options-reference.md).

## 3.3 Running a generated app by hand
```bash
cd "D:\out\MyApp\react-app"            # or angular-app
npm install                            # first time (the compile gate already did it if you used --compile-gate)
npm run dev                            # React: http://localhost:5173   |   Angular: npm start -> http://localhost:4200
npm run build                          # production build, must pass
npm run lint                           # must report 0 problems
```
The same app routes in both targets: `/` the entry form, `/<page-name>` one page on its own (kebab-case of the component name, for example `/search-results`), `/pages` the list of pages, `/modules` the catalog (only when generated with `--catalog` or `--units all`).

The app shell and viewer are separate apps in `react-appshell` and `react-viewer` (`npm install && npm run dev`).
