# Changelog — 0.1

Reconstructed from the merged slices (PR numbers refer to the repository's pull requests). Dates 2026-10-10 … 2026-10-13.

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

## Not yet built
Backend REST API generator; Angular and HTML+jQuery targets and libraries; LLM tier; real tree view, list box, rich/masked text, month calendar components.
