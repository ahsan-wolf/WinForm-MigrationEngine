# Changelog — 0.2

Reconstructed from the merged slices (PR numbers refer to the repository's pull requests). 0.1 covers 2026-10-10 … 2026-10-13, 0.2 covers 2026-10-13 … 2026-10-17.

## 0.2 — controls, popups, icons, app shell dialogs (14–17 Oct)
- #30 Docs: versioned guide v0.1.
- #31 Real components for masked text, rich text, month calendar and list box; duplicate canonical events emitted once; `ToString()` on model-backed members; `useWfModel` keeps one identity for stored model objects.
- #32 Real `WfTreeView` with `Nodes` / `SelectedNode` / `TreeNode` translation.
- #33 Standard-control wave: checked list box, up/down box, track and scroll bars, drop-down buttons; native `<dialog>` popups with `MessageBox` button sets and icons; date min/max; menu and toolbar icons from `.resx` images (ICO and BMP converted to PNG, placeholders otherwise); data grid editing, paging, column resize, row keys; tree lazy loading; `readOnly={false}` makes grid columns editable.
- #34 `dialog.close(result?)` and `onRootClose`; generated pages open with `dialog.show()` and close with `dialog.close()`; popups that translation lost are restored (`nav-mock-open`, `nav-mock-close`); title-bar ✕; open menus paint over the toolbar.
- #35 App shell opens and closes dialogs (visual-only pages).
- Versions: engines, `ui-spec` and `@wfm/ui-react` 0.1.0 → 0.2.0; IR schema stays 0.1.0. Guide v0.2 with decision records and the roadmap.

## Unreleased (0.3.0 line)
- #37 Each page is a markup file plus a code-behind file (`X.tsx` + `X.code.tsx`) with an exported `wiring` table.
- #38 `WfTabs` is controlled only when a change handler is passed (a designer-set `SelectedIndex` no longer freezes the tabs).
- Event wiring from code-behind subscriptions (`WinFormsCodeWiringExtractor`, `source: 'code'`) and `Path.*` helpers in the generated `lib/csharp.ts`.

- Grid rows model: `Rows.Add/Clear/RemoveAt/Remove/Count`, `Cells[k].Value`, `SelectedRows`, `CurrentRow`, `Columns.Add` over `vm.<grid>.{columns,rows,selected}` with `wfGrid*` helpers; a ListView that no code or event uses is rendered as a grid.

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
