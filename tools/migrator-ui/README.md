# Migrator UI

A local web application for running the migration without typing CLI options: choose the WinForms source and an output folder, pick **what to generate** (IR only, working app, app shell, component viewer) and the target (React, Angular), set the scope and translation options, press **Run**, read the live log and the migration report, then **install and start** the generated app from the same window (the run command is shown and can be copied).

The UI is built with the project's own library (`@wfm/ui-react`: group boxes, radio and check boxes, combo boxes, tabs, list box, dialogs). It is a tool, not a demo: nothing here is referenced by the engine or the library.

```
tools/migrator-ui/
├─ host/        MigratorUi.Host: ASP.NET minimal API (net10.0). Runs `migrator` and `npm` as child processes, streams their output (server-sent events), serves web/dist.
├─ web/         React 18 + TypeScript + Vite UI using @wfm/ui-react
└─ start.ps1    builds the UI once, starts the host, opens the browser
```

## Run it
```bash
cd ui-library/ui-core && npm ci && npm run build && cd ../ui-react && npm ci && npm run build && cd ../..   # once
pwsh tools/migrator-ui/start.ps1                          # or by hand:
cd tools/migrator-ui/web && npm install && npm run build
cd ../host && dotnet run                                  # http://127.0.0.1:5280
```
UI development: `dotnet run` in `host/` and `npm run dev` in `web/` (Vite proxies `/api` to the host).

## What the host does
| Endpoint | Purpose |
|---|---|
| `GET /api/info` | repository root, engines.json, available targets and modes |
| `GET /api/browse?path=` | folder listing for the folder picker (loopback only) |
| `POST /api/migrate` | `migrator ir` or `migrator migrate ...` from the form; returns a run |
| `POST /api/app/install`, `/api/app/start` | `npm install`, then `npm run dev` (React) or `npx ng serve` (Angular) in a generated app; a free port from 5400 |
| `GET /api/runs`, `/api/runs/{id}/log`, `POST /api/runs/{id}/stop` | every run, its output as a stream, cancel / stop (kills the process tree and the port's listener) |
| `GET /api/report?dir=` | `MIGRATION_REPORT.md` of an output folder |

## The generated API
Choose the target `dotnet-api` to generate the server side (a .NET 10 minimal API for the data-access classes); *Install* runs `dotnet build`, *Start* runs `dotnet run` on a free port from 5000. Web targets have the option *Call the generated .NET API for data access* (`--api`). See `docs/guide/v0.5/12-api.md`.

## Safety
- Bound to `127.0.0.1`. Every POST must carry the header `X-Migrator-UI` (no CORS is enabled, so a page on another origin cannot send it).
- Only fixed commands run: the migrator CLI, `npm install`, `npm run dev`, `npx ng serve`. App commands run only in a folder whose `package.json` is a generated app (`@wfm/ui-*` dependency or a `migrated-` name).
- The output folder must be outside this repository (CLAUDE.md §0.5). Inputs (sources, contracts, WSDLs) are read, never copied into the repo.
- The form is remembered in the browser (`localStorage`) only.

## Limits
App shell and component viewer exist for React only. The report tab shows the working-app report; the IR-only, app shell and viewer modes write none. Runs are kept in memory until the host stops (dev servers are stopped with it).
