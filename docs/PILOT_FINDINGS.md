# Pilot findings — ECMNavigator (WinForms, .NET Framework 4.8)

Source: 36 units (35 forms + 1 UserControl), startup form `frmNavigator`. Numbers come from the IR emitted by `migrator ir` (after the
overload and navigation fixes), not from compiling the app. Scan date 2026-10-11. Heuristic regex counts on method bodies, so treat as
"at least this much", not exact.

## 1. Controls (856 non-root controls)
Everything is a standard `System.Windows.Forms` control. **No third-party or owner-drawn controls were found.**

| Group | Controls |
|---|---|
| Menus / toolbars | ToolStripMenuItem 347, ToolStripSeparator 124, ToolStripButton 29, MenuStrip 16, ContextMenuStrip 19, ToolStrip 4, StatusStrip 11, ToolStripComboBox 4 |
| Inputs | Button 79, TextBox 26, ComboBox 22, CheckBox 12, RadioButton 9, DateTimePicker 4 |
| Display / layout | Label 69, Panel 18, GroupBox 18, SplitContainer 8, PictureBox 7, ProgressBar 5 |
| Data | **ListView 19** (filled at runtime, no designer columns), DataGridView 2, TreeView 1, ImageList 2 |

Implication for the UI library: **menus/toolbars/status bar and ListView come first**; `WfDataGrid` and `WfTreeView` are rare here.

## 2. Startup form `frmNavigator` (the first React target)
118 controls, 192 members, 61 workflows. Mostly chrome: 60 menu items, 26 separators, 14 toolbar buttons, 1 MenuStrip, 1 ToolStrip, 1 StatusStrip,
1 SplitContainer, 3 Panels, **1 DataGridView**, 1 ProgressBar. Navigation: **24 `ShowDialog` + 3 `Close`**, reaching 16 forms directly.
28 of 36 units are reachable from it (16 at depth 1, 10 at depth 2, 1 at depth 3). Not reachable: `frmAdminOptions`, `frmConvertFileExtensions`,
`frmLogon`, `frmMySubscriptions`, `frmOptionList`, `frmPictureMode`, `frmValueList`, `ucCustomField` (opened by code the extractor cannot see, or unused).

## 3. Logic and backend usage (method bodies)
1,015 methods, ~18.8k lines, **median 7 lines**, 10 methods over 100 lines, none over 300 (largest: `frmNavigator.CheckInContent` 143,
`frmNavigator.SearchECM` 140). Small-model translation (P4) is realistic; the long ones need review.

| API family | Methods | Forms | Handling |
|---|---:|---:|---|
| **`ECMLibrary.*` (the backend)** | 294 | 31 | The data tier. Needs a typed service facade + mock (`// MOCK`). Top types: `UCM` 297 refs, `Services` 154, `UCMField` 144, `UCMContentItem` 124, `ECMQueryItem` 84, `UCMProfile` 63 |
| MessageBox | 74 | 21 | `WfDialogService` (Skill 5/11) |
| File system (File/Directory/Path/streams) | 70 | 12 | Browser limitation: server-side service or upload/download; TODO + report |
| `Process.Start` | 13 | 7 | Browser limitation (open document/URL): `window.open` where it is a URL, else TODO |
| DataSet/DataTable/BindingSource | 3 | 1 | Typed row arrays |
| COM/Interop, Clipboard, GDI+, threading | 1 each | 1 each | Flag individually |
| Direct ADO.NET, Registry, P/Invoke, Settings/config, printing, drag-drop | 0 | 0 | none found |

**No direct database access**: all data goes through `ECMLibrary`, so the React app needs an API contract for that library, not SQL replacement.
That is a different (and cleaner) migration problem than the Web Forms sample: define `ecmService.ts` from the type names above and mock it.

## 4. Consequences for the plan
1. **P2 control set** (in this order): WfForm, menu bar + items + separators, toolbar, status bar, button, label, textbox, combobox, checkbox, radio group,
   panel, group box, split, ListView, DataGridView (basic), PictureBox, ProgressBar, DateTimePicker, dialog service, `WfUnsupported`.
2. **First React target**: `frmNavigator` shell (menu + toolbar + grid + status bar) with its 16 direct dialogs as stubs/modals; the other forms listed in the report.
3. **Risks to track**: `ECMLibrary` contract (unknown shapes beyond names), file-system and `Process.Start` behaviours, ListViews populated only in code
   (need inferred row models, marker `// inferred — check against backend contract`).
4. Extractor findings fixed on the way: overloaded handlers aborted 7 units; navigation hidden in helper methods (0 -> 61 targeted `ShowDialog`);
   file dialogs wrongly counted as form navigation.

## 5. Results of the migration so far (ECMNavigator, 2026-10-12)
`migrate --translate rules --compile-gate --contract <ECMLibrary contract>` on the 17 forms reachable from `frmNavigator` (depth all), output in a folder outside the repo:

| Measure | Result |
|---|---|
| Forms generated | 17 of 36 units (the rest are not reachable from the startup form) |
| Page methods translated | **375 complete (53%)**, 209 partial, 122 not translated (81 of those demoted by the compile gate), of 706 |
| Project classes (`main`, `Preferences`, `ECMConfiguration`, data classes, 13 enums) | 33 types, 157 members: 56 complete, 58 partial, 43 stubs |
| Generated app | `tsc` 0 errors, lint 0 errors, build OK; all 17 pages render without throwing (jsdom smoke) |
| Controls without a library component | 7 (TreeView, tabs, radio group, date picker, ...) |

How the number moved: 321 (first rule translator) -> 332 (ListView) -> 375 (project classes extracted and called as in C#). Main remaining blockers, from `MIGRATION_REPORT.md`:
declarations that depend on an untranslated statement (cascade), members of types with no browser equivalent (`ImageList`, `TimeSpan`, WinForms types), `ref`/`out` arguments, `ConfigurationManager`,
event subscriptions in code, `dgv.Rows` / `pnl.Controls`. Pages open with **mock data** (fixture rows from the contract, sample list rows); the opener does not pass real constructor arguments yet.
Everything is reviewed by the owner before use: translated logic is deterministic but unverified.

## 6. Second application (an HR suite, 2026-10-13): what it exposed
A multi-project solution (17 projects; 15 forms + 144 user controls; ~2,200 controls; custom control libraries; code-only base classes for every screen; a shell that opens modules by reflection). It was the first test of project independence.

| Finding | Effect | Fix |
|---|---|---|
| Controls come from custom libraries (`Vendor.Controls.TextBox : System.Windows.Forms.TextBox`, project `XComboBox`) | 3% of controls recognised | Base-type chain resolution (`ResolvedType`): ~99% resolve to standard types, ~93% supported by the spec (rest: DateTimePicker 79, TreeView, RadioButton, TabControl, ListBox, ...) |
| Every screen derives from code-only base classes | Base members (mode flags, find/export hooks) unresolved: 15% of methods complete | Inherited members merged: 43% complete (1,759 / 4,123 methods) |
| Business layer (252 classes) extracted as "support" | 23,600-line module | Pruned to reachable classes (145 classes) |
| Shell opens screens via `Activator.CreateInstance(type)` | Only the main form was reachable | `--units all` (a module registry with `typeof`/`Activator` translation is the next step) |
| Designer leaves grid columns declared but unattached; duplicate column keys; verbatim `@param` names | placeholders / crash / invalid TypeScript | inert-column rule, de-duplication, `ValueText` identifiers |
| Several lint rules fire on faithfully translated code | 634 lint errors | rules off in the generated config, empty-branch comments, `const` merge |

Result: 159 units generated, `tsc` 0 errors, lint 0, build OK; first application unchanged (375 / 706).

Second-app follow-ups: the entities the pages use live in a **referenced project outside the analysed folder** (a generated entity layer). Following such references (classes as typed shapes, library bodies not translated), merging partial classes, real inheritance,
ComboBox `Items`/`SelectedIndex` support and statement-level demotion in the compile gate took the HR app from 1,792 / 4,123 complete (+ 766 methods demoted whole) to 1,736 complete, 1,428 partial, 959 not translated (496 demoted whole), `tsc` 0, lint 0, with typed entities available to translated code.
The first application: 375 / 237 / 94 (was 375 / 209 / 122). Server-side code (data access, configuration) is listed in the report: 53 classes and ~300 methods in the HR app are backend work.

Control wave A (date/time picker, radio button with groups, numeric up/down, tabs, link label as real components): the HR app's fallback-rendered controls dropped from 111 to 14 (tree views, list boxes, ...); `tsc`/lint/build clean, all 159 pages render.
