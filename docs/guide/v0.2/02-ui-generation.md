# 2. UI generation — what exists

## 2.1 Source side: from WinForms to IR
| Capability | How it works |
|---|---|
| Project analysis | Finds Designer/code-behind pairs; classifies Form vs UserControl by following the base-type chain across **all** `.cs` files; startup form and framework version |
| Designer parsing | Roslyn syntax. Control tree from `Controls.Add` edges (menus, tabs, split panels, table cells), nested properties, DataGridView columns, item literals, data bindings, tooltips, `.resx` text merged (code wins). Unhandled statements go to `project.ir.json` `Shared.Unsupported`, never dropped |
| Members and workflows | Fields, properties, methods (bodies kept), `.ctor` (page inputs), event wiring from the designer (`OnClick` → handler), call-order workflow steps, navigation (`Show`/`ShowDialog`/`Close`…) through the shared `NavigationIr` |
| Custom controls | Resolved through their base-type chain (`ResolvedType`); pilot B went from 3% to ~99% of controls recognised |
| Inheritance | Members of code-only base classes are merged into the unit |
| Support classes | The app's own non-UI classes → `support.ir.json`, pruned to what the generated pages reach |
| Referenced projects | `ProjectReference`s outside the source folder are followed; their classes become typed shapes |
| Images | Readable byte-array images of a `.resx` (`resources.GetObject("x.Image")`, form icon) are kept as base64 pseudo-attributes `Image.Data` / `Image.Format` (png, gif, jpg, bmp, ico, svg) / `Icon.Data` |
| Dependencies | Referenced binary assemblies in `Shared.ExternalAssemblies`; `contract` reads a DLL's public API from metadata only |

## 2.2 UI library
`@wfm/ui-react` 0.2.0 (`ui-spec` 0.2.0: 40 entries, 36 of which map WinForms types). The control table is in `../../UI_CONTROL_MAPPING.md`. It has no third-party or jQuery dependency; behaviour comes from native HTML elements:
- **Shell:** `WfForm`, `WfPanel`, `WfGroupBox`, `WfSplit`, `WfTabs`/`WfTabPage`, `WfContextMenu`; menus and bars with icons: `WfMenuBar`, `WfMenuItem` (icon column), `WfToolbar`, `WfToolbarButton` (image by default when it has one, `displayStyle`), `WfDropDownButton` (split option), `WfStatusBar`.
- **Inputs:** `WfTextBox`, `WfMaskedTextBox` (mask applied while typing), `WfRichTextBox` (plain text), `WfComboBox`, `WfListBox`, `WfCheckBox`, `WfCheckedListBox`, `WfRadioButton` (groups), `WfDateTimePicker` (native date/time input, min/max), `WfNumericUpDown`, `WfDomainUpDown`, `WfTrackBar`/`WfScrollBar` (native range input), `WfMonthCalendar`, `WfLinkLabel`.
- **Data:** `WfDataGrid` (sort, editable cells, pager, column resize, row keys, formatter, arrow keys), `WfListView`, `WfTreeView` (lazy loading, node icons, check boxes).
- **Services:** `WfDialogProvider` + `useWfDialog` (`alert`, `confirm`, `message`, `show`, `close`), `useWfModel`, `WfUnsupported`.
- Inputs are controlled when a change handler is passed and uncontrolled otherwise. Theming only through CSS variables. Only `ToolStripTextBox` still uses a stand-in (`fallbacks.json`); unknown controls get the closest component (panel with children, otherwise label).

## 2.3 Generation
- **Layout** from Dock/Anchor/TabIndex/table facts into flex/grid; never pixels.
- **State:** `useWfModel` gives `vm` (control properties, combo and list models, grid rows, tree nodes, checked indices) and `f` (form fields); reads are immediate, writes batch one re-render.
- **Pages:** one `src/pages/<Unit>Page.tsx` (user controls `<Unit>Control.tsx`) per WinForms unit, header comment names the source file, original code in `src/legacy/`.
- **Popups:** `ShowDialog` → `await dialog.show(() => <XPage />, { title })`; `Close()` → `dialog.close()`; DialogResult buttons → `dialog.close("OK")`; `MessageBox.Show` → `dialog.alert` / `confirm` / `message` (YesNoCancel, RetryCancel, AbortRetryIgnore return the DialogResult name, icons become badges). Every popup has a title-bar ✕ and closes on Escape. When translation lost the statement that opened or closed a form, the handler still opens or closes it, marked `// MOCK` and reported (`nav-mock-open`, `nav-mock-close`).
- **Icons:** `Image.Data` is written to `public/icons/<unit>/<control>.png`; ICO and BMP are converted to PNG in managed code (no Windows API, largest frame up to 32 px); unreadable images (ImageList entries) get a placeholder and a report entry. Menu items show an icon column; toolbar buttons show the image (text becomes the tooltip) unless `DisplayStyle` says otherwise.
- **Grids:** columns are editable only for an explicit `ReadOnly = false` (WinForms default would turn every migrated grid into an editor); edits are written back to the page model.
- **Constructor arguments** become optional props with fixture defaults (`// MOCK`); `/<page-name>` (for example `/search-results`) opens a page on its own and `/pages` lists them; the app uses History API routes with a relative `<base>`.
- **Contracts:** `--contract` turns a backend assembly's public API into typed mock services; secret-looking members are omitted and listed as SERVER-ONLY.

## 2.4 Translation (`--translate rules`)
Deterministic Roslyn C# → TypeScript for page methods and project classes, checked by `tsc` (compile gate, statement-level demotion); Windows-only APIs bypassed with `// BYPASSED` (`rules/windows-bypass.json`); server-bound code left as backend work (`rules/server-bound.json`); every untranslated statement stays as a commented original with a TODO and a report entry.

## 2.5 Viewer and app shell
`--preview` → `react-viewer`: every form and user control as a visual-only page, no handlers. `--appshell` → `react-appshell`: the same pages in a WinForms-style window; the **only** behaviour wired there is opening and closing dialogs (a button or menu item that opened a form in the original opens that page as a popup; Close, OK, Cancel, ✕ and Escape close it).

## 2.6 Measured results (pilot data, application-neutral)
| | Pilot A (36 units) | Pilot B (159 units, 17 projects, custom control libraries) |
|---|---|---|
| Units generated | 28 reachable | 159 |
| Methods complete / partial / not translated | 476 / 299 / 129 (of 904) | 1,743 / 1,423 / 957 (of 4,123) |
| `tsc` errors / lint problems | 0 / 0 | 0 / 0 |
| Icon files written | 100 (11 converted from ICO/BMP) | 0 (its forms have no menu or toolbar images) |
| `ShowDialog` navigations that open a popup | 57 of 58 | 1 of 2 |
| Dialogs that open and close in a browser run | 16 of 16 main-window menu items that open a form | not run |

Details and history: `../../PILOT_FINDINGS.md`.

## 2.7 Known gaps
- **Collection members:** `dgv.Rows` / `Cells[]` / `SelectedRows`, `panel.Controls` and `TabPages` are not translated yet (see [08-roadmap.md](08-roadmap.md)); they are why handlers such as Select All, Add Files and Clear Metadata do nothing.
- **Events subscribed in code** (`ctl.Click += handler`) stay as commented TODOs (about 400 in the two pilots outside designers).
- **`ImageList`** images are not decoded (binary, Windows-only); `Path.*` file-name calls are not translated; there are no per-page routes.
- Not translated: `ref`/`out`, lambdas, runtime-created controls, `CellFormatting`, `string.Format` specifiers. No pixel fidelity. Angular and HTML+jQuery libraries and the LLM tier are not built. Translated logic is never verified.
