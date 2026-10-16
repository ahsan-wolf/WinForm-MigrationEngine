# 3. Backend track

## 3.1 Why a backend track
A browser cannot call legacy ASMX/WCF clients, open SQL connections, read the registry or use Active Directory. WinForms apps usually reach their data through a client library (a "facade") that talks to services. The UI migration therefore stops at a typed mock service (`--contract`); a real application needs a REST API that does what the facade did. Requirements set for this track:
- REST API on **.NET 10**; legacy libraries are .NET Framework 4.8 with source, services are ASMX (source available) or third-party SOAP (WSDL only).
- Windows-specific calls are **ignored or bypassed** with a marker, never silently dropped.
- **Project independent**: generic engine + per-project data outside the repo.

## 3.2 Built: `migrator backend` (read-only analyzer)
```bash
dotnet run --project src/Migrator.Cli -- backend --source <dir> [--source <dir>...] [--wsdl <dir>...] --out <git-ignored dir> [--rules <file>]
```
Syntax-only (Roslyn); nothing is compiled or run. Output: `BACKEND_ANALYSIS.md` and `backend-analysis.json`. Source text, connection-string values and secrets are never copied: only names, counts and file/line locations.

| Section | What it tells you |
|---|---|
| Sources | Folders scanned: files, classes, `.asmx`, config files |
| Services | Classes deriving from a service base or carrying a service attribute (`WebService`, `ServiceContract`); each `[WebMethod]`/`[OperationContract]`: return type, parameters, statement count, classes called, flags for data access / Windows / host context / config |
| SOAP proxies | Classes deriving from `SoapHttpClientProtocol`-style bases: operations, `Url` assignments |
| Facades | Public classes: methods, **thin** (≤ 8 statements and calls a proxy) vs **logic**, data-access and Windows counts, proxies used |
| Findings | Per category — `windows`, `config`, `host-context`, `data-access` — pattern, count, classes |
| Static mutable state | Static non-readonly fields, static readonly collections, static properties with setters |
| Configuration keys | `appSettings` keys and connection-string **names** from `web.config`/`app.config` |
| WSDL | Per file: services, operations, endpoint hosts |

What counts as Windows-specific, config, host-bound or data access is **data**: `rules/backend-patterns.json` (`windowsNamespaces`, `windowsNames`, `windowsAttributes`, `configAccess`, `hostContext`, `dataAccessTypes`, base types and attributes that mark services/proxies/operations). Pass `--rules` with an extended copy; no rebuild.

## 3.3 What the first analysis showed (pilot data)
A .NET 4.8 client library (59 files) + two ASMX services + 13 third-party WSDLs: 320 classes, **2 services with 141 operations** (sampled operations are 1–3 statements that delegate to one logic class), **12 SOAP proxies** (2 for the own services, 10 for the third-party endpoints), **71 facade classes** (the main static facade has 137 methods of which 1 is a thin proxy call, so real logic lives in the library, not only in the services), 300 Windows-specific hits (almost all `System.Windows.Forms`/`System.Drawing` in dialogs and image code, plus Active Directory, registry, `Environment.UserName`, `Marshal`, two `DllImport`), only **12 static mutable members**, direct SQL in the library's own data classes.
Consequences: session state is small; the Windows UI code is simply left out of the API; Active Directory, registry and `UserName` need a per-project decision (bypass, or replace with the API's authenticated user).

## 3.4 Planned: .NET 10 REST API generator
Not built yet. Design, driven by the analysis:
| Concern | Approach |
|---|---|
| Hosting variant | **A (recommended)**: keep the ASMX services running; the API hosts the ported library facades and calls the services over SOAP (clients regenerated from WSDL). **B**: also port the services (only if the ASMX hosts are to be retired) |
| Endpoints | One REST resource per facade/service operation, typed DTOs from the legacy signatures; the TypeScript client replaces the generated mock service (same shapes as the `--contract` types) |
| Windows-specific code | Dropped from the API with `// BYPASSED` and a report entry (same marker policy as the UI side; list in `rules/backend-patterns.json` + `rules/windows-bypass.json`) |
| Static mutable state | Per-user/session state or a scoped DI service; never a process-wide static |
| Third-party SOAP | Clients generated from the WSDL files; endpoint URLs and credentials come from configuration at runtime, never from generated source |
| Secrets | Connection strings, keys, URLs live in the API's configuration/secret store; never in browser code or in the repo |
| Authentication | Decision pending (per service); default is a mock with `// MOCK — replace` |
| Per-project data | A profile file kept **with the project**, outside this repo: source paths, WSDL files, overrides, extra bypass rules. Defaults work with no profile |
| Engine shape | A target engine (`IProjectTargetEngine`) over the IR plus the analysis JSON, registered in `engines.json`; no change to the host |

Open decisions: variant A or B; authentication needs of the third-party services.
