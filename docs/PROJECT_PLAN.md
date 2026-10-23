# WinForms Modernization Engine — Project Plan & Analysis

**Status:** v0.5 — P0, P1 done; P2/P3 React slice and P4.2 rule-based translation done (see §0) · **Date:** 2026-10-12 · **Owner:** TBD
**Related docs:** `CLAUDE.md` (rules & architecture) · `SKILLS.md` (control mappings) · `docs/USER_GUIDE.md` (how to run it) · `docs/PROJECT_DOCUMENTATION.md` (as-built architecture) · `docs/ENGINE_AUTHORING.md` (extension & conventions) · `docs/PILOT_FINDINGS.md` · `WINFORMS_MIGRATION_PROMPT.md`

> Estimates are **planning-grade (±30%)**, in person-days (PD), and assume reuse of the existing WebFormsMigrator IR platform. They must be re-baselined after the Phase 1 spike and the first pilot application.

---

## 0. Progress snapshot (as of 2026-10-20, release 0.2)

| Phase | State | Notes |
|---|---|---|
| **P0 Foundation** | ✅ done (M0 met) | `net10.0` solution; engine contracts, registry, `engines.json` allow-list, plugin loader with isolated load contexts and schema check; `migrator engines`. M0 verified with throwaway plugins (valid / wrong-schema / missing / unlisted) — no test projects, per rule. |
| **P1 WinForms source engine** | 🟡 largely done | Prototype ported to real `IIrExtractor`s (tree → members → semantics → workflows → navigation); project analyzer; **six IR files + `project.ir.json`**; designer hardening (menus, tabs, split panels, grid columns, TableLayout cells/styles, items, DataBindings, tooltips, `.resx`); unhandled statements reported. `migrator ir` works end to end. |
| **P2 UI library** | 🟡 React 0.3.0 | `ui-spec` 0.3.0 + `@wfm/ui-react` 0.3.0 (36 WinForms-mapped controls: native HTML, no third-party dependency; popups on `<dialog>` with `dialog.show()` / `dialog.close()`; icons; editable/paged grid; lazy tree; earlier text: 28 components: form, menus, toolbar, status bar, split, panel, group box, label/button/textbox/combobox/checkbox, progress, picture, grid, list view, context menu, dialog service, `useWfModel`, `WfUnsupported`). No stand-ins remain for standard controls (check list, up/down, track/scroll bars and drop-down buttons are real components). Wave B added masked/rich text, month calendar, list box; the tree view followed. `ui-angular` / `ui-html` not started. |
| **P3 Target engines** | 🟡 React + Angular | Angular: `migrate --target angular-app` (shared `Migrator.Target.Core`, `@wfm/ui-angular`, `ngc` gate): both pilots generate with the same translation numbers as React, 0 TypeScript errors, `ng build` and lint clean. React:  `migrator migrate` generates an app from the IR folder + `ui-spec`: layout from Dock/Location (no pixels), `ShowDialog` -> dialog service, state for inputs, mock grid rows, `MIGRATION_REPORT.md`. Pilot: 17 forms generated; install, `tsc`, build and lint with zero errors. `migrate --contract` emits typed mock services from an assembly contract (secrets omitted). Per-page state is one `useWfModel` model (`vm` controls, `f` fields). Constructor arguments become props with fixture defaults; the app's own static classes become `src/support/index.ts`; a dev launcher (`?page=<Name>`) opens any page. Angular and HTML+jQuery targets not started. |
| **P4.2 Rule translation** | ✅ done | `migrate --translate rules --compile-gate`: deterministic Roslyn C# -> TS for every method of a page and for the project's own classes; `tsc` as the oracle (failing methods are demoted to commented C#); lint-clean by construction. Pilot: **375 / 706 methods complete (53%)**, 209 partial, 122 not translated; 33 project classes (157 members, 56 complete); 0 TS errors, 0 lint errors, all 17 pages render. ListView, GroupBox, `Button.DialogResult`, `ListViewItem`, overload/constructor merging included. |
| **Tooling** | ✅ | `migrator contract` (public API of a managed assembly from metadata only, never executed); `tools/win-ir-viewer` (React page for reviewing IR; on branch `feature/win-ir-viewer`, not yet merged to `main`). |
| **Archetype controls** | ✅ done (0.5) | Units are classified as Grid / Tree / Menu / Toolbar / List / Form (designer first, name second: `rules/control-archetypes.json`); missing columns, nodes and items are inferred from the code and marked; `--catalog` (implied by `--units all`) writes a `/modules` page for units no code opens, in the React and Angular apps. See `guide/v0.5/10-archetypes.md` |
| **Backend track** | 🟡 analysis built | `migrator backend`: read-only survey of class libraries, ASMX services and WSDL (rules in `rules/backend-patterns.json`); the .NET 10 REST API generator is next (variant A/B decision pending) |
| **P4.3 LLM tier** / P5 report polish / P6 pilots | ⏳ P4.3 next | Ollama (phi4-mini default) on the residue the rules cannot finish: one method per call, validated and compile-gated, cached, marked `// LLM-translated — verify`. Measured: phi4-mini ~48 s per 30-line method, phi4 3x slower and invents APIs, so deterministic first. Pilot run: `docs/PILOT_FINDINGS.md`. |

**P1 remaining (small):** externalize `SemanticKeywords` (1 PD); populate `Shared.Settings/Resources/Dependencies`; handlers in a third partial file; multi-level workflow steps; flag controls created at runtime (loops, `Controls.Add` in code). The real pilot run is done: 36/36 units extract (892 controls, 420 workflows, 0 unresolved handlers) after three extractor bugs it exposed were fixed (overloaded handlers, navigation in helper methods, file dialogs counted as navigation). M1-lite is met (startup form -> React shell that builds and renders); full M1 needs handler translation (P4).

**Decisions taken so far:** D1 -> engine on `net10.0`; shared IR still `net8.0`. D3 (interim) -> `ProjectReference` to a sibling checkout via `build/IrReference.props`; **the WebFormsMigrator repo is never modified from here**. D6 -> pilot is ECMNavigator (35 forms + 1 user control). NavigationIr gap -> WinForms vocabulary on existing fields via `WinNavigation`. Pilot backend `ECMLibrary` is a SOAP web-service client (not a database layer): the app needs a thin REST layer behind a typed `ecmService`; secrets are never emitted to the browser. Local inputs (assemblies) live in git-ignored `local-inputs/`, derived contracts in `local-outputs/`; neither is ever committed.

---

## 1. Executive Summary

Many organizations still run business-critical **.NET WinForms** applications. They work, but they are Windows-only, desktop-deployed, tightly coupled to the database, and hard to staff. Rewriting them by hand is slow and risky.

This project builds a **standalone, modular migration engine** that reads WinForms source (`*.Designer.cs` + code-behind), produces a **framework-neutral Intermediate Representation (IR)** with a complete hierarchy (project → forms → controls → members → workflows → navigation), and generates a **minimum working application** in **React, Angular, or HTML/jQuery**. Generated apps use an **independent, reusable UI control library** (`Wf*` components) rather than raw markup, so every migrated screen looks and behaves consistently.

It is the WinForms sibling of the existing Web Forms → Blazor migrator and reuses its IR schema, contracts and LLM plumbing.

**Headline numbers (planning-grade)**
| | |
|---|---|
| Base effort | **≈ 105–145 PD** (≈ 120–165 PD with 15% contingency) |
| 1 engineer | ≈ 24–33 weeks |
| 3 engineers (parallel streams) | ≈ 10–14 weeks |
| First vertical slice (1 form → React) | ≈ week 5 (3-person team) |

---

## 2. Purpose & Scope

### 2.1 Purpose
1. **Analyze once, reuse everywhere:** convert WinForms source into a versioned IR that any tool (viewer, API, generator) can consume.
2. **Generate working modern apps** from the IR for multiple targets without re-parsing source.
3. **Standardize the UI** through an independent, framework-parallel control library.
4. **Stay honest:** flag every machine-translated, inferred, mocked or unsupported element for human review.
5. **Stay extensible:** new source technologies (WPF, VB6…), targets (Vue, Blazor…) and controls are added as modules — not by editing the core.

### 2.2 In scope (v1)
- WinForms **source-based** input (Designer.cs + code-behind, custom UserControls inlined).
- IR with complete hierarchy and 6 IR files per form (see `CLAUDE.md` §4).
- Targets: `react-app`, `angular-app`, `html-jquery-app`.
- UI library: `ui-spec`, `@wfm/ui-react`, `@wfm/ui-angular`, `@wfm/ui-html`.
- LLM-assisted C# → TypeScript logic translation (local model, opt-in).
- Mock backend / mock auth so generated apps run standalone.
- `MIGRATION_REPORT.md` with manual-work checklist.

### 2.3 Out of scope (v1)
Pixel-perfect rendering · production auth · real DB access from the browser · MAUI/mobile · automatic translation of P/Invoke / COM / GDI+ painting · binary-only (EXE/DLL) input *(optional Phase 7)* · test projects and the demo/showcase app *(separate project)*.

---

## 3. Legacy WinForms: Problems & Challenges

### 3.1 Why legacy WinForms hurts
| Area | Issue |
|---|---|
| **Platform reach** | Windows-only, install-per-machine; no browser, tablet or mobile access; remote work needs VPN/RDP/Citrix. |
| **Deployment** | MSI/ClickOnce rollouts, version drift across desktops, slow patching. |
| **Runtime lifecycle** | Many apps still sit on .NET Framework 4.x; mixed 32/64-bit, COM/ActiveX and old third-party control suites (DevExpress, Telerik, Infragistics) add upgrade friction. |
| **Architecture** | Business logic lives in event handlers (`btnSave_Click`); forms call `SqlConnection` directly (2-tier); global/static state; god-forms with thousands of lines. |
| **Security** | Connection strings and DB credentials live on client machines; the database is reachable from every desktop. |
| **Maintainability** | Designer-generated code, visual inheritance, no tests, dwindling WinForms skills in the hiring market. |
| **Integration** | Hard to expose logic as APIs; hard to integrate with SSO, web portals, or cloud services. |
| **UX** | Fixed-pixel layouts; no responsive design; dated look. |

### 3.2 Migration-specific challenges (why it's hard)
| Challenge | Detail | How this project addresses it |
|---|---|---|
| **No markup file** | UI is code (`InitializeComponent`), not declarative markup. | `WinFormsDesignerParser` (Roslyn) turns it into a control tree with parent/child edges. |
| **Stateful → stateless** | Desktop forms hold state in fields; web is request/response + component state. | Public props/fields → component state; `FormMode`-style patterns → state; app-state service. |
| **Event-driven, chatty UI** | Dozens of events per form (`Validating`, `SelectedIndexChanged`, `CellFormatting`). | Event map (Skill 6) + `WorkflowIr` call chains. |
| **Rich controls** | `DataGridView`, `TreeView`, `ListView`, MDI, `MenuStrip` have no 1:1 HTML equivalent. | Purpose-built `WfDataGrid`, `WfTreeView`, `WfTabs`, `WfMenuBar` in the UI library. |
| **Absolute layout** | `Location`/`Size`/`Anchor`/`Dock`. | Layout facet in IR → flex/grid derivation (Skill 4); pixels deliberately not copied. |
| **Modal dialogs** | `ShowDialog()` blocks and returns `DialogResult`. | `WfDialogService` returning promises; `NavigationIr` distinguishes `Show` vs `ShowDialog`. |
| **Data access in UI** | `DataSet`/`SqlDataAdapter` in forms. | Replaced by typed services + mock API; real data tier remains server-side (a human task). |
| **Threading** | `BackgroundWorker`, `Invoke`, `Timer`. | `async/await`, timers as hooks/services (Skill 9). |
| **Desktop integration** | Files, printers, registry, serial ports, `Process.Start`. | Flagged ⛔; server-side service or browser limitation; listed in report. |
| **Third-party suites** | Vendor grids/charts with proprietary APIs. | `WfUnsupported` placeholder + report; mapping added per vendor on demand. |
| **Business-logic fidelity** | Small local LLMs make mistakes on complex logic. | Mandatory `// LLM-translated — verify` markers; deterministic code for everything else. |
| **Extractor blind spots** | Controls created dynamically at runtime, handlers in a third partial file, deep call chains. | Documented limitations (CLAUDE.md §4.3); extractor improvements in Phase 1. |
| **Organizational** | User retraining (keyboard-heavy workflows), parallel-run period, data migration, auth change (Windows auth → OIDC). | Out of engine scope; covered in rollout guidance and risk register (§9). |

---

## 4. Why This Approach — Advantages

### 4.1 Compared with alternatives
| Option | Pros | Cons | Fit |
|---|---|---|---|
| **A. Manual rewrite** | Cleanest result; full control | Slowest, most expensive, high regression risk, inconsistent UI across screens | Small apps / major redesign |
| **B. Upgrade WinForms to current .NET (stay desktop)** | Lowest risk; fast | Still Windows-only, still desktop deploy, same architecture | When desktop is acceptable |
| **C. Wrap in WebView2 / hybrid shell** | Gradual | Doesn't remove WinForms; two UI stacks | Transitional |
| **D. Generic auto-converters / commercial tools** | Fast first pass | Black-box, lock-in, fixed targets, source leaves premises (if cloud) | Evaluate per case |
| **E. This engine (IR + library + LLM-assist)** | Repeatable, multi-target, consistent UI, reviewable, extensible, local/private | Needs upfront build; output still needs human review of logic; not for every app | Many screens, many apps, web goal |

### 4.2 Advantages of this solution
- **Analyze once, consume many:** one IR (`complete.ir.json` shape) feeds viewer, API, generators — no repeated parsing.
- **Reuse of existing investment:** same `PageIr` schema, contracts, LLM registry and IR viewer as the Web Forms track; Web Forms and WinForms estates can be migrated by one team.
- **Multi-target from one source:** React, Angular, HTML/jQuery today; Vue/Blazor later by adding a target engine + library package.
- **Consistent UI everywhere:** library-first generation means one look, one behavior, one place to fix a bug or restyle (CSS tokens).
- **Library is independent and reusable:** hand-written apps and future products can use `@wfm/ui-*` without the engine.
- **Modular and extensible:** engines are plugins; adding a control, technology or target does not modify the host.
- **Transparent and reviewable:** markers (`LLM-translated`, `MOCK`, `inferred`, `TODO`) and `MIGRATION_REPORT.md` show exactly what a human must check.
- **Private by design:** a local model (e.g. phi4-mini via Ollama) keeps source code on-premises; cloud LLMs remain optional via config.
- **Deterministic where it matters:** markup, routes, models, services are template-generated; the LLM is used only for logic translation.
- **Incremental adoption:** migrate form by form; run IR-only analysis to size the estate before generating anything.
- **Immediate runnable result:** mock API/auth means stakeholders can click through screens on day one.

### 4.3 Advantages of modernizing to web (business view)
Zero-install delivery and instant updates · browser/mobile reach · modern hiring pool · API-first architecture and reuse · centralized security (no DB credentials on clients) · SSO/OIDC integration · cloud/container hosting · responsive, accessible UX · easier monitoring and DevOps.

### 4.4 When *not* to use it
Apps with heavy device/hardware integration, GDI+ custom drawing, real-time low-latency desktop UX, or where staying on desktop (option B) fully meets the need.

---

## 5. Solution Overview

```
Legacy WinForms source
  *.Designer.cs + *.cs
        │
        ▼
┌──────────────────────────────┐        ┌────────────────────────┐
│ SOURCE ENGINE: WinForms      │        │ llm-config/ (prompts,  │
│ analyzer → designer parser → │        │ models; runtime-       │
│ members → semantics →        │        │ extensible)            │
│ workflows → navigation       │        └───────────┬────────────┘
└──────────────┬───────────────┘                    │
               ▼                                    │
     IR (ProjectIr → PageIr → ControlNodeIr / MemberIr / WorkflowIr / NavigationIr)
     6 files per form · project.ir.json
               │
     ┌─────────┼───────────────┬───────────────┐
     ▼         ▼               ▼               ▼
 IR Viewer   (future API)  TARGET ENGINES: react-app · angular-app · html-jquery-app
                               │      uses ui-spec (data) + LLM only for logic bodies
                               ▼
                  Generated app  ──imports──►  UI LIBRARY (independent)
                  pages · services · models      ui-spec · @wfm/ui-react · @wfm/ui-angular · @wfm/ui-html
                  mocks · routes · auth(mock)
               │
               ▼
        MIGRATION_REPORT.md  (manual checklist)
```

Key design decisions (details in `CLAUDE.md`): separate repo/solution · engines as plugins (`ISourceEngine` / `ITargetEngine` / `IEngineModule`) · IR reused as NuGet packages · library-first generation · additive-only schema/spec changes · no test/demo projects here · local git branches only, push done by the user.

---

## 6. Technology Stack

| Layer | Choice | Notes |
|---|---|---|
| **Engine language / runtime** | C# on **.NET 10 (LTS)** | See §6.1 — the existing Web Forms project targets .NET 8, whose support ends **Nov 10, 2026**. |
| **Source analysis** | **Roslyn** (`Microsoft.CodeAnalysis.CSharp`) | Syntax-level; no need to compile or run the legacy app. |
| **IR** | `WebFormsMigrator.IR` + `.IR.Contracts` (NuGet) · `System.Text.Json` (indented PascalCase) | Schema `0.1.0`, additive evolution. |
| **CLI / host** | .NET console, `System.CommandLine`, `engines.json`, `AssemblyLoadContext` plugin loader | Thin CLI over `Migrator.Engine`. |
| **LLM** | **Ollama** local endpoint + **phi4-mini** (configurable); `LlmRegistry`, `ILlmClient`, `PromptTemplate` | `/api/chat`, one file per call, temp ≤ 0.2, ≥ 10-min timeout. Larger/cloud models via config. |
| **UI spec** | JSON contract + CSS-variable design tokens | Single source of truth for all three libraries. |
| **React library** | TypeScript (strict), React 18, Vite library mode | Headless/primitive base chosen *inside* the library. |
| **Angular library** | TypeScript (strict), Angular 17+ standalone, signals, `ng-packagr` | |
| **HTML/jQuery library** | HTML5 + CSS + vanilla JS, jQuery 3 adapter, `esbuild`/Rollup bundle | Custom events `wf:*`. |
| **Grid / tree / chart internals** | Library-private choices (e.g. TanStack Table or AG Grid; jstree/Chart.js) | **Licensing review required** (Decision D4). Hidden from generated code. |
| **Generated apps** | Vite + React · Angular CLI · static HTML | Mock layer: `json-server` or MSW; mock `AuthService`. |
| **Tooling** | Git (branch-per-task, local commits), `dotnet` CLI, npm workspaces, ESLint | No test frameworks by decision. |

### 6.1 Runtime note
Microsoft's support policy shows .NET 8 in Maintenance with end of support on **November 10, 2026**, and .NET 10 (LTS, released Nov 11, 2025) supported until **November 14, 2028**. The new standalone engine should therefore **target `net10.0`**; the shared IR packages need a multi-target (`net8.0;net10.0`) or retarget decision (Decision D1). The *legacy* apps being analyzed can be any .NET version — Roslyn reads source only.

---

## 7. Workflow

### 7.1 End-to-end migration workflow
```
1 INVENTORY ─► 2 EXTRACT IR ─► 3 REVIEW/PLAN ─► 4 GENERATE ─► 5 TRANSLATE LOGIC ─► 6 WIRE ─► 7 VERIFY ─► 8 REPORT ─► 9 HUMAN COMPLETION
```
| # | Step | Output | Automated? |
|---|---|---|---|
| 1 | **Inventory** — find Designer/code-behind pairs; detect UserControls; startup form | project summary | ✅ |
| 2 | **Extract IR** — control tree, members, semantics, workflows, navigation | `ir/{Form}/*.ir.json`, `project.ir.json` | ✅ |
| 3 | **Review & plan** — view IR in the viewer; list unsupported controls, forms, routes | migration plan | ✅ + human review |
| 4 | **Generate (deterministic)** — pages/components from IR + `ui-spec`; models, services, routes | app skeleton | ✅ |
| 5 | **Translate logic** — C# bodies → TS via LLM, markers added | translated handlers | ✅ (opt-in) + human review |
| 6 | **Wire** — routing, dialogs, mock API/auth, state | runnable app | ✅ |
| 7 | **Verify** — `npm run build`, lint, smoke-run each route | build status | ✅ |
| 8 | **Report** — `MIGRATION_REPORT.md` | checklist | ✅ |
| 9 | **Human completion** — verify logic, wire real API/auth, polish UX | production app | 👤 |

### 7.2 Per-form pipeline
`Designer.cs` → parser → control nodes → `Members` (Roslyn) → semantics → workflows/navigation → `PageIr` → facet projections → target generator → `Wf*` markup + handlers → report row.

### 7.3 Engineering workflow (every task)
1. New branch from latest default (`feature/…`, `fix/…`, `docs/…`).
2. Read what's needed (no permission prompts), plan briefly.
3. Implement inside the right module only; small local commits.
4. Verify (build/lint/smoke) — no test projects.
5. Hand over: branch name, summary, push + fetch/update command block. **The user pushes.**

### 7.4 Extension workflow
New control → `ui-spec` → all three libraries → SKILLS row. New engine → new project → contract → `engines.json` → SKILLS section. No host edits.

---

## 8. Planning & Effort

### 8.1 Work breakdown (person-days, one engineer-equivalent)
| Phase | Scope | Low | High |
|---|---|---:|---:|
| **P0 Foundation** | Repo + solution; retarget decision; consume IR packages; `ISourceEngine`/`ITargetEngine`/`IEngineModule`; plugin loader + `engines.json`; CLI skeleton; conventions | 5 | 7 |
| **P1 WinForms source engine** | Build/verify the existing prototype in a real .NET environment (1–2); project analyzer (2–3); designer-parser hardening — TableLayoutPanel cells, nested containers, Menu/ToolStrip items, DataGridView columns, DataBindings, `.resx` (4–6); workflow improvements — call order, multi-level, partial files (2–3); `ProjectIr` + 6 IR facet files (3–4); externalized semantic keywords (1) | 13 | 19 |
| **P2 UI library** | `ui-spec` + tokens (3–4) · React lib (12–16) · Angular lib (12–16) · HTML/jQuery lib (10–14). Includes `WfDataGrid`, `WfTreeView`, `WfTabs`, menus/toolbars, dialog service, `WfUnsupported`. | 37 | 50 |
| **P3 Target engines** | Shared generator core — spec-driven binder, Dock/Anchor→CSS layout deriver, naming, route builder, model inference (6–8); React target (5–7); Angular target (6–8); HTML target (5–7); mock layer — fixtures, mock auth, app-state, API services (4–5) | 26 | 35 |
| **P4 LLM translation** | Per-method one-file-per-call prompts; `/api/chat`; marker enforcement; fallback; prompt tuning per target; evaluation on sample methods | 7 | 10 |
| **P5 Reporting, CLI, docs** | `MIGRATION_REPORT.md`; CLI verbs; IR schema docs; engine-authoring guide | 5 | 7 |
| **P6 Pilots & hardening** | 2–3 real WinForms apps (small / medium / large); fix gaps; extend mappings | 10 | 15 |
| **Base total** | | **103** | **143** |
| Contingency (15%) | | +15 | +21 |
| **Planned total** | | **≈ 118** | **≈ 164** |
| *P7 (optional)* | Binary-only EXE/DLL input (reflection/decompiler, members-first, lossy) | 6 | 9 |

**Why P2 is the biggest line:** three parallel libraries × ~45 controls, with DataGrid/Tree/Menu/Dialog being genuinely complex. Mitigation: ship an **MVP control set first** (Form, Button, Label, TextBox, TextArea, CheckBox, RadioGroup, ComboBox, ListBox, Numeric, DatePicker, Panel, GroupBox, Flow, TableLayout, Tabs, DataGrid, Dialog, Unsupported), then the extended set.

### 8.2 Team scenarios
| Scenario | Calendar | Notes |
|---|---|---|
| 1 engineer, sequential | ≈ 24–33 weeks | Lowest cost, slowest; library work dominates. |
| **2 engineers** (1 .NET, 1 frontend) | ≈ 15–20 weeks | Frontend becomes the bottleneck (3 libraries). |
| **3 engineers** (A .NET engine/generators · B React + HTML libs · C Angular lib/target) | **≈ 10–14 weeks** | Recommended; assumes ~25% coordination overhead. |

### 8.3 Indicative timeline (3-person team)
| Weeks | Stream A (.NET) | Stream B (React + HTML) | Stream C (Angular) |
|---|---|---|---|
| 1–2 | P0 foundation | `ui-spec` + tokens; React MVP controls | Angular MVP controls |
| 3–5 | P1 source engine + IR files | React DataGrid, HTML MVP | Angular DataGrid |
| **5** | **M1: ActorManagement → React, end-to-end** | | |
| 5–8 | P3 generator core + React target | Extended controls (React/HTML) | Extended controls + Angular target |
| 8–10 | P4 LLM translation; HTML target | Mock layer; HTML target | Angular hardening |
| 10–11 | P5 report/CLI/docs | Docs for library | Docs |
| 11–14 | P6 pilots & fixes (all) | | |

### 8.4 Milestones & acceptance
| ID | Milestone | Acceptance |
|---|---|---|
| **M0** | Foundation ready | Solution builds; engine registry loads a dummy plugin from `plugins/`. |
| **M1** | Vertical slice | Sample `ActorManagementForm` → IR (6 files) → React app that builds, runs on mock data; search/add/edit/delete work. |
| **M2** | IR complete | All sample forms + a custom UserControl produce consistent facet files; no anonymous nodes. |
| **M3** | Library MVP in 3 frameworks | Same control contract renders in React, Angular, HTML/jQuery. |
| **M4** | 3 targets | One form generates and builds in all targets. |
| **M5** | LLM translation | Handlers translated, all marked; fallback proven on a non-conforming model response. |
| **M6** | Pilot complete | 2–3 real apps migrated; report quality accepted; backlog triaged. |

### 8.5 Proposed success metrics (calibrate in pilot)
- ≥ 90% of controls in pilot apps map to non-`WfUnsupported` components.
- 100% of generated apps build with zero TypeScript errors.
- 100% of LLM-translated / mocked / inferred items carry a marker and appear in the report.
- Time-to-first-runnable-screen measured per pilot and compared with an estimated manual rewrite (measure, don't assume).

### 8.6 Assumptions
- Existing IR models/contracts/LLM plumbing and the WinForms extractor prototype are reusable; the prototype has **not yet been verified on a real .NET build** — if it needs substantial rework, P1 trends to its upper bound or beyond.
- Pilot apps are available with source code.
- A local machine capable of running a ~4B-parameter model (CPU inference is slow: multi-minute responses).
- No test/demo projects in this repo (separate demo project is planned elsewhere and **not** estimated here).

---

## 9. Risks & Mitigations

| # | Risk | L | I | Mitigation |
|---|---|:-:|:-:|---|
| R1 | Prototype extractor fails on real-world designers (runtime-created controls, visual inheritance, third-party controls) | H | H | P1 spike on pilot code first; `WfUnsupported` + report; extend parser iteratively. |
| R2 | Small-model translation quality / format non-compliance | H | M | One file per call, `/api/chat`, low temp, markers, fallback file, optional larger model via config. |
| R3 | UI library scope creep (45+ controls × 3 frameworks) | H | H | MVP set first; spec-first; additive changes; defer rare controls. |
| R4 | Three libraries drift apart | M | H | `ui-spec` is the single contract; identical names/props/events; review checklist per control. |
| R5 | Third-party grid/chart licensing | M | M | Decision D4 early; keep library-private so it can be swapped. |
| R6 | Visual/behavior expectations ("looks like the desktop app") | M | M | Agree up front: structure/behavior parity, not pixel parity; tokens for theming. |
| R7 | Business-logic regressions in translated code | M | H | Mandatory verify markers; human review of every `LLM-translated` item; side-by-side checklist in report. |
| R8 | Data tier missing (apps talk directly to SQL) | H | H | Mock API for demo; real API/service layer is a separate workstream — surfaced in report. |
| R9 | .NET 8 end of support (Nov 10, 2026) | H | L | Target `net10.0` from day one (D1). |
| R10 | Schema churn breaks consumers | L | M | Additive-only changes, `IrSchema.Version`, engines declare supported schema range. |
| R11 | Large grids perform poorly in browser | M | M | Virtualization / server-side paging in `WfDataGrid`; flagged in report. |
| R12 | Parallel-run / change-management friction | M | M | Out of engine scope; include rollout guidance in docs. |
| R13 | Shared IR repo is a sibling checkout (`ProjectReference`); path drift or net8/net10 divergence breaks the build | M | M | Single switch in `build/IrReference.props`; `WebFormsMigratorRoot` override; move to a NuGet feed (D3). |
| R14 | Verification so far used hand-written fixtures; real designers (helper calls, loops, third-party controls) may expose gaps | H | M | Pilot run (D6); `Shared.Unsupported` makes gaps visible instead of silent. |

---

## 10. Open Decisions

| ID | Decision | Recommendation |
|---|---|---|
| D1 | Runtime target for the new engine and shared IR packages | `net10.0`; multi-target shared packages during transition. **Engine: decided (net10.0). Shared IR packages: still net8.0 — owner of that repo to decide.** |
| D2 | npm scope/names (`@wfm/*`) and registry (public vs private) | Private registry or local `npm pack` initially. |
| D3 | How shared IR packages are published | Local NuGet feed first; proper feed later. **Interim in use: `ProjectReference` to sibling checkout (`build/IrReference.props`).** |
| D4 | Grid/tree/chart internals and their licenses | Prefer permissive licenses; hide behind `Wf*`. |
| D5 | Angular baseline version | 17+ standalone (current LTS-supported line at start of work). |
| D6 | Pilot app selection (small / medium / large) | Choose before P1 starts; drives mapping priorities. **Still open and now blocking P1 sign-off.** |
| D7 | Cloud LLM allowed for non-sensitive code? | Default local-only; cloud opt-in via `llm-config`. |
| D8 | Include binary-only input (P7)? | Defer until after pilots. |

---

## 11. Next Steps
Original first-two-weeks list, with status: D1 confirmed; D2 and D6 still open · P0 scaffold done · P1 spike done (gaps fixed: form-level events, `ClientSize` noise node, `MessageBox.Show` as navigation, step order) · `ui-spec` + `WfButton`/`WfTextBox`/`WfDataGrid` (P2) next · M1 after that.

Done since: pilot run (D6) and the P1 gaps it exposed, `ui-spec` MVP + React library, React target (M1-lite), contracts, translation (P4.2) and the slices listed in §0.

Next, in order (the engine slices 1–7 are costed in `guide/v0.3/08-roadmap.md`):
0. **Slices for 0.3.0:** (done: event wiring from code-behind, `Path.*` helpers), `dgv.Rows`, `panel.Controls` dynamic host, tab pages, image resources (replacing `ImageList`), `Path.*` helpers, History-API routes. **To-do list:** the backend gateway (variant A/B, service URLs and authentication still open).
1. **P4.3 LLM tier** on the residue (~120 methods the rules cannot translate): one method per call through the shared `LlmRegistry`, compile gate, cache in git-ignored `local-outputs/llm-cache/`.
2. **Real data for pages:** the opener passes real constructor arguments to dialogs; the remaining `ref`/`out`, `string.Format` specifiers, event subscriptions in code, `dgv.Rows`/`pnl.Controls` members; dynamic fields (`WfDynamicFields`).
3. Missing controls: none of the standard ones (check list, up/down, track/scroll bar, drop-down button done). Tree view done (`Nodes`, `SelectedNode`, `new TreeNode`, `Expand*` translate). Done: date/time picker, radio button (groups), numeric up/down, tabs, link label, masked text, rich text, month calendar, list box.
3a. **Component viewer and app shell** (done): `migrate --preview` generates `<out>/react-viewer` (a visual-only page per form and user control with a plain navigation shell, to review what the engine understood); `--appshell` hosts the same pages in a WinForms-style window with hyperlink navigation and a form-style frame (`<out>/react-appshell`).
3b. **Backend track (project-independent):** (the report already lists the server-side classes and methods it needs: `Server-side code left out`; `Shared.ExternalAssemblies` lists referenced binaries.) a generic engine that retargets a project's .NET Framework class libraries and ASMX/WCF services to **.NET 10** (SDK-style projects, SOAP proxies detected by base type and regenerated for .NET 10 from WSDL, Windows-specific calls bypassed with `// BYPASSED` markers from an editable rules file, mutable statics turned into per-user session state), hosts the facades the generated pages call as a REST API (OpenAPI), and generates the TypeScript client that replaces the mock service. Variant A keeps the existing services running and calls them; variant B also ports the services. All project specifics (source paths, WSDLs, endpoints, overrides) live in a per-project profile outside this repo; secrets only in user secrets/environment. Starts with a read-only analysis report of the sources.
4. **Angular** and **HTML+jQuery** targets with `ui-angular` / `ui-html` (the IR and `ui-spec` are framework-neutral, so only generators and libraries are new).
5. Separate demo project (never inside this repo) and the first full pilot sign-off with the owners of ECMNavigator.

---

## Appendix A — Reuse from the Web Forms project
| Asset | Reuse |
|---|---|
| `PageIr`, `ControlNodeIr`, `MemberIr`, `WorkflowIr`, `NavigationIr`, `IrSchema` | Directly (NuGet) |
| `IIrExtractor`, `IIrGenerator`, `IrPipelineRegistry` | Directly |
| `LlmRegistry`, `ILlmClient`, `OllamaLlmClient`, `PromptTemplate`, `LlmScaffoldGenerator` | Directly; refactor to per-file/`/api/chat` (P4) |
| IR viewer (`tools/ir-viewer`) | Directly (schema-identical) |
| WinForms extractor prototype (designer parser, member/workflow/navigation extractors, `SemanticKeywords`) | Starting point for P1 |
| Learnings: 100 s HTTP timeout bug, delimiter non-compliance of small models, anonymous-node noise in web IR | Built into rules in `CLAUDE.md` |

## Appendix B — Deliverables checklist
- [x] `WinFormsMigrator` repo/solution with engine host and plugin loader
- [x] `Migrator.Source.WinForms` (IR: 6 files/form + `project.ir.json`) — pending pilot validation
- [ ] `ui-library/` — `ui-spec`, `ui-react`, `ui-angular`, `ui-html`
- [ ] `Migrator.Target.React` / `.Angular` / `.Html`
- [ ] LLM prompts in `llm-config/`
- [ ] `MIGRATION_REPORT.md` generator
- [ ] Docs: IR schema, control catalog (engine authoring guide exists: `docs/ENGINE_AUTHORING.md`)
- [ ] Pilot results and backlog
