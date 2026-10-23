# 2. Setup

## 2.1 Prerequisites
| Need | Notes |
|---|---|
| **.NET SDK 10** | The engine and the Migrator UI host target `net10.0` |
| **Node.js LTS with npm** | Vite and the generated apps. Angular 21 (the Angular target) needs Node 20.19, 22.12 or newer |
| **The shared IR checkout** | A sibling folder `..\MIgration Engine 2` of this repository (the engine only references it, never edits it). Another location: `dotnet build -p:WebFormsMigratorRoot=<path>` |
| **The legacy source** | `*.cs`, `*.Designer.cs`, `*.resx` of the WinForms project. Binary-only input (EXE/DLL without source) is possible but lossy |
| Optional: backend client DLL / ASMX source / WSDLs | For typed mock services and the backend survey ([Workflow steps 4–5](04-workflow.md#44-step-4-optional-backend-inputs)) |
| Optional: PowerShell 7 (`pwsh`) | For `tools/migrator-ui/start.ps1`; you can start the UI by hand without it |

Not needed: Ollama or any model (the LLM tier is planned).

## 2.2 One-time build
Run from the repository root.

```bash
dotnet build WinFormsMigrator.sln

# the UI library, in this order (ui-react and generated apps import ui-core)
cd ui-library/ui-core    && npm ci && npm run build && cd ../..
cd ui-library/ui-react   && npm ci && npm run build && cd ../..
cd ui-library/ui-angular && npm ci && npm run build && cd ../..     # only if you will generate Angular apps
```

Check that the engines load:

```bash
dotnet run --project src/Migrator.Cli -- engines
```
Expected: the source engine `winforms` and the targets `react-app` and `angular-app`, each with a version and the IR schema (`0.1.0`). Only engines listed (and enabled) in `engines.json` load; a DLL in `plugins/` that is not listed is reported and never executed.

To start the Migrator UI (optional) build its web part once — `start.ps1` does it for you the first time:

```bash
cd tools/migrator-ui/web && npm install && npm run build
```

## 2.3 Folders: what goes where
| Folder | Holds | Committed? |
|---|---|---|
| The legacy **source** | Read-only input | not in this repository |
| `local-inputs/` | DLLs, WSDLs, anything taken from a customer application | never (git-ignored) |
| **Output folder** (`--out`) | IR, generated apps, reports, analyses, contracts | **outside the repository**, never committed |
| `rules/`, `ui-library/ui-spec/` | Generic data: bypass lists, server-bound markers, archetype names, fallbacks | yes, with no application names |
| A copy of a rules file | Overrides for one application | kept with that project, outside this repository |

The Migrator UI and the CLI both refuse or warn about an output folder inside the repository for this reason.

## 2.4 Windows path note
On Windows, a path containing `&` (for example `D:\R&D\...`) breaks npm's `.bin` shims. The generated apps and the UI library call tools as `node ./node_modules/<tool>/bin/...` to avoid this. If you add your own npm scripts, do the same; if a tool fails with a truncated path, this is the cause.

## 2.5 Updating
```bash
git pull --ff-only origin main
dotnet build WinFormsMigrator.sln
# rebuild a UI library package only if its sources changed (ui-core first)
```
Regenerate apps afterwards: the generators are deterministic, so unchanged input and tool version give identical output.
