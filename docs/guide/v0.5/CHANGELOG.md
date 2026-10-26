# Changelog — 0.5

Reconstructed from the merged slices (PR numbers refer to the repository's pull requests). 0.1 covers 2026-10-10 … 2026-10-13, 0.2 covers 2026-10-13 … 2026-10-17.

## 0.5 — archetype controls and the modules catalog (23 Oct)
- `ControlArchetypes` (`Migrator.Target.Core`): classifies every generated unit as Grid, Tree, Menu, Toolbar, List or Form by its designer first and by name patterns (`rules/control-archetypes.json`, option `--archetypes`) second; `Synthesize` adds the missing primary control or its columns / nodes / items from literals in the code or from the row type of a data source (marked, listed in the report).
- `/modules` catalog page for React (`src/Modules.tsx`) and Angular (`modules.component.ts`): units no code opens, grouped by archetype, opened in a content area; `--catalog`, implied by `--units all`; `ITargetBackend.WriteApp` takes the catalog entries.
- Report section "Archetypes". Generated pages of every existing sample and pilot are byte-identical; only reports gain the section and `--units all` apps gain the catalog.
- Versions: React and Angular targets 0.5.0. Guide chapter [10-archetypes.md](10-archetypes.md).
- Single-file forms: the analyzer finds classes that declare `InitializeComponent` themselves; the designer parser reads decompiled style (`base.` roots, locals as collection aliases and child arrays), `MainMenu` / `MenuItem`, the old `DataGrid` with table styles; short framework type names are qualified; the startup form is found in any file; `fallbacks.json` `namePatterns` / `nonVisualPatterns` for third-party controls; `OleDb*` markers in `rules/server-bound.json`.
- `docs/handbook/`: summary, setup, ways to run, detailed workflow, options reference, reading results, troubleshooting, glossary.
- `tools/migrator-ui` (host + web), guide chapter [11-ui.md](11-ui.md): a window to choose IR / working app / app shell / viewer, run it, read the log and report, install and start the generated app.

## 0.2 — controls, popups, icons, app shell dialogs (14–17 Oct)
- #30 Docs: versioned guide v0.1.
- #31 Real components for masked text, rich text, month calendar and list box; duplicate canonical events emitted once; `ToString()` on model-backed members; `useWfModel` keeps one identity for stored model objects.
- #32 Real `WfTreeView` with `Nodes` / `SelectedNode` / `TreeNode` translation.
- #33 Standard-control wave: checked list box, up/down box, track and scroll bars, drop-down buttons; native `<dialog>` popups with `MessageBox` button sets and icons; date min/max; menu and toolbar icons from `.resx` images (ICO and BMP converted to PNG, placeholders otherwise); data grid editing, paging, column resize, row keys; tree lazy loading; `readOnly={false}` makes grid columns editable.
- #34 `dialog.close(result?)` and `onRootClose`; generated pages open with `dialog.show()` and close with `dialog.close()`; popups that translation lost are restored (`nav-mock-open`, `nav-mock-close`); title-bar ✕; open menus paint over the toolbar.
- #35 App shell opens and closes dialogs (visual-only pages).
- Versions: engines, `ui-spec` and `@wfm/ui-react` 0.1.0 → 0.2.0; IR schema stays 0.1.0. Guide v0.2 with decision records and the roadmap.

## 0.4 — shared core, ui-core, angular-app (21–22 Oct)
- `Migrator.Target.Core`: translator, contracts, spec catalog, IR folder, naming, layout, icons, runtime helpers, `PageGenerator` and `ProjectGenerator` (with `ITargetBackend`, `IPageGenerator`, `IPageAssembler`) moved out of the React target; React output byte-identical for both pilots and all samples.
- `@wfm/ui-core` (grid model, image list, tree, list items, checked list helper, mnemonic, mask, `wf.css`); `ui-react` re-exports it; generated apps declare it.
- `@wfm/ui-angular` and `Migrator.Target.Angular`: `migrate --target angular-app` (standalone components, router, zone.js), compile gate through `ngc` with strict templates, 36 controls, dialog service, dynamic host, image lists; `engines.json` entry; guide chapter [09-angular.md](09-angular.md).
- Compile gate: full paths, ANSI/pretty diagnostics, "could not run" instead of a false zero.
- Versions: React and Angular targets, `ui-core`, `ui-react`, `ui-angular` 0.4.0.

## 0.3 — page split, wiring in code, grids, tabs, images, routes (17–21 Oct)
- #37 Each page is a markup file plus a code-behind file (`X.tsx` + `X.code.tsx`) with an exported `wiring` table.
- #38 `WfTabs` is controlled only when a change handler is passed (a designer-set `SelectedIndex` no longer freezes the tabs).
- Event wiring from code-behind subscriptions (`WinFormsCodeWiringExtractor`, `source: 'code'`) and `Path.*` helpers in the generated `lib/csharp.ts`.

- Versions: engines, `ui-spec` and `@wfm/ui-react` 0.2.0 → 0.3.0; IR schema stays 0.1.0. Guide v0.3.
- Grid rows model: `Rows.Add/Clear/RemoveAt/Remove/Count`, `Cells[k].Value`, `SelectedRows`, `CurrentRow`, `Columns.Add` over `vm.<grid>.{columns,rows,selected}` with `wfGrid*` helpers; a ListView that no code or event uses is rendered as a grid.
- Tab pages: `TabPages.Add/Insert/Remove/RemoveAt/Clear/Contains/IndexOf/Count`, `TabCount`, `SelectedTab` (compare, assign, `.Name`/`.Text`) over `vm.<tab>.selectedIndex` and `vm.<page>.visible`; `WfTabPage` gets `visible`, `disabled` and `icon`.
- Controls added in code: `panel.Controls.Add(new UserControl(args))`, `Clear`, `RemoveAt`, `Count` over `vm.<panel>.controls` rendered by the new `WfDynamicHost`; constructor arguments become the component's props; such user controls join the generated scope; a user control's `Load` runs once on mount (`WfPanel` `onLoad`).
- ImageList: the `ImageStream` resource is decoded in managed code (`ImageListDecoder`) to one image per entry, written as `public/images/<unit>/<list>/<key>.png`; `keys`/`urls` in the page model with `Images.Add/Count/Clear/RemoveAt/RemoveByKey/ContainsKey/IndexOfKey/SetKeyName` and `il.Images[k]`; `ImageKey`/`ImageIndex` on tab pages, toolbar and menu items (at generation time) and on tree nodes and list items (`imageList` prop of `WfTreeView` / `WfListView`). IR pseudo-attributes `Images.Keys` / `Images.Data` / `Images.Size` (additive).
- `CellFormatting`: the handler runs for every shown cell with `e.Value` readable and writable (`onCellFormatting`, `WfGridCellEvent.value`); `e.CellStyle` is dropped as appearance. `int.TryParse(text, out x)` (and long, short, double, decimal, float) through `tryParseInt` / `tryParseNumber` in `lib/csharp.ts`.
- Routes: standard History API paths (`/`, `/<page-name>`, `/pages`) from a generated `src/lib/router.ts`; `<base href>` + Vite `base: './'` make asset URLs and links relative (icons are now `icons/…`, not `/icons/…`).
- Resources: `resources.ir.json` carries the referenced images of the project's `Resources.resx` (`Properties.Resources.X`), written to `public/images/resources/`; `--images <folder>` supplies images by name; the entry form's icon becomes the app's favicon (`<link rel="icon">`).
- Translator: `ref` arguments to methods that never assign the parameter, expression-bodied lambdas as arrow functions and common LINQ calls (`Where`, `Select`, `FirstOrDefault`, `Any`, `All`, `Count`, `OrderBy`, `Sum`, ...), `grid.DataSource = list` (and `== null`), `e.RowIndex` / `e.ColumnIndex` in grid cell handlers, `CellDoubleClick` (library: `onCellDoubleClick`, `WfGridCellEvent`).
- App shell and viewer: tab page icons from an ImageList show in the visual-only pages.

## 0.1 (10–13 Oct)

## Foundation (10 Oct)
- #1 Engine contracts, registry, `engines.json` allow-list, plugin loader, `WinNavigation`; shared IR referenced from the sibling checkout via `build/IrReference.props`.
- #2 WinForms source engine: designer parser and extractors on the real `IIrExtractor` pipeline; project analyzer.

## IR (10–11 Oct)
- #3 Facet files (controls, control-properties, code-behind, layout), `ProjectIr`, `IrFileWriter`.
- #4 Designer hardening: menus, tabs, split panels, table layouts, grid columns, items, data bindings, tooltips, `.resx` merge, `Shared.Unsupported`.
- #5, navigation fixes: overloaded handlers, navigation in helper methods, common dialogs excluded.

## React target and UI library (11–12 Oct)
- #6 `ui-spec` 0.1.0, `@wfm/ui-react` MVP, `react-app` target, `migrate` verb.
- #7, #9 `contract` verb (metadata only) and typed mock services from a contract.
- #13 `useWfModel`; #14 rule-based C#→TS translation with the compile gate.
- #15 `WfListView`; #16 dev page launcher; #17 `WfGroupBox`, constructor arguments as props.
- #18 Support classes (`support.ir.json`, `src/support`); #19 user guide, project documentation.

## Project independence and robustness (12–13 Oct)
- #20 Pilot names removed from engine code; namespace roots derived from the contract.
- #21 Custom-control resolution by base-type chain, inherited-member merge, `--units all`, support pruning.
- #22 Fallback components (`fallbacks.json`), Windows bypass rules, mock instances for unset statics.

## Viewing (13 Oct)
- #23 Component viewer (`--preview`). #24 WinForms-style app shell (`--appshell`).

## More controls and depth (13 Oct)
- #25 Control wave A: date/time picker, radio buttons with groups, numeric up/down, tabs, link label.
- #26 Referenced projects outside the source folder, partial-class merge, ComboBox model, shape-only library classes, class inheritance, server-bound rules, statement-level compile-gate demotion.
- #27 Inputs uncontrolled when unwired (visual-only pages accept typing).
- #28 UI control mapping document.

## Backend (13 Oct)
- #29 `backend` verb: read-only analyzer for class libraries, ASMX services and WSDL; `rules/backend-patterns.json`.

## Not yet built (as of 0.1; see [08-roadmap.md](08-roadmap.md) for 0.2)
Backend REST API generator; Angular and HTML+jQuery targets and libraries; LLM tier; real tree view, list box, rich/masked text, month calendar components.
