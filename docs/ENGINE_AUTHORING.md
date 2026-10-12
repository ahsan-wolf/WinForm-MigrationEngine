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

Dotted/pseudo attribute keys (`ParentSlot`, `TableLayout.*`, `DataBinding.*`, `DefaultCellStyle.*`) cannot collide with C# property names.
Anything else in `InitializeComponent` goes to `project.ir.json` `Shared.Unsupported` as `"{Unit}: {statement}"` (via the optional
`IUnsupportedReporter`; capped at 50 per unit) — never silently dropped. Partially understood statements are reported too, e.g.
non-literal table coordinates (`Controls.Add(btn, dynCol, 1)` keeps the literal row and reports the rest).

Known limitations (carry into reports): handlers in a third partial file are not resolved; only direct calls become workflow
steps (not 2+ levels), ordered by call order; a handler name inside an inlined UserControl that equals a form method name
would be attributed to the form; a custom base class stays as `RootControl.TagName` (e.g. `BaseForm`) rather than resolving to `Form`;
`Controls.Add` order in designer code is reverse z-order, so child order is not visual order (generators order by TabIndex/Dock, Skill 4);
non-string `.resx` resources (images, icons) and culture-specific `.resx` files are not read; ToolStrip items added through helper
methods or loops, and any layout computed at runtime, are not seen.

### Navigation extraction (found on the ECMNavigator pilot)
Forms are usually opened from **helper methods** that handlers call (`using (frmX f = new frmX(..)) { f.ShowDialog(); }`), so navigation sites are
searched in **every** method and attributed to each wired control event that reaches them through calls to the form's own methods
(transitive, depth <= 8, cycle-safe). Sites no wired handler reaches (handlers wired in code-behind such as `Form_Closing`) are kept against the
root control with `TriggerEvent = "Method:{name}"`. Not navigation: `MessageBox`, `OpenFileDialog`, `SaveFileDialog`, `FolderBrowserDialog`,
`ColorDialog`, `FontDialog`, `PrintDialog`, `PageSetupDialog`, `PrintPreviewDialog`; and `Close`/`Hide` only count on the form itself
(`conn.Close()` / `reader.Close()` are far too common to guess). Effect on the pilot: `frmNavigator` -> 16 forms (was 0).

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
| Navigation | `ShowDialog` -> `await dialog.show(close => <XPage onClose={close}/>)`; `Close/Hide` -> `onClose?.()`; several targets in one handler -> first wired + TODO; target outside scope -> alert + TODO |
| Handlers | Not translated by default: each keeps its original C# as comments + TODO and the full code-behind is in `src/legacy/*.cs.txt`. With `--translate rules` every method of the page is translated (see below) |
| Not rendered | non-visual components (ImageList, ToolTip, BindingSource, Timer, file dialogs, ...) are listed in the report |
| Unsupported | no spec entry -> `WfUnsupported` + TODO + report entry; inlined user controls render as a panel |
| Grids | designer columns -> `WfGridColumn[]`; typed mock rows marked `// MOCK`; grids with columns created in code get a TODO |
| Output | `src/pages/*.tsx`, `src/legacy/*.cs.txt`, app shell (package.json, vite, tsconfig, eslint), `MIGRATION_REPORT.md` |

Known limits: `Show()` opens as a dialog (no router yet); no auth/mock services; TreeView/Tabs/RadioGroup/DatePicker/GroupBox have no library component yet
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
`DataTable` -> `Row[]` marked inferred, unknown external types -> `unknown`). Pages do not call the service yet.

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
`await dialog.alert/confirm`, `ShowDialog()` is `await dialog.show(...)`, `Close()` is `onClose?.(result)`, contract facade calls are `await <instance>.<Facade>.<Method>(...)`
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
- **Button.DialogResult:** clicking a button whose designer sets `DialogResult` runs its Click handler then `onClose(result)`, as WinForms closes the dialog (not part of translation; applies in every mode).

Pilot numbers (ECMNavigator, 17 forms, 706 methods): 375 complete (53%), 209 partial, 122 not translated (81 demoted by the gate); plus 33 project classes with 157 members (56 complete); `tsc` 0 errors, build and lint clean. What blocks the rest, from the report:
**grid and container members** (`dgv.Rows`, `pnl.Controls`), `ref`/`out` arguments (`TryParse`), `string.Format` with specifiers, event subscriptions in code, constructors of WinForms types. Everything is listed per method in `MIGRATION_REPORT.md`.
Rule-translated code is deterministic but **unreviewed**: check logic, async behaviour and every TODO.

## Commands
```bash
dotnet build WinFormsMigrator.sln
dotnet run --project src/Migrator.Cli -- engines [--config path/to/engines.json]
dotnet run --project src/Migrator.Cli -- ir --source <winforms-folder> --out <dir>   # six IR files per unit under <dir>/ir/<Form>/ + <dir>/ir/project.ir.json
```
