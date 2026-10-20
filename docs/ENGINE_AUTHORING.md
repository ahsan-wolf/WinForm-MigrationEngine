# Engine authoring (P0)

## Add an engine
1. New project under `src/` referencing `Migrator.Engine` (contracts only) and importing `build/IrReference.props`.
2. Implement `ISourceEngine` or `ITargetEngine` (both extend `IEngine`: `Id`, `Version`, `SchemaVersion`).
3. Add one public `IEngineModule` that registers it. Optional constructor `(EngineOptions options)` receives `options` from `engines.json`.
4. List it in `engines.json`. Nothing in `Migrator.Engine` changes.

```json
{ "id": "react-app", "enabled": true, "assembly": "Migrator.Target.React", "options": { } }
```

## Loading rules
- `engines.json` is an allow-list. A DLL in `plugins/` that isn't listed is reported (warning) and never executed.
- `assembly` is resolved from the app directory first (built-in; referenced by the CLI composition root), then `plugins/<assembly>.dll`, then `plugins/<assembly>/<assembly>.dll`.
- Plugins load in their own `AssemblyLoadContext`; `Migrator.Engine`, the IR package and the framework are shared with the host so contract types unify.
- Each engine loads in isolation: a failure becomes an `[ERROR]` diagnostic and the remaining engines still load.
- Schema check: same major; while major is `0`, the minor must also match. Otherwise the registry refuses the engine.

## Shared IR reference
`build/IrReference.props` is the only place that decides how projects consume `WebFormsMigrator.IR` and `WebFormsMigrator.IR.Contracts`.
By default it ProjectReferences a sibling checkout of the real WebFormsMigrator repo (`..\..\MIgration Engine 2` relative to this repo);
override with `-p:WebFormsMigratorRoot=<path>`. Nothing is copied. When the packages are on a feed, use
`-p:UseSharedIrPackages=true -p:SharedIrVersion=<x.y.z>`. Our contracts use the real `IIrExtractor` (`Facet`, `Extract(context, PageIr)`)
and `IIrGenerator` (`Target`, `Generate(PageIr, outputDirectory)`).

### Known schema gaps for WinForms (changes belong in the WebFormsMigrator repo, additive only)
- `NavigationIr` is web-shaped. **Resolved without touching the shared IR:** WinForms entries reuse the existing fields (`SourceControlId` = trigger control, `TriggerEvent` = event, `Target` = target form, `TargetType` = `Show|ShowDialog|Close|Hide|Exit`). Create and read them through `Migrator.Engine.Navigation.WinNavigation` (typed `TryGetKind`, `TargetForm`, `FindWorkflow`); don't hand-write the strings. If richer data is ever needed (DialogResult, ctor args), add a WinForms-owned sidecar file keyed by control + event.
- `MemberIr.Kind` / `Visibility` are enums (`Visibility` includes `Protected`/`Internal`), not the ints in the prototype.
- `PageIr.RootControl` is nullable; the prototype assumed non-null.
- The real projects target `net8.0` and `IR.Contracts` pulls AngleSharp + Roslyn 4.8.0 into every consumer.

## WinForms source engine (P1, first slice)
Pipeline (declared by the engine, order matters): `controlTree` -> `members` -> `semantics` -> `workflows` -> `navigation`.
Later facets read earlier ones from the `PageIr` only (event wiring is read back from `Attributes["OnClick"]` etc.).
`IProjectAwareSourceEngine.CreateExtractors(analysis)` lets an engine build extractors that need the whole project
(UserControl inlining); `Migrator.Engine.Pipeline.SourceIrRunner` runs them per unit with per-unit failure isolation.

### Designer constructs captured (no schema change: facts live in `Attributes`)
| Construct | Result |
|---|---|
| `Controls/Items/DropDownItems/TabPages/Columns .Add\|AddRange`, `Panel1/Panel2.Controls.Add` | child nodes under the right parent (menus, tabs, split panels, grid columns); non-`Controls` slot in `ParentSlot` |
| `TableLayoutPanel`: `Controls.Add(c, col, row)`, `SetColumnSpan/SetRowSpan`, `ColumnStyles/RowStyles` | `TableLayout.Column/Row/ColumnSpan/RowSpan` on the child; `ColumnStyles`/`RowStyles` JSON on the table |
| `DataGridView` columns + designer-local `DataGridViewCellStyle` | column nodes with `HeaderText`, `DataPropertyName`, `Width`, ...; `DefaultCellStyle.Format` |
| `Items.AddRange(new object[]{"a","b"})` / `Items.Add("x")` (combo, list, combo column) | `Items` = JSON string array |
| `DataBindings.Add(new Binding("Text", src, "Member"))` | `DataBinding.Text` = `src.Member` |
| `toolTip.SetToolTip(ctrl, "text")` | `ToolTip` on the control |
| `resources.ApplyResources(...)` + sibling `.resx` | `Text`, `ToolTip`, `ToolTipText`, `HeaderText`, `PlaceholderText` merged when not set in code (code wins); geometry, binary and `>>` metadata entries ignored; a malformed `.resx` is skipped, not fatal |

Dotted/pseudo attribute keys (`ParentSlot`, `TableLayout.*`, `DataBinding.*`, `DefaultCellStyle.*`, `Wired.<Event>`, `Image.Data` / `Image.Format`) cannot collide with C# property names. `Wired.<Event> = "code"` marks event wiring that came from a subscription in the code-behind (see `WinFormsCodeWiringExtractor`: method groups, `new EventHandler(m)`, lambdas and anonymous methods, the last two as synthetic handler members `<ctl>_<Event>_lambda<n>`); designer wiring wins when both exist, and subscriptions on controls that are not in the tree are left alone.
Anything else in `InitializeComponent` goes to `project.ir.json` `Shared.Unsupported` as `"{Unit}: {statement}"` (via the optional
`IUnsupportedReporter`; capped at 50 per unit) — never silently dropped. Partially understood statements are reported too, e.g.
non-literal table coordinates (`Controls.Add(btn, dynCol, 1)` keeps the literal row and reports the rest).

Known limitations (carry into reports): handlers in a third partial file are not resolved; only direct calls become workflow
steps (not 2+ levels), ordered by call order; a handler name inside an inlined UserControl that equals a form method name
would be attributed to the form; a custom base class stays as `RootControl.TagName` (e.g. `BaseForm`) rather than resolving to `Form`;
`Controls.Add` order in designer code is reverse z-order, so child order is not visual order (generators order by TabIndex/Dock, Skill 4);
culture-specific `.resx` files, `ImageList` streams and external (`ResXFileRef`) resources are not read (controls using them get a placeholder icon); readable byte-array images are kept as `Image.Data` (base64) and `Image.Format` (png, gif, jpg, bmp, ico, svg), the form icon as `Icon.Data`; ToolStrip items added through helper
methods or loops, and any layout computed at runtime, are not seen.

### Navigation extraction (found on the ECMNavigator pilot)
Forms are usually opened from **helper methods** that handlers call (`using (frmX f = new frmX(..)) { f.ShowDialog(); }`), so navigation sites are
searched in **every** method and attributed to each wired control event that reaches them through calls to the form's own methods
(transitive, depth <= 8, cycle-safe). Sites no wired handler reaches (handlers wired in code-behind such as `Form_Closing`) are kept against the
root control with `TriggerEvent = "Method:{name}"`. Not navigation: `MessageBox`, `OpenFileDialog`, `SaveFileDialog`, `FolderBrowserDialog`,
`ColorDialog`, `FontDialog`, `PrintDialog`, `PageSetupDialog`, `PrintPreviewDialog`; and `Close`/`Hide` only count on the form itself
(`conn.Close()` / `reader.Close()` are far too common to guess). Effect on the pilot: `frmNavigator` -> 16 forms (was 0).

## Custom controls and inheritance (any project)
- **Custom controls** (a class derived from a standard control, in the project or in a class library it references) keep their declared type in `TagName`; the source engine follows the base-type chain through every class of the project
  (`ProjectAnalysis.TypeBases`) and records the standard type in the control attribute `ResolvedType` (`System.Windows.Forms.TextBox`). Generators map by `ControlTypes.Effective(node)` (resolved type, else `TagName`), so a project-specific
  `FancyTextBox` or a vendor grid with source behaves like its base. Types whose chain ends outside the project (a vendor control without source, a report viewer) stay unresolved and become `WfUnsupported` until a mapping exists.
- **Reusable controls with a designer**: a class that resolves to any standard control other than a Form (for example a combo box with its own designer) is a user-control unit, not a form.
- **Code inheritance**: members (fields, properties, methods, events) of project base classes are merged into each derived unit, marked `inherited:<Base>` in `MemberIr.Attributes`; the derived class and closer bases win; constructors, designer
  boilerplate and the base designer partial are not merged. Known limits: controls declared in a base class designer (visual inheritance) are not merged; every derived unit carries its own copy of the base members (translated once per unit).
- `migrate --units all` generates every form and user control (needed when a shell opens modules dynamically, e.g. `Activator.CreateInstance(type)` from menu data, which static navigation extraction cannot see).
- Support classes are pruned to those the generated pages reach (directly or through each other); classes deriving from controls/components are UI and are never support classes.

## Control fallbacks and Windows bypass (data-driven, project-independent)
- **Fallback controls** (`ui-library/ui-spec/fallbacks.json`): a WinForms control (resolved type) with no component in `controls.json` is rendered by the closest component: ToolStripTextBox -> `WfTextBox`, anything else -> `WfPanel` (has children) or `WfLabel` (text or `[Type]`).
  (DateTimePicker, RadioButton, NumericUpDown, TabControl/TabPage and LinkLabel are real components now: `WfDateTimePicker` with `kind` from `Format`/`CustomFormat`, `WfRadioButton` with a `group` = parent container id and exclusivity written into the page model, `WfNumericUpDown`, `WfTabs`/`WfTabPage` with `SelectedIndex` in the model when code uses it, `WfLinkLabel`.)
  `propertyAliases` / `eventAliases` rename members (`Value` -> `Text`, `ValueChanged` -> `TextChanged`) so code that reads `dt.Value` reads the replacement's state. The generator rewrites the node in memory (`ResolvedType` = the replacement's type, `Fallback.From`, `Fallback.Alias.*`),
  adds `/* FALLBACK: <Type> */` to the element and reports category `fallback-control`. Edit the file to change a mapping; no rebuild. When a real component is added to `ui-spec`, remove its fallback entry.
- **Windows bypass** (`rules/windows-bypass.json`, option `bypassRulesPath` of the target engine): types (optionally with specific members) and namespaces a browser cannot use. A *simple* statement (expression, declaration, return, throw) that uses one becomes
  `// BYPASSED — Windows-specific API (...)` with the original kept; declared names are poisoned so dependents fall back; the rest of the method translates. Statements inside conditions are not bypassed (they keep their TODO). Reported in a "Windows-specific code bypassed" section.
  A project copies the file, adds its own entries and points `bypassRulesPath` at it.

## Component viewer (`migrate --preview`)
`GenerationOptions.Preview` makes the React target write `<out>/react-viewer` instead of the functional app (`ReactAppGenerator.RunPreview`): all units, no entry form, no contracts/translation/support/legacy copies. `PageGenerator` runs with `preview: true`:
`GeneratePreview` emits `export function XPage() { return (...) }` importing only `Wf*` from the UI library; `BuildEvents`, `Prescan`, the model, dialogs, ctor props, the `Visible` wrapper and `SeedListViewSamples` are skipped; grids and list views get constant columns/rows
(designer columns, else `Column 1..3`; shared builders `GridColumns`/`GridSampleRows`/`ListColumnDefs` with the functional mode); combo boxes without items get two sample items; unnamed labels/buttons show `[controlId]`; `ApplyFallbacks` still applies.
`GenerationOptions.AppShell` (`--appshell`, implies Preview) selects the second shell style: `ViewerShell.Write(..., appShell: true)` writes `src/appshell.css` + `src/main.tsx` from `AppShellTemplates` (bordered window, caption + status bar, hyperlink navigation pane with splitter, form-style host frame) into `<out>/react-appshell`; the manifest (plus `shell.title`, the project folder name) and the visual-only pages are identical to the viewer's, so a new shell style is just another template pair.
The visual-only pages pass `value`/`checked` without change handlers, so the library inputs (`WfTextBox`, `WfComboBox`, `WfCheckBox`, `WfRadioButton`, `WfDateTimePicker`, `WfNumericUpDown`) render **uncontrolled** (`defaultValue`/`defaultChecked`) when nothing listens for changes: the viewer's and shell's fields show their designed text and accept typing; when a handler is passed they stay controlled.
`ViewerShell` writes `src/manifest.ts` (`ViewerUnit`: id, component, file, kind form|control, title, group = source folder, source, controls, fallbacks[], unsupported[], designWidth/designHeight from the root layout, error?), `viewer.css` and the shell `main.tsx`
(lazy per-unit pages through `import.meta.glob`, per-selection error boundary, hash routing, filter, prev/next, design-size and background toggles). The shell is generated output, not a project in this repository (CLAUDE.md §0.3). A target that adds a viewer implements the same `Preview` option.

## Referenced projects, library classes and server-side code (any project)
- **Referenced projects outside the source folder.** The analyzer follows `ProjectReference`s that point outside `--source` (generated entity layers, shared libraries), transitively (cap 40; a project whose folder contains the source folder is skipped).
  Their classes join the analysis (`ProjectAnalysis.AdditionalRoots`, listed in `project.ir.json` `Source.AdditionalRoots` and printed by the CLI); their forms are not units. `--no-external-projects` turns it off.
- **Library classes are shape only.** Support classes from those folders carry `SupportClassIr.External = true`: they are emitted with typed fields/properties, method stubs that accept any arguments and an any-argument constructor (their bodies are never translated),
  reachability follows their signatures only, and members the base chain already declares are not redeclared. This keeps a large generated entity layer small (tens of thousands of lines instead of hundreds of thousands) and still gives pages typed entities.
- **Support class details.** Partial classes (generated + hand-written halves) are merged into one; interfaces are not support classes; TypeScript inheritance is real (`class A extends B`, bases defined first, `super(...)` placeholders, hidden base members keep the base's declaration);
  instance fields never default to `new T()` (object graphs can be cyclic), static fields may.
- **Server-side code** (`rules/server-bound.json`, option `serverBoundRulesPath`): a method whose body uses a data-access / file-system / configuration marker becomes a typed stub; a class where at least `classThreshold` of its methods do is not generated nor followed. The report lists both under
  "Server-side code left out (backend work)": it is what a REST layer must provide.
- **External assemblies.** `IDependencySourceEngine` lists referenced binary assemblies (classic `<Reference>` with a HintPath) in `project.ir.json` `Shared.ExternalAssemblies { Name, Path, Exists, ReferencedBy }`, the candidates for `migrator contract` (metadata only).
- **ComboBox** members: when code touches `Items`, `SelectedIndex`, `SelectedItem` or `SelectedValue` the combo keeps `items` / `selectedIndex` in the page model, with accessors `text` and `selected` over them and a `clear()` method (all kept consistent on any write); `Items.Add/AddRange/Insert/RemoveAt/Remove/Clear/Contains/IndexOf` translate.

## Compile gate: statement-level demotion
`tsc` errors are demoted at the finest safe grain: one failing *simple statement* (a line ending in `;` that does not continue a previous line) is commented out on its own with `// TODO: not translated — this statement fails the TypeScript check: <original line>`, the method becomes Partial,
and locals that lost their last reader are renamed `_x`; only a failing header, block or continuation line demotes the whole method (also after 30 statements in one method). A statement is addressed as `Method#n`, n = its index among the body's code lines of the first emission
(`LineDemotion`), mapped against the emission each tsc round refers to, so addresses stay valid over up to 10 rounds. Effect on the second pilot: about 450 methods moved from "not translated" to "partial".

## IR files (CLAUDE.md §4.1)
`migrator ir` writes, per Form/UserControl, `<out>/ir/{Name}/{Name}.{facet}.ir.json` for `complete` (source of truth), `controls`,
`control-properties`, `code-behind` and `layout`, plus `<out>/ir/project.ir.json`. Every facet is a pure function of the
`PageIr` (`IIrProjection.Project(PageIr)`), so facets cannot diverge from `complete`. The host writes the generic three; the
source engine contributes technology-specific ones through the optional `IIrProjectionProvider` (WinForms: `layout`).

- `layout`: `Dock`, `Anchor[]`, `Location{X,Y}`, `Size{Width,Height}`, `TabIndex`, `Margin`/`Padding{Left,Top,Right,Bottom}`, `Visible`,
  `Enabled`. `null` = not set in the designer (WinForms default applies) **or** not a plain literal (e.g. `new Point(offsetX, 5)`);
  the raw text is still in `control-properties`. The root's size is read from `ClientSize`.
  `TableLayout {Row, Column, RowSpan, ColumnSpan}` is on every control (all null when it is not in a table); the `TableLayoutPanel`
  itself carries `TableDefinition {Columns[], Rows[]}` of `{SizeType: Absolute|Percent|AutoSize, Value}` (null elsewhere).
- `project.ir.json` (`ProjectIr`, an additive wrapper owned by this repo): `Source {Technology, FrameworkVersion, RootPath, StartupForm}`,
  `Forms[]` / `UserControls[]` of `{Name, Kind, IrFile}` (`IrFile` relative to project.ir.json), `Shared {Settings, Resources, Dependencies,
  Unsupported}` (`Unsupported` is filled from unhandled designer statements; `Settings`, `Resources`, `Dependencies` are **empty lists for now**), and `Failures[]` for units whose extraction failed (never silently omitted).

## React target (`Migrator.Target.React`, first slice)
`migrator migrate --source <dir> --out <dir>` runs the ir step, then generates `<out>/react-app/` from the **IR folder only** (project.ir.json +
`complete` + `layout`), through the optional `IProjectTargetEngine` contract. It reads `ui-library/ui-spec/controls.json` as data (paths come from the engine's
`options` in engines.json): `TagName` -> `Wf*` component, `Attributes` -> props (`from`/`transform` in the spec), `OnX` -> the spec's canonical event.
Generated pages import only `@wfm/ui-react`.

| Concern | Behaviour |
|---|---|
| Scope | entry form (default: startup form) + forms reachable by `Show/ShowDialog` within `--depth` hops (default 1; `all` = everything reachable) |
| Layout | Dock/Location facts -> top chrome, free rows (clustered by Y, ordered by X), left/fill/right band, bottom chrome (`wfm-*` flex classes). No pixels. MenuStrip/ToolStrip default to top, StatusStrip to bottom |
| State | One **model per page**: `const vm = useWfModel({ txtSearch: { text: "", enabled: true }, ... })` (declared only when referenced). Holds user-editable inputs (TextBox/ComboBox/CheckBox), hidden controls (`visible`), grid `rows`, and every Text/Enabled/Visible/Checked/Value/ReadOnly the code-behind reads or writes (`btnSave.Enabled = false`, `this.Text = ...`). JSX binds `text={vm.lbl.text}`, `disabled={!vm.btn.enabled}`, `{vm.pnl.visible && ...}`. The model is a deep proxy: reads are immediate, writes re-render once, so translated WinForms code maps 1:1 (no stale closures, no setters). The generated eslint config turns `react-hooks/immutability` off on purpose; do not enable the React Compiler on generated code |
| Navigation | `ShowDialog` -> `await dialog.show(() => <XPage />, { title })`; `Close/Hide` -> `dialog.close()`; several targets in one handler -> first wired + TODO; target outside scope -> alert + TODO |
| Handlers | Not translated by default: each keeps its original C# as comments + TODO and the full code-behind is in `src/legacy/*.cs.txt`. With `--translate rules` every method of the page is translated (see below) |
| Not rendered | non-visual components (ImageList, ToolTip, BindingSource, Timer, file dialogs, ...) are listed in the report |
| Unsupported | no spec entry -> `WfUnsupported` + TODO + report entry; inlined user controls render as a panel |
| Grids | designer columns -> `WfGridColumn[]`; typed mock rows marked `// MOCK`; grids with columns created in code get a TODO |
| Code-behind | Each page is **two files**: `XPage.tsx` (markup only: JSX, `const { vm, handleSaveClick, … } = useXPageCode()`) and `XPage.code.tsx` (hook `useXPageCode`: dialog service, state, `vm` / `f` models, handlers and translated methods; exported top-level constants such as grid columns; exported `wiring` table `{ control, event, handler, method, source }`). Imports are decided per file from the text it contains. The compile gate maps TypeScript errors to methods through the code-behind file (`GeneratedPage.CodeSource`, `MethodRanges`); errors in the markup file are reported but never demote a method. The launcher lists only markup files |
| Output | `src/pages/*.tsx`, `src/pages/*.code.tsx`, `src/legacy/*.cs.txt`, app shell (package.json, vite, tsconfig, eslint), `MIGRATION_REPORT.md` |

Known limits: `Show()` opens as a dialog (no router yet); no auth; TreeView/Tabs/RadioGroup/DatePicker have no library component yet
(they become `WfUnsupported`); the generated app links the local `@wfm/ui-react` with `file:` (Vite `resolve.dedupe` keeps a single React).

## Assembly contract (`migrator contract`)
`migrator contract --assembly <dll> --out <dir>` reads a managed assembly's **public API from metadata only** (`System.Reflection.Metadata`; the file is parsed,
never loaded or executed, method bodies are not read) and writes `<dir>/<name>/contract.json` (types, kinds, base types, properties, method signatures, enum
values) and `data-access.json` (table / procedure **names** found in SQL-looking string literals; connection-string-like literals are only counted and discarded).
Use it for binary-only dependencies the app calls (e.g. a backend client library) to get exact contracts instead of inferring them from usage.
Write output to a git-ignored folder (`local-outputs/`); do not commit the assembly or its generated contract. The console prints counts only.

### External contracts in the React target (`migrate --contract`)
`migrate ... --contract <contract.json> [--contract-scope used|all]` (repeatable) turns an assembly contract (see "Assembly contract") into typed code inside the
generated app: `src/services/<assembly>/types.ts` (DTO interfaces + enum unions), `fixtures.ts` (deterministic fake rows) and `service.ts` (a Promise-based
`<Assembly>Service` interface with one method per **static facade method the generated forms call**, plus a `mock...Service` and an exported instance). Names mirror the C# API
(`ecmLibrary.UCM.CheckInList(...)`), so translated handlers map 1:1; every call is async. C# -> TS mapping follows CLAUDE.md §8 (`Guid`/`DateTime` -> `string`,
`DataTable` -> `Row[]` marked inferred, unknown external types -> `unknown`). With `--translate rules` translated pages and project classes call the service (`await ecmLibrary.UCM.X(...)`).

**Secrets never reach the browser.** Names matching password/secret/encryptionkey/licensecode/connectionstring/credential/token/api-key are handled by rule:
- a method whose name or return type is secret-like, or that takes a secret as a plain `string`/primitive (e.g. `password`), is **omitted** (listed as SERVER-ONLY in the report);
- a secret-looking **object** parameter (a credentials holder) is **stripped** and the operation is kept: the server supplies credentials;
- secret-looking DTO properties are omitted.
The filter is deliberately over-inclusive; the report lists everything it removed. Scope `used` scans the generated forms' members with a regex (aliases and
`using static` calls are missed; a miss only means a method is not emitted).

### Translation (`migrate --translate rules`)
Deterministic C# -> TypeScript for **every method of a generated page** (not only wired handlers: most handlers are one-line delegations and the logic is in helpers),
using Roslyn on the IR's method bodies. No LLM. Conventions (so the output is 1:1 with the C#): control properties are `vm.<id>.<prop>` (text, enabled, visible,
checked, value, readOnly), form fields are `f.<name>` (initial values from their C# initializers, recorded in `MemberIr.Body` for fields/properties), `MessageBox.Show` is
`await dialog.alert/confirm`, `ShowDialog()` is `await dialog.show(...)`, `Close()` is `dialog.close(result)`, contract facade calls are `await <instance>.<Facade>.<Method>(...)`
(credentials parameters already stripped), `main.WriteErrorLog(MethodBase.GetCurrentMethod(), ex)` is `console.error(...)`, `DateTime` is an ISO string, `string.Format` becomes a template literal (or
`formatString` for a non-literal format).

- **Per statement fallback.** A statement the rules cannot translate faithfully stays as a commented original with `// TODO: not translated — <reason>` (or `// TODO: wire to real API — ...` for
  references that cannot be resolved from the IR). A declaration that falls back *poisons* its name, so dependent statements fall back too (no dangling references).
- **Dropped, with a comment:** appearance-only writes (Anchor, Dock, Size, Font, colors, Cursor, Icon, ...), `Application.DoEvents()`. They are not TODOs.
- **Per method status:** Complete / Partial (TODOs inside) / Not translated (kept as commented C#; wired handlers keep the navigation-aware stub; untranslated helpers become typed no-op stubs so callers compile).
- **async:** a fixpoint over the call graph: any method that awaits (dialog, service) is `async`, all callers `await` it.
- **Compile gate** (`--compile-gate`: run `npm install` if needed; default: runs only when dependencies already exist; `--no-compile-gate`: skip): `tsc --noEmit` runs on the generated app, every translated
  method containing an error is demoted to commented C#, the page is regenerated (up to 5 rounds). An invented or mistyped API cannot ship. Errors outside translated methods are reported.
- **Lint-clean by construction:** `const` for locals never reassigned in the emitted text, `_name` for locals nothing reads, usage-based declarations/imports (`dialog`, `vm`, `f`, service, types), an
  eslint-disable note on functions nothing calls, `use*` method names renamed `doUse*`. The generated eslint config turns `react-hooks/immutability`, `no-extra-boolean-cast` and `preserve-caught-error` off on purpose.
- **ListView:** columns (designer `ColumnHeader` children) and items live in the model (`vm.<id>.columns`, `vm.<id>.items`, rows `{ text, subItems, checked, selected, tag }`); the page writes selection/check state back to
  the rows, so code reads them at once. Translated: `Items` / `SelectedItems` / `CheckedItems` / `SelectedIndices` / `Columns` (`.Count`, indexer, `foreach`, `Add`, `Insert`, `RemoveAt`, `Clear`, `SelectedItems.Clear()`),
  `new ListViewItem(...)` (text, `string[]`, text + image key), item `Text/Checked/Selected/Tag/Name`, `SubItems.Add/Clear/Count`, `SubItems[k].Text` (literal `k` for writes), own helpers returning `ListViewItem`.
  `Refresh/Invalidate/Update/Begin/EndUpdate/Focus` on any control are dropped with a comment. Not translated (stays TODO): `ListViewItemSorter`/`Sort`, `item.Index`, `Items.Remove(item)`, image lists, groups.
- **Project classes (`src/support/index.ts`):** the source engine also extracts the app's own non-form classes (`ISupportSourceEngine` -> `ir/support.ir.json`; static helpers like `main`, `Preferences`, `ECMConfiguration`,
  data classes, structs, nested enums). The React target emits them as one module (enums -> `export enum`, classes -> `export class` with static/instance members; overloads become `Foo`, `Foo_2`; constructors merged into
  the longest, extra parameters default to `null!`; properties with accessors -> `get`/`set`), translated by the same rules and compile gate as page methods (stubs keep every member callable and typed). Pages call them as in C#
  (`main.GetImageListKey(...)`, `Preferences.MyPreferences`, `new BrowseFolderOptions()`), awaited when async. Static fields without an initializer get sample data for contract types (`fixtures.X[0]`, `// MOCK`) or `new T()`
  for project classes. Contract types used only by these classes are emitted too. Limits: UI inside them (MessageBox, closing forms) and `ConfigurationManager`/registry/file access stay TODO; base types are not generated;
  `ref`/`out` methods are stubs; classes are emitted in an order that keeps static initializers valid (one module, no import cycles).
- **Constructor arguments -> props:** a form's constructor (`.ctor` member) parameters become optional props of the page. Contract data types default to a fixture row (`fixtures.UCMProfile[0]`, lists to the fixture list; `// MOCK`),
  and `field = argument;` statements seed the field model from the prop, so a page opens on its own and its Load handler has data. Other constructor statements are kept as `// TODO` notes. The opener passing real values is
  not wired yet (constructor arguments at `new Form(...)` call sites are still a TODO in the caller).
- **Sample rows:** a ListView whose rows/columns the translated code never writes gets 3 `// MOCK` sample rows (and sample columns when the designer has none) so it is visible.
- **GroupBox** maps to `WfGroupBox` (fieldset + legend). The launcher shows a page's render error instead of a blank screen.
- **Dev launcher:** the generated `main.tsx` opens the entry form by default; `?page=<Name>` (e.g. `?page=SearchResults`) renders one generated page on its own with mock data and `?page=` lists all pages. Needed for forms
  whose opener passes constructor arguments (not translated yet), which the entry form cannot reach.
- **Button.DialogResult:** clicking a button whose designer sets `DialogResult` runs its Click handler then `dialog.close(result)`, as WinForms closes the dialog (not part of translation; applies in every mode).

Pilot numbers (ECMNavigator, 17 forms, 706 methods): 375 complete (53%), 209 partial, 122 not translated (81 demoted by the gate); plus 33 project classes with 157 members (56 complete); `tsc` 0 errors, build and lint clean. What blocks the rest, from the report:
**grid and container members** (`dgv.Rows`, `pnl.Controls`), `ref`/`out` arguments (`TryParse`), `string.Format` with specifiers, event subscriptions in code, constructors of WinForms types. Everything is listed per method in `MIGRATION_REPORT.md`.
Rule-translated code is deterministic but **unreviewed**: check logic, async behaviour and every TODO.

## Extending the translator (rules)
1. Find the reason in `MIGRATION_REPORT.md` (top reasons) or the commented statement (`// TODO: not translated — <reason>`).
2. Add the rule in `Translation/MethodTranslator.cs` at the narrowest point: `Invoke` (calls), `MemberAccess` (members), `Create` (constructors), `Binary`/`Assign` (operators), `Stmt*` (statements). Throw `Untranslatable(reason)` for anything you cannot do faithfully (statement-level fallback keeps the original), `Dropped(reason)` for appearance-only code.
3. If it needs page knowledge, add it to `PageScope` (filled by `PageTranslator`) rather than a global. If it needs a runtime helper, add it to the library (`ui-react`) or `src/lib/csharp.ts` and let the generator import it from the emitted text.
4. Re-run the pilot with `--compile-gate`: `tsc` must stay at 0 errors and `npm run lint` at 0. A rule that produces code `tsc` rejects is demoted automatically, but fix the rule.
5. Record user-visible behaviour here and in SKILLS.md (Skill 14); keep determinism (same IR -> same output).

## Project classes IR (`support.ir.json`) conventions
`SupportFileIr { SchemaVersion, Classes[] }`; `SupportClassIr { Name, Kind class|struct|enum, IsStatic, Container, BaseType, RelativePath, Members[], EnumValues[], Unsupported[] }`. Members are `MemberIr`:
fields/auto-properties carry the initializer in `Body`; methods the full body; constructors are methods named `.ctor`; properties with accessors have `Body = "{ get {...} set {...} }"` and the attribute `mod:accessors`;
modifiers appear as `mod:const`, `mod:readonly`, `mod:auto`, `mod:async`. Nested types are flattened (`Container` names the outer class). A target must treat the file as optional.

## Commands
```bash
dotnet build WinFormsMigrator.sln
dotnet run --project src/Migrator.Cli -- engines [--config path/to/engines.json]
dotnet run --project src/Migrator.Cli -- ir --source <winforms-folder> --out <dir>   # six IR files per unit under <dir>/ir/<Form>/, <dir>/ir/support.ir.json, <dir>/ir/project.ir.json
dotnet run --project src/Migrator.Cli -- migrate --source <dir> --out <dir> [--translate rules] [--compile-gate] [--contract <contract.json>] [--entry <form>] [--depth <n|all>]
dotnet run --project src/Migrator.Cli -- contract --assembly <dll> --out <dir>      # public API from metadata only; use a git-ignored folder
```
