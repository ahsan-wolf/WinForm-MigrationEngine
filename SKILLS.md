# SKILLS.md — WinForms → React / Angular / HTML+jQuery Mapping Catalog

Single source of truth for **control, event, property, and pattern mappings**.
Read `CLAUDE.md` first for rules, git workflow, and markers.

**Legend:** ✅ direct · 🟡 approximate (review) · 🔧 composite (several elements) · ⛔ no web equivalent (manual)
**Stack assumed:** React 18 + TS (hooks) · Angular 17+ (standalone, signals, reactive forms) · HTML5 + jQuery 3 — **always through the independent UI library** (`@wfm/ui-react`, `@wfm/ui-angular`, `@wfm/ui-html`).

> **How to read this file now (library-first).**
> Generated code imports **`Wf*` library components only** (Skill 0). The React / Angular / HTML+jQuery columns in Skills 1–15
> describe (a) what the library component does *internally* and (b) the raw fallback to use **inside the library** when
> implementing a new component. Generated pages must never import AG Grid, DataTables, Material, jstree, etc. directly.

---

## Skill 0 — UI Library Contract & Control Catalog

### 0.1 Packages (independent of the engine and IR)
| Package | Framework idiom |
|---|---|
| `ui-spec` | JSON contract + design tokens (single source for names/props/events) |
| `@wfm/ui-react` | `<WfButton onClick={…}/>` |
| `@wfm/ui-angular` | `<wf-button (wfClick)="…"/>` |
| `@wfm/ui-html` | `<button data-wf="button">` + custom events `wf:click`; jQuery: `$('#x').wfButton({…})` |

### 0.2 Common props (every control)
`id, label, value, disabled, visible, readOnly, tooltip, accessKey, tabIndex, className, error`
Event naming (canonical → React / Angular / HTML): `click → onClick / (wfClick) / wf:click`, `valueChange → onValueChange / (wfValueChange) / wf:valueChange`, `selectionChange`, `checkedChange`, `blur`, `enter`, `rowSelect`, `cellClick`, `cellEdit`, `sort`, `tabChange`, `open`, `close`.

### 0.3 WinForms → library catalog
| WinForms | Library component | Key props | Key events |
|---|---|---|---|
| `Form` | `WfForm` | `title, acceptButtonId, cancelButtonId` | `load, closing` |
| `Button` | `WfButton` | `text, variant, icon, type, dialogResult` | `click` |
| `Label` | `WfLabel` | `text, for` | — |
| `LinkLabel` | `WfLinkLabel` ✅ | `text, disabled` | `click` (`LinkClicked`/`Click`) |
| `TextBox` | `WfTextBox` | `value, maxLength, readOnly, password, placeholder, casing` | `valueChange, enter, blur` |
| `TextBox (Multiline)` | `WfTextArea` | `value, rows, wrap` | `valueChange` |
| `MaskedTextBox` | `WfMaskedTextBox` | `value, mask` | `valueChange` |
| `RichTextBox` | `WfRichTextBox` | `value` (plain text) | `valueChange` |
| `CheckBox` | `WfCheckBox` | `checked, indeterminate, text` | `checkedChange` |
| `RadioButton` ✅ | `WfRadioButton` | `text, checked, group` (= parent container id; radio buttons of one container are exclusive) | `checkedChange` |
| `ComboBox` | `WfComboBox` | `items, displayMember, valueMember, value, editable` | `selectionChange` |
| `ListBox` | `WfListBox` | `items, value` (single selection) | `selectionChange` |
| `NumericUpDown` ✅ | `WfNumericUpDown` | `value, min, max, step, decimals, readOnly` | `valueChange` |
| `DateTimePicker` ✅ | `WfDateTimePicker` | `value` (ISO text), `kind` (date \| datetime \| time, from `Format`/`CustomFormat`) | `valueChange` |
| `MonthCalendar` | `WfMonthCalendar` | `value` (ISO date from `SelectionStart`) | `valueChange` |
| `PictureBox` | `WfPictureBox` | `src, alt, fit(contain\|fill\|none\|cover)` | `click` |
| `ProgressBar` | `WfProgress` | `value, max, indeterminate` | — |
| `TrackBar` | `WfTrackBar` | `value, min, max, step, orientation` | `valueChange` |
| `ToolTip` | `WfTooltip` (wrapper/directive) | `text` | — |
| `ErrorProvider` | `error` prop on inputs / `WfFieldError` | `message` | — |
| `DataGridView` | `WfDataGrid` | `rows, columns[], selectionMode, sortable, pageSize, readOnly, allowAdd, allowDelete` | `rowSelect, cellClick, cellDoubleClick, cellEdit, rowAction, sort` |
| ↳ columns | `WfGridColumn` types: `text, checkbox, combo, button, link, image` | `field, header, format, width, frozen, render` | — |
| `TreeView` | `WfTreeView` | `nodes, selectedNode, checkBoxes` (lazy loading not yet) | `selectionChange, nodeCheckedChange, nodeClick, nodeDoubleClick` |
| `ListView` | `WfListView` | `items, columns, checkBoxes, multiSelect, disabled` (icons/tiles/groups not yet) | `selectionChange, itemCheckedChange, itemActivate, click, doubleClick, keyUp, columnClick` |
| `TabControl` / `TabPage` ✅ | `WfTabs` / `WfTabPage` | `selectedIndex` (controlled when code uses it) / `title` | `selectionChange` (`SelectedIndexChanged`) |
| `SplitContainer` | `WfSplit` | `orientation, sizes` | `resize` |
| `Panel` | `WfPanel` | `visible, scroll` | — |
| `GroupBox` | `WfGroupBox` | `title` | — |
| `FlowLayoutPanel` | `WfFlow` | `direction, wrap, gap` | — |
| `TableLayoutPanel` | `WfTableLayout` | `columns[], rows[], cells(row,col,span)` | — |
| `MenuStrip` / `ToolStripMenuItem` | `WfMenuBar` / `WfMenuItem` | `text, shortcut, checked, items` | `click` |
| `ToolStrip` | `WfToolbar` | `items` | `click` |
| `StatusStrip` | `WfStatusBar` | `items` | — |
| `ContextMenuStrip` | `WfContextMenu` | `items` | `click, open` |
| `MessageBox` / custom dialogs | `WfDialogService` (`alert, confirm, prompt, open`) | `title, message, buttons, icon` | promise result |
| `OpenFileDialog` / `SaveFileDialog` | `WfFilePicker` / `wfSaveFile()` | `accept, multiple` | `change` |
| `ColorDialog` | `WfColorPicker` | `value` | `valueChange` |
| `Chart` | `WfChart` | `type, series, categories` | `pointClick` |
| `Timer` | `useWfTimer` (React) / `WfTimerService` (Angular) / `wfTimer()` (HTML) | `interval, enabled` | `tick` |
| `WebBrowser` | `WfFrame` | `src, sandbox` | `load` |
| `Cursor = WaitCursor` | `WfBusy` | `busy` | — |
| anything unmapped | `WfUnsupported` | `type, reason` | — |

### 0.4 Usage (the same screen fragment, three frameworks)
```tsx
// React
<WfTextBox id="txtSearch" value={searchTerm} onValueChange={setSearchTerm} placeholder="Enter actor name..." />
<WfButton id="btnSearch" text="Search" variant="primary" onClick={handleSearchClick} />
```
```html
<!-- Angular -->
<wf-text-box id="txtSearch" [value]="searchTerm()" (wfValueChange)="searchTerm.set($event)" placeholder="Enter actor name..." />
<wf-button id="btnSearch" text="Search" variant="primary" (wfClick)="onSearchClick()" />
```
```html
<!-- HTML + jQuery -->
<input id="txtSearch" data-wf="textbox" placeholder="Enter actor name...">
<button id="btnSearch" data-wf="button" data-variant="primary">Search</button>
<script>
  $('#btnSearch').on('wf:click', () => loadActors($('#txtSearch').val().trim()));
</script>
```

### 0.5 Generator binding rules
1. Look up `ControlNodeIr.TagName` in `ui-spec` → pick the `Wf*` component; map `Attributes` → props using the spec's property table.
2. Map `OnX` attributes → the spec's canonical event; wire to the translated handler.
3. No spec entry → `WfUnsupported` + TODO + report entry.
4. Ignore `Location/Size/BackColor/Font`; use layout containers (`WfPanel/WfFlow/WfTableLayout`) per Skill 4.

---

## Skill 1 — Form / UserControl → Component

| WinForms | React | Angular | HTML+jQuery |
|---|---|---|---|
| `Form` (main/screen) ✅ | `pages/XPage.tsx` (markup) **+ `pages/XPage.code.tsx` (code-behind hook `useXPageCode`)**, route in router | `x.component.ts` standalone + route | `x.html` + `x.js` module |
| `UserControl` ✅ | `components/X.tsx` with props (generated: `XControl.tsx` + `XControl.code.tsx`) | `x.component.ts` with `@Input()/@Output()` | partial `<template>` + `X.init(container, opts)` |
| `Form.Text` | `document.title` / `<h1>` | `Title` service / `<h1>` | `<title>` / `<h1>` |
| `Form.Load` | `useEffect(() => {...}, [])` | `ngOnInit()` | `$(function(){ ... })` |
| `Form.Shown` | `useEffect` after first render (`useLayoutEffect` if needed) | `ngAfterViewInit()` | `$(window).on('load')` |
| `Form.FormClosing` (cancelable) 🟡 | `useBlocker` (react-router) / `beforeunload` | `CanDeactivate` guard | `$(window).on('beforeunload')` |
| `Form.FormClosed` / `Dispose` | `useEffect` cleanup | `ngOnDestroy()` | `$(el).off(); clearInterval()` |
| `Form.AcceptButton` 🟡 | `<form onSubmit>` with that button as `type="submit"` | `<form (ngSubmit)>` | `form.on('submit')` / Enter key handler (as in sample jQuery) |
| `Form.CancelButton` 🟡 | `onKeyDown` Esc → handler | `@HostListener('keydown.escape')` | `$(document).on('keydown')` Esc |
| `Form.WindowState / FormBorderStyle / StartPosition / ControlBox` ⛔ | ignore (note in report) | ignore | ignore |
| `Form.ActiveControl` | `ref.current.focus()` in effect | `@ViewChild().nativeElement.focus()` | `$('#id').trigger('focus')` |
| `Form.DialogResult` | modal promise `resolve(result)` (Skill 11) | `MatDialogRef.close(result)` | modal `hidden.bs.modal` + stored result |
| `Form.Tag` / `Control.Tag` | `data-tag` attr / state | `[attr.data-tag]` | `.data('tag')` |

---

## Skill 2 — Standard Controls

| WinForms control | React | Angular | HTML + jQuery | Notes |
|---|---|---|---|---|
| `Label` ✅ | `<label htmlFor>` / `<span>` | `<label for>` / `<span>` | `<label for>` | Use `<label>` if next to an input; `&` mnemonic → `accesskey`. |
| `LinkLabel` ✅ | `<a href>` / `<Link to>` | `<a [routerLink]>` | `<a href>` | `LinkClicked` → `onClick`; `Process.Start(url)` → `window.open`. |
| `Button` ✅ | `<button onClick>` | `<button (click)>` | `<button>` + `.on('click')` | `DialogResult` property → closes modal with result. |
| `TextBox` (single) ✅ | `<input value onChange>` | `<input formControlName>` | `<input class="form-control">` | `MaxLength`→`maxlength`; `ReadOnly`→`readOnly`; `PasswordChar`→`type="password"`; `CharacterCasing`→CSS `text-transform` + normalize on change. |
| `TextBox` (`Multiline`) ✅ | `<textarea>` | `<textarea formControlName>` | `<textarea>` | `ScrollBars`→CSS overflow; `WordWrap`→`wrap` attr. |
| `MaskedTextBox` 🟡 | `react-imask` / `react-input-mask` | `ngx-mask` directive | `jquery.inputmask` | Convert `Mask` string (`000-0000`) to library pattern. |
| `RichTextBox` 🟡 | `react-quill` / Tiptap | `ngx-quill` / Tiptap | Quill / TinyMCE | RTF ↔ HTML conversion is manual (TODO). |
| `CheckBox` ✅ | `<input type="checkbox" checked onChange>` | `<input type="checkbox" formControlName>` | `<input type="checkbox">` | `ThreeState`→ indeterminate prop; `CheckedChanged`→`change`. |
| `RadioButton` (group in container) ✅ | `<input type="radio" name>` set bound to one state value | `formControlName` shared across radios | `<input type="radio" name>` | Group = same parent `Panel/GroupBox` → single state variable. |
| `ComboBox` (DropDownList) ✅ | `<select value onChange>` | `<select formControlName>` / `mat-select` | `<select>` | `Items`→options; `DisplayMember/ValueMember`→`label/value` keys; `SelectedValue`→value. |
| `ComboBox` (DropDown, editable) 🟡 | `<input list>` + `<datalist>` | `mat-autocomplete` | `<input list>` / jQuery UI autocomplete | `AutoCompleteMode` → autocomplete lib. |
| `ListBox` ✅ | `<select multiple size>` / list of selectable `<li>` | `<mat-selection-list>` / `<select multiple>` | `<select multiple>` / `<ul class="list-group">` | `SelectionMode.MultiExtended`→ Ctrl/Shift multi. |
| `CheckedListBox` 🔧 | map of `<input type=checkbox>` in a list | `FormArray` of checkboxes | list of checkboxes | `GetItemChecked(i)` → read state array. |
| `NumericUpDown` ✅ | `<input type="number" min max step>` | same + `formControlName` | `<input type="number">` | `DecimalPlaces`→`step`; `Minimum/Maximum/Increment`. |
| `DateTimePicker` ✅ | `<input type="date|datetime-local|time">` | same / `mat-datepicker` | same | `Format`/`CustomFormat` → date `Intl`/`date-fns` formatting; `ShowUpDown`→ ignore. Sample uses `TextMode=Date`. |
| `MonthCalendar` 🟡 | `react-day-picker` / `react-calendar` | `mat-calendar` | `flatpickr` inline | `SelectionRange`→range mode. |
| `PictureBox` ✅ | `<img src alt>` | `<img [src]>` | `<img>` | `SizeMode`→CSS `object-fit` (Zoom→`contain`, StretchImage→`fill`, CenterImage→`none`). Embedded resources → `/assets`. |
| `ProgressBar` ✅ | `<progress value max>` / Bootstrap progress | `mat-progress-bar` | `<div class="progress">` | `Style=Marquee` → indeterminate. |
| `TrackBar` ✅ | `<input type="range">` | `<input type="range">` / `mat-slider` | `<input type="range">` | `TickFrequency`→`step`. |
| `ToolTip` ✅ | `title` attr / Tooltip lib | `matTooltip` | `title` / Bootstrap tooltip | `SetToolTip(ctrl,text)` → attach to that control. |
| `ErrorProvider` 🔧 | per-field error `<div class="invalid-feedback">` | `<mat-error>` / `*ngIf="ctrl.invalid"` | `.addClass('is-invalid')` + feedback div | See Skill 12. |
| `HelpProvider` ⛔ | tooltip/help link | tooltip | tooltip | Manual. |
| `NotifyIcon` ⛔ | Notification API / toast | snackbar | toast | Tray icon has no web analog. |
| `WebBrowser` ✅ | `<iframe src>` | `<iframe [src]>` (DomSanitizer) | `<iframe>` | `Navigate(url)`→set `src`. Sandbox attribute. |
| `HScrollBar / VScrollBar` 🟡 | CSS overflow / `<input type=range>` | same | same | Usually remove. |
| `DomainUpDown` 🟡 | `<select>` | `<select>` | `<select>` | Treat as select of strings. |

---

## Skill 3 — Complex / Data Controls

### 3.1 DataGridView (the most important one)

| DataGridView feature | React | Angular | HTML+jQuery |
|---|---|---|---|
| Control | `<table>` + `rows.map()` (simple) · **TanStack Table** / **AG Grid** (sort/filter/page) | `<table mat-table>` / `*ngFor` · AG Grid Angular | `<table class="table table-striped">` · **DataTables.net** |
| `DataSource` | `rows` state (array) | `rows = signal<Row[]>([])` | `table.DataTable({ data })` or `.empty().append(tr…)` |
| `AutoGenerateColumns=false` + `Columns` | `columns: ColumnDef[]` | `displayedColumns` + `matColumnDef` | `columns:[{data,title}]` |
| `DataGridViewTextBoxColumn` | text cell (`HeaderText`→header, `DataPropertyName`→accessor) | `<td>{{row.x}}</td>` | `{data:'x'}` |
| `DataGridViewCheckBoxColumn` | checkbox cell | `mat-checkbox` cell | checkbox render fn |
| `DataGridViewComboBoxColumn` | `<select>` cell | `mat-select` cell | `<select>` render fn |
| `DataGridViewButtonColumn` | `<button onClick={()=>onRowAction(row)}>` | `<button (click)="onRowAction(row)">` | delegated `$('#t').on('click','.btn-x', …)` |
| `DataGridViewLinkColumn` | `<a>` | `<a [routerLink]>` | `<a>` |
| `DataGridViewImageColumn` | `<img>` | `<img>` | `<img>` |
| `DefaultCellStyle.Format` (`d`, `N2`, `C`) | `Intl.DateTimeFormat / NumberFormat` | `date` / `number` / `currency` pipes | `toLocaleDateString` / `Intl` |
| `ReadOnly` / `AllowUserToAddRows` / `AllowUserToDeleteRows` | omit edit UI / add/delete row buttons | same | same |
| `AllowUserToOrderColumns` / `AllowUserToResizeColumns` | grid lib option | grid lib option | DataTables `colReorder/colResize` |
| `SelectionMode=FullRowSelect` | `selectedRow` state, row click | `selection = new SelectionModel()` | `.on('click','tr')` toggles `.table-active` |
| `MultiSelect` | `Set<id>` state | `SelectionModel(true)` | multi-class toggle |
| `SelectedRows[0]` / `CurrentRow` | **as built (React):** `vm.<grid>.selected[0]` (the selection is written back to the model by `onRowSelect`) | `selectedRow()` | stored object |
| `Rows.Add / Rows.Remove / Rows.Clear` | **as built (React):** `wfGridAddRow(vm.g, [..])` / `wfGridRemoveRow` / `wfGridClearRows` over the model rows; `Columns.Add(name, header)` → `wfGridAddColumn`; `Rows[i].Cells[k].Value` → `wfGridCell(vm.g, row, k)` (k: index, field or header), assignment → `wfGridSetCell` | `signal.update()` | DataTables `row.add/remove/clear().draw()` |
| `Sort(column, direction)` / header click | table lib sort state | `matSort` | DataTables `order` |
| Paging (manual) | `slice` / table lib | `mat-paginator` | DataTables paging |
| `Frozen` columns | CSS `position: sticky` | `sticky` column | CSS sticky |
| **Events** | | | |
| `CellClick` | `onClick` on `<td>` with `(row, col)` | `(click)` | delegated `td` click |
| `CellDoubleClick` | `onDoubleClick` | `(dblclick)` | `.on('dblclick')` |
| `CellContentClick` (button column) | button `onClick` | button `(click)` | delegated button click |
| `CellValueChanged` / `CellEndEdit` | `onChange`/`onBlur` of cell input → update row | `(change)`/`(blur)` | `.on('change')` |
| `CellFormatting` 🟡 | cell renderer function (`render: row => …`) | pipe or `[ngClass]` | DataTables `createdRow/render` |
| `CellValidating` / `DataError` | validate in `onChange`, show error state | validator fn | validate + `.addClass('is-invalid')` |
| `SelectionChanged` | `useEffect` on selection | `effect()` on selection | `.on('select')` |
| `RowEnter` / `RowLeave` | `onMouseEnter/Leave` | `(mouseenter)` | `.hover()` |
| `UserDeletingRow` | confirm → remove | confirm → remove | confirm → remove |
| `ColumnHeaderMouseClick` | sort handler | `matSort` | DataTables order |
| `RowPostPaint / CellPainting` ⛔ | custom renderer (manual) | manual | manual |

**Model inference:** columns → `interface Actor { actorId: number; firstName: string; lastName: string; dateOfBirth: string }` (mark `// inferred`).

### 3.2 TreeView
| WinForms | React | Angular | jQuery |
|---|---|---|---|
| `TreeView` 🔧 | recursive `<TreeNode>` / `react-arborist` | `mat-tree` / recursive component | `jstree` |
| `Nodes / TreeNode.Nodes` | `children: Node[]` | same | same |
| `AfterSelect` | `onSelect(node)` | `(selectionChange)` | `.on('select_node.jstree')` |
| `BeforeExpand` (lazy load) | async load on expand | `loadChildren()` | `jstree` lazy `data` fn |
| `CheckBoxes=true` | checkbox per node (+ cascade logic) | `mat-checkbox` w/ cascade | `jstree` checkbox plugin |
| `ImageList` icons | icon per node type map | `mat-icon` map | CSS class per type |
| `NodeMouseDoubleClick` | `onDoubleClick` | `(dblclick)` | `dblclick.jstree` |

### 3.3 ListView
| WinForms | React | Angular | jQuery |
|---|---|---|---|
| `ListView` (`View.Details`) 🔧 | same as DataGridView table; **React target: a ListView with no wired event and no mention in any method is generated as `WfDataGrid`** (`ColumnHeader` → column, sample rows), otherwise `WfListView` | same | DataTables |
| `View.LargeIcon / SmallIcon / Tile` | CSS grid of cards | CSS grid of `mat-card` | Bootstrap card grid |
| `Groups` | grouped sections / `Object.groupBy` | `*ngFor` grouped | grouped `<tbody>` |
| `Columns` / `ListViewItem.SubItems` | `columns` + row fields | same | same |
| `CheckBoxes` / `SelectedIndexChanged` / `ItemActivate` | checkbox col / selection / dblclick | same | same |

### 3.4 Tabs, Splits, Containers
| WinForms | React | Angular | HTML+jQuery |
|---|---|---|---|
| `TabControl` / `TabPage` 🔧 | `useState(activeTab)` + tab buttons + conditional panel (or MUI/React-Bootstrap Tabs) | `<mat-tab-group>` / `<mat-tab label>` | Bootstrap `nav-tabs` + `tab-pane` |
| `TabControl.SelectedIndexChanged` | `onChange` of tab state | `(selectedIndexChange)` | `shown.bs.tab` |
| `SplitContainer` 🟡 | `react-resizable-panels` / CSS flex w/ 2 panes | `angular-split` / flex | Split.js / flex |
| `Panel` ✅ | `<div>` (conditionally rendered if `Visible` toggled) | `<div *ngIf>` | `<div>` + `.toggle()` |
| `GroupBox` ✅ | `<fieldset><legend>` | `<fieldset><legend>` | `<fieldset><legend>` |
| `FlowLayoutPanel` ✅ | `<div style="display:flex; flex-wrap:wrap; gap">` (`FlowDirection`→`flex-direction`) | same | `.d-flex.flex-wrap.gap-2` |
| `TableLayoutPanel` ✅ | CSS Grid (`gridTemplateColumns` from `ColumnStyles`, `gridRow/Column` from cell position, `ColumnSpan`→`gridColumn: span n`) | same | Bootstrap grid / CSS grid |
| `ScrollableControl` / `AutoScroll` | `overflow:auto` | same | same |
| `StatusStrip` | `<footer class="status-bar">` | `<footer>` | `<footer>` |
| `ToolStripContainer` | page header/footer regions | same | same |
| `PropertyGrid` ⛔ | generated key/value form (manual) | manual | manual |
| `Chart` (`System.Windows.Forms.DataVisualization`) 🟡 | **Recharts** / Chart.js | **ng2-charts** / ngx-charts | **Chart.js** |
| `ReportViewer` / Crystal ⛔ | server-rendered PDF in `<iframe>` (manual) | same | same |
| `DataRepeater` (PowerPacks) 🔧 | `items.map()` | `*ngFor` | loop/template |

---

## Skill 4 — Layout (Dock / Anchor / Location → CSS)

**Never copy `Location`/`Size`.** Derive layout from tree + these rules:

| WinForms | CSS |
|---|---|
| Child order in parent `Controls` | DOM order (sort by `TabIndex`, then Y, then X for siblings in a free-form Panel) |
| `Dock = Top` | first child, `width:100%` in a column flex parent |
| `Dock = Bottom` | last child, `margin-top:auto` |
| `Dock = Left / Right` | flex row; fixed `width` from `Size.Width` (rounded to rem) |
| `Dock = Fill` | `flex:1 1 auto; min-height:0` |
| `Anchor = Top,Left` (default) | no-op |
| `Anchor = Left,Right` | `width:100%` |
| `Anchor = Top,Bottom` | `align-self:stretch` |
| `Anchor = Right` / `Bottom,Right` | `margin-left:auto` / `margin-left:auto; margin-top:auto` |
| Free-form `Panel` with several `Location`s | cluster rows by Y (±8px); each row = flex row; label+input pairs = `.form-group` |
| `Padding` / `Margin` | rem spacing utilities (`p-2`, `m-1`, …) — round to scale |
| `AutoSize` | default (no fixed size) |
| `Visible=false` | `hidden` / conditional render, bound to a boolean state if code toggles it |
| `Enabled=false` | `disabled` bound to state if code toggles it |
| `TabIndex` | DOM order; `tabindex` only when non-sequential |
| `TabStop=false` | `tabindex="-1"` |
| `BackColor/ForeColor/Font` | **ignore**, except semantic (error=red, warning=amber). Record in report. |
| `Cursor` | CSS `cursor` (only `Hand`→pointer) |

---

## Skill 5 — Menus, Toolbars, Context Menus, Dialogs

| WinForms | React | Angular | HTML+jQuery |
|---|---|---|---|
| `MenuStrip` / `ToolStripMenuItem` 🔧 | `<nav>` with nested menu (MUI Menu / headless) | `mat-menubar` / `mat-menu` | Bootstrap navbar + dropdown |
| `ToolStripMenuItem.Click` | `onClick` | `(click)` | `.on('click')` |
| `ToolStripMenuItem.ShortcutKeys` | `useHotkeys` / `onKeyDown` | `@HostListener('window:keydown')` | `$(document).on('keydown')` |
| `ToolStripMenuItem.Checked` | checkmark icon + state | same | `.toggleClass('active')` |
| `ToolStripSeparator` | `<hr class="dropdown-divider">` | `<mat-divider>` | `<hr class="dropdown-divider">` |
| `ToolStrip` (buttons/labels/combos/text) 🔧 | `<div class="toolbar">` w/ buttons | `mat-toolbar` | `.btn-toolbar` |
| `ToolStripStatusLabel` | status-bar `<span>` bound to state | same | same |
| `ContextMenuStrip` / `Control.ContextMenuStrip` 🔧 | `onContextMenu` → positioned menu | `matMenuTriggerFor` + `(contextmenu)` | `contextmenu` event + Bootstrap dropdown / `jquery.contextMenu` |
| `MessageBox.Show(text)` | `window.alert` / toast | `MatSnackBar` / `alert` | `alert` / Bootstrap toast |
| `MessageBox.Show(..., YesNo)` | `await confirmDialog()` (promise modal) | `dialog.open(Confirm).afterClosed()` | `confirm()` or Bootstrap modal w/ deferred |
| `MessageBoxIcon` | icon in dialog | `mat-icon` | Bootstrap icon |
| `OpenFileDialog` | `<input type="file" accept>` + `FileReader` | `<input type="file" (change)>` | `<input type="file">` |
| `SaveFileDialog` | create `Blob` + `<a download>` | same (`file-saver`) | same |
| `FolderBrowserDialog` ⛔ | `<input type="file" webkitdirectory>` (limited) | same | same |
| `ColorDialog` | `<input type="color">` | same | same |
| `FontDialog` ⛔ | `<select>` of fonts (manual) | manual | manual |
| `PrintDialog / PrintDocument` 🟡 | `window.print()` + `@media print` CSS | same | same |
| `InputBox` (VB) / custom prompt | prompt modal | `MatDialog` | `prompt()` / modal |
| `Clipboard.SetText` | `navigator.clipboard.writeText` | same | same |
| `Cursor.Current = WaitCursor` | `isBusy` state → spinner overlay | `isBusy` signal | `$('body').addClass('busy')` |
| `Application.DoEvents()` ⛔ | remove; use `async/await` | remove | remove |

---

## Skill 6 — Event Mapping

**Where the wiring lives (React target):** the markup file binds handlers by name (`onClick={handleSaveClick}`); the handlers, the model and the translated methods are in the page's **code-behind file** `XPage.code.tsx`, which also exports a `wiring` table (`control`, `event`, `handler`, `method`, `source`) listing every `control.event → handler` of the original form. That is the file to open when tracing an event back to the WinForms `X.cs` (`source` is `designer`, or `code` for wiring read from code-behind statements `ctl.Click += handler`, `new EventHandler(m)` or lambdas; the original statement stays in the translated code as a `// wired in the markup` comment).

| WinForms event | React | Angular | jQuery | Note |
|---|---|---|---|---|
| `Click` | `onClick` | `(click)` | `.on('click')` | |
| `DoubleClick` | `onDoubleClick` | `(dblclick)` | `dblclick` | |
| `MouseDown / MouseUp / MouseMove` | `onMouseDown/Up/Move` | `(mousedown)…` | same | `e.Button` → `e.button` |
| `MouseEnter / MouseLeave / MouseHover` | `onMouseEnter/Leave` | `(mouseenter)` | `.hover()` | |
| `MouseWheel` | `onWheel` | `(wheel)` | `wheel` | |
| `KeyDown` | `onKeyDown` | `(keydown)` | `keydown` | `e.KeyCode==Keys.Enter` → `e.key==='Enter'` |
| `KeyPress` | `onKeyDown`/`onBeforeInput` | `(keydown)` | `keypress` | `e.Handled=true` → `e.preventDefault()` |
| `KeyUp` | `onKeyUp` | `(keyup)` | `keyup` | |
| `TextChanged` | `onChange` | `valueChanges` / `(input)` | `input` | |
| `Enter` (focus) | `onFocus` | `(focus)` | `focus` | |
| `Leave` / `LostFocus` | `onBlur` | `(blur)` | `blur` | |
| `Validating` | `onBlur` → validate; set error | validator / `(blur)` | `blur` → validate | `e.Cancel=true` → keep error + refocus |
| `Validated` | after-valid callback | `statusChanges` valid | after valid | |
| `SelectedIndexChanged` | `onChange` (select) | `(change)` / `valueChanges` | `change` | |
| `SelectedValueChanged` | `onChange` | same | same | |
| `SelectionChangeCommitted` | `onChange` (user only) | `(selectionChange)` | `change` | |
| `CheckedChanged` / `CheckStateChanged` | `onChange` (checked) | `(change)` | `change` | |
| `ValueChanged` (Numeric/DateTime/TrackBar) | `onChange` | `valueChanges` | `change` | |
| `DropDown / DropDownClosed` | `onFocus`/`onBlur` | `(openedChange)` | `focus/blur` | |
| `Resize / SizeChanged` | `ResizeObserver` hook | `ResizeObserver` / `@HostListener('window:resize')` | `$(window).resize` | |
| `Paint` ⛔ | `<canvas>` + `useEffect` (manual) | `<canvas>` | `<canvas>` | GDI+ → Canvas 2D manual |
| `DragEnter / DragDrop` 🟡 | `onDragOver/onDrop` | `(dragover)/(drop)` | `dragover/drop` | `e.Data.GetData` → `dataTransfer` |
| `Scroll` | `onScroll` | `(scroll)` | `scroll` | |
| `Tick` (Timer) | `setInterval` in `useEffect` (Skill 9) | `interval()` (rxjs) | `setInterval` | |
| `FormClosing / Load / Shown` | see Skill 1 | | | |
| `Sender` param | `e.currentTarget` | `$event.target` | `this` / `e.currentTarget` | |
| `EventArgs e` | synthetic event | `$event` | jQuery event | Drop if unused. |

Custom events on UserControls (`public event EventHandler Saved;`) → React callback prop `onSaved`; Angular `@Output() saved = new EventEmitter<…>()`; jQuery `$(el).trigger('saved', payload)`.

---

## Skill 7 — Properties, Binding, State

| WinForms | React | Angular | jQuery |
|---|---|---|---|
| `Text` (TextBox) | `value` state | `FormControl` | `.val()` |
| `Text` (Label/Button) | JSX text / state if assigned in code | interpolation `{{ }}` | `.text()` |
| `Checked` | `checked` state | `FormControl<boolean>` | `.prop('checked')` |
| `SelectedItem / SelectedValue / SelectedIndex` | state of option/value/index | form control value | `.val()` |
| `Items.Add / Items.Clear / DataSource` | state array | signal/array | `.append()/.empty()` |
| `Enabled` | `disabled={!enabled}` | `[disabled]` | `.prop('disabled')` |
| `Visible` | conditional render | `*ngIf` / `@if` | `.toggle()` |
| `ReadOnly` | `readOnly` | `[readonly]` | `.prop('readonly')` |
| `Focus()` | `ref.focus()` | `ViewChild.focus()` | `.trigger('focus')` |
| `BindingSource` + `DataBindings.Add("Text", bs, "Name")` 🔧 | controlled inputs bound to `selectedRecord` | reactive form `patchValue(record)` | populate fields from record |
| `BindingSource.Filter / Sort` | `useMemo` filter/sort | `computed()` | filter array, re-render |
| `BindingNavigator` | prev/next buttons + `index` state | same | same |
| `Settings.Default.X` (user settings) | `localStorage` wrapper / settings context | settings service | `localStorage` |
| `Application.StartupPath` / file paths ⛔ | not applicable (server) | | | TODO |
| Static/global form fields | app-state service (Context / injectable) | injectable singleton | module-level object |
| **As built (React):** control properties and form fields in generated pages | one `useWfModel` per page: `vm.<id>.<prop>` (text, enabled, visible, checked, value, readOnly, ListView `items`/`columns`) and `f.<field>` (initial values from the C# initializers); deep proxy = immediate reads, one batched re-render | | |
| **As built:** the app's own static classes (`main`, `Preferences`, ...) | module `src/support/index.ts`: `export class main { static ... }`; pages call `main.X(...)`; static fields start as sample data (`// MOCK`) | | |
| `Form.Tag` / passing data to child form | route state / props | `Router.navigate(..., {state})` / `@Input` | `sessionStorage` / query string |
| `FormMode` (Add/Edit) pattern | `mode` state | `mode = signal<'Add'|'Edit'>` | `data-mode` / variable |

---

## Skill 8 — Data Access

WinForms commonly uses `SqlConnection/SqlCommand`, `DataSet/DataTable/SqlDataAdapter`, typed DataSets, Entity Framework, or ADO `TableAdapter`s. **Browsers never connect to the DB.**

| WinForms | Generate |
|---|---|
| `SELECT … FROM Actors` (`LoadActors`) | `ActorService.getAll(search?)` → `GET /api/actors?search=` |
| `SELECT … WHERE Id=@Id` | `getById(id)` → `GET /api/actors/{id}` |
| `INSERT` | `create(dto)` → `POST /api/actors` |
| `UPDATE` | `update(id, dto)` → `PUT /api/actors/{id}` |
| `DELETE` | `remove(id)` → `DELETE /api/actors/{id}` |
| `LIKE @Search` | server-side filter; client sends `search` param |
| `DataTable dt; da.Fill(dt); grid.DataSource = dt` | `rows = await service.getAll()` |
| Stored procedure call | `POST /api/{procName}` — `// TODO: wire to real API` |
| Transaction | `// TODO: server-side` |
| Connection string / `app.config` | **do not emit** in client; list in report |

Per target:
- **React:** `services/actorService.ts` using `fetch`; optional `useActors()` hook (+ TanStack Query).
- **Angular:** `services/actor.service.ts` using `HttpClient`, `providedIn:'root'`.
- **jQuery:** `services/actorService.js` using `$.ajax`/`fetch` returning Promises.
- **As built (React):** the ECMLibrary-style backend is a *contract*, not SQL: `migrate --contract` emits `src/services/<assembly>/{types,fixtures,service}.ts` (typed mock service, secret-looking members omitted); translated code calls `await ecmLibrary.UCM.X(...)`.
- **Mock backend:** `mocks/db.json` + `json-server` (or MSW). Fixture rows are plausible and shaped by the inferred model. Marker: `// MOCK — replace with real implementation`.

---

## Skill 9 — Non-visual Components, Threading, Timers

| WinForms | React | Angular | jQuery |
|---|---|---|---|
| `Timer` (`Interval`, `Tick`, `Enabled`) | `useEffect(() => { const t=setInterval(fn,ms); return ()=>clearInterval(t) }, [enabled])` | `interval(ms).pipe(takeUntilDestroyed())` | `setInterval` / `clearInterval` |
| `BackgroundWorker` (`DoWork`/`ProgressChanged`/`RunWorkerCompleted`) | `async` function + progress state (or Web Worker for CPU work) | `async` method / RxJS stream | `Promise` + progress callback |
| `Thread` / `Task.Run` | `async/await` / Web Worker | `async/await` / Web Worker | `Promise` / Web Worker |
| `Invoke` / `BeginInvoke` (UI thread marshaling) | remove (single-threaded) | remove | remove |
| `Control.CheckForIllegalCrossThreadCalls` | remove | remove | remove |
| `Thread.Sleep` | `await new Promise(r=>setTimeout(r,ms))` | same | same |
| `ImageList` | icon map / sprite | `mat-icon` registry | CSS sprite |
| `FileSystemWatcher`, `SerialPort`, `Registry`, `Process`, P/Invoke, COM ⛔ | server-side API — TODO | same | same |

---

## Skill 10 — Navigation & Multi-Form

| WinForms (`NavigationIr.Kind`) | React | Angular | jQuery |
|---|---|---|---|
| `new FormX().Show()` | `navigate('/x')` (router) | `router.navigate(['/x'])` | `location.href='x.html'` |
| `new FormX().ShowDialog()` | `await openModal(<X/>)` returning result | `dialog.open(XComponent).afterClosed()` | Bootstrap modal + callback |
| `this.Close()` (from main/page form) | `navigate(-1)` / `/` | `location.back()` | `history.back()` |
| `this.Close()` (from dialog) | `dialog.close(result)` | `dialogRef.close(result)` | hide modal + resolve |
| `this.Hide()` | `setVisible(false)` | `*ngIf` | `.hide()` |
| `Application.Exit()` | navigate to `/goodbye` (or no-op + TODO) | same | same |
| `Application.Run(new MainForm())` | `/` route = main form | default route | `index.html` |
| MDI (`IsMdiContainer`, `MdiParent`, `LayoutMdi`) 🔧 | tabbed workspace: `openTabs[]` state, each tab renders a page | `mat-tab-group` with dynamic tabs | Bootstrap tabs, loaded via AJAX |
| `Owner` / passing arguments through constructor | route params / props / location state | route params / `@Input` | query string / `sessionStorage` |
| Returning data via public property after `ShowDialog` | modal result payload | `afterClosed()` payload | callback arg |

Generate `routes.*` from `NavigationIr` entries + main form. Dialog forms are **not** routes; they are components opened by the dialog service.

**As built (React):** constructor parameters of a form become optional props of its page (contract data types default to a fixture row, `// MOCK`; `field = arg;` seeds the field model); `Button.DialogResult` closes the dialog after the Click handler with that result; the generated `main.tsx` is also a **dev launcher**: `?page=<Name>` renders one page on its own, `?page=` lists them, and a render error is shown instead of a blank screen.

---

## Skill 11 — Dialogs (provided by the library)

Dialogs are **library services**, not generated code. Generated handlers call them:

| Framework | Call |
|---|---|
| React | `const dialog = useWfDialog(); if (await dialog.confirm('Are you sure?')) { … }` |
| Angular | `private dialog = inject(WfDialogService); if (await this.dialog.confirm('Are you sure?')) { … }` |
| HTML+jQuery | `if (await Wf.dialog.confirm('Are you sure?')) { … }` |

`ShowDialog()` forms become components passed to `dialog.open(Component, data)` returning the result (`DialogResult`).

---

## Skill 12 — Validation

| WinForms | React | Angular | jQuery |
|---|---|---|---|
| `ValidateForm()` returning bool | `validate(values): Errors` called in `onSubmit` | `Validators` + `form.invalid` | `validate()` toggling `.is-invalid` |
| `string.IsNullOrWhiteSpace(txt.Text)` | `!value.trim()` → `required` | `Validators.required` + `trim` | `!$.trim(val)` |
| `MaxLength` | `maxLength` attr + counter | `Validators.maxLength` | `maxlength` attr |
| Regex check | `pattern` | `Validators.pattern` | `pattern` / `RegExp.test` |
| `int.TryParse` / `decimal.TryParse` | `Number.isFinite(Number(v))` | `Validators.pattern(/^-?\d+$/)` | same |
| `ErrorProvider.SetError(ctrl, msg)` | `errors[field]` → message under field | `<mat-error>` | `.addClass('is-invalid').next('.invalid-feedback').text(msg)` |
| `ErrorProvider.Clear()` | `setErrors({})` | `form.reset()` errors | `.removeClass('is-invalid')` |
| `MessageBox.Show("Please enter…")` as validation | inline error (preferred) | inline error | inline error |

The sample `ValidateForm()` (`FirstName`/`LastName` required; DOB optional) → `required` on both; **fix** the empty "Show validation message" TODO by rendering real inline messages.

---

## Skill 13 — Security, Session, Auth

| WinForms | Generate |
|---|---|
| `WindowsIdentity.GetCurrent()`, `Environment.UserName`, `Thread.CurrentPrincipal` | `AuthService` / `useAuth()` returning `{ userName, userId, roles }` — **MOCK** |
| `IsInRole("Admin")`, `[PrincipalPermission]` | route guard (`CanActivate` / `<RequireRole>`) + conditional UI (`*ngIf="auth.isAdmin()"`) |
| Properties like `IsRoc`, `IsAdmin`, `UserId`, `UserName` (sample) | fields on `AuthState`; show "You do not have permission" panel when false |
| `Settings`, `ConfigurationManager.AppSettings` | `environment.ts` / `.env` / `config.json` |
| `Application.Run` login form → main form | `/login` route + guard + redirect to `/` |
| Static `Session`/global state classes | app-state service/context, in-memory (marker `MOCK`) |

All auth is **mock**: no token validation. Mark every file `// MOCK — replace with real implementation`.

---

## Skill 14 — C# → TypeScript/JS Translation Cheat Sheet (for LLM prompts & manual use)

| C# | TypeScript |
|---|---|
| `string.IsNullOrEmpty(s)` | `!s` |
| `string.IsNullOrWhiteSpace(s)` | `!s?.trim()` |
| `s.Trim() / ToUpper() / ToLower() / Contains / StartsWith` | `s.trim() / toUpperCase() / toLowerCase() / includes / startsWith` |
| `string.Format("{0}-{1}", a, b)` / `$"{a}-{b}"` | `` `${a}-${b}` `` |
| `Convert.ToInt32(x)` / `int.Parse(x)` | `Number.parseInt(x, 10)` |
| `Convert.ToDateTime(x)` | `new Date(x)` |
| `DateTime.Now / Today` | `new Date()` |
| `x.ToString("yyyy-MM-dd")` | `date.toISOString().slice(0,10)` / `Intl.DateTimeFormat` |
| `List<T>` `.Add/.Remove/.Count/.Clear()` | `arr.push / filter / length / arr = []` |
| `foreach (var x in xs)` | `for (const x of xs)` |
| LINQ `.Where/.Select/.OrderBy/.First/.Any/.Sum` | `.filter/.map/.sort/.find/.some/.reduce` |
| `Dictionary<K,V>` | `Map<K,V>` / `Record<K,V>` |
| `try/catch/finally` | `try/catch/finally` |
| `throw new Exception(msg)` | `throw new Error(msg)` |
| `null` / `?.` / `??` | `null` / `?.` / `??` |
| `async Task` / `await` | `async` / `await` Promise |
| `switch` | `switch` (add `break`) |
| `using (var x = …)` | remove; `try/finally` if disposable matters |
| `Console.WriteLine` / `Debug.WriteLine` | `console.log` |
| `MessageBox.Show` | Skill 5 |
| `this.txtX.Text` | state variable `x` (generated React: `vm.txtX.text`) |
| `lv.Items.Add(new ListViewItem(...))`, `lv.SelectedItems`, `lv.Columns.Add(...)` | `vm.lv.items.push(wfListItem(...))`, `vm.lv.items.filter((it) => it.selected)`, `vm.lv.columns.push({ header, width })` |
| `x.Equals(y)` | `x === y` (via the `==` rule, incl. `DialogResult`) |
| `&` / `\|` between two conditions | `&&` / `\|\|` |
| `main.X(...)`, `Preferences.P`, `enum_E.V`, `new Data()` (project classes) | `main.X(...)`, `Preferences.P`, `enum_E.V`, `new Data()` imported from `../support` |
| `Refresh()/Invalidate()/BeginUpdate()/Focus()` | dropped with a comment |
| `sender as Button` | `event.currentTarget` |
| `ViewState["Key"]` / `Session["Key"]` | app-state field |

Prompting rules for translation: one method per call · include the class's field list for context ·
require exact marker `// LLM-translated — verify` on the first line · forbid inventing APIs ·
unknown calls become `// TODO: wire to real API`.

---

## Skill 15 — Unsupported / Manual (always flag)

| Item | Handling |
|---|---|
| Owner-drawn controls, `Paint` override, GDI+ (`Graphics`, `Pen`, `Brush`) | `<canvas>` placeholder + TODO + original C# in comment |
| P/Invoke (`DllImport`), COM interop, ActiveX | server-side service; TODO |
| `Process.Start`, file system, registry, serial/USB, printers | server-side API or browser limitation; TODO |
| `PropertyGrid`, `DesignSurface`, custom designers | manual |
| `Application.DoEvents`, modal message loops | remove; restructure async |
| Multi-monitor / `Screen` / window position APIs | drop |
| Third-party suites (DevExpress, Telerik, Infragistics, Syncfusion) | map to vendor's web equivalent (list in report); otherwise manual |
| **Any control with no library component (as built)** | closest library component from `ui-spec/fallbacks.json` (date picker -> `WfTextBox`, radio -> `WfCheckBox`, list/tree -> `WfListView`, tabs -> `WfPanel`/`WfGroupBox`, link label -> `WfButton`, other -> `WfPanel`/`WfLabel`), marked `/* FALLBACK */`, reported |
| **Windows-only APIs (as built)** | bypassed with `// BYPASSED` (rules in `rules/windows-bypass.json`): registry, clipboard, `Process.Start`, directory services, principals, interop, `Application.Exit/DoEvents`, `Environment.GetFolderPath` ... |
| Crystal Reports / ReportViewer | server PDF + `<iframe>` |
| Localization via `.resx` | i18n JSON (`react-i18next` / `@ngx-translate`) — mechanical; list in report |
| `ConfigurationManager` / `app.config` sections, registry, `Environment.UserName` | TODO (server-supplied config); never emit connection strings or keys |
| `ref` / `out` parameters | the generated method takes the value only and is a stub; callers with `ref` arguments stay TODO |
| UI calls inside non-form classes (`MessageBox`, closing a form) | TODO (dialogs are a React hook of the page) |
| Base types of project classes, `typeof`, lambdas, LINQ query syntax, `goto`, `lock` | TODO + original kept as a comment |

Placeholder: `<WfUnsupported type="System.Windows.Forms.X" reason="Not migrated — see MIGRATION_REPORT.md" />` (React) · `<wf-unsupported …>` (Angular) · `<div data-wf="unsupported" data-type="…">` (HTML). Rendered as a visible ⚠ box by the library.

---

## Skill 16 — Deterministic File Templates

### 16.1 Model (all targets with TS)
```ts
// models/actor.ts — inferred — check against backend contract
export interface Actor {
  actorId: number;
  firstName: string;
  lastName: string;
  dateOfBirth: string; // ISO date
}
```

### 16.2 React page skeleton (library-first)
```tsx
import { WfForm, WfTextBox, WfButton, WfDataGrid, WfGridColumn, useWfDialog } from '@wfm/ui-react';

export default function ActorManagementPage() {
  const [rows, setRows] = useState<Actor[]>([]);
  const [searchTerm, setSearchTerm] = useState('');
  const [form, setForm] = useState<ActorForm>(emptyForm);
  const [mode, setMode] = useState<'Add' | 'Edit'>('Add');
  const { isRoc, isAdmin, userName, userId } = useAuth(); // MOCK — replace with real implementation
  const dialog = useWfDialog();

  useEffect(() => { void loadActors(); }, []);            // Form.Load

  async function loadActors(term?: string) { setRows(await actorService.getAll(term)); }
  // ...handlers translated from code-behind, each marked "// LLM-translated — verify"

  if (!(isRoc || isAdmin)) return <div className="alert alert-warning">You do not have permission to access this page.</div>;
  return (
    <WfForm title="Actor Management System">
      {/* Wf* components in control-tree order; layout via WfPanel / WfFlow / WfTableLayout */}
    </WfForm>
  );
}
```

### 16.3 Angular component skeleton (library-first)
```ts
@Component({
  selector: 'app-actor-management', standalone: true,
  imports: [CommonModule, ReactiveFormsModule, WfFormComponent, WfTextBoxComponent, WfButtonComponent, WfDataGridComponent],
  templateUrl: './actor-management.component.html'
})
export class ActorManagementComponent implements OnInit {
  private svc = inject(ActorService); private dialog = inject(WfDialogService);
  auth = inject(AuthService);                              // MOCK — replace with real implementation
  rows = signal<Actor[]>([]); mode = signal<'Add'|'Edit'>('Add'); searchTerm = signal('');
  form = new FormGroup({ actorId: new FormControl<number|null>(null),
    firstName: new FormControl('', { validators: [Validators.required, Validators.maxLength(50)], nonNullable: true }),
    lastName:  new FormControl('', { validators: [Validators.required, Validators.maxLength(50)], nonNullable: true }),
    dateOfBirth: new FormControl('') });
  ngOnInit() { void this.loadActors(); }                  // Form.Load
}
```

### 16.4 HTML+jQuery page skeleton (library-first)
```html
<link rel="stylesheet" href="node_modules/@wfm/ui-html/dist/wf.css">
<div id="actor-management" class="wf-form">
  <!-- elements with data-wf="…" in control-tree order -->
</div>
<script src="node_modules/jquery/dist/jquery.min.js"></script>
<script src="node_modules/@wfm/ui-html/dist/wf.js"></script>
<script>
(function ($) {
  const state = { rows: [], mode: 'Add' };
  async function loadActors(term) { state.rows = await ActorService.getAll(term); $('#dgvActors').wfDataGrid('setRows', state.rows); }
  $(function () {                                          // Form.Load
    $('#txtSearch').on('wf:enter', () => $('#btnSearch').trigger('wf:click'));
    $('#btnSearch').on('wf:click', () => loadActors($('#txtSearch').val().trim()));
    loadActors();
  });
})(jQuery);
</script>
```

### 16.5 Routes from NavigationIr
```ts
// React Router
[{ path: '/', element: <MainForm/> }, { path: '/actors', element: <ActorManagementPage/> }]
// Angular
export const routes: Routes = [{ path: '', component: MainFormComponent }, { path: 'actors', component: ActorManagementComponent, canActivate: [roleGuard] }];
```

---

## Skill 17 — Worked Example (ActorManagementForm)

> Outputs below use library components: `txtSearch → WfTextBox`, buttons → `WfButton`, `dgvActors → WfDataGrid` (columns `ActorId/FirstName/LastName/DateOfBirth` + `button` column Edit/Delete), `txtDOB → WfDatePicker`, role gate → conditional render/guard.

| IR element | React output | Angular output |
|---|---|---|
| `txtSearch` (TextBox) | `<input value={searchTerm} onChange=… placeholder="Enter actor name…"/>` | `<input [(ngModel)]="searchTerm" placeholder="Enter actor name…">` |
| `btnSearch.Click` → `LoadActors(term)` | `onClick={() => loadActors(searchTerm.trim() \|\| undefined)}` | `(click)="loadActors(searchTerm.trim() \|\| undefined)"` |
| `btnClear.Click` → reset + load | `setSearchTerm(''); loadActors();` | `searchTerm=''; loadActors();` |
| `dgvActors` columns ID/First/Last/DOB/Actions | `<table>` + `.map()`, DOB via `toLocaleDateString('en-US')`, Edit/Delete buttons | `<table>` + `*ngFor`, `date:'MM/dd/yyyy'` pipe |
| Delete (confirm) | `if (await confirm('Are you sure?')) { await remove(id); loadActors(); }` | `if (await dialog.confirm('Are you sure?')) {…}` |
| `btnSave.Click` workflow `[ValidateForm, Insert/Update, ClearForm, LoadActors]` | `async function onSaveClick(){ if(!validate()) return; mode==='Edit' ? await update() : await create(); clearForm(); await loadActors(); }` | same with signals |
| `btnCancel.Click` | `clearForm(); setMode('Add')` | `clearForm(); mode.set('Add')` |
| `hdnActorId` (HiddenField / backing field) | `form.actorId` in state (not rendered) | hidden `FormControl` |
| `<%= FormMode %> Actor` heading | `<h3>{mode} Actor</h3>` | `<h3>{{ mode() }} Actor</h3>` |
| `IsRoc \|\| IsAdmin` gate | early return alert / `RequireRole` | `@if (auth.isRoc() \|\| auth.isAdmin())` |
| `LIKE @Search` | `GET /api/actors?search=` (mock json-server `q=`) | same |

---

## Skill 18 — Adding a New Engine or Control (keep it modular)

### 18.1 New control (all targets)
1. Add entry to `ui-spec/controls.json`: `name, winformsTypes[], props, events, slots, a11y, example`.
2. Implement in **all three** packages (`ui-react`, `ui-angular`, `ui-html`) with identical prop/event names.
3. Add a row to **Skill 0.3**. Generators pick it up via the spec — no engine code change for simple controls.
4. Additive only; bump `ui-spec` minor version. No tests/demo in this repo.

### 18.2 New source technology (e.g. WPF, VB6)
Create `Migrator.Source.<Tech>` implementing `ISourceEngine`; emit the **same** `PageIr`/`ProjectIr` hierarchy (CLAUDE.md §4). Add a mapping section to this file using the template:

| Source control | `ui-spec` control | Prop mapping | Event mapping | Notes / ✅🟡🔧⛔ |
|---|---|---|---|---|

### 18.3 New target framework (e.g. Vue, Blazor)
Create `Migrator.Target.<X>` implementing `ITargetEngine` + new `ui-library/ui-<x>` package implementing `ui-spec`. Add a column set to Skills 1–15 describing the library's internal implementation and a usage snippet to Skill 0.4.

### 18.2a WinForms source engine (`Migrator.Source.WinForms`) — what the IR carries
Generators read these from `ControlNodeIr.Attributes` / `PageIr` (never from the source); details in `docs/ENGINE_AUTHORING.md` and CLAUDE.md §4.2.

| Source construct | IR location | Generator use |
|---|---|---|
| `Controls/Items/DropDownItems/TabPages/Columns/Panel1/Panel2 .Add` | child nodes; `ParentSlot` attr | menus → `WfMenuBar`, tabs → `WfTabs`, split → `WfSplit`, grid columns → `WfGridColumn` |
| `TableLayoutPanel` cell/span/styles | child `TableLayout.*`; table `ColumnStyles`/`RowStyles` (+ `layout` facet `TableDefinition`) | `WfTableLayout` columns/rows/cells (Skill 3.4) |
| `Items.AddRange(new object[]{...})` | `Items` JSON array | `WfComboBox`/`WfListBox` `items` |
| `DataBindings.Add(new Binding("Text", bs, "Name"))` | `DataBinding.Text = bs.Name` | controlled input bound to record field (Skill 7) |
| `SetToolTip`, `.resx` Text/ToolTip | `ToolTip`, `Text` attrs | `tooltip`, labels |
| `Show/ShowDialog/Close/Hide/Application.Exit` | `NavigationIr` via `WinNavigation` (`TargetType` = kind, `Target` = form) | Skill 10 routes / dialog service |
| Form events (`this.Load += ...`) | `RootControl.Attributes["OnLoad"]` + `WorkflowIr` | Skill 1 `Form.Load` |
| Unrecognized designer statement | `project.ir.json` `Shared.Unsupported` | goes to `MIGRATION_REPORT.md`; never dropped |

### 18.3a React target (`Migrator.Target.React`) - as built
Reads the IR folder (including `support.ir.json`) + `ui-spec`; pages import only `@wfm/ui-react`. Dialogs: `ShowDialog` -> `dialog.show(() => <XPage />)`, `Close/Hide` -> `dialog.close()`. Layout: Dock/Location -> top chrome, free rows (clustered by Y), left/fill/right band, bottom chrome. External binary dependencies: `migrate --contract` emits `src/services/<assembly>/` (types, fixtures, mock service); secret-looking members are omitted. Methods: `--translate rules` (deterministic C# -> TS, statement-level fallback, `--compile-gate` demotes anything `tsc` rejects); without it handler bodies stay commented C#. Project classes -> `src/support/index.ts`. Conventions and the translator extension steps: `docs/ENGINE_AUTHORING.md`; how to run: `docs/USER_GUIDE.md`.

### 18.2b Optional source-engine capabilities (as built)
| Interface | Purpose | WinForms |
|---|---|---|
| `IProjectAwareSourceEngine` | extractors needing the whole project | UserControl inlining |
| `IIrProjectionProvider` | technology-specific facet files | `layout` |
| `IUnsupportedReporter` | unhandled constructs -> `project.ir.json` | designer statements |
| `ISupportSourceEngine` | the project's own non-UI classes -> `ir/support.ir.json` | `main`, `Preferences`, enums, data classes |

### 18.4 Branch & report reminder
Every addition is done on a **new branch**, committed locally, **not pushed**; finish with the push + fetch/update command block from `CLAUDE.md` §0.2.
