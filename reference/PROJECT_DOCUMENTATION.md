# WebFormsMigrator — Project Documentation

Generated: 2026-08-02
Branch at time of writing: `feature/ir-navigation-workflow-semantics`

---

## 1. Current Purpose

`WebFormsMigrator` is a migration engine that converts legacy **ASP.NET Web
Forms** applications (`.aspx` / `.ascx` / `.master` + code-behind) into
modern **Blazor Server** applications targeting **.NET 8**.

Beyond straight code generation, the project is evolving a second purpose:
producing a **platform-neutral Intermediate Representation (IR)** of a Web
Forms application — its control tree, members, assets, navigation, and
workflows — so that:

- analysis (the expensive, fragile part) runs once and can be reused,
- the resulting structure can be inspected/visualized without running a full
  migration,
- other consumers (a visualizer, an IDE extension, a different code
  generator/target framework) can consume the IR instead of re-parsing
  source, and
- generated Blazor output can eventually be produced *from* the IR rather
  than directly from raw markup.

In short: today it is a **Web Forms → Blazor code generator**; it is being
extended into an **analysis platform** where Blazor generation is just one
consumer of a reusable IR.

---

## 2. Architecture

### 2.1 Solution structure

The `.sln` has four projects:

| Project | Type | Role |
|---|---|---|
| `WebFormsMigrator` | Console app (net8.0) | The original migration engine: parses Web Forms source and generates a Blazor Server project. Contains the new IR extractors as well. |
| `WebFormsMigrator.IR` | Class library | Defines the framework-neutral IR data model (`PageIr`, `ControlNodeIr`, `MemberIr`, `NavigationIr`, `WorkflowIr`, etc). |
| `WebFormsMigrator.IR.Contracts` | Class library | Extensibility contracts: `IIrExtractor`, `IIrGenerator`, `IrPipelineRegistry`. Defines *how* extractors/generators plug into the pipeline without the pipeline needing to change. |
| `WebFormsMigrator.IR.Api` | ASP.NET Core minimal API (scaffold) | Intended read-only HTTP API over analysis results. Currently a placeholder (`/` returns a scaffold message; no real endpoints yet). |

### 2.2 Migration engine (existing, working pipeline)

```
Program.cs
   -> MigrationEngine.MigrateAsync()
        -> ProjectAnalyzer          (finds .aspx/.ascx/.master/Web.config)
        -> AspxParser               (markup -> control tree, code blocks, content sections)
        -> CodeBehindAnalyzer       (regex-based extraction of handlers/props/fields)
        -> ProjectFileGenerator     (Blazor .csproj)
        -> ProgramFileGenerator     (Blazor Program.cs)
        -> UserContextGenerator     (IUserContext / UserContext service)
        -> LayoutGenerator          (Master pages -> MainLayout.razor / PageShell.razor)
        -> ComponentGenerator       (.ascx -> .razor components)
        -> PageGenerator            (.aspx -> .razor pages with @page routes)
        -> ControlConverter         (asp:* controls -> HTML/Blazor markup, used by the generators above)
        -> MigrationReportGenerator (MIGRATION_REPORT.md)
```

Core data models live in `MigrationModels.cs` (`ProjectAnalysis`,
`WebFormsPage`, `UserControl`, `MasterPage`, `ServerControl`,
`ControlTreeNode`, `WebFormsEventHandler`, ...). This is a transient,
in-memory object graph — it is never persisted, which is the gap the IR
initiative (below) is closing.

### 2.3 IR platform (new, in progress)

The IR initiative (see `docs/ir-platform/SCOPE.md` and
`docs/ir-platform/PLAN.md`) introduces a serialized, versioned,
framework-neutral representation that sits between analysis and generation.

Extensibility model (`WebFormsMigrator.IR.Contracts`):

- **`IIrExtractor`** — contributes one facet of a `PageIr` (control tree,
  members, CSS/JS, navigation/workflow, ...) from raw source. Each concern is
  its own extractor so new extraction logic doesn't touch existing ones.
- **`IIrGenerator`** — consumes a finished `PageIr` and produces output for
  one target (Blazor today; React/other targets are explicitly designed for
  but not built).
- **`IrPipelineRegistry`** — holds the registered extractors/generators and
  drives `BuildIr()` / `GenerateAll()`. Adding a facet or target means
  registering a new implementation, not editing the pipeline.

Extractors implemented so far (in `WebFormsMigrator`, namespace
`WebFormsMigrator.Analyzers`):

- **`ControlTreeIrExtractor`** — maps `AspxParser`'s existing
  `ControlTreeNode` tree into `ControlNodeIr` (no new parsing logic; reuses
  `AspxParser.BuildControlTree`).
- **`RoslynMemberExtractor`** — Roslyn-based extraction of members
  (properties/fields/methods/events) from code-behind, replacing the
  original regex-based `CodeBehindAnalyzer` approach for the IR path.
- **`NavigationWorkflowExtractor`** — runs *after* the two extractors above.
  Adds:
  - **Semantic labels** on every server/user control, inferred from
    naming-keyword heuristics (e.g. an id containing "save" →
    "Persists form data (create or update)").
  - **Navigation edges** (`NavigationIr`): links (`NavigateUrl`),
    cross-page postbacks (`PostBackUrl`), and `Response.Redirect` /
    `Server.Transfer` calls found inside event handler bodies.
  - **Workflows** (`WorkflowIr`): for each control event attribute
    (`On*`) wired to a known handler method, walks the handler body to find
    which other methods it calls, producing an ordered step list plus an
    inferred one-line semantic summary of the whole workflow.

IR model (`WebFormsMigrator.IR.Models.IrModels.cs`):
`PageIr` (root), `ControlNodeIr`, `MemberIr` + `ParameterIr`, `CssAssetIr`,
`JsAssetIr`, `NavigationIr`, `WorkflowIr`, `IrSchema.Version` (currently
`0.1.0`).

### 2.4 Tools

- **`tools/ir-viewer/index.html`** — a static, dependency-light HTML/JS
  viewer that loads an IR JSON document (e.g.
  `sample-ActorManagement.ir.json`) and renders the control tree, members,
  navigation, and workflow data for visual inspection. This is the first
  (informal) consumer of the IR, ahead of the formal API.

---

## 3. Flow (End-to-End)

### 3.1 Blazor generation flow (production path today)

1. User runs the console app with a source path (Web Forms project or
   `.csproj`) and optional output path.
2. `ProjectAnalyzer` scans for `.aspx`, `.ascx`, `.master`, `Web.config`.
3. `AspxParser` parses each markup file into a control tree, content
   sections, and inline code blocks (`<% %>`, `<%= %>`).
4. `CodeBehindAnalyzer` extracts event handlers/properties/fields from the
   matching `.cs` file via regex.
5. Everything is assembled into a `ProjectAnalysis` graph.
6. The output folder structure is created; `ProjectFileGenerator` and
   `ProgramFileGenerator` scaffold the new Blazor project's `.csproj` /
   `Program.cs`.
7. `LayoutGenerator` converts master pages to `MainLayout.razor` /
   `PageShell.razor` (named `RenderFragment` slots for each
   `ContentPlaceHolder`).
8. `ComponentGenerator` converts `.ascx` user controls to `.razor`
   components with `[Parameter]` properties.
9. `PageGenerator` converts `.aspx` pages to routed `.razor` pages, using
   `ControlConverter` to translate individual `asp:*` controls to
   HTML/Blazor equivalents.
10. `UserContextGenerator` emits `IUserContext`/`UserContext` to carry
    forward session/user-identity concepts.
11. `MigrationReportGenerator` writes `MIGRATION_REPORT.md` summarizing what
    was converted and what still needs manual attention.

### 3.2 IR extraction flow (new path, feeding the viewer/API)

1. Raw markup + code-behind for a page/control is wrapped in an
   `IrExtractionContext` (file path, markup, code-behind text).
2. `IrPipelineRegistry.BuildIr()` runs each registered `IIrExtractor` in
   order against a fresh `PageIr`:
   - `ControlTreeIrExtractor` populates `RootControl`.
   - `RoslynMemberExtractor` populates `Members`.
   - `NavigationWorkflowExtractor` (must run last — depends on both of the
     above) populates control `Semantic` labels, `Navigations`, and
     `Workflows`.
3. The resulting `PageIr` can be serialized to JSON (see
   `tools/ir-viewer/sample-ActorManagement.ir.json` for the shape) and
   handed to any consumer — currently the static IR viewer HTML page.
4. (Planned, not yet built) `IrPipelineRegistry.GenerateAll()` would let a
   registered `IIrGenerator` (e.g. a Blazor generator) produce output
   directly from the `PageIr` instead of from the live `ProjectAnalysis`
   graph, and the `WebFormsMigrator.IR.Api` project would expose the IR
   over HTTP.

---

## 4. Main Features

- Converts `.aspx` pages → routed `.razor` pages.
- Converts `.master` pages → Blazor layout components with named
  `RenderFragment` slots.
- Converts `.ascx` user controls → reusable `.razor` components.
- Maps common ASP.NET server controls (`Button`, `TextBox`, `DropDownList`,
  `GridView`, `Repeater`, `CheckBox`, `LinkButton`, `FileUpload`, etc.) to
  native HTML + Blazor bindings/events.
- Converts inline code blocks (`<% %>`, `<%= %>`) to Razor syntax.
- Extracts `Web.config` settings toward `appsettings.json`.
- Generates `IUserContext`/`UserContext` as a Blazor-appropriate
  replacement for Web Forms session/identity access patterns.
- Produces a detailed `MIGRATION_REPORT.md` (summary, mapping table, file
  structure, manual task checklist, quality checklist).
- **(New)** Extracts a framework-neutral IR of a page: control tree,
  members (via Roslyn), CSS/JS assets *(see limitations)*, navigation
  actions, and inferred workflows with plain-language semantic summaries.
- **(New)** A standalone HTML IR viewer (`tools/ir-viewer/`) for inspecting
  an IR JSON document's control tree, members, navigation, and workflows.

---

## 5. Completed Features

| Feature | Status | Notes |
|---|---|---|
| Web Forms project analysis (`ProjectAnalyzer`) | ✅ Done | Detects pages, controls, master pages, config. |
| Markup parsing to control tree (`AspxParser`) | ✅ Done | Handles nested structures, templates, placeholders, code blocks. |
| Regex-based code-behind analysis (`CodeBehindAnalyzer`) | ✅ Done | Legacy path still used by the main Blazor generation pipeline. |
| Server control → Blazor mapping (`ControlConverter`) | ✅ Done | Covers the common control set listed in `MIGRATION_PATTERNS.md`. |
| Master page → layout conversion (`LayoutGenerator`) | ✅ Done | |
| User control → component conversion (`ComponentGenerator`) | ✅ Done | |
| Page → routed page conversion (`PageGenerator`) | ✅ Done | |
| Project/startup file generation | ✅ Done | `ProjectFileGenerator`, `ProgramFileGenerator`. |
| `IUserContext` scaffold | ✅ Done | `UserContextGenerator`; business logic still manual. |
| Migration report generation | ✅ Done | `MigrationReportGenerator`. |
| **IR Phase 0 — schema design** | ✅ Done | `WebFormsMigrator.IR` models + `WebFormsMigrator.IR.Contracts` extensibility contracts, `IrSchema.Version = 0.1.0`. |
| **IR Phase 1 — Roslyn member extraction** | ✅ Done | `RoslynMemberExtractor.cs` exists and is wired as an `IIrExtractor`. |
| **IR control tree extraction** | ✅ Done | `ControlTreeIrExtractor` reuses `AspxParser.BuildControlTree`. |
| **IR navigation & workflow extraction (this branch)** | ✅ Done | `NavigationWorkflowExtractor.cs` — semantic labels, navigation edges, workflow step chains with inferred summaries. Sample output regenerated in `tools/ir-viewer/sample-ActorManagement.ir.json`. |
| **IR viewer tool** | ✅ Done (v2) | `tools/ir-viewer/index.html` renders control tree/members/navigation/workflow from an IR JSON file, with a Detailed/Simple mode toggle. |

---

## 6. Features To Do (Backlog)

From `docs/ir-platform/PLAN.md`, work still outstanding:

1. **CSS/JS/HTML asset extraction** (`docs/ir-platform/PLAN.md` Phase 2) —
   `CssAssetIr` / `JsAssetIr` exist in the model but there is no extractor
   populating them yet (linked stylesheets/scripts, inline `<style>`/
   `<script>`, control-ID → CSS-class / control-ID → JS-function
   cross-referencing).
2. **IR serialization & generator decoupling** (Phase 3) — add an
   `IrSerializer`, write one IR document per page/control/master page under
   `<output>/ir/`, and refactor `PageGenerator`/`ComponentGenerator`/
   `LayoutGenerator`/`ControlConverter` to read from the IR instead of the
   live `ProjectAnalysis` graph. Acceptance bar: no change in generated
   Blazor output.
3. **Read-only IR API** (Phase 4) — `WebFormsMigrator.IR.Api` is currently a
   one-line scaffold. Planned endpoints:
   - `POST /projects/analyze`
   - `GET /projects/{runId}/pages`
   - `GET /projects/{runId}/pages/{name}/ir`
   - `GET /projects/{runId}/controls/{name}/ir`
4. **Documentation & handover** (Phase 5) — `docs/ir-platform/SCHEMA.md`
   documenting every IR type/field for external consumers does not exist
   yet; `README.md`/`PROJECT_SUMMARY.md` haven't been updated to describe
   the IR/API as part of the architecture.
5. **Retire or reconcile `MigrationModels.cs` vs the IR model** — the plan
   notes `WebFormsMigrator.Models` "stays Blazor-shaped for now or is
   retired once generators migrate to the IR"; this decision is still open.
6. Not part of the current phased plan, but flagged as explicit non-goals
   that would need new scoping if picked up later: a second target-framework
   generator (React/Angular/MAUI), a UI visualizer/front-end beyond the
   static HTML viewer, authentication/multi-tenant hosting/production
   deployment of the API.
7. **New scope requested 2026-08-02 — see [Section 6a](#6a-newly-requested-scope-expansion-2026-08-02)
   below**: binary (DLL/EXE) source inputs for both Web Forms and WinForms,
   a simple-rendering IR view mode, and an LLM-driven layout-only generator
   for additional target frameworks.

---

## 6a. Newly Requested Scope Expansion (2026-08-02)

Four capabilities were requested to extend the engine beyond its current
"parse Web Forms source → generate Blazor" scope. None of these exist in
the codebase today; each is a new analyzer/generator input or output, not a
tweak to an existing one.

### 6a.1 Generate output from compiled Web Forms assemblies (DLL) and user-control libraries

Today `ProjectAnalyzer`/`AspxParser`/`CodeBehindAnalyzer` require **source**
(`.aspx`/`.ascx`/`.master` + `.cs`). This item asks for a path that starts
from a **compiled Web Forms DLL** (a page/user-control class library built
from `System.Web.UI.Page` / `System.Web.UI.UserControl` types) with no
`.aspx`/`.ascx` markup source available.

- Requires a reflection-based (or decompiler-based, e.g. via
  ICSharpCode.Decompiler/ILSpy libraries) analyzer that inspects the
  assembly's types, base classes, and members to reconstruct an approximate
  `PageIr`/control member set without a real control tree (no markup means
  no `ControlNodeIr` tree can be recovered directly — at best, controls
  declared as fields/properties on the code-behind class can be inferred).
- This is fundamentally different from the current markup-first pipeline:
  it is **members-first, structure-inferred**, the inverse of today's
  **structure-first (markup), members-second (code-behind)** flow.
- Open question to resolve before implementation: how much fidelity is
  achievable without markup (event wiring declared only in `.aspx`, e.g.
  `OnClick="..."`, is invisible to a DLL-only analyzer unless it was also
  wired in code-behind via `+=`).

### 6a.2 Generate output from .NET WinForms EXE / WinForms & user-control libraries

Same "compiled binary as input" idea, applied to **WinForms** instead of
Web Forms — a `.exe` or class library containing `System.Windows.Forms.Form`
/ `UserControl` types, with the `.Designer.cs` (or embedded resources)
describing the control layout.

- WinForms has a **designer-generated control tree** (`InitializeComponent()`
  in `*.Designer.cs`), which is a much better-structured source of layout
  truth than Web Forms markup — closer to `AspxParser`'s job than to the
  DLL-reflection problem in 6a.1, *if* source is available. A binary-only
  (EXE with no source) path faces the same members-first/structure-inferred
  limitation as 6a.1.
- Would need a new analyzer family (`WinFormsProjectAnalyzer`,
  `WinFormsDesignerParser` or similar) parallel to the existing
  `ProjectAnalyzer`/`AspxParser`, plus new `IIrExtractor`s that map WinForms
  controls (`Button`, `TextBox`, `DataGridView`, `ComboBox`, ...) into the
  same neutral `ControlNodeIr`/`MemberIr` shapes already used for Web
  Forms — this is the kind of second source format the IR was designed to
  decouple from a single origin technology.

### 6a.3 Simple rendering mode for the IR viewer — ✅ Implemented (`feature/ir-viewer-simple-mode`)

`tools/ir-viewer/index.html` currently renders the full IR (control tree
with attributes, member list, navigation list, workflow list) as
structured/technical data. This item asks for an additional **simple
rendering mode**: a lightweight, less technical view of the same IR
document — e.g. collapsed/summarized nodes, semantic labels only (hiding
raw attributes/IDs), a plain-language read of navigation/workflow instead
of a table — for reviewers who care about *what the page does* rather than
its exact markup shape.

- Smallest-scope option: a mode toggle in the existing
  `tools/ir-viewer/index.html` that swaps the current detailed
  tree/table rendering for a simplified summary rendering of the same
  loaded `PageIr` JSON — no new data, no new extractor, purely a second
  render path over data already produced by `NavigationWorkflowExtractor`.
- Implemented as a "Detailed / Simple" toggle in the viewer header. Simple
  mode renders an approximate **visual layout preview** rather than a data
  view: server controls are mapped to generic wireframe elements (buttons,
  text inputs, selects, checkboxes/radios, labels, links, image
  placeholders), `GridView` is rendered as a mock table using its
  `BoundField`/`TemplateField` children for headers and nested
  action-button controls, `Repeater`/`DataList`/`ListView` render their
  templated controls once as a sample item, and `Content`/`Panel`
  containers group children into labeled sections. Headings (`<h1>`–`<h6>`)
  found in raw markup blobs are extracted and shown as section titles.
  This is a best-effort approximation, not a pixel-accurate render: the
  IR does not retain raw-HTML nesting (plain `<div>`/`<table>` structure is
  stored as unparsed text, not a tag tree), so grouping beyond
  controls/headings is not attempted; no original CSS/inline styles are
  used. The member table still collapses to a bare kind + name list;
  workflow and navigation panels are shared, unchanged, between both modes.

### 6a.4 LLM-driven generation of a minimum working business application (Angular/React/etc.)

**Revised scope (supersedes the original "layout-only" framing above):**
the generator's goal is a **minimum *working* application** in the target
framework — not just a visual scaffold. "Minimum working" means: it
compiles/runs, controls are wired to real state and real (translated)
event handlers, navigation actually routes, and grids/forms are bound to
real (if mocked) data — with LLM-translated business logic flagged for
human review rather than omitted.

- Input: the full `PageIr` — `ControlNodeIr` tree, `MemberIr` (incl.
  `Body` — the actual C# logic, now used for translation, not just
  reference), `NavigationIr`, `WorkflowIr`.
- Output: a runnable framework project (Angular CLI workspace / React +
  Vite app) with components, routing, a generated state/service layer, and
  best-effort translated logic — not just markup.
- "Configurable local LLM" (e.g. phi4-mini via an Ollama-style local
  endpoint) is used for the translation steps (C# method body →
  TypeScript/JS), not just for layout wording — implemented as an
  `IIrGenerator` with `Target` = `"angular-app"` / `"react-app"`.
- **Honesty constraint**: phi4-mini is a small (~3.8B parameter) local
  model. It can reliably do mechanical translation (loops, conditionals,
  simple string/number logic, basic validation) and will make mistakes on
  complex/domain-specific logic (LINQ-heavy queries, intricate
  conditionals, third-party API calls). All LLM-translated logic must be
  emitted with a `// LLM-translated — verify` marker so it's reviewable,
  not silently trusted. "Minimum working" means *it runs*, not that every
  translated line is guaranteed correct.
- **✅ Implemented (`feature/ir-viewer-simple-mode`): runtime-extensible
  models/prompts.** `WebFormsMigrator.IR.Contracts` now has `LlmRegistry`,
  `ILlmClient`, `LlmModelConfig`, and `PromptTemplate`. New models/prompts
  are added by dropping a `*.model.json` / `*.prompt.json` file into the
  `llm-config/` directory (see `phi4-mini.model.json`, `angular-app.prompt.json`,
  `react-app.prompt.json`) and calling `LlmRegistry.LoadFromDirectory(...)`
  again — no rebuild, no code change. `OllamaLlmClient` is the default
  `ILlmClient` (POSTs to an Ollama-compatible `/api/generate`); a different
  local runtime can register its own client via `RegisterClient` without
  touching the registry.
- **✅ Implemented: `LlmScaffoldGenerator` (`IIrGenerator`).** Serializes a
  `PageIr` to JSON, renders it through a named prompt template (e.g.
  `angular-app`/`react-app`) via `LlmRegistry.RunAsync`, and parses the
  model's response for `===FILE: path=== ... ===END FILE===` blocks
  (the prompt templates instruct the model to use this delimiter),
  writing each to `outputDirectory`. If the model doesn't follow the
  delimiter format, the raw response is written to a single
  `{componentName}.generated.txt` fallback file rather than silently
  dropped. One `LlmScaffoldGenerator` instance per `Target`
  (`"angular-app"` / `"react-app"`), constructed with the registry, target
  name, prompt key, and model name. Covered by 7 unit tests (file-block
  parsing, component-name derivation, fallback-file behavior, and an
  end-to-end `Generate()` test using a fake `ILlmClient`) — 16 tests total
  in `WebFormsMigrator.IR.Contracts.Tests` pass.
- **✅ Wired into the CLI, opt-in via `WFM_LLM_TARGET` env var.**
  `MigrationEngine.BuildAndSerializeIr()` calls `TryGenerateLlmScaffold()`
  per page (not per user control, to bound cost) right after writing that
  page's `.ir.json`. When `WFM_LLM_TARGET` is unset (the default), this
  code path never runs — existing migration behavior is unchanged. When
  set (e.g. `WFM_LLM_TARGET=angular-app`), it loads `llm-config/` from the
  current working directory, registers `OllamaLlmClient`, and writes
  scaffold output to `<output>/llm-scaffold/{target}/{pageName}/`. A
  failure (no Ollama running, model not pulled, bad response) is caught
  per-page and logged as a warning — it never fails the overall migration,
  since generated Blazor output never depends on this step.
- **✅ Verified against a real, locally running Ollama + phi4-mini.** A
  one-off smoke test called `LlmScaffoldGenerator` on the real
  `sample-ActorManagement.ir.json`, end to end, against an actual local
  `ollama serve` with `phi4-mini` pulled (no mocks). Two things came out
  of it:
  - **Bug found and fixed**: the default `HttpClient` in `OllamaLlmClient`
    used .NET's 100-second timeout, but local CPU inference for a
    multi-file scaffold response routinely exceeds that (the real call
    took ~6.5 minutes) — every real request was timing out before this
    fix. Default timeout raised to 10 minutes (a caller-supplied
    `HttpClient` can still set its own).
  - **phi4-mini did not follow the `===FILE:===` delimiter instructions**
    on this prompt — it returned free-form prose instead of the requested
    file blocks (it *did* correctly reference `gvActors`/GridView context
    from the IR JSON, showing it read the input, but ignored the output
    format). `LlmScaffoldGenerator`'s fallback path worked exactly as
    designed: no files parsed → the raw response was written to
    `ActorManagement.generated.txt` rather than silently dropped or
    crashing. This is concrete, not hypothetical, evidence for the
    "phi4-mini is small and may not reliably follow instructions" caveat
    stated in Section 6a.4 — a next iteration should try the `/api/chat`
    endpoint with a system-role instruction (rather than raw
    `/api/generate`) and/or a smaller single-file-at-a-time prompt shape,
    since asking a 3.8B model for 5 coordinated files in one completion is
    likely too much in a single shot.

### 6a.4a Output Specification — phi4-mini target, Angular/React, minimum working app

| # | Concern | What phi4-mini + this generator produces |
|---|---|---|
| 1 | Server controls → HTML/Angular/React controls | `asp:Button` → `<button (click)="onBtnSearchClick()">Search</button>` (Angular) / `<button onClick={handleBtnSearchClick}>Search</button>` (React) — real event wiring, not a static tag. `asp:TextBox` → `<input [(ngModel)]="searchTerm">` / `<input value={searchTerm} onChange={...}>`, bound to a real component field (see #14). Styling uses the target framework's own component library defaults (e.g. Angular Material / a plain React CSS baseline), not the original CSS. |
| 2 | Custom user controls (`.ascx`) | A real Angular component / React component with `@Input()`/props for each public property (#4) and `@Output()`/callback props for each event the control raises, referenced from the parent at its original tree position — a functional child component, not just a layout placeholder. |
| 3 | Server-side events (`OnClick`, `OnSelectedIndexChanged`, ...) | A real handler method is generated and wired (Angular `(click)="onBtnSearchClick()"` / React `onClick={handleBtnSearchClick}`) **with a translated body**: the LLM converts the C# handler's statements (and the methods it calls, per `WorkflowIr.Steps`) into the equivalent TypeScript/JS, marked `// LLM-translated — verify`. Handlers whose body only calls `Response.Redirect`/`Server.Transfer` become real `router.navigate(...)` calls (tied to #15). |
| 4 | Properties | Public/protected properties become real component fields — `@Input()` for values set by a parent, plain component state otherwise — with getter/setter logic translated the same way as methods (#3) when it's more than a trivial backing field. |
| 5 | Server-side methods (business methods) | **Translated, not just stubbed.** Each `MemberIr` of kind `Method` gets a same-named TypeScript/JS method with its body translated from `MemberIr.Body` by the LLM, marked `// LLM-translated — verify`. Methods calling out to types/APIs unknown to the IR (e.g. a repository class) are translated up to the call site, with the call itself left as `// TODO: wire to real API — call to {TypeName}.{MethodName} not resolvable from IR`. |
| 6 | Code-behind logic (control flow, calculations, validation) | Translated inline as part of #3/#5 rather than excluded. Validation logic (e.g. `if (string.IsNullOrEmpty(...))`) translates well with a small model; multi-step business rules should be manually verified — the `// LLM-translated — verify` marker exists specifically for this risk. |
| 7 | Container controls (`GridView`, `Panel`, `Repeater`, etc.) | `GridView` → a real bound table: Angular `*ngFor` over a component array property (typed via #12) with `<th>` from `BoundField.HeaderText`, action-column buttons wired to the same translated handlers as #3; React equivalent using `.map()`. `Panel` → a container `<div>`, optionally conditionally rendered (`*ngIf` / `{condition && ...}`) if IR/code-behind shows visibility toggled by a boolean. `Repeater`/`DataList`/`ListView` → a real loop over an array field, not a single static sample. |
| 8 | JavaScript logic (inline `<script>`, jQuery, `OnClientClick`) | Still gated on the not-yet-built CSS/JS asset extractor (Section 6, Phase 2). Once that facet exists, simple inline script (e.g. a `confirm()` before delete, seen in the sample IR's `OnClientClick`) translates to the framework-native equivalent (`window.confirm(...)` in a handler); jQuery plugin usage (e.g. `.autocomplete(...)`) is flagged `// TODO: no Angular/React equivalent generated — replace jQuery plugin manually`, since translating a jQuery plugin call into a framework paradigm isn't reliable for a small model. |
| 9 | Session state, cookies | Generated as a real **app-level state service**: Angular injectable service (`BehaviorSubject`/signal-backed) or React Context + hook, with fields matching the `Session["Key"]`/`ViewState["Key"]` usages found in translated method bodies. This is a working default (in-memory, per-session), not a stub — but it does not persist beyond the browser session unless a real backend is wired in. |
| 10 | Authentication / Authorization | Generated as a minimum working **mock auth**: an `AuthService`/`useAuth()` hook with `isAuthenticated`/`roles` state, route guards (Angular `CanActivate` / React route wrapper) applied to pages whose original `[PrincipalPermission]`/role checks were detected, and a login form scaffold. It is explicitly a **local mock** (no real token validation/backend call) unless a real identity provider is configured separately — "minimum working" here means the app's authorization *branches* work in the browser, not that it's production-secure. |
| 11 | HTTP handlers (custom `.ashx`, API endpoints) | Still out of scope for this generator specifically (no IR facet represents them — see #12/#13). A minimum working app instead gets a **mock API layer**: an Angular `HttpClient`-based service / React `fetch` hook per detected data operation (e.g. "search actors", "save actor"), pointed at a generated **in-memory mock backend** (e.g. a `json-server`-style fixture) so the app runs end-to-end without a real .NET backend. Wiring the mock service to the *real* backend endpoint is a manual follow-up. |
| 12 | Models / entities | Generated as TypeScript interfaces, inferred from `BoundField.DataField`/`HeaderText` pairs and any translated method signatures that reference the same field names (e.g. `ActorId: number`, `FirstName: string`, `DateOfBirth: Date`) — type inference is heuristic (string/number/Date guessed from field name and format strings like `DataFormatString`), so generated types should be checked against the real backend contract. |
| 13 | Database logic | **Never generated as real DB access** (Angular/React are browser clients; this is architecturally correct, not a limitation to fix). Instead, feeds the mock API layer in #11 with plausible fixture data shaped by the models in #12, so the app is runnable standalone. Real database logic stays server-side and out of this generator's scope by design. |
| 14 | Binding | Real, working binding: Angular `[(ngModel)]`/reactive forms (`formControlName`) or React controlled components (`useState` + `value`/`onChange`), tied to the fields generated in #4/#12. This is the key difference from the original layout-only scope — controls are functional, not static mockups. |
| 15 | Navigation | Real routing: `NavigationIr` entries become registered routes (Angular `RouterModule` config / React Router `<Route>`), and the translated handlers in #3 call `router.navigate('/target')` / `navigate('/target')` instead of just rendering a `<Link>`. |

**Net summary:** with phi4-mini and this revised scope, the generator aims
for and can plausibly produce a Angular/React app that **runs end-to-end
against mocked data** — real controls, real event handlers with translated
logic, a real (mock) state/auth/API layer, and real navigation. The
honesty caveat is about **correctness, not existence**: everything is
generated, but LLM-translated business logic (#3, #5, #6) and inferred
types (#12) are marked for human review rather than assumed correct, and
anything that requires a genuine backend/database/identity provider (#9's
persistence, #10's real auth, #11/#13's real data) is deliberately backed
by a working **mock** rather than left unbuilt, so the app is runnable
without a companion backend on day one.

### 6a.4b Confirmed today via manual workflow (no code changes)

Validated 2026-08-02: pasting an IR JSON document directly into a general
chat LLM (tested with Claude) and prompting it, with no tooling built,
already reproduces both outcomes scoped above:

- **"generate angular UI"** → a full Angular 17+ component set: a
  standalone component (`.ts`/`.html`/`.css`), a matching `*.service.ts`
  making HTTP calls to an assumed REST API (e.g. `/api/actors`), and a
  `*.model.ts` with interfaces inferred from `BoundField` names
  (`ActorId`/`FirstName`/`LastName`/`DateOfBirth`). Reactive Forms with
  validators mirroring the legacy `ValidateForm()`, delete-confirm mapped
  to `confirm(...)`, permission-gated inputs (`isRoc`/`isAdmin`) surfaced
  as things the caller must wire up rather than invented — matching the
  #4/#9/#10/#12 behavior predicted above (real controls/binding, but
  auth/session and the backend contract flagged as the human's job).
- **"just show control in modern UI leaving specific details"** → a
  clean, restyled visual mock (search bar, card-style table with
  icon actions, compact add/edit form) with explicitly *no wiring* — the
  same intent as the IR viewer's Simple mode (Section 6a.3) and the
  original layout-only framing of 6a.4, produced instead via a general
  chat prompt rather than the built-in viewer.

**Takeaway:** the IR is already information-rich enough to drive both
outcomes through nothing more than a copy-paste prompt to any capable LLM
— this is strong evidence the scoped generator (6a.4/6a.4a) is buildable,
not speculative. What the built pipeline adds over ad-hoc pasting:
automation across many pages without manual copy/paste, a **local**
configurable model (phi4-mini via Ollama) instead of a dependency on a
cloud chat product, consistent conventions/output shape across an entire
project, and integration into `IrPipelineRegistry` so it runs as part of
the normal migration flow rather than a one-off manual step.

---

## 6b. Phased Implementation Plan for Section 6a (Effort Estimate)

One engineer, person-days, planning-grade not a commitment.

| Phase | Scope | Days |
|---|---|---|
| A0 | Shared prep: reflection/decompile helper lib + `IIrExtractor`/`IIrGenerator` scaffolds for new sources/targets | 2–3 |
| A1 | Web Forms DLL/UC-library input (6a.1): reflection analyzer, members-first `PageIr`, no control tree | 5–7 |
| A2 | WinForms EXE/library input (6a.2): `Designer.cs` parser (source case) + reflection fallback (binary-only case), control→`ControlNodeIr` mapping | 6–9 |
| A3 | IR viewer simple mode (6a.3): render-mode toggle, summarized/semantic-only view over existing IR JSON | 1–2 |
| A4 | LLM layout-only generator (6a.4): pluggable local-LLM client config, layout-prompt builder from `ControlNodeIr`+`Semantic`, `angular-layout`/`react-layout` `IIrGenerator`s | 6–9 |
| A5 | Docs + samples for all above | 1–2 |
| **Total** | | **21–32 days** (~4.5–6.5 weeks) |

Notes:
- A1/A2 are highest-risk (binary-only fidelity is inherently lossy — no event wiring visible without source).
- A3 has no dependency on A1/A2/A4 — can run first/in parallel.
- A4 depends only on existing `ControlNodeIr`/`Semantic` data, not on A1/A2.
- Sequencing: A0 → {A1, A2, A4 in parallel} → A3 (anytime) → A5.

---

## 7. User Guide

### 7.1 Prerequisites

- .NET 8 SDK
- (Optional) Visual Studio 2022 or VS Code

### 7.2 Running a migration

```bash
cd WebFormsMigrator
dotnet build

# Basic usage
dotnet run -- "C:\Path\To\WebFormsProject"

# Custom output path
dotnet run -- "C:\Path\To\WebFormsProject" "C:\Output\BlazorApp"
```

Input can be a path to a `.csproj` or a directory containing Web Forms
files (`*.aspx`, `*.aspx.cs`, `*.ascx`, `*.ascx.cs`, `*.master`,
`*.master.cs`, `Web.config`).

### 7.3 After migration

1. Open the generated `MIGRATION_REPORT.md` in the output project — this is
   the authoritative checklist of manual work remaining.
2. `cd` into the generated project, `dotnet restore && dotnet build`.
3. Fill in `appsettings.json` connection strings/settings called out in the
   report.
4. Implement the real logic behind the generated `UserContext` service and
   any business services referenced by converted code.
5. Work through the manual task checklist in the report (event handler
   logic, ViewState/Session → component state/scoped services, auth,
   database access, static assets, JS interop, layout verification).
6. `dotnet run` and manually test each converted page.

### 7.4 Inspecting the IR (new workflow)

1. Open `tools/ir-viewer/index.html` directly in a browser (no build step,
   no server required).
2. Load an IR JSON file — `tools/ir-viewer/sample-ActorManagement.ir.json`
   is a working example produced from the `ActorManagement.aspx` sample.
3. Browse the rendered control tree (with inferred semantic labels),
   extracted members, navigation actions, and workflows.

There is currently no CLI command that emits a standalone IR JSON file for
an arbitrary project — the sample file was generated as part of building
the extractors. Wiring "generate IR JSON for this project" into the main
console app's flow is part of the Phase 3 backlog above.

### 7.5 Reference material

- `README.md` — feature overview, conversion mapping table, troubleshooting.
- `PROJECT_SUMMARY.md` — architecture/workflow summary of the core engine.
- `MIGRATION_PATTERNS.md` — side-by-side Web Forms → Blazor code patterns
  for pages, layouts, controls, ViewState/Session, data binding, events,
  JS interop, file upload/download, and auth; plus a migration checklist.
- `docs/ir-platform/SCOPE.md` — what the IR initiative is and isn't.
- `docs/ir-platform/PLAN.md` — phased implementation plan and estimates.

---

## 8. Suggested Features (Not Yet Planned)

Beyond the existing IR-platform backlog, worth considering:

- **CLI flag to emit IR JSON** (`--emit-ir <dir>`) as part of the main
  console app, independent of full Blazor generation — lets the IR viewer
  (or the future API) be used against any project today, without waiting
  for Phase 3/4.
- **Diff/compare mode**: run the IR extractor twice (e.g. before/after a
  source change) and diff the resulting `PageIr` documents to see exactly
  what structural/semantic changes a code edit produced — useful both for
  validating the migration engine itself and for change review on the
  legacy app.
- **Confidence scoring on semantic inference**: `NavigationWorkflowExtractor`
  currently returns a single best-guess semantic string per control/workflow
  from keyword matching; surfacing a confidence level (or the matched
  keyword) would help a human reviewer judge how much to trust it.
- **Workflow visualization as a flow diagram** in the IR viewer (nodes =
  controls/methods, edges = calls/navigation) rather than the current
  tree/list rendering — much faster to audit a page's behavior at a glance.
- **Batch/project-level IR viewer**: today the viewer loads one page's IR
  JSON at a time; a project-level index (list of pages/controls with
  drill-down) would match how `MIGRATION_REPORT.md` already summarizes at
  the project level.
- **Snapshot testing for the IR** (per the Phase 3 acceptance-bar idea in
  `PLAN.md`): golden IR JSON files per sample page, asserted in CI, so
  regressions in extraction are caught independent of Blazor-output diffing.
- **Pluggable semantic-keyword configuration**: externalize
  `NavigationWorkflowExtractor.SemanticKeywords` into a JSON/YAML file so
  teams can tune naming-convention heuristics per-codebase without a code
  change/rebuild.
- **JS/CSS extractor** could reuse the same keyword/heuristic approach as
  navigation/workflow to attach semantic labels to style/script assets
  (e.g. "validation styling", "grid sorting behavior") once Phase 2 lands.
- **Second-target-generator proof of concept** (explicitly out of scope for
  the current phase per `SCOPE.md`, but worth prototyping once Phase 3 is
  stable): a minimal `IIrGenerator` for a second framework, to validate that
  the IR is genuinely framework-neutral rather than accidentally
  Blazor-shaped.

---

*This document was generated by reading the current state of the
repository (source files, `README.md`, `PROJECT_SUMMARY.md`,
`MIGRATION_PATTERNS.md`, `docs/ir-platform/SCOPE.md`,
`docs/ir-platform/PLAN.md`) as of 2026-08-02. It reflects a snapshot, not a
live view — re-generate for future dates rather than treating this as
permanently current.*
