# 5. Options reference

`migrator` stands for `dotnet run --project src/Migrator.Cli --` (or the built `migrator.exe`). `migrator --help` prints the usage. The last column names the field of the [Migrator UI](03-ways-to-run.md#31-the-migrator-ui) that sets the option.

All commands accept `--config <engines.json>`; without it the tool looks for `engines.json` from the current folder upwards, so run from inside the repository or pass the path.

## 5.1 engines, ir
| Command | Options | Effect |
|---|---|---|
| `engines` | `[--config <engines.json>]` | Lists the loaded source and target engines with versions and the IR schema; prints diagnostics for engines that did not load. Exit code 2 when an engine failed |
| `ir` | `--source <dir>` `--out <dir>` `[--engine <id>]` `[--config ...]` | Extract the IR only. Writes `<out>/ir/<Unit>/…` (five files per unit) and `<out>/ir/project.ir.json` (plus `support.ir.json`, `resources.ir.json` when there is something to write). `--engine` picks a source engine by id (default `winforms`). UI: mode *IR only* |

## 5.2 migrate
`migrate --source <dir> --out <dir> [options]` runs the IR step, then generates into `<out>/<target>` (or `<out>/react-appshell`, `<out>/react-viewer`).

| Option | Default | Effect | UI field |
|---|---|---|---|
| `--source <dir>` | required | WinForms project folder | WinForms source |
| `--out <dir>` | required | Output folder (outside the repository); IR in `<out>/ir`, app next to it | Output folder |
| `--target react-app\|angular-app\|dotnet-api` | `react-app` | Target engine; the Angular app is written to `<out>/angular-app` | Target |
| `--entry <form>` | the startup form | Entry form (route `/`) | Entry form |
| `--depth <n\|all>` | `1` | `Show` / `ShowDialog` hops from the entry form to generate; `all` follows everything reachable | Depth |
| `--units all` | off | Generate every form and user control, not only those reachable; also writes the `/modules` catalog | *Every form and user control* |
| `--catalog` | off | Write the `/modules` page for units no code opens (generates those units). Implied by `--units all` | */modules catalog* |
| `--archetypes <file>` | `rules/control-archetypes.json` | A copy of the archetype rules with project-specific name patterns | Archetype rules |
| `--no-external-projects` | off | Do not follow `ProjectReference`s outside the source folder (their classes are otherwise included as typed API shapes) | *Do not follow projects outside the source folder* |
| `--translate none\|rules\|llm` | `none` | `none`: handlers stay commented C#. `rules`: deterministic C# → TypeScript. `llm` currently behaves like `rules` | Translate |
| `--compile-gate` | off | Install npm dependencies if missing, then run `tsc` / `ngc`; methods that fail are demoted to commented C# and the page is regenerated (several rounds). Without the flag the gate runs only when dependencies already exist | *Compile gate* |
| `--no-compile-gate` | off | Skip the gate even when dependencies exist (shows the raw errors of translated code) | |
| `--contract <file>` (repeatable) | none | A `contract.json` from `migrator contract`: typed mock services, fixtures and types | Contract files |
| `--contract-scope used\|all` | `used` | Emit only what the generated code uses, or every public type and static method | Contract scope |
| `--api` | off | Web targets: keep the server-bound data-access classes and call the generated API for them (`src/lib/api.ts`); generate the server with `--target dotnet-api` | *Call the generated .NET API* |
| `--db-provider sqlclient\|oledb` | `sqlclient` | `dotnet-api`: the ADO.NET provider the ported data classes run against | API database |
| `--images <folder>` | none | Image files named by key (`Name.png`, `Name.ico`, …) for `Properties.Resources.Name` and image-list entries the engine could not extract | Images folder |
| `--appshell` | off | Write `<out>/react-appshell`: visual-only pages in a WinForms-style window (React only). Implies all units; entry, depth, contracts and translation are ignored | mode *App shell* |
| `--preview` | off | Write `<out>/react-viewer`: a visual-only page per unit with a sidebar (React only). Same implications | mode *Component viewer* |
| `--config <engines.json>` | found upwards | Engine allow-list and paths | (set by the host) |

Notes
- `--units all`, `--catalog`, `--appshell`, `--preview` change **what is generated**; `--translate`, `--compile-gate`, `--contract*` change **how code is translated** and only matter for the working app.
- Generation is deterministic: the same IR and tool version give identical output, so differences between runs come from changed input or a changed tool.

## 5.3 contract
`contract --assembly <dll> --out <dir>`

Reads the **public API of a managed assembly from metadata only** (the DLL is never loaded or executed) and writes `<dir>/<AssemblyName>/contract.json` and `data-access.json` (SQL table and procedure names; connection-string-like literals are only counted and discarded). The console shows type, member and data-access counts. Use a git-ignored output folder.

## 5.4 backend
`backend --source <dir>… [--wsdl <dir>…] --out <dir> [--rules <file>]`

Read-only survey of .NET Framework class libraries, ASMX services and WSDLs: services and operations, SOAP proxies, facade thickness (thin or logic), Windows-specific calls, static mutable state, configuration key names, data access. Writes `BACKEND_ANALYSIS.md` and `backend-analysis.json`. `--source` and `--wsdl` repeat. `--rules` points at a copy of `rules/backend-patterns.json` (default: the one in the repository). Use a git-ignored output folder.

## 5.5 Rule files (data, no rebuild)
| File | Controls | Option |
|---|---|---|
| `rules/windows-bypass.json` | Windows-only API calls that are bypassed with a marker | target option `bypassRulesPath` in `engines.json` |
| `rules/server-bound.json` | Markers of server-side code (data access, file system, configuration) left to the backend | `serverBoundRulesPath` |
| `rules/control-archetypes.json` | Name patterns and the input threshold that classify grid / tree / menu / toolbar / list controls | `archetypeRulesPath`, `--archetypes` |
| `rules/backend-patterns.json` | What the backend survey looks for | `backend --rules` |
| `ui-library/ui-spec/fallbacks.json` | The stand-in component for a control without one | |

Extend a **copy** per project, keep it with the project (outside this repository), and never put application names in the repository's files.

## 5.6 Typical combinations
| Situation | Options |
|---|---|
| First look at the structure | `ir` |
| Show every screen to stakeholders | `migrate --appshell` |
| Quick app, no logic | `migrate --depth all` (translate none) |
| Normal working app | `migrate --translate rules --compile-gate --depth all` |
| Application that loads screens from menu data or permissions | add `--units all` (or `--catalog`) and, if names are unusual, `--archetypes <copy>` |
| Application with a client DLL for services | `contract …` first, then add `--contract <contract.json>` |
| Application with resource images that were not extracted | add `--images <folder>` |
| Analyse only one part of a big solution | `--entry <form>` with a small `--depth` |
| Angular instead of React | add `--target angular-app` |
| Application with data-access classes (ADO.NET, DataSets) | run once with `--target dotnet-api` for the server, then the web target with `--api` |
