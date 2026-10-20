# 4. Workflow — from a new application to a checked app

## 4.1 Prerequisites and one-time setup
.NET SDK 10, Node.js LTS, a sibling checkout of the shared IR repository (`-p:WebFormsMigratorRoot=<path>` to relocate), the legacy **source**.
```bash
dotnet build WinFormsMigrator.sln
cd ui-library/ui-react && npm ci && npm run build && cd ../..
```
Only engines listed in `engines.json` load. On Windows, paths containing `&` or spaces break npm `.bin` shims; generated scripts call `node ./node_modules/<tool>/...` for that reason.

## 4.2 Folders (what is committed, what is not)
| Folder | Holds | Committed |
|---|---|---|
| `local-inputs/` | DLLs, WSDLs, anything from the customer | never (git-ignored) |
| `local-outputs/`, or a folder outside the repo | IR, generated apps, analyses, contracts | never |
| `rules/`, `ui-library/ui-spec/`, `docs/` | generic data and documentation | yes — no application names |
| per-project profile/rules copy | overrides for one application | with the project, outside this repo |

## 4.3 Runbook
| # | Step | Command | Check |
|---|---|---|---|
| 1 | Engines load | `migrator engines` | source `winforms`, target `react-app` listed |
| 2 | IR only | `migrator ir --source <src> --out <out>` | `project.ir.json`: `Failures` empty, read `Unsupported`; open a few `*.complete.ir.json` |
| 3 | Look at every screen | `migrator migrate --source <src> --out <out> --appshell` (or `--preview`) | all units listed in the app; `VIEWER_REPORT.md` fallbacks |
| 4 | Backend survey (if the app has a service layer) | `migrator backend --source <lib> --source <svc> --wsdl <dir> --out <out>/backend` | read `BACKEND_ANALYSIS.md`: services, proxies, thin vs logic, Windows hits, static state |
| 5 | Backend contract (if a client DLL exists) | `migrator contract --assembly <dll> --out <out>/contracts` | `contract.json`, `data-access.json` |
| 6 | Functional app | `migrator migrate --source <src> --out <out> --translate rules --compile-gate --depth all [--units all] [--contract <contract.json>]` | console counts; `tsc` errors 0 |
| 7 | Verify generated app | `npm run build`, `npm run lint`, `npm run dev`, then `?page=<Name>` for each page | build passes, lint 0, pages render |
| 8 | Read the report | `MIGRATION_REPORT.md` | top reasons; fix at the right layer (below) |
| 9 | Repeat 6–8 | | `tsc` and lint stay at 0 |
| 10 | Review | TODO / MOCK / BYPASSED / FALLBACK items with people who know the application | |

`--units all` for shells that open screens from menu data or reflection; `--depth all` to follow every `Show/ShowDialog`.

### 4.3.1 Verifying in a browser
After a run, check behaviour in a real browser as well as the build:
1. Start the generated app (`node ./node_modules/vite/bin/vite.js --port <n>` in `react-app` or `react-appshell`) and open it. A hard refresh (Ctrl+Shift+R) picks up a regenerated library.
2. Click through the main window: every menu item and toolbar button that opens a form should open its page as a popup and close again with Close, OK, Cancel, the title-bar ✕ or Escape. Items that do nothing are usually file or collection operations; the report's "most common reasons" section says why.
3. The visual-only **app shell** opens and closes dialogs but runs no handlers; the **viewer** has no popups. Test behaviour in the functional app.
4. Test harnesses (jsdom suites, browser scripts, sample applications, temporary copies) live **outside the repository**, for example `D:\test\tempoarary`; never add test projects to the repo.

## 4.4 Fix at the right layer
| Symptom | Fix in |
|---|---|
| Control or event missing from the IR | the **extractor** (Source engine) |
| Statement pattern not translated | a **translator rule** (target) |
| Control has no component | add to **`ui-spec`** and every `ui-*` package (or edit `fallbacks.json` to choose a different stand-in) |
| Windows or server-only code | add a pattern to the **rules data** (`windows-bypass.json`, `server-bound.json`) |
| App-specific need | the **project profile** outside the repo — never the engine |
Never patch generated output by hand; it does not survive a rerun.

## 4.5 Markers in generated code
`// TODO: not translated — <reason>` (original kept) · `// TODO: wire to real API` · `// MOCK — replace with real implementation` · `// inferred — check against backend contract` · `// dropped (no browser equivalent: …)` · `// BYPASSED — Windows-specific API (…)` · `/* FALLBACK: Type */` · `<WfUnsupported/>` (last resort) · `// LLM-translated — verify` (reserved for the LLM tier). Nothing is dropped silently; `src/legacy/*.cs.txt` keeps every original code-behind.

## 4.6 Definition of done (per application)
IR files emitted and consistent, no failures; generated code imports only `Wf*` components; `tsc` 0, lint 0, build OK; every unit renders; every navigation is a route or dialog; grids and lists show typed mock data; every inferred/mock/fallback/bypassed item is marked and in the report; no application names in engine, rules, ui-spec or docs; no test, demo or Storybook projects added.

## 4.7 Git workflow when changing the tool
Branch from the latest `main` (`feature/…`, `fix/…`, `docs/…`), small commits, never push or open PRs from the agent; the maintainer pushes. Exact block: `CLAUDE.md` §0.2.
