# CLAUDE.md — WinForms Migration Engine (standalone, modular)

Project guidance for Claude (and any agent). Read this first, then `SKILLS.md` for control-by-control mappings.
Humans: `README.md` → `docs/USER_GUIDE.md` (run it) · `docs/PROJECT_DOCUMENTATION.md` (as-built architecture) · `docs/ENGINE_AUTHORING.md` (extend it) · `docs/PROJECT_PLAN.md` (status & roadmap).
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

### 0.5 Project independence (the engine knows no application)
- Engine code, `ui-spec`, the UI library, prompts and docs' rules must not contain names from a pilot or customer application (class, namespace, assembly, service, form or property names). Pilot names may appear only in `docs/PILOT_FINDINGS.md` and in examples labelled as pilot data.
- Behaviour comes from **structure** (base types such as `SoapHttpClientProtocol`, attributes such as `[WebMethod]`, static mutable state, API usage) or from **data**: the loaded contract, the IR, and an optional per-project profile/rules file that lives **with the project, outside this repo** (source paths, WSDL files, overrides, extra bypass rules). Defaults must work with no profile.
- Rule lists (Windows-specific calls to bypass, name hints) are data files that can be extended without a rebuild, like `llm-config`.
- Check before finishing a slice: `grep -rnE "<pilot names>" src ui-library` finds nothing outside comments; the pilot result is unchanged; when feasible, also run a second, different application.
- Rule lists (`rules/windows-bypass.json`, `rules/server-bound.json`, `rules/control-archetypes.json`, `ui-spec/fallbacks.json`) are data: extend a copy per project, never hard-code application names in them.
- Backend code (class libraries, ASMX/WCF services), WSDLs, URLs, credentials and connection strings of an application are **inputs**: read-only, kept in git-ignored folders, never committed, never emitted to browser code.

---

## 1. Mission

Convert legacy **.NET WinForms** applications (`*.Designer.cs` + code-behind `*.cs`, plus user-control libraries) into a **minimum working application** in a modern target:

| Target key | Output |
|---|---|
| `react-app` | React 18 + TypeScript + Vite, using `@wfm/ui-react` |
| `angular-app` | Angular 21 standalone components, router, zone.js, using `@wfm/ui-angular` (built; see `docs/guide/v0.4/09-angular.md`) |
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
│  ├─ Migrator.Target.Core/               # SHARED by every target: IR folder loading, ui-spec catalog, C# -> TypeScript translation, contract services, naming, layout, icons
│  ├─ Migrator.Target.React/              # TARGET ENGINE: IIrGenerator -> react-app
│  ├─ Migrator.Target.Angular/            # TARGET ENGINE: IProjectTargetEngine -> angular-app (standalone components, ngc gate)
│  ├─ Migrator.Target.Html/               # TARGET ENGINE: IIrGenerator -> html-jquery-app
│  └─ Migrator.Cli/                       # thin console entry point (args -> Engine)
├─ ui-library/                            # INDEPENDENT of engine + IR (own build, own versions)
│  ├─ ui-spec/                            # framework-neutral control contract (JSON) + design tokens
│  ├─ ui-core/                            # @wfm/ui-core: framework-free helpers and types shared by the ui-* packages (grid model, image list, tree nodes, list items, mnemonics)
│  ├─ ui-react/                           # @wfm/ui-react
│  ├─ ui-angular/                         # @wfm/ui-angular (consumed as TypeScript source through tsconfig paths)
│  └─ ui-html/                            # @wfm/ui-html (HTML/CSS/vanilla JS + jQuery adapter)
├─ tools/migrator-ui/                     # local web UI for running migrations (host = ASP.NET minimal API, web = React app on @wfm/ui-react); a tool, not a demo
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
Target.React / Target.Angular -> Target.Core -> Engine   # the framework-neutral part lives once, in Target.Core
Target.* -> ui-spec (read-only, as data)          # never imports ui-react/angular/html code
ui-library/*  -> nothing from src/ (zero knowledge of IR, WinForms, or the engine)
Source.* ✗ Target.*   Target.* ✗ Target.*   Source.* ✗ Source.*   (engines never reference each other)
```
Engines communicate **only through `PageIr` / `ProjectIr`**.

> **As built:** the contracts live in `Migrator.Engine` (§3), so `Source.*` / `Target.*` reference `Engine` **for contracts only** — the arrow
> "Engine -> Source/Target" above is logical (the host loads them by name from `engines.json`), not a project reference. `Migrator.Engine`
> references no engine. `Migrator.Cli` is the composition root: it references the built-in engines only so they land in the app directory.

### 2.2 Shared IR (reference, never copy)
Consume `WebFormsMigrator.IR` (models) and `WebFormsMigrator.IR.Contracts` (`IIrExtractor`, `IIrGenerator`, `IrPipelineRegistry`, `LlmRegistry`, `ILlmClient`, `PromptTemplate`) as **NuGet packages** (preferred; local `dotnet pack` feed until published), a git submodule, or — **the current interim** — a `ProjectReference` to a sibling checkout of the WebFormsMigrator repo. Do not fork or duplicate them.

**How it is wired:** `build/IrReference.props` is the *only* place that decides this; projects `<Import>` it. Default = `ProjectReference` to `..\..\MIgration Engine 2` (override with `-p:WebFormsMigratorRoot=<path>`); once packages exist, `-p:UseSharedIrPackages=true -p:SharedIrVersion=<x.y.z>`. No C# changes either way (same namespaces).

**This repo never edits the WebFormsMigrator repo.** If WinForms needs something the shared IR does not model, adapt on our side (see §4.2: `WinNavigation`, attribute conventions) or, only if truly required, make an additive change in the owning project and bump `IrSchema.Version`.

Real contract shapes: `IIrExtractor { string Facet; void Extract(IrExtractionContext, PageIr) }`, `IIrGenerator { string Target; void Generate(PageIr, string outputDirectory) }`, `IrExtractionContext { FilePath, MarkupContent, CodeBehindContent }` (for WinForms: Designer.cs text, code-behind text).

---

## 3. Extensibility model (engines)

An **engine** is a self-contained module of one of two kinds:

| Kind | Job | Examples (now / later) |
|---|---|---|
| **Source Engine** | Analyze a legacy technology → produce IR | WinForms (now) · WPF · VB6 · Delphi · Web Forms (existing, separate repo) |
| **Target Engine** | Consume IR → generate an app | react-app, angular-app, html-jquery-app · vue-app, blazor-app |

Contracts (implemented in `Migrator.Engine`; build on the real `IIrExtractor` / `IIrGenerator`):

```csharp
public interface IEngine { string Id { get; } Version Version { get; } Version SchemaVersion { get; } }   // identity every engine exposes
public interface IEngineModule { void Register(IEngineRegistry registry); }          // entry point of every engine/plugin; optional ctor (EngineOptions)
public interface IEngineRegistry { void RegisterSource(ISourceEngine e); void RegisterTarget(ITargetEngine e); }

public interface ISourceEngine : IEngine {
    string Technology { get; }                 // "WinForms"
    IProjectAnalyzer Analyzer { get; }         // Analyze(root) -> ProjectAnalysis { Forms, UserControls, StartupForm, FrameworkVersion }
    IEnumerable<IIrExtractor> Extractors { get; }   // ordered: tree -> members -> code wiring -> semantics -> workflows -> navigation
}

public interface ITargetEngine : IEngine {
    IIrGenerator Generator { get; }
    string UiLibraryPackage { get; }           // "@wfm/ui-react"
    IReadOnlyList<string> RequiredSpecVersion { get; }   // compatible ui-spec range
}

// Optional capabilities a source engine may implement (the host checks with `is`):
public interface IProjectAwareSourceEngine : ISourceEngine { IEnumerable<IIrExtractor> CreateExtractors(ProjectAnalysis a); } // needs whole-project knowledge (UserControl inlining)
public interface IIrProjectionProvider { IEnumerable<IIrProjection> Projections { get; } }   // technology-specific facet files (WinForms: layout)
public interface IUnsupportedReporter { IEnumerable<string> FindUnsupported(IrExtractionContext c); } // unhandled constructs -> project.ir.json Shared.Unsupported
public interface ISupportSourceEngine { SupportFileIr ExtractSupport(ProjectAnalysis a); }   // the project's own non-UI classes -> ir/support.ir.json (WinForms-owned sidecar)
public interface IResourceSourceEngine { ResourceFileIr ExtractResources(ProjectAnalysis a); }   // images of the project's Resources.resx the code references -> ir/resources.ir.json (WinForms-owned sidecar)
public interface IDependencySourceEngine { IReadOnlyList<ExternalAssemblyIr> FindExternalAssemblies(ProjectAnalysis a); }   // referenced binary assemblies -> project.ir.json Shared.ExternalAssemblies
public interface IProjectTargetEngine : ITargetEngine { GenerationResult GenerateProject(string irFolder, string outDir, GenerationOptions o); } // whole-app generation from the IR FOLDER only (never a source engine)
```
Host pieces: `EngineRegistry`, `EngineLoader` (reads `engines.json`), `SourceIrRunner` (per-unit failure isolation), `IrFileWriter` + `IIrProjection` (facet files), `WinNavigation` (see §4.2).

Rules:
1. `engines.json` is the **allow-list** of enabled engines. Built-in engines resolve from the app directory; others from `plugins/<assembly>.dll` or `plugins/<assembly>/<assembly>.dll` (own `AssemblyLoadContext`; host/contract assemblies are shared). A DLL in `plugins/` that is *not listed* is reported as a warning and **never executed**. **Adding an engine must not require editing `Migrator.Engine`.**
2. Pipeline order is declared by the source engine's extractor list (same as `IrPipelineRegistry.BuildIr()`).
3. Every engine exposes `Id`, `Version`, `SchemaVersion` it targets. The registry refuses an engine whose schema does not match: same major; while major is `0` the minor must match too (0.x is unstable); otherwise engine minor <= host minor.
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
| `{Form}.layout.ir.json` | Per-control layout facts: `Dock, Anchor[], Location, Size, TabIndex, Margin, Padding, Visible, Enabled, TableLayout {Row, Column, RowSpan, ColumnSpan}`; `TableDefinition {Columns[], Rows[]}` on a `TableLayoutPanel`. `null` = not set / not a literal | `html-css.ir.json` (WinForms has no HTML/CSS; layout is the equivalent facet) |
| `support.ir.json` | Project-level sidecar (not per unit): the app's own non-UI classes — static helper modules (`main`), data classes, structs, enums (nested ones flattened, `Container` set). Members reuse `MemberIr`; modifiers as `mod:const/auto/accessors/...` in `Attributes` | — |
| `resources.ir.json` | Project-level sidecar: the images of the project's `Resources.resx` that code references (`Properties.Resources.X`), `{Name, Format, Data}` with base64 file bytes; written only when there are any | — |
| `project.ir.json` | `ProjectIr` index: `Source`, `Forms[]`/`UserControls[]` (`{Name, Kind, IrFile}`), `Shared` (`Unsupported` filled from unhandled designer statements; `Settings`/`Resources`/`Dependencies` still empty), `Failures[]` | — |

Rules:
- **One extraction, many projections.** Facet files are *derived from* `complete.ir.json`, never separately re-extracted, so they cannot diverge.
- Output under `<output>/ir/{Form}/…` plus `<output>/ir/project.ir.json`.
- **No anonymous noise nodes.** The Web Forms IR emitted many `Id:""` raw-text fragment nodes; the WinForms IR must contain only real controls.
- Keep designer properties in `Attributes` (string → string, serialized values) before adding schema fields. Schema changes are **additive only** and bump `IrSchema.Version`.
- JSON is indented PascalCase, matching existing samples.
- `Semantic` labels come from `SemanticKeywords` (Id first, then `Text`; first match wins).

### 4.2 Conventions specific to WinForms IR
- `RootControl.Id` = class name; `RootControl.TagName` = base type (`System.Windows.Forms.Form` / `UserControl`).
- Event wiring: `Attributes["OnClick"] = "btnSave_Click"` (same key style as ASP.NET `OnClick`). Subscriptions in the code-behind (`btn.Click += handler`, `new EventHandler(m)`, lambdas) are recorded the same way plus the marker `Wired.Click = "code"`; lambdas become synthetic handler members.
- Base type is read from the code-behind class first, then designer, then defaults to `Form`.
- Root event wiring (`this.Load += ...`) is `RootControl.Attributes["OnLoad"]`; a custom base class stays as the root `TagName` (e.g. `BaseForm`).
- **Navigation** reuses the shared web-shaped `NavigationIr` without changing it. Always create/read through `Migrator.Engine.Navigation.WinNavigation`: `SourceControlId` = trigger control, `TriggerEvent` = event, `Target` = target form (empty for Close/Hide/Exit), `TargetType` = `Show | ShowDialog | Close | Hide | Exit`. The handler is the `WorkflowIr` with the same control + event (`FindWorkflow`). `Show/ShowDialog` count only on `new T()` or a local initialised with one (so `MessageBox.Show` is never navigation). If richer data is ever needed (DialogResult, ctor args), add a WinForms-owned sidecar file — do not change `NavigationIr`.
- Real IR enums: `MemberIr.Kind` = `MemberKind` (Property, Field, Method, Event), `Visibility` = Public/Protected/Private/Internal; `PageIr.RootControl` is nullable; `MemberIr.Attributes` lists attribute names.
- **Designer facts without a property of their own** are stored under dotted pseudo-attribute keys that cannot collide with C# property names: `ParentSlot` (Items, DropDownItems, TabPages, Columns, Panel1, Panel2), `TableLayout.Row/Column/RowSpan/ColumnSpan`, `ColumnStyles`/`RowStyles` (JSON), `Items` (JSON string array), `DataBinding.<Property>` (= `source.Member`), `ToolTip`, `DefaultCellStyle.*`, `Image.Data`/`Image.Format` (control images), `Images.Keys`/`Images.Data`/`Images.Size` (an ImageList's keys, one base64 BMP per image, cell size). `.resx` Text/ToolTip values are merged when not set in code (code wins). Full table: `docs/ENGINE_AUTHORING.md`.
- `this.X = new T()` declares a control **only** when `X` is a field of the class; otherwise it is a root property (`ClientSize`, `AutoScaleDimensions`).
- Instance **constructors** are recorded as a method member named `.ctor` (parameters + body); generators turn their parameters into page inputs (props). They are not translated as methods.
- For **fields and properties**, `MemberIr.Body` holds the initializer expression text (e.g. `"Add"`, `new List<string>()`); for methods it is the full body. Translation reads constants such as `c_NoFilter` from it.
- Designer statements the parser does not understand are listed in `project.ir.json` `Shared.Unsupported` (`"{Unit}: {statement}"`, capped at 50 per unit) — never silently dropped.
- Generators consume IR only. If something is missing from IR, **fix the extractor**, not the generator.

### 4.2a Archetype controls (target side, no IR change)
- A user control that code never opens (loaded dynamically or by permission logic) is classified by `ControlArchetypes` as Grid / Tree / Menu / Toolbar / List / Form: **designer content first, name patterns second** (`rules/control-archetypes.json`, data; generic words only, extend a copy per project with `--archetypes`).
- A missing primary control, or its columns / nodes / items, is inferred from the designer, then from literals in the code, then from the row type of the data source, else marked generic placeholders; inferred parts carry the target-side pseudo-attributes `Archetype.*`, are marked `// MOCK` where they are data, and are listed under "Archetypes" in `MIGRATION_REPORT.md`. The IR files never contain them.
- `--catalog` (implied by `--units all`) writes a `/modules` page in each target listing the units no code opens, by archetype. The loading / permission logic itself is not translated.

### 4.3 Known extractor limitations (carry into every report)
- Handlers in a third partial-class file aren't resolved.
- Only direct method calls become workflow steps (not 2+ levels deep); steps follow **call order** (implemented).
- A handler name inside an inlined UserControl that equals a form method name would be attributed to the form.
- `Controls.Add` order in designer code is reverse z-order, so child order is not visual order (generators order by TabIndex/Dock, Skill 4).
- Culture-specific `.resx` files and external `.resx` file references are not read; readable byte-array images become the pseudo-attributes `Image.Data` / `Image.Format` (the React target writes them to `public/icons`, converting ICO/BMP to PNG; unreadable ones get a placeholder icon); `ImageList` streams are decoded in managed code (1, 4, 8, 24 and 32 bits per pixel) into `Images.Data` / `Images.Keys` / `Images.Size` (other depths: placeholders), and the referenced images of the project's own `Resources.resx` become `resources.ir.json`; items added by helper methods/loops and layout computed at runtime are not seen.
- No pixel/visual fidelity (layout facts only).
- Binary-only inputs (EXE/DLL without source) are members-first and lossy; no event wiring unless wired with `+=` in code.

---

## 5. UI Library — independent of the engine

The controls live in a **separate, UI-independent library** under `ui-library/`. Generated apps depend on it; the engine only *references it by name* through `ui-spec`.

| Package | Role |
|---|---|
| `ui-spec` | Single contract: for each control — name, WinForms types it replaces, props, events, slots, a11y rules, example. JSON + design tokens (CSS variables). Versioned (semver). |
| `@wfm/ui-react` | React 18 + TS components implementing the spec |
| `@wfm/ui-angular` | Angular 21 standalone components implementing the spec (selectors `wf-*`, outputs `wf` + event name) |
| `@wfm/ui-html` | HTML/CSS + vanilla JS web-friendly components; jQuery adapter (`$.fn.wfDataGrid`, …) |

**As built:** `ui-spec` 0.3.0 (`controls.json` + `fallbacks.json` + `tokens.css`) and `@wfm/ui-react` 0.3.0 exist, with 41 spec entries (36 map WinForms controls; native HTML only, no third-party dependency) plus the dialog service (`dialog.show()` / `dialog.close()`), `useWfModel` and `WfUnsupported`; see `ui-library/README.md` and `docs/UI_CONTROL_MAPPING.md`. `@wfm/ui-core` 0.4.0 (framework-free helpers) and `@wfm/ui-angular` 0.4.0 exist too (React 0.4.0); `ui-html` is not started. Generators read `controls.json` as data (`winformsTypes`, `props[].from/transform`, `events[].from`).

Hard rules:
1. **Library-first generation.** Generated pages import `Wf*` components only. They never import AG Grid / DataTables / Material / jstree etc. directly — those are the library's private implementation detail (peer deps).
2. **Zero coupling.** The library has no reference to IR, WinForms, the engine, or generated apps. It must be installable and usable by hand-written apps.
3. **Same contract across frameworks.** Identical component names (`WfButton` / `<wf-button>` / `data-wf="button"`), identical prop names, identical canonical events (`click`, `valueChange`, `selectionChange`, `cellClick`, …). Framework idiom for binding: React `onValueChange`, Angular `(wfValueChange)`, HTML/jQuery `wf:valueChange` CustomEvent.
4. **Themeable** through CSS variables from `ui-spec` tokens; no hard-coded colors.
5. **Accessible by default** (labels, roles, keyboard, `accesskey` from `&` mnemonics).
6. **Versioned & backward compatible.** Spec changes are additive; breaking changes require a major bump and a migration note in `docs/`.
7. A control not in the library → the generator renders the **closest available library component** (`ui-spec/fallbacks.json`, data; default: a panel when it has children, else a label), marked `/* FALLBACK: <Type> */` and listed in the report. `WfUnsupported` + TODO remains only for controls that cannot even be given a fallback. Never raw third-party markup. To make a fallback a real control: add it to `ui-spec` and implement it in all three packages (Skill 18) and drop its fallback entry.
8. No tests, Storybook or demo app inside `ui-library/` — the separate demo project covers that.

The control catalog and WinForms → `Wf*` mapping is in **SKILLS.md (Skill 0)**.

---

## 6. Honesty & review markers (non-negotiable)

Small local models (e.g. phi4-mini ≈ 3.8B) are good at mechanical translation, unreliable on complex logic.

| Situation | Required marker |
|---|---|
| C# body translated by an LLM | `// LLM-translated — verify` (first line of the method) |
| Call to a type/API not resolvable from IR | `// TODO: wire to real API — call to {Type}.{Method} not resolvable from IR` |
| Control with no library component | Rendered by the closest library component with `/* FALLBACK: {Type} */` + report entry; `<WfUnsupported …/>` + `// TODO: no {Target} equivalent generated — replace manually` only when no fallback exists |
| Windows-specific API with no browser equivalent (registry, clipboard, directory services, `Process.Start`, ...) | `// BYPASSED — Windows-specific API ({api}) has no browser equivalent: {original}` (rules in `rules/windows-bypass.json`) + report section |
| Type inferred from columns/format | `// inferred — check against backend contract` |
| Mock auth / mock API / in-memory state | `// MOCK — replace with real implementation` |
| Member/method of an external contract that looks like a secret (password, key, license, connection string, credentials) | Omitted from generated services, or a credentials-object parameter is stripped and the operation kept; listed in `MIGRATION_REPORT.md` (SERVER-ONLY). Never in browser code |

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
- **Code-behind (React target):** every form/user control is generated as two files like the WinForms original: `XPage.tsx` (markup only, the Designer) and `XPage.code.tsx` (code-behind: hook `useXPageCode` with model, handlers, translated methods, and the exported `wiring` table `{ control, event, handler, method, source }`). The compile gate maps errors to methods through the code-behind file; the launcher/viewer list markup files only.

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
dotnet run --project src/Migrator.Cli -- engines [--config engines.json]       # EXISTS: list engines that load
dotnet run --project src/Migrator.Cli -- ir --source <path> --out <dir>      # EXISTS: IR only -> six files per unit + project.ir.json
dotnet run --project src/Migrator.Cli -- migrate --source <path> --out <dir> [--target react-app] [--entry <form>] [--depth <n|all>] [--contract <contract.json>]... [--translate none|rules|llm] [--images <folder>] [--compile-gate]   # EXISTS: ir step + app into <out>/react-app; rules = deterministic C#->TS (llm tier planned)
dotnet run --project src/Migrator.Cli -- contract --assembly <dll> --out <dir>   # EXISTS: public API of a managed assembly from metadata only (never executed); use a git-ignored folder
dotnet run --project src/Migrator.Cli -- backend --source <dir>... [--wsdl <dir>...] --out <dir>   # EXISTS: read-only survey of backend code (class libraries, ASMX) -> BACKEND_ANALYSIS.md; rules in rules/backend-patterns.json; git-ignored --out
dotnet run --project src/Migrator.Cli -- migrate --source <path> --out <dir> --units all [--catalog] [--archetypes <rules.json>]   # EXISTS: also units no code opens; classifies grid/tree/menu/toolbar/list controls and writes the /modules catalog (react-app and angular-app)
dotnet run --project src/Migrator.Cli -- migrate --source <path> --out <dir> --preview   # EXISTS: component viewer -> <out>/react-viewer: a visual-only page per form and user control + a sidebar shell (no handlers, data or logic)
dotnet run --project src/Migrator.Cli -- migrate --source <path> --out <dir> --appshell  # EXISTS: WinForms-style app shell -> <out>/react-appshell: bordered host window, hyperlink navigation on the left, the selected unit in a form-style frame on the right (visual-only pages, same as --preview)
pwsh tools/migrator-ui/start.ps1                                              # EXISTS: the Migrator UI (http://127.0.0.1:5280): source/output pickers, mode (IR / app / app shell / viewer), options, run + log + report, install/start the generated app
set WFM_LLM_TARGET=react-app                                                   # opt-in LLM translation
cd ui-library/ui-core && npm ci && npm run build && npm run lint   # first: ui-react and generated apps import it
cd ../ui-react && npm ci && npm run build && npm run lint
```
(Adjust CLI verbs to what exists; if a command doesn't exist yet, add it in `Migrator.Cli` as a thin wrapper over the Engine.)

## 11. Extending
- **New source tech** (WPF, VB6…): new `Migrator.Source.X` project + register + Skill 18 section.
- **New target** (Vue, Blazor…): new `Migrator.Target.X` + new `ui-library/ui-x` package implementing `ui-spec` + Skill 18 section.
- **New control:** add to `ui-spec` → implement in every ui-library package → add a row to SKILLS.md Skill 0 → generators pick it up through the spec (no generator code change for simple controls).
- **New semantic keyword:** `SemanticKeywords` (first match wins); long-term externalize to JSON.

## 12. Non-goals
Pixel-perfect reproduction · browser-side DB access · production-grade auth · MAUI/mobile targets · automatic translation of P/Invoke/COM/GDI+ painting · test projects · demo/showcase apps (separate project) · pushing or merging git changes.
