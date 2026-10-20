# UI control mapping — which React component represents which WinForms control

As built, for the React target (`@wfm/ui-react`). The sources of truth are `ui-library/ui-spec/controls.json` (components), `ui-library/ui-spec/fallbacks.json` (stand-ins) and `ui-library/ui-spec/tokens.css` (theme); this page summarises them.
Generated pages import `Wf*` components only. Identical names, props and canonical events are the contract for the Angular and HTML+jQuery libraries (not started).

## 1. Components that represent a WinForms control directly (37)

| WinForms control(s) | React component | Props (from the WinForms property) | Events (from the WinForms event) |
|---|---|---|---|
| `Form`, custom base forms | `WfForm` | `title` (Text), `acceptButtonId`, `cancelButtonId` | `onLoad` (Load) |
| `MenuStrip` | `WfMenuBar` | children | — |
| `ToolStripMenuItem` | `WfMenuItem` | `text` (the `&` mnemonic becomes an underline and access key), `shortcut`, `checked`, `icon` (web path in the icon column), `disabled` | `onClick` |
| `ToolStripSeparator` | `WfSeparator` | — | — |
| `ToolStrip` | `WfToolbar` | children | — |
| `ToolStripButton` | `WfToolbarButton` | `text`, `tooltip`, `icon`, `displayStyle` (Image, Text or ImageAndText; default: the image when there is one, else the text), `disabled` | `onClick` |
| `StatusStrip` | `WfStatusBar` | children | — |
| `ContextMenuStrip` | `WfContextMenu` (wraps the control that owns it) | menu items | — |
| `Panel`, `FlowLayoutPanel` | `WfPanel` | `visible` | — |
| `GroupBox` | `WfGroupBox` | `title`, `visible`, `disabled` | — |
| `SplitContainer` | `WfSplit` + `WfSplitPane` | `orientation`, `splitterDistance` | — |
| `TabControl` / `TabPage` | `WfTabs` / `WfTabPage` | `selectedIndex` / `title`, `disabled`, `visible`, `icon` (pages are fixed at design time; `TabPages.Add/Remove` show and hide them, `SelectedTab` is the index) | `onSelectionChange` |
| `Label`, `ToolStripLabel`, `ToolStripStatusLabel` | `WfLabel` | `text` | — |
| `Button` | `WfButton` | `text`, `disabled` | `onClick` (a `DialogResult` also closes the dialog) |
| `LinkLabel` | `WfLinkLabel` | `text`, `disabled` | `onClick` (LinkClicked / Click) |
| `TextBox` | `WfTextBox` | `value`, `readOnly`, `maxLength`, `multiline`, `password` | `onValueChange` (TextChanged) |
| `ComboBox`, `ToolStripComboBox` | `WfComboBox` | `items`, `value`, `disabled` | `onSelectionChange` |
| `CheckBox` | `WfCheckBox` | `text`, `checked` | `onCheckedChange` |
| `RadioButton` | `WfRadioButton` | `text`, `checked`, `group` (the parent container: its radio buttons are mutually exclusive) | `onCheckedChange` |
| `DateTimePicker` | `WfDateTimePicker` | `value` (ISO text), `kind` = date / datetime / time (from Format / CustomFormat), `minDate` / `maxDate` (from MinDate / MaxDate); a native `input type=date|time|datetime-local` | `onValueChange` |
| `MaskedTextBox` | `WfMaskedTextBox` | `value`, `mask` (WinForms mask applied while typing: `0` `9` `#` `L` `?` `A` `a` `&` `C`; other characters are literals), `readOnly` | `onValueChange` (TextChanged) |
| `RichTextBox` | `WfRichTextBox` | `value`, `readOnly` (plain text; formatting is not reproduced) | `onValueChange` |
| `MonthCalendar` | `WfMonthCalendar` | `value` (from `SelectionStart`, ISO date) | `onValueChange` (DateChanged / DateSelected) |
| `ListBox` | `WfListBox` | `items`, `value` (selected text; the page model also keeps `SelectedIndex` / `SelectedItem`, as for a combo box) | `onSelectionChange` |
| `TreeView` | `WfTreeView` (+ helpers `wfTreeNode`, `wfSetExpanded`) | `nodes` (page model: text, name, tag, checked, expanded, `icon`, `hasChildren`, nodes), `selectedNode`, `checkBoxes` | `onSelectionChange` (AfterSelect), `onNodeCheckedChange` (AfterCheck), `onNodeClick`, `onNodeDoubleClick`, `onNodeExpand` (BeforeExpand: lazy loading, may be async, shows "Loading…") |
| `CheckedListBox` | `WfCheckedListBox` | `items`, `checkedIndices` (page model; `CheckedItems`, `GetItemChecked`, `SetItemChecked` translate), `value` | `onSelectionChange`, `onItemCheck` |
| `DomainUpDown` | `WfDomainUpDown` | `items`, `value`, `readOnly` (up/down buttons and arrow keys step through the items) | `onSelectionChange` |
| `TrackBar` | `WfTrackBar` | `value`, `min`, `max`, `step` (SmallChange), `orientation` (native range input) | `onValueChange` (Scroll / ValueChanged) |
| `HScrollBar`, `VScrollBar` | `WfScrollBar` | `value`, `min`, `max`, `step`; vertical for a `VScrollBar` | `onValueChange` |
| `ToolStripDropDownButton`, `ToolStripSplitButton` | `WfDropDownButton` | `text`, `tooltip`, `icon`, `split` (split button), menu items as children | `onClick` (Click / ButtonClick) |
| `NumericUpDown` | `WfNumericUpDown` | `value`, `min`, `max`, `step`, `decimals`, `readOnly` | `onValueChange` |
| `ProgressBar`, `ToolStripProgressBar` | `WfProgress` | `value`, `max` | — |
| `PictureBox` | `WfPictureBox` | `src`, `fit` | `onClick` |
| `DataGridView` (with its columns) | `WfDataGrid` | `rows`, `columns` (header, field, width, format, `editable`, `formatter`), `selectionMode`, `readOnly` (`true`: nothing editable; `false`: every column editable except `editable: false`; omitted: only `editable: true` columns), `rowKey`, `pageSize` (pager), `resizable` (drag column edges); header click sorts (numbers numerically); arrow keys move the selection; double-click or F2/Enter edits an `editable` cell (explicit `ReadOnly = false` in the designer; the grid never changes `rows`, the page writes the value back) | `onRowSelect` (SelectionChanged), `onCellClick` (CellClick / CellContentClick / CellMouseClick), `onCellEdit` (CellEndEdit) |
| `ListView` (with `ColumnHeader`, `ListViewItem`) | `WfListView` (+ helpers `wfListItem`, `wfListItemFromArray`). **A ListView that no handler is wired to and no method of the form mentions is rendered as `WfDataGrid`** (designer `ColumnHeader`s become the columns, three sample rows marked `// MOCK`; `listview-as-grid` in the report) so it is never an empty box. A ListView that code reads, fills or listens to stays a `WfListView`. | `columns`, `items`, `checkBoxes`, `multiSelect` | `onSelectionChange`, `onItemCheckedChange`, `onItemActivate`, `onClick`, `onDoubleClick`, `onKeyUp`, `onColumnClick` |

Inputs are controlled when a change handler is passed and uncontrolled otherwise (the value is then only the starting value), so the visual-only viewer and app shell accept typing.

## 2. Services and helpers (not visual)

`wfGrid*` helpers (`wfGridAddColumn`, `wfGridAddRow`, `wfGridClearRows`, `wfGridRemoveRow`, `wfGridCell`, `wfGridSetCell`) are the counterparts of the DataGridView members WinForms code uses. A grid that any method of the form mentions keeps its columns, rows and selection in the page model (`vm.<id>.columns`, `.rows`, `.selected`): `Columns.Add`, `Rows.Add`, `Rows.Count`, `Rows[i].Cells[k].Value` (index, field name or header), `foreach (DataGridViewRow r in grid.Rows)`, `SelectedRows`, `CurrentRow`, `Rows.Clear`, `RemoveAt` and `Remove` translate onto it; column indexes may be set at run time (`idx = grid.Columns.Count; grid.Columns.Add(...)`). Grids the code never mentions keep the static columns and marked sample rows.

| Piece | Role |
|---|---|
| `WfDialogProvider` + `useWfDialog` | Modal popups on the native `<dialog>` element (top layer, focus containment, Escape): `alert`, `confirm` (Yes/No and OK/Cancel give a boolean), `message(text, { buttons, icon, title })` (YesNoCancel, RetryCancel, AbortRetryIgnore give the DialogResult name; Escape answers the cancelling button), `show(render)` for `ShowDialog` forms and `close(result?)` for `this.Close()` / `DialogResult` (closes the topmost dialog and resolves its `show` promise; with no dialog open it calls the provider's `onRootClose`). Every popup has a title-bar ✕ (answers like Escape), so a form without its own Close button can still be closed. `MessageBoxIcon` becomes an `info` / `warning` / `error` / `question` badge |
| `useWfModel` | Per-page state as a deep proxy: reads are immediate, writes re-render once. `vm` holds control properties (text, enabled, visible, checked, value, items, selected index, rows), `f` holds form fields, so translated imperative WinForms code maps one to one |
| `WfDynamicHost` + `wfDynamicControl(Component, props)` | Renders the user controls a page adds to a panel, group box or tab page in code (`panel.Controls.Add(new X(args))`): the entries live in `vm.<id>.controls`, the constructor arguments are the props. Calls on the created control, `foreach` and `Controls[i]` are not translated |
| `wfImageList*` (`WfImageList { keys, urls }`, `wfImageList`, `wfImageListUrl`, `wfImageListAdd`, `wfImageListRemoveAt`, `wfImageListRemoveByKey`, `wfImageListClear`, `wfPlaceholderImage`) | The counterpart of an `ImageList`: image resources by key or position, plain data in the page model. `WfTreeView` / `WfListView` take `imageList` and show `imageKey` / `imageIndex` of their nodes / items |
| `WfUnsupported` | Last-resort visible placeholder for a control that cannot even get a fallback |

## 3. Stand-ins for controls without a component (`fallbacks.json`)

The closest component is rendered, marked `/* FALLBACK: Type */`, and listed in the report. Edit the data file to change a mapping.

| WinForms control(s) | Rendered as |
|---|---|
| `ToolStripTextBox` | `WfTextBox` (`Value` read as `Text`) |
| anything else (third-party, owner-drawn, report viewers, ...) | `WfPanel` when it has children, otherwise `WfLabel` (its text, or `[TypeName]`) |

## 4. How other controls get there

- **Icons** (`Image` of menu items, toolbar buttons and drop-down buttons): the source engine keeps the image of a readable `.resx` byte-array entry (`resources.GetObject("x.Image")`) as base64; the React target writes it as a web file under `public/icons/<unit>/<control>.png` and passes the path as `icon`. PNG, GIF, JPEG and SVG are copied; **ICO and BMP are converted to PNG** in managed code (no Windows API, the largest frame up to 32 px). An image that cannot be read (ImageList entries, external file references, unsupported formats) shows a neutral placeholder icon and is listed in the report (`icon-placeholder`).
- **Custom controls** (a class derived from a standard control, in the project or a referenced library) resolve through their base-type chain (`ResolvedType`) and use the component of the standard type.
- **User controls** are inlined into their host as a `WfPanel`, and are also available as their own page (viewer, app shell).
- **ImageList** is not rendered; its images (decoded from the `.resx` `ImageStream`) are written to `public/images/<unit>/<list>/<key>.png` and used through `ImageKey` / `ImageIndex` (see `wfImageList*` in section 2).
- **Non-visual components** are not rendered and are listed in the report: `ToolTip`, `BindingSource`, `Timer`, the file/folder/colour/font/print/page-setup dialogs, `NotifyIcon`, `ErrorProvider`, `BackgroundWorker`, `HelpProvider`, `PrintDocument`.
- **Navigation the translator could not keep:** when the statement that opened (`ShowDialog`) or closed a form sat inside code that could not be translated, the handler still opens the popup (with the sample data every generated page has) or calls `dialog.close()`, marked `// MOCK` and listed in the report (`nav-mock-open`, `nav-mock-close`).
- **Layout** is derived from Dock/Location facts (flex rows and columns), never from pixels.
- **Windows-only APIs** are bypassed with a marker, and data-access/file/configuration code is left to the backend (see `ENGINE_AUTHORING.md`).

## 5. Gaps

Every standard control now has a component; only `ToolStripTextBox` still uses a stand-in. `@wfm/ui-angular` and `@wfm/ui-html` are not started: they will implement the same names, props and events.
