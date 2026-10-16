# 5. Project independence

The engine knows **no application**. The same build migrated two very different applications (a single-project client with a service library; a 17-project suite with custom control libraries) and a backend of ASMX services, with no application-specific code.

## 5.1 The rule
Engine code, `ui-spec`, the UI library, prompts, rule files and the rules in docs contain no class, namespace, assembly, service, form or property name of a pilot or customer application. Pilot names may appear only in `docs/PILOT_FINDINGS.md` and in examples labelled as pilot data.

## 5.2 Where behaviour comes from instead
| Source of behaviour | Examples |
|---|---|
| **Structure** of the code | Base types (`Form`, `UserControl`, `SoapHttpClientProtocol`), attributes (`[WebMethod]`), static mutable state, API usage (`SqlConnection`, `Registry`), base-type chains for custom controls, `ProjectReference`s |
| **Data in the repo** (generic, extendable without a rebuild) | `ui-spec/controls.json` (control contract), `ui-spec/fallbacks.json` (stand-ins), `rules/windows-bypass.json`, `rules/server-bound.json`, `rules/backend-patterns.json` |
| **Data of the project** (inputs; outside the repo) | The IR, the loaded assembly contract, WSDL files, an optional per-project profile/rules copy (extra bypass rules, overrides, source paths) |

Defaults work with no profile.

## 5.3 Inputs are never committed or emitted
Backend source, DLLs, WSDLs, URLs, credentials and connection strings are read-only inputs in git-ignored folders. The analyzer records names, counts and locations, never values. Members that look like secrets are omitted from generated services and listed as SERVER-ONLY. Nothing of this reaches browser code.

## 5.4 Onboarding a new application (checklist)
1. Put source (and DLLs/WSDLs) under `local-inputs/` or a folder outside the repo. Outputs go outside the repo.
2. Run `ir`; fix extractor gaps generically if any construct is unsupported (never by name).
3. Run `--appshell`; look at every unit; note fallback-rendered controls.
4. If controls fall back too often, extend `fallbacks.json` in a copy for the project, or add a real component for the type (generic, benefits every project).
5. If translation is blocked by Windows-only or server-only code, extend the rule files in the project's copy; promote a rule to the repo only if it is generic.
6. Run the backend analyzer if there is a service layer; keep its output in `local-outputs/`.
7. Run the full migration with the compile gate; read the report.
8. Before finishing: `grep -rnE "<names from this application>" src ui-library rules docs` finds nothing outside `PILOT_FINDINGS.md`; first pilot's numbers unchanged; ideally also run a second, different application.

## 5.5 What stays per project
Which Windows calls to bypass beyond the defaults; which classes are server-bound; stand-ins preferred for specific controls; authentication choice for services; endpoint URLs and credentials at runtime; which screens matter (`--entry`, `--depth`, `--units`).

## 5.6 What evidence exists
Pilot A numbers stayed identical while pilot B drove the generic fixes (custom control resolution, inheritance merge, fallbacks, bypass, referenced projects); the backend analyzer was run on a third kind of input (ASMX + WSDL) with the same rules file format. Details: `../../PILOT_FINDINGS.md`.
