# 1. Overview

## 1.1 Mission
Turn a legacy .NET WinForms application (`*.Designer.cs` + code-behind + user-control libraries) into a **minimum working** modern application: it builds, screens render, controls are wired to real state and translated handlers, data is mock. It does not promise correct business logic; everything unverified is marked (see [04-workflow.md](04-workflow.md) §markers).

## 1.2 Pipeline
```
legacy source ──► Source engine ──► IR folder ──► Target engine ──► app + report
 (WinForms)       (Roslyn, syntax)   (JSON)        (react-app)      (uses ui-library)

legacy backend ──► backend analyzer ──► BACKEND_ANALYSIS.md ──► (planned) REST API generator
```
Analyse once, serialise the IR, generate from the IR. Generators never read source; they read the IR folder and `ui-spec` only.

## 1.3 Modules
| Module | Role | Knows about |
|---|---|---|
| `Migrator.Engine` | Host: contracts, engine registry/loader (`engines.json`), IR file writer, facet projections, navigation helper, assembly-contract reader | no engine |
| `Migrator.Source.WinForms` | Source engine: project analyzer, designer parser, extractors, support-class and dependency extraction | Engine contracts |
| `Migrator.Target.React` | Target engine: page generator, rule translator, compile gate, viewer, app shell | Engine contracts, `ui-spec` as data |
| `Migrator.Source.Backend` | Read-only backend analyzer (syntax only) | nothing else |
| `Migrator.Cli` | Composition root: verbs `engines`, `ir`, `contract`, `backend`, `migrate` | all of the above |
| `ui-library/ui-spec` | Control contract (`controls.json`), stand-ins (`fallbacks.json`), tokens | nothing from `src/` |
| `ui-library/ui-react` | `@wfm/ui-react`, the React implementation of the spec | nothing from `src/` |
| `rules/*.json` | Data: Windows bypass, server-bound code, backend patterns | — |

Dependency direction: `Cli → Engine ← Source.* / Target.*`; engines never reference each other; the ui-library knows nothing about the engine. The shared IR models come from the sibling WebFormsMigrator checkout (reference only, never edited).

## 1.4 Targets
| Target | State |
|---|---|
| `react-app` (React 18 + TS + Vite, `@wfm/ui-react`) | Built |
| `angular-app`, `html-jquery-app` | Planned: same names/props/events as the spec; needs a shared generation core first |
| .NET 10 REST API | Planned; analysis built (see [03-backend.md](03-backend.md)) |

## 1.5 Outputs of a run
| Folder | Content |
|---|---|
| `<out>/ir/` | Per unit: `complete`, `controls`, `control-properties`, `code-behind`, `layout` IR files; `support.ir.json` (the app's own non-UI classes); `project.ir.json` (index, failures, unsupported constructs, external assemblies) |
| `<out>/react-app/` | Functional app + `MIGRATION_REPORT.md` + `src/legacy/*.cs.txt` (original code kept) |
| `<out>/react-viewer/`, `<out>/react-appshell/` | Visual-only apps (`--preview`, `--appshell`) |

Always write outputs to a git-ignored folder outside the repo or under `local-outputs/`.
