# UI control mapping — which React component represents which WinForms control

As built, for the React target (`@wfm/ui-react`). The sources of truth are `ui-library/ui-spec/controls.json` (components), `ui-library/ui-spec/fallbacks.json` (stand-ins) and `ui-library/ui-spec/tokens.css` (theme); this page summarises them.
Generated pages import `Wf*` components only. Identical names, props and canonical events are the contract for the Angular and HTML+jQuery libraries (not started).

## 1. Components that represent a WinForms control directly (32)

| WinForms control(s) | React component | Props (from the WinForms property) | Events (from the WinForms event) |
|---|---|---|---|
| `Form`, custom base forms | `WfForm` | `title` (Text), `acceptButtonId`, `cancelButtonId` | `onLoad` (Load) |
| `MenuStrip` | `WfMenuBar` | children | — |
| `ToolStripMenuItem` | `WfMenuItem` | `text` (the `&` mnemonic becomes an underline and access key), `shortcut`, `checked`, `disabled` | `onClick` |
| `ToolStripSeparator` | `WfSeparator` | — | — |
| `ToolStrip` | `WfToolbar` | children | — |
| `ToolStripButton` | `WfToolbarButton` | `text`, `tooltip`, `disabled` | `onClick` |
| `StatusStrip` | `WfStatusBar` | children | — |
| `ContextMenuStrip` | `WfContextMenu` (wraps the control that owns it) | menu items | — |
| `Panel`, `FlowLayoutPanel` | `WfPanel` | `visible` | — |
| `GroupBox` | `WfGroupBox` | `title`, `visible`, `disabled` | — |
| `SplitContainer` | `WfSplit` + `WfSplitPane` | `orientation`, `splitterDistance` | — |
| `TabControl` / `TabPage` | `WfTabs` / `WfTabPage` | `selectedIndex` / `title` | `onSelectionChange` |
| `Label`, `ToolStripLabel`, `ToolStripStatusLabel` | `WfLabel` | `text` | — |
| `Button` | `WfButton` | `text`, `disabled` | `onClick` (a `DialogResult` also closes the dialog) |
| `LinkLabel` | `WfLinkLabel` | `text`, `disabled` | `onClick` (LinkClicked / Click) |
| `TextBox` | `WfTextBox` | `value`, `readOnly`, `maxLength`, `multiline`, `password` | `onValueChange` (TextChanged) |
| `ComboBox`, `ToolStripComboBox` | `WfComboBox` | `items`, `value`, `disabled` | `onSelectionChange` |
| `CheckBox` | `WfCheckBox` | `text`, `checked` | `onCheckedChange` |
| `RadioButton` | `WfRadioButton` | `text`, `checked`, `group` (the parent container: its radio buttons are mutually exclusive) | `onCheckedChange` |
| `DateTimePicker` | `WfDateTimePicker` | `value` (ISO text), `kind` = date / datetime / time (from Format / CustomFormat) | `onValueChange` |
| `MaskedTextBox` | `WfMaskedTextBox` | `value`, `mask` (WinForms mask applied while typing: `0` `9` `#` `L` `?` `A` `a` `&` `C`; other characters are literals), `readOnly` | `onValueChange` (TextChanged) |
| `RichTextBox` | `WfRichTextBox` | `value`, `readOnly` (plain text; formatting is not reproduced) | `onValueChange` |
| `MonthCalendar` | `WfMonthCalendar` | `value` (from `SelectionStart`, ISO date) | `onValueChange` (DateChanged / DateSelected) |
| `ListBox` | `WfListBox` | `items`, `value` (selected text; the page model also keeps `SelectedIndex` / `SelectedItem`, as for a combo box) | `onSelectionChange` |
| `TreeView` | `WfTreeView` (+ helpers `wfTreeNode`, `wfSetExpanded`) | `nodes` (page model: text, name, tag, checked, expanded, nodes), `selectedNode`, `checkBoxes` | `onSelectionChange` (AfterSelect), `onNodeCheckedChange` (AfterCheck), `onNodeClick`, `onNodeDoubleClick` |
| `NumericUpDown` | `WfNumericUpDown` | `value`, `min`, `max`, `step`, `decimals`, `readOnly` | `onValueChange` |
| `ProgressBar`, `ToolStripProgressBar` | `WfProgress` | `value`, `max` | — |
| `PictureBox` | `WfPictureBox` | `src`, `fit` | `onClick` |
| `DataGridView` (with its columns) | `WfDataGrid` | `rows`, `columns` (header, field, width, format), `selectionMode`, `readOnly` | `onRowSelect` (SelectionChanged), `onCellClick` (CellClick / CellContentClick / CellMouseClick) |
| `ListView` (with `ColumnHeader`, `ListViewItem`) | `WfListView` (+ helpers `wfListItem`, `wfListItemFromArray`) | `columns`, `items`, `checkBoxes`, `multiSelect` | `onSelectionChange`, `onItemCheckedChange`, `onItemActivate`, `onClick`, `onDoubleClick`, `onKeyUp`, `onColumnClick` |

Inputs are controlled when a change handler is passed and uncontrolled otherwise (the value is then only the starting value), so the visual-only viewer and app shell accept typing.

## 2. Services and helpers (not visual)

| Piece | Role |
|---|---|
| `WfDialogProvider` + `useWfDialog` | `alert`, `confirm`, `show(render)`: replace `MessageBox` and `ShowDialog` (DialogResult) |
| `useWfModel` | Per-page state as a deep proxy: reads are immediate, writes re-render once. `vm` holds control properties (text, enabled, visible, checked, value, items, selected index, rows), `f` holds form fields, so translated imperative WinForms code maps one to one |
| `WfUnsupported` | Last-resort visible placeholder for a control that cannot even get a fallback |

## 3. Stand-ins for controls without a component (`fallbacks.json`)

The closest component is rendered, marked `/* FALLBACK: Type */`, and listed in the report. Edit the data file to change a mapping.

| WinForms control(s) | Rendered as |
|---|---|
| `DomainUpDown`, `ToolStripTextBox` | `WfTextBox` (`Value` read as `Text`) |
| `CheckedListBox` | `WfListView` (flat list) |
| `TrackBar`, `HScrollBar`, `VScrollBar` | `WfProgress` |
| `ToolStripDropDownButton`, `ToolStripSplitButton` | `WfToolbarButton` |
| anything else (third-party, owner-drawn, report viewers, ...) | `WfPanel` when it has children, otherwise `WfLabel` (its text, or `[TypeName]`) |

## 4. How other controls get there

- **Custom controls** (a class derived from a standard control, in the project or a referenced library) resolve through their base-type chain (`ResolvedType`) and use the component of the standard type.
- **User controls** are inlined into their host as a `WfPanel`, and are also available as their own page (viewer, app shell).
- **Non-visual components** are not rendered and are listed in the report: `ImageList`, `ToolTip`, `BindingSource`, `Timer`, the file/folder/colour/font/print/page-setup dialogs, `NotifyIcon`, `ErrorProvider`, `BackgroundWorker`, `HelpProvider`, `PrintDocument`.
- **Layout** is derived from Dock/Location facts (flex rows and columns), never from pixels.
- **Windows-only APIs** are bypassed with a marker, and data-access/file/configuration code is left to the backend (see `ENGINE_AUTHORING.md`).

## 5. Gaps

No real component yet for `CheckedListBox` (it uses the stand-in above). `@wfm/ui-angular` and `@wfm/ui-html` are not started: they will implement the same names, props and events.
