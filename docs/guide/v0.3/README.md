# WinForms Migration Engine — guide v0.3

Complete working guide for engine release **0.3** (WinForms source engine 0.3.0, React target engine 0.3.0, IR schema `0.1.0`, `ui-spec` `0.3.0`, `@wfm/ui-react` `0.3.0`). Written for the person who runs, reviews or extends a migration. It describes `main` as of the merge of pull request #35.

| Part | Read it for |
|---|---|
| [01-overview.md](01-overview.md) | What the engine is, the modules, how data flows |
| [02-ui-generation.md](02-ui-generation.md) | Everything the UI side does (IR, controls, popups, icons, translation, viewer/app shell), measured results and gaps |
| [03-backend.md](03-backend.md) | The backend track: the analyzer that exists and the gateway that is on the to-do list |
| [04-workflow.md](04-workflow.md) | Step-by-step runbook from a new application to a checked app, including browser verification |
| [05-project-independence.md](05-project-independence.md) | How the engine stays free of any one application; onboarding a new one |
| [06-extending.md](06-extending.md) | Add a control, a rule, a target, a source technology |
| [07-decisions.md](07-decisions.md) | Decision records: event wiring in code, image resources instead of `ImageList`, `Path` helpers, routes |
| [08-roadmap.md](08-roadmap.md) | The next slices with size, risk and the backend gateway to-do |
| [CHANGELOG.md](CHANGELOG.md) | What was built, in order |
| [VERSIONING.md](VERSIONING.md) | Version policy and how to cut `v0.4` |

Detail documents that this guide summarises (not duplicates): `../../USER_GUIDE.md` (every option), `../../PROJECT_DOCUMENTATION.md` (as-built architecture), `../../ENGINE_AUTHORING.md` (contracts, IR keys), `../../UI_CONTROL_MAPPING.md` (control table), `../../PROJECT_PLAN.md` (roadmap), `../../../CLAUDE.md` (rules), `../../../SKILLS.md` (mappings).

## State in one paragraph
A WinForms project goes in; out come an IR folder, a React + TypeScript app that uses only `@wfm/ui-react`, a visual-only component viewer and a WinForms-style app shell, and a report of what still needs a person. Business code is translated by deterministic rules and checked by `tsc`. Every standard control now has a real component; popups are native `<dialog>` elements opened with `dialog.show()` and closed with `dialog.close()`; menu and toolbar icons come from the designer's `.resx` images (ICO and BMP converted to PNG). The app shell opens and closes dialogs so the original window flow can be walked through. A read-only backend analyzer describes the legacy service layer; the REST gateway is on the to-do list.

## What changed since v0.2
- **Page structure:** every page is a markup file plus a code-behind file (`X.tsx` + `X.code.tsx`) with an exported `wiring` table; event wiring is read from code-behind subscriptions too.
- **Grids:** `Rows`, `Columns`, `Cells[k].Value`, `SelectedRows`, `CurrentRow`, `DataSource`, cell click / double click / formatting events with `e.RowIndex` / `e.ColumnIndex` / `e.Value`; a ListView that nothing uses is rendered as a grid.
- **Containers:** tab pages (`TabPages`, `SelectedTab`, `TabCount`) and controls added in code (`panel.Controls.Add(new UserControl(args))`).
- **Images:** `ImageList` streams are decoded to PNG files and used by key or index (tree, list, tabs, toolbar, menus); `Properties.Resources.X` and `--images <folder>`; the form icon is the favicon.
- **Translator:** `Path.*` helpers, `ref` to non-assigning methods, `int.TryParse(..., out x)`, expression lambdas and common LINQ calls.
- **Routes:** History API paths (`/`, `/<page-name>`, `/pages`) with a relative `<base>`; the app shell and viewer keep hash routes.
- **Versions:** engines, `ui-spec` and `@wfm/ui-react` move from 0.2.0 to 0.3.0; the IR schema stays 0.1.0 (the pseudo-attributes `Images.*` and the sidecar `resources.ir.json` are additive).

## What changed since v0.1
- **Controls:** 12 new components (masked and rich text, month calendar, list box, tree view, checked list box, up/down box, track bar, scroll bar, drop-down button), plus grid editing, paging and column resize, lazy tree loading and date limits. No stand-ins remain for standard controls.
- **Popups:** native `<dialog>`, `dialog.show()` / `dialog.close(result?)` / `message()` with button sets and icons, a title-bar ✕, and `onRootClose` for standalone pages. Popups that translation lost are restored (marked `// MOCK`).
- **Icons:** images from `.resx` become web files under `public/icons` (see [02](02-ui-generation.md)).
- **App shell:** dialogs open and close; the viewer stays handler-free.
- **Versions:** engines, `ui-spec` and `@wfm/ui-react` move from 0.1.0 to 0.2.0; the IR schema stays 0.1.0.
- **Verification:** browser checks in a visible Edge window next to the jsdom suites (see [04](04-workflow.md)).
