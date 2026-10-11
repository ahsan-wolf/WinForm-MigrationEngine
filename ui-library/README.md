# ui-library

Independent of the migration engine and of any IR (CLAUDE.md §5). Generated apps depend on it; the engine only reads `ui-spec` as data.

| Package | Role | Status |
|---|---|---|
| `ui-spec` (`@wfm/ui-spec`) | `controls.json` (names, WinForms types, prop/event mapping, a11y, example) + `tokens.css` | 0.1.0 — MVP set |
| `ui-react` (`@wfm/ui-react`) | React 18 + TS implementation of the spec | 0.1.0 — MVP set |
| `ui-angular`, `ui-html` | later (P2 continues) | not started |

MVP control set = what the pilot startup form needs: `WfForm`, `WfMenuBar`/`WfMenuItem`/`WfSeparator`, `WfToolbar`/`WfToolbarButton`, `WfStatusBar`,
`WfPanel`, `WfSplit`/`WfSplitPane`, `WfLabel`, `WfButton`, `WfTextBox`, `WfComboBox`, `WfCheckBox`, `WfProgress`, `WfPictureBox`, `WfDataGrid`,
`WfContextMenu`, dialog service (`WfDialogProvider` + `useWfDialog`: `alert`, `confirm`, `show`), `WfUnsupported`. Not yet: ListView, TreeView, Tabs,
RadioGroup, DatePicker, GroupBox (the generator emits `WfUnsupported` for them).

```bash
cd ui-library/ui-react && npm ci && npm run build && npm run lint
```
Use `@wfm/ui-react/styles.css` (tokens inlined) next to the JS import. Theme through the `--wf-*` CSS variables. Spec changes are additive; a breaking
change needs a major bump and a note in `docs/`. Mnemonics (`&File`) become an underline + `accessKey`. No tests, Storybook or demo here (the separate demo project covers that).
