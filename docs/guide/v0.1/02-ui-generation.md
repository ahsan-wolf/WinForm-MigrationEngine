# 2. UI generation — what exists

## 2.1 Source side: from WinForms to IR
| Capability | How it works |
|---|---|
| Project analysis | Finds Designer/code-behind pairs; classifies Form vs UserControl by following the base-type chain across **all** `.cs` files (so code-only base classes resolve); startup form and framework version |
| Designer parsing | Roslyn syntax. Control tree from `Controls.Add` edges (menus, tabs, split panels, table cells), nested properties, DataGridView columns, item literals, data bindings, tooltips, `.resx` text merged (code wins). Unhandled statements go to `project.ir.json` `Shared.Unsupported`, never dropped |
| Members and workflows | Fields, properties, methods (bodies kept), `.ctor` (becomes page inputs), event wiring (`OnClick` → handler), call-order workflow steps, navigation (`Show`/`ShowDialog`/`Close`…) through the shared `NavigationIr` |
| **Custom controls** | Resolved through their base-type chain (`ResolvedType`): a class deriving from a standard control, in the project or a referenced library, uses that standard control's mapping. Pilot B: 3% → ~99% of controls recognised |
| **Inheritance** | Members of code-only base classes are merged into the unit (`inherited:Base`), so inherited calls resolve (pilot B: 15% → 43% of methods complete) |
| **Support classes** | The app's own non-UI classes (static helpers, data classes, structs, enums, partial classes merged) → `support.ir.json`; pruned to what the generated pages reach |
| **Referenced projects** | `ProjectReference`s outside the source folder are followed (entity layers, shared libraries); their classes are marked `External` and become typed shapes only |
| Dependencies | Referenced binary assemblies listed in `Shared.ExternalAssemblies`; `contract` reads a DLL's public API from metadata only (never executed) |

## 2.2 UI library
`@wfm/ui-react` has 27 visual components plus `WfDialogProvider/useWfDialog`, `useWfModel` and `WfUnsupported`. The control table is in `../../UI_CONTROL_MAPPING.md`. Properties: library-first (generated pages import only `Wf*`), themeable by CSS variables, inputs controlled when a change handler exists and uncontrolled otherwise (so visual-only pages accept typing).

**Fallbacks.** A control with no component renders as the closest one, marked `/* FALLBACK: Type */` and listed in the report: text-like (month calendar, masked/rich text) → text box; list-like (list box, tree) → list view; scroll/track bars → progress; unknown → panel (with children) or label. The mapping is data: `ui-spec/fallbacks.json`.

## 2.3 Generation
- **Layout** from Dock/Anchor/TabIndex/table facts into flex/grid; never pixels.
- **State**: `useWfModel` gives a deep proxy with `vm` (control properties, incl. combo `items/selectedIndex/text/selected`, list rows) and `f` (form fields); reads are immediate, writes batch one re-render, so imperative WinForms code translates one to one.
- **Dialogs**: `ShowDialog` → dialog service; `DialogResult` buttons close the dialog; `MessageBox` → alert/confirm.
- **Constructor arguments** become optional props with fixture defaults (`// MOCK`), so a page opens standalone.
- **Dev launcher**: `?page=<Name>` opens one generated page; `?page=` lists them.
- **Contracts**: `--contract` turns a backend assembly's public API into typed mock services and fixtures; secret-looking members are omitted and listed as SERVER-ONLY.

## 2.4 Translation (`--translate rules`)
Deterministic Roslyn C# → TypeScript for page methods and project classes (class modules with `extends`, load order by initialisers and base classes).
1. **Rules** translate statement by statement; unknown calls get `// TODO: wire to real API`.
2. **Windows bypass** (`rules/windows-bypass.json`): registry, clipboard, directory services, `Process.Start`… are kept as comments with `// BYPASSED`, never silently removed.
3. **Server-bound pruning** (`rules/server-bound.json`): classes/methods that use databases, files, configuration or service stacks are not translated; they are stubs and listed as backend work.
4. **Compile gate** (`--compile-gate`): `tsc` is the oracle. A failing simple statement is demoted alone (`LineDemotion`); anything structural demotes the whole method; up to 5 rounds; the page is regenerated each round.
5. **Report**: per-form complete/partial/not translated, top reasons, project classes, manual work by category.

## 2.5 Viewer and app shell
`--preview` → `react-viewer`: every form and user control (inlined ones too) as a visual-only page with placeholder data, sidebar, filter, URL per unit, design-size and background switches. `--appshell` → `react-appshell`: the same pages in a WinForms-style window (bordered navigation pane with hyperlinks, form-style frame on the right). Neither contains handlers, services or translated code.

## 2.6 Measured results (pilot data, ecosystem-neutral)
| | Pilot A (36 units, one project) | Pilot B (159 units, 17 projects, custom control libraries) |
|---|---|---|
| Units generated | 17 reachable | 159 (`--units all`) |
| Methods complete / partial / not translated | 375 / 237 / 94 (of 706) | 1,736 / 1,428 / 959 (of 4,123) |
| `tsc` errors / lint problems | 0 / 0 | 0 / 0 |
| Pages that render in the smoke test | 17 of 17 | 159 of 159 |
| Fallback-rendered controls | 7 | 14 (down from 111 after the date/radio/numeric/tabs wave) |

Details and history: `../../PILOT_FINDINGS.md`.

## 2.7 Known gaps
No real components yet for tree view, list box, rich/masked text, month calendar. Not translated: controls and `this` as values, lambdas, `ref/out`, event subscriptions in code, `string.Format` specifiers, runtime-created controls, LINQ query syntax. 496 methods in pilot B still demote whole. Handlers in a third partial file are not resolved. No pixel fidelity. Angular/HTML targets and LLM tier are not built. Translated logic is never verified.
