# Handbook — running the WinForms Migration Engine

A short, task-oriented guide for the person who **runs** a migration and reviews the result. It summarises the engine and walks through the whole workflow; the reference documents behind it are linked where they matter. It describes `main`.

## Reading order
| # | Chapter | Read it for |
|---|---|---|
| 1 | [Summary](01-summary.md) | What the engine does, what it produces, what it does not promise |
| 2 | [Setup](02-setup.md) | Prerequisites, the one-time build, folder rules |
| 3 | [Ways to run](03-ways-to-run.md) | The Migrator UI, the command line, running a generated app |
| 4 | [Workflow](04-workflow.md) | The detailed step-by-step workflow from a new application to a checked app (with a worked example) |
| 5 | [Options reference](05-options-reference.md) | Every command and option, with the UI field that sets it |
| 6 | [Reading the results](06-reading-results.md) | Output folders, the report, the markers in generated code, how to judge quality |
| 7 | [Fixing and troubleshooting](07-fixing-and-troubleshooting.md) | Fix at the right layer, common problems, FAQ |
| 8 | [Glossary and cheat sheet](08-glossary-and-cheatsheet.md) | Terms, commands and URLs on one page |

## I want to…
| Goal | Go to |
|---|---|
| Try the tool on my application in ten minutes | [Ways to run → Migrator UI](03-ways-to-run.md#31-the-migrator-ui) and [Workflow, quick path](04-workflow.md#40-the-quick-path) |
| Migrate an application properly | [Workflow](04-workflow.md) |
| Get an Angular app instead of React | [Options reference → `--target`](05-options-reference.md#52-migrate) and [Workflow, Angular differences](04-workflow.md#411-angular-differences) |
| Include screens the original loads dynamically (menu data, permissions) | [Workflow step 3](04-workflow.md#43-step-3-choose-the-scope) and [Reading the results → archetypes](06-reading-results.md#64-archetypes-and-the-modules-catalog) |
| Look at every screen first, without logic | [Workflow step 8](04-workflow.md#48-step-8-walk-the-original-window-flow-app-shell-and-viewer) |
| Understand a report or a marker in the generated code | [Reading the results](06-reading-results.md) |
| Fix something that came out wrong | [Fixing and troubleshooting](07-fixing-and-troubleshooting.md) |
| Look up an option | [Options reference](05-options-reference.md) |

## Deeper documentation (reference, not needed to run a migration)
| Document | Content |
|---|---|
| [../USER_GUIDE.md](../USER_GUIDE.md) | The original single-file user guide |
| [../guide/LATEST.md](../guide/LATEST.md) | The versioned guide: UI generation, backend, decisions, roadmap, Angular, archetypes, the UI |
| [../PROJECT_DOCUMENTATION.md](../PROJECT_DOCUMENTATION.md) | As-built architecture |
| [../ENGINE_AUTHORING.md](../ENGINE_AUTHORING.md) | Extending the engine: contracts, IR keys, translator rules |
| [../UI_CONTROL_MAPPING.md](../UI_CONTROL_MAPPING.md) | Which library component represents which WinForms control |
| [../../SKILLS.md](../../SKILLS.md) | Control, event and C# → TypeScript mappings |
| [../../CLAUDE.md](../../CLAUDE.md) | Rules for contributors and agents (git workflow, markers) |
| [../../tools/migrator-ui/README.md](../../tools/migrator-ui/README.md) | The Migrator UI in detail |

Paths and names in this handbook are placeholders (`C:\src\MyApp`, `D:\out\MyApp`); the engine knows no application.
