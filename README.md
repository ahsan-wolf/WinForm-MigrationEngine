# WinForms Migration Engine

Converts legacy **.NET WinForms** applications into a **minimum working React + TypeScript app**: analyze once → framework-neutral IR → deterministic generation against an independent UI library (`@wfm/ui-react`) → rule-based C# → TypeScript translation checked by `tsc` → migration report.

```bash
dotnet build WinFormsMigrator.sln
cd ui-library/ui-react && npm ci && npm run build && cd ../..
dotnet run --project src/Migrator.Cli -- migrate --source "<winforms project>" --out "<output outside this repo>" --translate rules --compile-gate --depth all
cd "<output>/react-app" && npm install && npm run dev      # ?page=<Name> opens any generated page, ?page= lists them
```

Output is a starting point, not a finished product: data is mock, translated logic is unverified, and every TODO/MOCK marker is listed in `MIGRATION_REPORT.md`.

| Read | For |
|---|---|
| [docs/USER_GUIDE.md](docs/USER_GUIDE.md) | Running a migration, reading the results, troubleshooting |
| [docs/PROJECT_DOCUMENTATION.md](docs/PROJECT_DOCUMENTATION.md) | As-built architecture, pipeline, IR, translation, results |
| [docs/PROJECT_PLAN.md](docs/PROJECT_PLAN.md) | Status, scope, plan, risks, roadmap |
| [docs/ENGINE_AUTHORING.md](docs/ENGINE_AUTHORING.md) | Adding engines, IR conventions, extending the translator |
| [SKILLS.md](SKILLS.md) | Control / event / pattern mappings and the C# → TypeScript cheat sheet |
| [CLAUDE.md](CLAUDE.md) | Rules, contracts, markers, git workflow (read first when changing the tool) |
| [docs/PILOT_FINDINGS.md](docs/PILOT_FINDINGS.md) | The first real application run (ECMNavigator) |
| [ui-library/README.md](ui-library/README.md) | The UI library and `ui-spec` |

Requirements: .NET SDK 10, Node.js with npm, and a sibling checkout of the shared IR repository (see `build/IrReference.props`). Never commit backend DLLs, derived contracts or generated output from a customer application (`local-inputs/` and `local-outputs/` are git-ignored).
