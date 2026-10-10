# WinForms IR Extractor

Generates a `PageIr` JSON document for a WinForms `Form`/`UserControl` — same
shape as `complete.ir.json` in the `WebFormsMigrator` sample set — from
`*.Designer.cs` + code-behind `.cs` source, instead of `.aspx` markup. This is
backlog item **6a.2** from `PROJECT_DOCUMENTATION.md` (§6a.2), which the docs
note doesn't exist in the codebase yet.

## ⚠️ Not compiled/run here

This sandbox has no .NET SDK and no network access, so I could not run
`dotnet build`/`dotnet run` to verify this compiles and produces correct
output. The Roslyn syntax-tree logic was written carefully and reviewed by
hand, but **build and test it yourself** before relying on it:

```bash
cd WinFormsIr
dotnet restore
dotnet build
dotnet run -- Sample/ActorManagementForm.Designer.cs Sample/ActorManagementForm.cs ActorManagement.winforms.ir.json Sample
```

The last two are the sample Form and its custom control's folder; the
generated `ActorManagement.winforms.ir.json` is what you'd compare against
`complete.ir.json`'s shape.

## What it does, mapped to the ASP.NET pipeline you already have

| ASP.NET Web Forms (existing) | WinForms (this tool) |
|---|---|
| `AspxParser` (markup → control tree) | `WinFormsDesignerParser` (`InitializeComponent()` → control tree) |
| `RoslynMemberExtractor` | `WinFormsMemberExtractor` |
| `NavigationWorkflowExtractor` — semantics, `NavigationIr`, `WorkflowIr` | `SemanticKeywords` + `WinFormsNavigationExtractor` + `WinFormsWorkflowExtractor` |
| `ControlTreeIrExtractor` | `WinFormsIrExtractor.BuildTree` |
| `IrPipelineRegistry.BuildIr()` orchestration | `WinFormsIrExtractor.Extract()` |

### Control tree (`RootControl`)

`WinFormsDesignerParser` reads `InitializeComponent()` statement-by-statement:

- `this.X = new SomeControl();` → declares control `X` of type `SomeControl`.
- `this.X.Prop = value;` → an attribute on `X` (mirrors `ControlNodeIr.Attributes`
  from the aspx `Attributes` dictionary).
- `this.X.Click += new EventHandler(this.Handler);` (or `+= this.Handler;`) →
  recorded as both an `OnClick`-style attribute on the node and as an event
  wiring used for workflow extraction.
- `this.Controls.Add(this.X)` / `this.panel1.Controls.Add(this.X)` → parent/child
  edges (`this.Controls` with no leading `.X` means the parent is the
  Form/UserControl root itself).
- Bare `this.Name = "..."` / `this.Text = "..."` (not `this.X.Prop = ...`) are
  captured as attributes on the root node (the Form/UserControl itself).

**Custom controls**: pass a `customControlLookup` dictionary (class name →
its own `Designer.cs`/`.cs` source) and any field whose declared type matches
a key in that dictionary gets its *own* control tree spliced in as children,
recursively, instead of being left as an opaque leaf — this is what the
sample's `actorSearchControl1` demonstrates.

### Members

`WinFormsMemberExtractor` walks the code-behind (non-Designer) partial class
and extracts properties, fields, events, and methods — skipping
`InitializeComponent`/`Dispose` and any field that's actually a control
backing field (those are already represented in the control tree, matching
how the aspx sample doesn't list `txtSearch` etc. as `Members`).

`MemberIr.Kind` values (0/1/2/3 for Property/Field/Method/Event) are a
best-effort reconstruction — the sample IR files only ever showed Kind 0
(properties) and Kind 2 (methods), so double check this against the real
`WebFormsMigrator.IR.Models.MemberKind` enum if you have it, and swap
`Models/WinFormsIrModels.cs` for a `using WebFormsMigrator.IR.Models;` once
you drop this into the real solution.

### Workflows

For every `Control.Event → HandlerMethod` wiring found in the designer,
`WinFormsWorkflowExtractor` looks at the handler body for direct calls to
other known methods on the class, and builds a `WorkflowIr` with:

- `Steps` ordered by the *declaration order of the called methods in
  `Members`* (this, not call order in the body, is what reproduces the step
  ordering seen in `complete.ir.json`'s `btnSave.OnClick` and
  `gvActors.OnRowCommand` workflows).
- `Semantic` = the **trigger control's own** inferred semantic label if it
  has one (e.g. `btnSave` → "Persists form data (create or update)"),
  falling back to `"Runs N step(s): ..."` only when the control itself has
  no single clear semantic (e.g. a grid whose one handler covers both Edit
  and Delete commands, matching `gvActors.OnRowCommand` in the sample).

These two behaviors were reverse-engineered from `complete.ir.json` rather
than read from source, since the actual `NavigationWorkflowExtractor.cs`
wasn't provided — recheck against it if you have access.

### Navigation

WinForms has no `NavigateUrl`/`Response.Redirect`, but has the analogous
"this changes what's on screen" calls: `SomeForm.Show()` /
`.ShowDialog()`, `this.Close()`/`.Hide()`, `Application.Exit()`.
`WinFormsNavigationExtractor` looks for these inside handler bodies.

### Not attempted

- `CssAssets`/`JsAssets` stay empty arrays — no WinForms equivalent, but kept
  for JSON-shape parity with `complete.ir.json`.
- No layout/rendering info (Anchor/Dock docking behavior isn't turned into a
  visual layout — that'd be the WinForms equivalent of backlog item 6a.3's
  simple-render mode, not attempted here).
- Cross-file member resolution (e.g. a handler defined in a `partial class`
  spread across more than the two files you pass in) isn't handled — pass the
  file(s) that actually contain the handler bodies.

## Integrating into the real WebFormsMigrator solution

1. Delete `Models/WinFormsIrModels.cs` and add
   `using WebFormsMigrator.IR.Models;` to the four files in `Extraction/`
   instead, so this reuses your real `PageIr`/`ControlNodeIr`/etc. types.
2. Move `Extraction/*.cs` into a new `WinFormsAnalyzers` folder inside the
   `WebFormsMigrator` console project (parallel to the existing `Analyzers/`
   folder holding `ControlTreeIrExtractor`, `RoslynMemberExtractor`,
   `NavigationWorkflowExtractor`).
3. If you want it to run through the existing `IIrExtractor` pipeline instead
   of the standalone `Program.cs` here, wrap `WinFormsIrExtractor.Extract()`
   in a class implementing `IIrExtractor` (from
   `WebFormsMigrator.IR.Contracts`) and register it in `IrPipelineRegistry`
   alongside a new `WinFormsProjectAnalyzer` that discovers `.Designer.cs` /
   `.cs` pairs and identifies `UserControl`-derived types as custom controls
   (this is the piece from backlog item 6a.2 parallel to `ProjectAnalyzer`
   for Web Forms).
4. Add `Microsoft.CodeAnalysis.CSharp` as a `PackageReference` on the
   `WebFormsMigrator` project if it isn't already there (it likely already
   is, since `RoslynMemberExtractor` depends on it).

## Files

```
WinFormsIr/
├── WinFormsIr.csproj
├── Program.cs                              # standalone CLI
├── Models/WinFormsIrModels.cs              # IR POCOs (delete if reusing real ones)
├── Extraction/
│   ├── SemanticKeywords.cs
│   ├── WinFormsDesignerParser.cs           # InitializeComponent() -> control tree
│   ├── WinFormsMemberExtractor.cs          # code-behind -> Members
│   ├── WinFormsWorkflowExtractor.cs        # event wiring -> WorkflowIr
│   ├── WinFormsNavigationExtractor.cs      # Show/Close/Exit -> NavigationIr
│   └── WinFormsIrExtractor.cs              # orchestrator, entry point
└── Sample/                                 # a WinForms Form + custom UserControl
    ├── ActorManagementForm.Designer.cs
    ├── ActorManagementForm.cs
    ├── ActorSearchControl.Designer.cs
    └── ActorSearchControl.cs
```
