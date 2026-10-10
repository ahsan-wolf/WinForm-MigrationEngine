# CLAUDE.md — WinForms Migration Engine (standalone, modular)

Project guidance for Claude (and any agent). Read this first, then `SKILLS.md` for control-by-control mappings.
This repo is a **separate project** from `WebFormsMigrator`. It reuses the same IR schema and contracts, but owns its own solution, engines, and UI library.

---

## 0. Non-negotiable operating rules

### 0.1 Autonomy — do not ask permission to read
- Reading is **pre-authorized**: list directories, open files, search the repo, read project docs and IR files **without asking**.
- Do not ask "may I read X?" or "should I look at the folder?". Just do it.
- Read each file **once** per task and keep what you learned. Re-read only if the file changed since you read it.
- Ask the user only for: (a) destructive or irreversible actions (deleting files/branches, force operations), (b) genuinely ambiguous requirements that would cause wasted work, (c) pushing/publishing (see 0.2), (d) installing global tools or touching anything outside the repo.

### 0.2 Git workflow — always branch, never push
1. **Before changing anything**, create a new branch from the latest default branch:
   `git fetch origin` (skip if offline) → `git switch -c <type>/<short-kebab-description> origin/<default>`
   Types: `feature/`, `fix/`, `docs/`, `refactor/`, `chore/`. Never commit to `main`/`develop`.
2. Commit in small logical commits (`feat(ui-react): add WfDataGrid`, `fix(extractor): …`).
3. **Never** `git push`, merge, rebase shared branches, or force anything. Do **not** open PRs.
4. When the task is done, **stop and ask the user to push**, and print this block (fill in the branch name):

```bash
# 1) Push the branch (you run this)
git push -u origin <branch>

# 2) Later: fetch latest and update local
git fetch origin --prune
git switch main
git pull --ff-only origin main

# 3) Bring your branch up to date with main (pick one)
git switch <branch>
git merge origin/main          # or: git rebase origin/main

# 4) After the branch is merged, clean up
git switch main
git branch -d <branch>
```
5. End every task report with: **Branch**, **commits made**, **files changed (summary)**, **what to verify**, and the block above.

### 0.3 No test projects
- **Do not create unit-test, integration-test, Storybook, or demo projects** in this repo.
- A **separate demo project** will be created later to showcase generated apps and the UI library. Do not scaffold it, do not reference it.
- Verification = build + lint + smoke-run of generated output (see §9). No test frameworks added to any `package.json` / `.csproj`.

### 0.4 Modularity
- Everything is a module with one responsibility and an explicit contract. No module reaches into another module's internals.
- Add capabilities by **adding** a module and registering it — not by editing the host (open/closed).
- Never copy shared IR models into this repo; reference them (§2.2).

---

## 1. Mission

Convert legacy **.NET WinForms** applications (`*.Designer.cs` + code-behind `*.cs`, plus user-control libraries) into a **minimum working application** in a modern target:

| Target key | Output |
|---|---|
| `react-app` | React 18 + TypeScript + Vite, using `@wfm/ui-react` |
| `angular-app` | Angular 17+ standalone, signals, reactive forms, using `@wfm/ui-angular` |
| `html-jquery-app` | Static HTML5 + jQuery 3, using `@wfm/ui-html` |
| *(extensible)* | Vue, Blazor, … add a Target Engine + a ui-library package |

"Minimum working" = builds/runs; controls wired to real state and real (translated) handlers; navigation routes; grids/forms bound to real (mocked) data. It does **not** mean translated business logic is guaranteed correct (see §6).

The migration mirrors the Web Forms migrator: analyze once → serialize IR → generate from IR.

---

## 2. Repository layout (separate project)

```
WinFormsMigrator/                         # standalone repo + solution (NOT inside WebFormsMigrator)
├─ WinFormsMigrator.sln
├─ src/
│  ├─ Migrator.Engine/                    # HOST: pipeline, engine registry, plugin loader, config, CLI services
│  ├─ Migrator.Source.WinForms/           # SOURCE ENGINE: project analyzer, designer parser, extractors
│  ├─ Migrator.Target.React/              # TARGET ENGINE: IIrGenerator -> react-app
│  ├─ Migrator.Target.Angular/            # TARGET ENGINE: IIrGenerator -> angular-app
│  ├─ Migrator.Target.Html/               # TARGET ENGINE: IIrGenerator -> html-jquery-app
│  └─ Migrator.Cli/                       # thin console entry point (args -> Engine)
├─ ui-library/                            # INDEPENDENT of engine + IR (own build, own versions)
│  ├─ ui-spec/                            # framework-neutral control contract (JSON) + design tokens
│  ├─ ui-react/                           # @wfm/ui-react
│  ├─ ui-angular/                         # @wfm/ui-angular
│  └─ ui-html/                            # @wfm/ui-html (HTML/CSS/vanilla JS + jQuery adapter)
├─ llm-config/                            # *.model.json / *.prompt.json (runtime-extensible)
├─ plugins/                               # drop-in engines (assemblies implementing IEngineModule)
├─ engines.json                           # which engines are enabled
├─ docs/                                  # IR schema docs, engine authoring guide
├─ CLAUDE.md
└─ SKILLS.md
```

### 2.1 Dependency direction (enforced)
```
Cli -> Engine -> (Source.* , Target.*) -> IR + IR.Contracts
Target.* -> ui-spec (read-only, as data)          # never imports ui-react/angular/html code
ui-library/*  -> nothing from src/ (zero knowledge of IR, WinForms, or the engine)
Source.* ✗ Target.*   Target.* ✗ Target.*   Source.* ✗ Source.*   (engines never reference each other)
```
Engines communicate **only through `PageIr` / `ProjectIr`**.

### 2.2 Shared IR (reference, never copy)
Consume `WebFormsMigrator.IR` (models) and `WebFormsMigrator.IR.Contracts` (`IIrExtractor`, `IIrGenerator`, `IrPipelineRegistry`, `LlmRegistry`, `ILlmClient`, `PromptTemplate`) as **NuGet packages** (preferred; local `dotnet pack` feed until published) or a git submodule. Do not fork or duplicate them. If a model change is needed, make it additive in the owning project and bump `IrSchema.Version`.

---

## 3. Extensibility model (engines)

An **engine** is a self-contained module of one of two kinds:

| Kind | Job | Examples (now / later) |
|---|---|---|
| **Source Engine** | Analyze a legacy technology → produce IR | WinForms (now) · WPF · VB6 · Delphi · Web Forms (existing, separate repo) |
| **Target Engine** | Consume IR → generate an app | react-app, angular-app, html-jquery-app · vue-app, blazor-app |

Proposed contracts (live in `Migrator.Engine`; build on the existing `IIrExtractor` / `IIrGenerator`):

```csharp
public interface IEngineModule { void Register(IEngineRegistry registry); }          // entry point of every engine/plugin

public interface ISourceEngine {
    string Id { get; }                         // "winforms"
    string Technology { get; }                 // "WinForms"
    IProjectAnalyzer Analyzer { get; }         // finds Forms/UserControls, startup form, deps
    IEnumerable<IIrExtractor> Extractors { get; }   // ordered: tree -> members -> semantics -> workflows/navigation
}

public interface ITargetEngine {
    string Id { get; }                         // "react-app"
    IIrGenerator Generator { get; }
    string UiLibraryPackage { get; }           // "@wfm/ui-react"
    IReadOnlyList<string> RequiredSpecVersion { get; }   // compatible ui-spec range
}
```

Rules:
1. `engines.json` lists enabled engines; the host loads built-in engines plus any assembly in `plugins/` that exposes an `IEngineModule`. **Adding an engine must not require editing `Migrator.Engine`.**
2. Pipeline order is declared by the source engine's extractor list (same as `IrPipelineRegistry.BuildIr()`).
3. Every engine exposes `Id`, `Version`, `SchemaVersion` it targets. The host refuses to run an engine whose schema range doesn't match.
4. Engines hold no global state; configuration arrives via constructor options (bound from `engines.json`).
5. Failure isolation: an engine failure on one Form is logged and reported; it never aborts the whole run.
6. New engine checklist: new project under `src/` → implement contract → register in `engines.json` → add its mapping section to `SKILLS.md` (Skill 18) → add `llm-config` prompt if LLM-assisted. **Nothing else changes.**

---

## 4. IR — complete hierarchy (same model as the Web Forms IR)

The WinForms IR is the **same schema** as `complete.ir.json` (SchemaVersion `0.1.0`), plus a project-level index. Hierarchy:

```
ProjectIr  (project.ir.json)                   # index/wrapper — additive, optional for consumers
├─ Source: { Technology: "WinForms", FrameworkVersion, RootPath, StartupForm }
├─ Forms[]         -> { Name, Kind: "Form",        IrFile }
├─ UserControls[]  -> { Name, Kind: "UserControl", IrFile }
├─ Shared: { Settings, Resources, Dependencies[], Unsupported[] }
└─ (each entry points to one PageIr)

PageIr  (one per Form/UserControl)             # == complete.ir.json shape
├─ SchemaVersion, Name, MasterPageName = null
├─ RootControl : ControlNodeIr                 # recursive control tree
│   ├─ Id, TagName (full .NET type), Attributes{ props…, OnEvent -> handler }, Semantic, RawContent(null)
│   └─ Children[] : ControlNodeIr              # real parent/child via Controls.Add; UserControls inlined
├─ Members[] : MemberIr                        # Kind 0 = field/property, Kind 2 = method
│   └─ Name, Type, Visibility, IsStatic, Parameters[] : ParameterIr, Body (full C# for methods)
├─ CssAssets[] / JsAssets[]                    # empty for WinForms (kept for schema parity)
├─ Navigations[] : NavigationIr                # TriggerControlId, TargetForm, Kind (Show|ShowDialog|Close|Hide|Exit)
└─ Workflows[]  : WorkflowIr                   # event -> handler -> Steps[{MethodName, Semantic}] + Semantic
```

### 4.1 IR files emitted per Form/UserControl (mirrors the 5 web IR files)
| File | Contents | Web analogue |
|---|---|---|
| `{Form}.complete.ir.json` | Full `PageIr` — the **source of truth** | `complete.ir.json` |
| `{Form}.controls.ir.json` | Control tree only: `Name, Text, ControlType, Children` | `controls.ir.json` |
| `{Form}.control-properties.ir.json` | Flat list: `Id, TagName, Attributes` (all props + event wiring) | `control-properties.ir.json` |
| `{Form}.code-behind.ir.json` | `Name` + `Members[]` (bodies included) | `code-behind.ir.json` |
| `{Form}.layout.ir.json` | Per-control layout facts: `Dock, Anchor, Location, Size, TabIndex, Margin, Padding, Visible, Enabled, TableLayout cell/span` | `html-css.ir.json` (WinForms has no HTML/CSS; layout is the equivalent facet) |
| `project.ir.json` | `ProjectIr` index | — |

Rules:
- **One extraction, many projections.** Facet files are *derived from* `complete.ir.json`, never separately re-extracted, so they cannot diverge.
- Output under `<output>/ir/{Form}/…` plus `<output>/ir/project.ir.json`.
- **No anonymous noise nodes.** The Web Forms IR emitted many `Id:""` raw-text fragment nodes; the WinForms IR must contain only real controls.
- Keep designer properties in `Attributes` (string → string, serialized values) before adding schema fields. Schema changes are **additive only** and bump `IrSchema.Version`.
- JSON is indented PascalCase, matching existing samples.
- `Semantic` labels come from `SemanticKeywords` (Id first, then `Text`; first match wins).

### 4.2 Conventions specific to WinForms IR
- `RootControl.Id` = class name; `RootControl.TagName` = base type (`System.Windows.Forms.Form` / `UserControl`).
- Event wiring: `Attributes["OnClick"] = "btnSave_Click"` (same key style as ASP.NET `OnClick`).
- Base type is read from the code-behind class first, then designer, then defaults to `Form`.
- Generators consume IR only. If something is missing from IR, **fix the extractor**, not the generator.

### 4.3 Known extractor limitations (carry into every report)
- Handlers in a third partial-class file aren't resolved.
- Only direct method calls become workflow steps (not 2+ levels deep); step order should follow call order — if the extractor orders by declaration, fix it.
- No pixel/visual fidelity (layout facts only).
- Binary-only inputs (EXE/DLL without source) are members-first and lossy; no event wiring unless wired with `+=` in code.

---

## 5. UI Library — independent of the engine

The controls live in a **separate, UI-independent library** under `ui-library/`. Generated apps depend on it; the engine only *references it by name* through `ui-spec`.

| Package | Role |
|---|---|
| `ui-spec` | Single contract: for each control — name, WinForms types it replaces, props, events, slots, a11y rules, example. JSON + design tokens (CSS variables). Versioned (semver). |
| `@wfm/ui-react` | React 18 + TS components implementing the spec |
| `@wfm/ui-angular` | Angular 17+ standalone components/directives implementing the spec |
| `@wfm/ui-html` | HTML/CSS + vanilla JS web-friendly components; jQuery adapter (`$.fn.wfDataGrid`, …) |

Hard rules:
1. **Library-first generation.** Generated pages import `Wf*` components only. They never import AG Grid / DataTables / Material / jstree etc. directly — those are the library's private implementation detail (peer deps).
2. **Zero coupling.** The library has no reference to IR, WinForms, the engine, or generated apps. It must be installable and usable by hand-written apps.
3. **Same contract across frameworks.** Identical component names (`WfButton` / `<wf-button>` / `data-wf="button"`), identical prop names, identical canonical events (`click`, `valueChange`, `selectionChange`, `cellClick`, …). Framework idiom for binding: React `onValueChange`, Angular `(wfValueChange)`, HTML/jQuery `wf:valueChange` CustomEvent.
4. **Themeable** through CSS variables from `ui-spec` tokens; no hard-coded colors.
5. **Accessible by default** (labels, roles, keyboard, `accesskey` from `&` mnemonics).
6. **Versioned & backward compatible.** Spec changes are additive; breaking changes require a major bump and a migration note in `docs/`.
7. A control not in the library → generator emits `WfUnsupported` + TODO (never raw third-party markup). Then add the control to `ui-spec` and implement it in all three packages (Skill 18).
8. No tests, Storybook or demo app inside `ui-library/` — the separate demo project covers that.

The control catalog and WinForms → `Wf*` mapping is in **SKILLS.md (Skill 0)**.

---

## 6. Honesty & review markers (non-negotiable)

Small local models (e.g. phi4-mini ≈ 3.8B) are good at mechanical translation, unreliable on complex logic.

| Situation | Required marker |
|---|---|
| C# body translated by an LLM | `// LLM-translated — verify` (first line of the method) |
| Call to a type/API not resolvable from IR | `// TODO: wire to real API — call to {Type}.{Method} not resolvable from IR` |
| Control/feature with no library component | `<WfUnsupported …/>` + `// TODO: no {Target} equivalent generated — replace manually` |
| Type inferred from columns/format | `// inferred — check against backend contract` |
| Mock auth / mock API / in-memory state | `// MOCK — replace with real implementation` |

Never silently drop code: untranslatable code stays as a commented original C# block + TODO + entry in `MIGRATION_REPORT.md`.

---

## 7. LLM usage rules
- **Deterministic first**: markup, routing, models, services, library wiring come from templates (SKILLS.md Skill 16). Use the LLM only for C# method body → TS/JS and ambiguous naming.
- **One file per LLM call** for small models; use `/api/chat` with a system-role instruction rather than raw `/api/generate`; temperature 0–0.2; HTTP timeout ≥ 10 min for local CPU inference.
- Output format: `===FILE: path===` … `===END FILE===`. If no blocks parse, write the raw response to `{formName}.generated.txt` (never drop output).
- LLM generation is **opt-in** (`WFM_LLM_TARGET`), per-form failures are caught and logged, and never fail the migration.
- Models/prompts are runtime-extensible via `llm-config/*.model.json|*.prompt.json` (`LlmRegistry.LoadFromDirectory`) — no rebuild.

---

## 8. Conventions
- **Naming:** strip Hungarian prefixes (`btn, txt, lbl, cmb, chk, rdo, dgv, lst, tvw, lvw, pnl, grp, tab, dtp, nud, pic, mnu, tsm, tss, tmr`); `btnSave` → handler `onSaveClick` (React `handleSaveClick`); `txtFirstName` → state `firstName`.
- **Types:** `int/decimal/double/float → number`, `string → string`, `bool → boolean`, `DateTime → string (ISO) / Date`, `Guid → string`, `List<T> → T[]`, `DataTable → Row[]`, `object → unknown`.
- **Code style:** TypeScript strict, no `any` without TODO; React function components + hooks; Angular standalone + `signal()` + typed reactive forms + `inject()`; jQuery IIFE module per page.
- **Layout:** never copy `Location`/`Size`; derive flex/grid from tree + Dock/Anchor/TabIndex (Skill 4).
- **Semantic labels** drive button variant, icons and comments.

---

## 9. Workflow for any task
1. **Branch** (§0.2). 2. **Read** what you need (no permission requests). 3. **Plan** briefly (modules touched, files, risks).
4. **Implement** inside the right module only. 5. **Verify**: `dotnet build` for engines; `npm run build` + `npm run lint` for ui-library and generated apps; smoke-run a generated app. 6. **Report** (§0.2 block).

Migration run steps: Analyze (find Designer/code-behind pairs, UserControls first) → Extract IR (full hierarchy, §4) → Plan (forms, components, services, routes, unsupported) → Generate deterministically against `ui-spec` → Translate logic (markers) → Wire (routing, dialogs, mock API, mock auth) → Verify → Write `MIGRATION_REPORT.md`.

### Definition of Done (per Form)
- [ ] All six IR files emitted; facets consistent with `complete.ir.json`; no anonymous nodes.
- [ ] Generated code imports **only** `Wf*` components from the ui-library package.
- [ ] Builds with zero TS errors; every visible control rendered; every wired event has a handler.
- [ ] Every `NavigationIr` entry is a working route/dialog.
- [ ] Grids/lists render typed mock data.
- [ ] Every LLM/inferred/mock/unsupported item carries its marker and is listed in the report.
- [ ] No Hungarian identifiers; no absolute pixel layout.
- [ ] No test/demo/Storybook projects added.

---

## 10. Commands (reference)
```bash
dotnet build WinFormsMigrator.sln
dotnet run --project src/Migrator.Cli -- migrate --source <path> --target react-app --out <dir>
dotnet run --project src/Migrator.Cli -- ir --source <path> --out <dir>      # IR only, no generation
set WFM_LLM_TARGET=react-app                                                   # opt-in LLM translation
cd ui-library/ui-react && npm ci && npm run build && npm run lint
```
(Adjust CLI verbs to what exists; if a command doesn't exist yet, add it in `Migrator.Cli` as a thin wrapper over the Engine.)

## 11. Extending
- **New source tech** (WPF, VB6…): new `Migrator.Source.X` project + register + Skill 18 section.
- **New target** (Vue, Blazor…): new `Migrator.Target.X` + new `ui-library/ui-x` package implementing `ui-spec` + Skill 18 section.
- **New control:** add to `ui-spec` → implement in every ui-library package → add a row to SKILLS.md Skill 0 → generators pick it up through the spec (no generator code change for simple controls).
- **New semantic keyword:** `SemanticKeywords` (first match wins); long-term externalize to JSON.

## 12. Non-goals
Pixel-perfect reproduction · browser-side DB access · production-grade auth · MAUI/mobile targets · automatic translation of P/Invoke/COM/GDI+ painting · test projects · demo/showcase apps (separate project) · pushing or merging git changes.
