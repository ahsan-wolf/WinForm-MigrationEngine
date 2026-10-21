# ui-library

Independent of the migration engine and of any IR (CLAUDE.md §5). Generated apps depend on it; the engine only reads `ui-spec` as data.

| Package | Role | Status |
|---|---|---|
| `ui-spec` (`@wfm/ui-spec`) | `controls.json` (names, WinForms types, prop/event mapping, a11y, example) + `tokens.css` | 0.3.0 |
| `ui-react` (`@wfm/ui-react`) | React 18 + TS implementation of the spec | 0.3.0 |
| `ui-angular`, `ui-html` | later (P2 continues) | not started |

MVP control set = what the pilot startup form needs: `WfForm`, `WfMenuBar`/`WfMenuItem`/`WfSeparator`, `WfToolbar`/`WfToolbarButton`, `WfStatusBar`,
`WfPanel`, `WfGroupBox` (fieldset + legend), `WfTabs`/`WfTabPage` (hidden / disabled pages, page icons), `WfDynamicHost` (user controls added in code, with `wfDynamicControl`), `wfImageList*` helpers (image resources by key or index for tree, list and tab icons), `WfDateTimePicker` (native date/time input, optional min/max), `WfRadioButton` (grouped), `WfNumericUpDown`, `WfLinkLabel`, `WfSplit`/`WfSplitPane`, `WfLabel`, `WfButton`, `WfTextBox`, `WfComboBox`, `WfCheckBox`, `WfProgress`, `WfPictureBox`, `WfDataGrid`,
`WfListView` (details list: columns, rows with sub items, row selection with Ctrl/Shift and arrow keys, check boxes; controlled, with `wfListItem` / `wfListItemFromArray` helpers), `WfMaskedTextBox` (WinForms mask applied while typing), `WfRichTextBox` (plain-text area), `WfMonthCalendar` (one-month picker, ISO value), `WfListBox` (always-open list; same page model as the combo box), `WfCheckedListBox` (native check box per item), `WfDomainUpDown` (up/down through a list), `WfTrackBar` / `WfScrollBar` (native range input), `WfDropDownButton` (toolbar button with a menu, optional split), `WfTreeView` (lazy loading, node icons, expand/collapse, selection, check boxes, arrow keys; nodes are plain data with `wfTreeNode` / `wfSetExpanded` helpers), `WfContextMenu`, dialog service (`WfDialogProvider` + `useWfDialog`: `alert`, `confirm`, `show`), `useWfModel` (imperative-compatible state: immediate reads, one batched re-render per write burst), `WfUnsupported`. Inputs are controlled when you pass their change handler (`onValueChange`, `onCheckedChange`, `onSelectionChange`) and uncontrolled otherwise (the value is the starting value). `ui-spec/fallbacks.json` lists which component stands in for a WinForms control that has none yet. Not yet: TreeView, ListBox/CheckedListBox, RichTextBox, MaskedTextBox, MonthCalendar (the generator emits `WfUnsupported` for them).

```bash
cd ui-library/ui-react && npm ci && npm run build && npm run lint
```
Use `@wfm/ui-react/styles.css` (tokens inlined) next to the JS import. Theme through the `--wf-*` CSS variables. Spec changes are additive; a breaking
change needs a major bump and a note in `docs/`. Mnemonics (`&File`) become an underline + `accessKey`. No tests, Storybook or demo here (the separate demo project covers that).
